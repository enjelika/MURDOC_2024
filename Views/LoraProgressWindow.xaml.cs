using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace MURDOC_2024.Views
{
    /// <summary>
    /// Progress window shown while lora_retrain.py runs in the background.
    /// Opens automatically when a session with more than 2 edited images ends.
    /// Closes itself when retraining completes or fails.
    /// </summary>
    public partial class LoraProgressWindow : Window
    {
        private readonly string _sessionId;
        private bool _retrainingComplete = false;

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
        /// Starts lora_retrain.py as a background process and streams its output
        /// to the status log. Closes the window when the process exits.
        /// Called from MainWindowViewModel after the window is shown.
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
                        {
                            Dispatcher.Invoke(() =>
                            {
                                AppendLog(e.Data);
                                UpdateStatusFromLine(e.Data);
                            });
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            // Python writes warnings/tracebacks to stderr — log but don't fail
                            Dispatcher.Invoke(() => AppendLog($"[stderr] {e.Data}"));
                        }
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

            // Update UI on completion
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

        // ─── Helpers ────────────────────────────────────────────────────────────

        private void AppendLog(string line)
        {
            StatusLog.Text += line + "\n";
            LogScroller.ScrollToBottom();
        }

        private void SetStatus(string text)
        {
            StatusLabel.Text = text;
        }

        /// <summary>
        /// Maps key output lines from lora_retrain.py to human-readable status messages.
        /// </summary>
        private void UpdateStatusFromLine(string line)
        {
            if (line.Contains("[INFO] Loading base model"))
                SetStatus("Loading base model…");
            else if (line.Contains("[INFO] Injecting LoRA"))
                SetStatus("Injecting LoRA adapters…");
            else if (line.Contains("[TRAIN] Session"))
                SetStatus("Training on your edits…");
            else if (line.Contains("Epoch"))
                SetStatus($"Training… {line.Trim()}");
            else if (line.Contains("[INFO] Saved"))
                SetStatus("Saving LoRA adapter weights…");
            else if (line.Contains("[DONE]"))
                SetStatus("Retraining complete.");
            else if (line.Contains("[ERROR]"))
                SetStatus($"Error: {line.Replace("[ERROR]", "").Trim()}");
        }
    }
}
