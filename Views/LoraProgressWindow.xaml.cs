using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace MURDOC_2024.Views
{
    /// <summary>
    /// Progress window shown while lora_retrain.py runs in the background.
    /// Opens automatically when a session with more than 2 edited images ends.
    /// Closes itself when retraining completes or fails.
    ///
    /// Progress bar behaviour:
    ///   - Indeterminate until the [TRAIN] line arrives (model loading phase)
    ///   - Switches to deterministic once total epoch count is known, advancing
    ///     one step per "  Epoch N/Total" line printed by lora_retrain.py
    ///   - Fills to 100 % (green) on [DONE], resets to 0 % (red) on failure
    /// </summary>
    public partial class LoraProgressWindow : Window
    {
        private readonly string _sessionId;
        private bool _retrainingComplete = false;

        // Epoch tracking for deterministic progress
        private int _totalEpochs = 0;

        // Matches:  "  Epoch   5/30 | Loss: ..."  or  "  Epoch  5/30 | ..."
        private static readonly Regex EpochRegex =
            new Regex(@"Epoch\s+(\d+)\s*/\s*(\d+)", RegexOptions.Compiled);

        // Matches:  "[TRAIN] Session: ... | 3 samples | 30 epochs"
        private static readonly Regex TrainHeaderRegex =
            new Regex(@"\[TRAIN\].*\|\s*(\d+)\s*epochs", RegexOptions.Compiled);

        public LoraProgressWindow(string sessionId)
        {
            InitializeComponent();
            _sessionId = sessionId;

            // Prevent the user from closing the window mid-retrain
            Closing += (s, e) =>
            {
                if (!_retrainingComplete)
                    e.Cancel = true;
            };
        }

        /// <summary>
        /// Starts lora_retrain.py as a background process, streams its stdout
        /// to the status log, and drives the progress bar from epoch output.
        /// Closes the window automatically when the process exits.
        /// </summary>
        public async Task RunRetrainingAsync(string workingDirectory)
        {
            AppendLog($"[INFO] Starting LoRA retraining for session: {_sessionId}");
            SetStatus("Loading model and session data…");

            bool success = false;
            string errorText = string.Empty;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"Model\\lora_retrain.py --session session_{_sessionId}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
                {
                    var tcs = new TaskCompletionSource<int>();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                            Dispatcher.Invoke(() => HandleOutputLine(e.Data));
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                            Dispatcher.Invoke(() => AppendLog($"[stderr] {e.Data}"));
                    };

                    process.Exited += (s, e) => tcs.TrySetResult(process.ExitCode);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    int exitCode = await tcs.Task;
                    success = (exitCode == 0);

                    if (!success)
                        errorText = $"Process exited with code {exitCode}";
                }
            }
            catch (Exception ex)
            {
                errorText = ex.Message;
            }

            // Final UI update
            Dispatcher.Invoke(() =>
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = success ? 100 : 0;
                ProgressBar.Foreground = success
                    ? System.Windows.Media.Brushes.Green
                    : System.Windows.Media.Brushes.Red;

                if (success)
                {
                    AppendLog("[DONE] Model updated successfully.");
                    SetStatus("Retraining complete. This window will close shortly.");
                }
                else
                {
                    AppendLog($"[ERROR] Retraining failed: {errorText}");
                    SetStatus("Retraining failed — see log above.");
                }
            });

            // Brief pause so the user can read the final status, then close
            await Task.Delay(success ? 2000 : 4000);

            Dispatcher.Invoke(() =>
            {
                _retrainingComplete = true;
                Close();
            });
        }

        // ─── Line handler ────────────────────────────────────────────────────────

        /// <summary>
        /// Called on every stdout line from lora_retrain.py.
        /// Appends to the log, updates status text, and advances the progress bar.
        /// </summary>
        private void HandleOutputLine(string line)
        {
            AppendLog(line);

            // ── Phase 1: indeterminate (model loading) ──────────────────────────
            if (line.Contains("[INFO] Loading base model"))
            {
                SetStatus("Loading base model…");
                return;
            }

            if (line.Contains("[INFO] Injecting LoRA"))
            {
                SetStatus("Injecting LoRA adapters…");
                return;
            }

            // ── Phase 2: switch to deterministic once epoch count is known ───────
            // [TRAIN] Session: session_id | 3 samples | 30 epochs
            var trainMatch = TrainHeaderRegex.Match(line);
            if (trainMatch.Success)
            {
                _totalEpochs = int.Parse(trainMatch.Groups[1].Value);
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = _totalEpochs;
                ProgressBar.Value = 0;
                SetStatus($"Training on your edits… (0 / {_totalEpochs} epochs)");
                return;
            }

            // ── Phase 3: advance bar one step per epoch ───────────────────────────
            // "  Epoch   5/30 | Loss: 0.123456 | LR: 1.00e-04"
            var epochMatch = EpochRegex.Match(line);
            if (epochMatch.Success && _totalEpochs > 0)
            {
                int current = int.Parse(epochMatch.Groups[1].Value);
                int total   = int.Parse(epochMatch.Groups[2].Value);

                // Guard: use parsed total if it differs from the header value
                if (_totalEpochs != total) _totalEpochs = total;

                ProgressBar.Maximum = _totalEpochs;
                ProgressBar.Value   = current;
                SetStatus($"Training on your edits… ({current} / {_totalEpochs} epochs)");
                return;
            }

            // ── Phase 4: late-stage messages ─────────────────────────────────────
            if (line.Contains("[INFO] Saved") || line.Contains("[INFO] Updated"))
                SetStatus("Saving LoRA adapter weights…");
            else if (line.Contains("[DONE]"))
                SetStatus("Retraining complete.");
            else if (line.Contains("[ERROR]"))
                SetStatus($"Error: {line.Replace("[ERROR]", "").Trim()}");
            else if (line.Contains("[WARN]"))
                SetStatus($"Warning: {line.Replace("[WARN]", "").Trim()}");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private void AppendLog(string line)
        {
            StatusLog.Text += line + "\n";
            LogScroller.ScrollToBottom();
        }

        private void SetStatus(string text)
        {
            StatusLabel.Text = text;
        }
    }
}
