using MURDOC_2024.Model;
using MURDOC_2024.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class MainWindowViewModel : ViewModelBase, IDisposable
    {
        private string _selectedImagePath;
        private BitmapImage _previewImage;
        private readonly ImageService _imageService;
        private readonly PythonModelService _python;
        private bool _isRunningModels;
        private string _latestAdjustedImagePath;

        // Performance metrics
        private PerformanceMetricsService _metricsService;
        private Stopwatch _modelExecutionTimer;

        // Detection tracking
        private List<DetectionResult> _currentDetections;

        #region Properties

        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set => SetProperty(ref _selectedImagePath, value);
        }

        public bool IsRunningModels
        {
            get => _isRunningModels;
            set
            {
                if (SetProperty(ref _isRunningModels, value))
                {
                    OnPropertyChanged(nameof(IsNotRunningModels));

                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ImageControlVM?.UpdateCommandStates();
                        MICAControlVM?.UpdateCommandStates();
                        EditorControlsVM?.UpdateCommandStates();
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
            }
        }

        public bool IsNotRunningModels => !IsRunningModels;

        // Child ViewModels
        public ImageControlViewModel ImageControlVM { get; }
        public InputImagePaneViewModel InputImageVM { get; }
        public PreviewPaneViewModel PreviewPaneVM { get; }
        public IAIOutputPaneViewModel IAIOutputVM { get; }
        public RankNetViewModel RankNetVM { get; }
        public EfficientDetD7ViewModel EfficientDetVM { get; }
        public FinalPredictionPaneViewModel FinalPredictionVM { get; }
        public MICAControlViewModel MICAControlVM { get; }
        public EditorControlsPaneViewModel EditorControlsVM { get; }
        public SessionInfoPaneViewModel SessionInfoVM { get; }

        #endregion

        #region Events

        public event EventHandler ResetDrawingRequested;
        public event EventHandler EnterEditModeRequested;
        public event EventHandler ExitEditModeRequested;
        public event EventHandler<PointEditMode> PointEditModeChanged;
        public event EventHandler SaveAllModificationsRequested;
        public event EventHandler<RankBrushEventArgs> RankBrushChangedRequested;

        #endregion

        public MainWindowViewModel()
        {
            _python = new PythonModelService();
            _python.SetDetectionParameters(1.5, 0.0);
            _imageService = new ImageService();

            // Initialize metrics service
            _metricsService = new PerformanceMetricsService();
            _metricsService.StartSession();
            _modelExecutionTimer = new Stopwatch();

            _currentDetections = new List<DetectionResult>();

            // Create child VMs
            InputImageVM = new InputImagePaneViewModel();
            PreviewPaneVM = new PreviewPaneViewModel();
            IAIOutputVM = new IAIOutputPaneViewModel();

            ImageControlVM = new ImageControlViewModel(
                runModelsAction: () => RunModelsCommand(),
                resetAction: ResetAll,
                imageSelectedAction: path => OnImageSelected(path),
                slidersChangedAction: (b, c, s) => AdjustInputImage(b, c, s)
            );

            RankNetVM = new RankNetViewModel(previewPath => HandlePreviewImageChanged(previewPath));
            EfficientDetVM = new EfficientDetD7ViewModel(HandlePreviewImageChanged);
            FinalPredictionVM = new FinalPredictionPaneViewModel();

            MICAControlVM = new MICAControlViewModel(
                pythonService: _python,
                runModelsAction: () => RunModelsCommand(),
                resetAction: ResetAll
            );

            SessionInfoVM = new SessionInfoPaneViewModel();
            SessionInfoVM.EndSessionRequested += OnEndSessionRequested;

            EditorControlsVM = new EditorControlsPaneViewModel();

            // Subscribe to EditorControls events
            EditorControlsVM.CorrectionModeToggled += OnCorrectionModeToggled;
            EditorControlsVM.FeedbackHistoryViewRequested += OnFeedbackHistoryViewRequested;
            EditorControlsVM.FeedbackExportRequested += OnFeedbackExportRequested;
            EditorControlsVM.SessionResetRequested += OnSessionResetRequested;
            EditorControlsVM.DetectionFeedbackProvided += OnDetectionFeedbackProvided;
            EditorControlsVM.EnterEditModeRequested += OnEnterEditModeRequested;
            EditorControlsVM.ExitEditModeRequested += OnExitEditModeRequested;
            EditorControlsVM.PointEditModeChanged += OnPointEditModeChanged;
            EditorControlsVM.SaveChangesRequested += OnSaveChangesRequested;
            EditorControlsVM.RankBrushChanged += OnRankBrushChanged;
        }

        #region Session Management

        private void OnEndSessionRequested(object sender, EventArgs e)
        {
            try
            {
                // Check if there are modifications to save
                if (!FinalPredictionVM.HasAnyModifications)
                {
                    MessageBox.Show(
                        "No modifications have been made to save.",
                        "End Session",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Show confirmation dialog with retraining option
                var result = MessageBox.Show(
                    "Save session modifications?\n\n" +
                    $"✓ Binary Mask: {(FinalPredictionVM.HasModifications ? "Edited" : "Unchanged")}\n" +
                    $"✓ Rank Map: {(FinalPredictionVM.HasAnyModifications ? "Edited" : "Unchanged")}\n\n" +
                    "Queue for LoRA retraining?\n\n" +
                    "Yes = Save & Queue for Retraining\n" +
                    "No = Save Only\n" +
                    "Cancel = Don't Save",
                    "End Session",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes || result == MessageBoxResult.No)
                {
                    // Save modifications
                    SaveAllModificationsRequested?.Invoke(this, EventArgs.Empty);

                    // Queue for retraining if user selected Yes
                    if (result == MessageBoxResult.Yes)
                    {
                        QueueSessionForRetraining();
                    }

                    // End session
                    SessionInfoVM.EndSessionInternal();

                    MessageBox.Show(
                        "Session saved successfully!" +
                        (result == MessageBoxResult.Yes ? "\n\nQueued for LoRA retraining." : ""),
                        "Session Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error ending session:\n{ex.Message}",
                    "Session Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void QueueSessionForRetraining()
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string sessionsDir = Path.Combine(exeDir, "training_sessions");

                // Create directory if it doesn't exist
                Directory.CreateDirectory(sessionsDir);

                string queueFile = Path.Combine(sessionsDir, "retrain_queue.txt");

                // Get the most recent session folder from training_sessions
                var sessionFolders = Directory.GetDirectories(sessionsDir, "session_*")
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToList();

                if (sessionFolders.Count > 0)
                {
                    string latestSession = Path.GetFileName(sessionFolders[0]);

                    // Append to queue file
                    File.AppendAllLines(queueFile, new[] { $"{latestSession}|{DateTime.Now:yyyy-MM-dd HH:mm:ss}" });

                    System.Diagnostics.Debug.WriteLine($"Queued session for retraining: {latestSession}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No session folder found to queue");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error queuing session: {ex.Message}");
            }
        }

        /// <summary>
        /// Update session modification tracking
        /// </summary>
        public void UpdateSessionModifications()
        {
            SessionInfoVM.UpdateModificationStatus(
                FinalPredictionVM.HasModifications,
                FinalPredictionVM.HasAnyModifications
            );
        }

        #endregion

        #region Detection Feedback Event Handlers

        private void OnCorrectionModeToggled(object sender, EventArgs e)
        {
            bool isActive = EditorControlsVM.IsCorrectionModeActive;
            System.Diagnostics.Debug.WriteLine($"Correction mode: {(isActive ? "ACTIVE" : "INACTIVE")}");

            _metricsService.LogInteraction("CorrectionModeToggled", new { Active = isActive });
        }

        private void OnFeedbackHistoryViewRequested(object sender, EventArgs e)
        {
            var feedbackHistory = EditorControlsVM.GetFeedbackHistory();

            MessageBox.Show(
                $"Total feedback items: {feedbackHistory.Count}\n" +
                $"Confirmed: {EditorControlsVM.ConfirmedCount}\n" +
                $"Rejected: {EditorControlsVM.RejectedCount}\n" +
                $"Corrections: {EditorControlsVM.CorrectionCount}",
                "Feedback History",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnFeedbackExportRequested(object sender, EventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".json",
                    FileName = $"feedback_session_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var feedbackHistory = EditorControlsVM.GetFeedbackHistory();
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                        feedbackHistory,
                        Newtonsoft.Json.Formatting.Indented);

                    File.WriteAllText(dialog.FileName, json);

                    MessageBox.Show(
                        $"Feedback exported successfully!\n{feedbackHistory.Count} items saved.",
                        "Export Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error exporting feedback:\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnSessionResetRequested(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset the session?\n\n" +
                "This will clear all:\n" +
                "• Detection feedback\n" +
                "• ROI selections\n" +
                "• Session statistics\n\n" +
                "This action cannot be undone.",
                "Reset Session",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                EditorControlsVM.ClearFeedback();
                _currentDetections.Clear();

                _metricsService.LogInteraction("SessionReset", null);

                MessageBox.Show(
                    "Session reset complete.",
                    "Reset Session",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OnDetectionFeedbackProvided(object sender, DetectionFeedback feedback)
        {
            _metricsService.LogFeedback(feedback.FeedbackType.ToString(), feedback.DetectionId);
        }

        #endregion

        #region Unified Edit Mode Event Handlers

        private void OnEnterEditModeRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Enter edit mode requested");
            EnterEditModeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnExitEditModeRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Exit edit mode requested");
            ExitEditModeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnPointEditModeChanged(object sender, PointEditMode mode)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow: Point edit mode changed to {mode}");
            PointEditModeChanged?.Invoke(this, mode);
        }

        private void OnSaveChangesRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Save changes requested");

            try
            {
                bool hadMaskEdits = FinalPredictionVM.HasModifications;
                bool hadRankEdits = FinalPredictionVM.HasAnyModifications;

                SaveAllModificationsRequested?.Invoke(this, EventArgs.Empty);

                // Update session modification tracking
                UpdateSessionModifications();

                EditorControlsVM.ExitEditMode();

                // Show what was saved
                string savedItems = "";
                if (hadMaskEdits) savedItems += "✓ Binary Mask (polygon)\n";
                if (hadRankEdits) savedItems += "✓ Rank Map (brush)\n";

                if (!string.IsNullOrEmpty(savedItems))
                {
                    MessageBox.Show(
                        $"Changes saved successfully!\n\n{savedItems}",
                        "Save Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error saving changes:\n{ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnRankBrushChanged(object sender, RankBrushEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow: Brush changed - Mode: {e.Mode}, Size: {e.BrushSize}, Strength: {e.BrushStrength}");
            RankBrushChangedRequested?.Invoke(this, e);
        }

        #endregion

        #region Detection Handling

        public void AddDetectionFeedback(DetectionFeedback feedback)
        {
            EditorControlsVM.AddFeedback(feedback);
        }

        public void OnDetectionClicked(DetectionResult detection)
        {
            if (detection == null) return;

            _metricsService.LogDetectionClick(
                detection.Id,
                detection.Label,
                detection.Confidence);

            EditorControlsVM.SelectDetection(detection);
        }

        public List<DetectionResult> GetCurrentDetections()
        {
            return _currentDetections;
        }

        private void ParseDetections(string iaiOutput)
        {
            _currentDetections.Clear();

            if (string.IsNullOrEmpty(iaiOutput))
                return;

            try
            {
                var lines = iaiOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                bool inConsolidatedSection = false;
                int detectionIndex = 0;

                foreach (var line in lines)
                {
                    if (line.Contains("consolidated region"))
                    {
                        inConsolidatedSection = true;
                        continue;
                    }

                    if (inConsolidatedSection && Regex.IsMatch(line.Trim(), @"^\d+\."))
                    {
                        var detection = ParseDetectionLine(line, detectionIndex);
                        if (detection != null)
                        {
                            _currentDetections.Add(detection);
                            detectionIndex++;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Parsed {_currentDetections.Count} detections from IAI output");

                // Auto-select first detection if available
                if (_currentDetections.Count > 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        EditorControlsVM.SelectDetection(_currentDetections[0]);
                        System.Diagnostics.Debug.WriteLine($"Auto-selected detection: {_currentDetections[0].Label}");
                    });
                }
                else if (iaiOutput.Contains("Object present"))
                {
                    var genericDetection = new DetectionResult
                    {
                        Id = Guid.NewGuid().ToString(),
                        Label = "Camouflaged Object",
                        Confidence = ExtractFirstConfidenceFromOutput(iaiOutput),
                        ImagePath = SelectedImagePath,
                        PartCount = 1
                    };

                    _currentDetections.Add(genericDetection);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        EditorControlsVM.SelectDetection(genericDetection);
                        System.Diagnostics.Debug.WriteLine($"Created and selected generic detection");
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing detections: {ex.Message}");
            }
        }

        private double ExtractFirstConfidenceFromOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
                return 0.5;

            try
            {
                var match = Regex.Match(output, @"Confidence:\s*([\d.]+)%");
                if (match.Success)
                {
                    return double.Parse(match.Groups[1].Value) / 100.0;
                }
            }
            catch { }

            return 0.5;
        }

        private DetectionResult ParseDetectionLine(string line, int index)
        {
            try
            {
                var content = Regex.Replace(line, @"^\d+\.\s*", "").Trim();
                var parts = content.Split(',');
                if (parts.Length < 2)
                    return null;

                var label = parts[0].Trim();

                var confidenceMatch = Regex.Match(parts[1], @"([\d.]+)%");
                if (!confidenceMatch.Success)
                    return null;

                var confidence = double.Parse(confidenceMatch.Groups[1].Value) / 100.0;

                int partCount = 1;
                var partCountMatch = Regex.Match(content, @"\((\d+)\s+parts?\)");
                if (partCountMatch.Success)
                {
                    partCount = int.Parse(partCountMatch.Groups[1].Value);
                }

                return new DetectionResult
                {
                    Id = Guid.NewGuid().ToString(),
                    Label = label,
                    Confidence = confidence,
                    PartCount = partCount,
                    ImagePath = SelectedImagePath
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing detection line '{line}': {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Model Execution

        private void OnImageSelected(string path)
        {
            SelectedImagePath = path;
            InputImageVM.LoadImage(path);
            MICAControlVM.OnImageSelected(path);

            _metricsService.StartTask(path);

            // Start session tracking with ORIGINAL filename (not temp adjusted filename)
            string imageName = Path.GetFileName(path);
            SessionInfoVM.StartSession(imageName);

            System.Diagnostics.Debug.WriteLine($"Selected image: {path}");
            System.Diagnostics.Debug.WriteLine($"Session started for: {imageName}");
        }

        private async void RunModelsCommand()
        {
            try
            {
                await RunModelsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RunModelsCommand: {ex.Message}");
                MessageBox.Show(
                    $"An error occurred while running the models:\n{ex.Message}",
                    "Model Execution Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task RunModelsAsync()
        {
            if (IsRunningModels)
                return;

            try
            {
                LogStateChange("Before setting IsRunningModels = true");

                IsRunningModels = true;
                ImageControlVM?.SetButtonStates(true);
                MICAControlVM?.SetButtonStates(true);

                LogStateChange("After setting IsRunningModels = true");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                });

                string imageToProcess = GetImageToUse();

                // DEBUG: Log which image is being processed
                System.Diagnostics.Debug.WriteLine($"=== PROCESSING IMAGE ===");
                System.Diagnostics.Debug.WriteLine($"Original image: {SelectedImagePath}");
                System.Diagnostics.Debug.WriteLine($"Processing: {imageToProcess}");
                System.Diagnostics.Debug.WriteLine($"File exists: {File.Exists(imageToProcess)}");

                string iaiMessage = null;

                _modelExecutionTimer.Restart();

                iaiMessage = await RunPythonModelAsync(imageToProcess);

                _modelExecutionTimer.Stop();
                _metricsService.LogModelRun(_modelExecutionTimer.Elapsed.TotalSeconds);

                ParseDetections(iaiMessage);

                await Task.Run(() => LoadModelOutputs());

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IAIOutputVM.IAIOutputMessage = iaiMessage ?? "Model execution completed.";
                });
            }
            catch (Exception ex)
            {
                LogStateChange("In catch block");
                Console.WriteLine($"Error in RunModelsAsync: {ex.Message}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IAIOutputVM.IAIOutputMessage = $"Error: {ex.Message}";
                });

                throw;
            }
            finally
            {
                LogStateChange("Before setting IsRunningModels = false");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = null;
                });

                IsRunningModels = false;
                ImageControlVM?.SetButtonStates(false);
                MICAControlVM?.SetButtonStates(false);

                LogStateChange("After setting IsRunningModels = false");

                await Task.Delay(100);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ImageControlVM?.UpdateCommandStates();
                    MICAControlVM?.UpdateCommandStates();
                    CommandManager.InvalidateRequerySuggested();
                });
            }
        }

        private async Task<string> RunPythonModelAsync(string imagePath)
        {
            bool useMica = MICAControlVM?.AutoUpdate == true;

            return useMica
                ? await _python.RunIAIModelsWithMICAAsync(imagePath)
                : await _python.RunIAIModelsBypassAsync(imagePath);
        }

        private void LoadModelOutputs()
        {
            if (string.IsNullOrEmpty(SelectedImagePath))
                return;

            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;

                // CRITICAL FIX: Get the filename from the actual processed image
                string processedImagePath = GetImageToUse();
                string selectedName = Path.GetFileNameWithoutExtension(processedImagePath);

                // But keep the original path for session tracking
                string originalPath = SelectedImagePath;

                string offrampsFolderPath = Path.Combine(exeDir, "offramp_output_images");
                string outputsFolderPath = Path.Combine(exeDir, "outputs", selectedName);
                string detectionFolderPath = Path.Combine(exeDir, "detection_results");
                string predictionFolderPath = Path.Combine(exeDir, "results");

                System.Diagnostics.Debug.WriteLine($"Loading outputs for: {selectedName}");
                System.Diagnostics.Debug.WriteLine($"Original image: {originalPath}");
                System.Diagnostics.Debug.WriteLine($"Processed image: {processedImagePath}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    RankNetVM.LoadResults(offrampsFolderPath, outputsFolderPath);
                    EfficientDetVM.LoadResults(detectionFolderPath, outputsFolderPath, selectedName);

                    // Pass the ORIGINAL image path for session tracking, not the temp adjusted path
                    FinalPredictionVM.LoadResult(predictionFolderPath, selectedName, originalPath);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading model outputs: {ex.Message}");
            }
        }

        private void LogStateChange(string context)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {context}:");
            System.Diagnostics.Debug.WriteLine($"  IsRunningModels: {IsRunningModels}");
            System.Diagnostics.Debug.WriteLine($"  IsNotRunningModels: {IsNotRunningModels}");
        }

        #endregion

        #region Image Adjustment

        private void AdjustInputImage(int brightness, int contrast, int saturation)
        {
            if (string.IsNullOrEmpty(SelectedImagePath))
                return;

            try
            {
                BitmapImage original = _imageService.LoadBitmapFully(SelectedImagePath);
                BitmapImage adjusted = _imageService.AdjustImage(original, brightness, contrast, saturation);

                if (adjusted == null)
                    return;

                _latestAdjustedImagePath = _imageService.SaveBitmapToTemp(adjusted);

                InputImageVM.InputImage = new BitmapImage(new Uri(_latestAdjustedImagePath));

                _metricsService.LogParameterChange("ImageAdjustment",
                    null,
                    new { Brightness = brightness, Contrast = contrast, Saturation = saturation });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adjusting image: {ex.Message}");
            }
        }

        private string GetImageToUse()
        {
            if (!string.IsNullOrEmpty(_latestAdjustedImagePath) && File.Exists(_latestAdjustedImagePath))
                return _latestAdjustedImagePath;

            return SelectedImagePath;
        }

        public void HandlePreviewImageChanged(string imagePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PreviewPaneVM.PreviewImage = new BitmapImage(new Uri(imagePath));
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating preview: {ex.Message}");
            }
        }

        #endregion

        #region Reset and Cleanup

        private void ResetAll()
        {
            SelectedImagePath = string.Empty;
            _latestAdjustedImagePath = null;

            InputImageVM.InputImage = null;
            IAIOutputVM.IAIOutputMessage = string.Empty;
            ImageControlVM.ExecuteResetCommand();
            ImageControlVM.SelectedImagePath = string.Empty;

            RankNetVM.Clear();
            EfficientDetVM.Clear();
            FinalPredictionVM.Clear();
            PreviewPaneVM.ClearPreview();

            _currentDetections.Clear();

            EditorControlsVM.ClearSelection();
            EditorControlsVM.CurrentDetections?.Clear();

            // Exit edit mode before resetting drawing
            if (EditorControlsVM.IsEditModeActive)
            {
                EditorControlsVM.ExitEditMode();
            }

            // End session if active
            if (SessionInfoVM.HasActiveSession)
            {
                SessionInfoVM.EndSessionInternal();
            }

            ResetDrawingRequested?.Invoke(this, EventArgs.Empty);

            ImageControlVM?.SetButtonStates(false);
            MICAControlVM?.SetButtonStates(false);

            ImageControlVM?.UpdateCommandStates();
            MICAControlVM?.UpdateCommandStates();
            EditorControlsVM?.UpdateCommandStates();
            CommandManager.InvalidateRequerySuggested();
        }

        public void Dispose()
        {
            _metricsService?.EndSession();
            (_python as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Metrics Export

        public void ExportPerformanceMetrics()
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = ".json",
                    FileName = $"metrics_session_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    if (saveDialog.FileName.EndsWith(".csv"))
                    {
                        _metricsService.ExportCSV(saveDialog.FileName);
                    }
                    else
                    {
                        _metricsService.ExportMetrics(saveDialog.FileName);
                    }

                    var report = _metricsService.GenerateReport();

                    MessageBox.Show(
                        $"Metrics exported successfully!\n\n" +
                        $"Session Duration: {TimeSpan.FromSeconds(report.SessionDuration):hh\\:mm\\:ss}\n" +
                        $"Total Tasks: {report.TotalTasks}\n" +
                        $"Total Interactions: {report.TotalInteractions}\n" +
                        $"Avg Task Duration: {report.AverageTaskDuration:F2}s\n" +
                        $"Avg Model Execution: {report.AverageModelExecutionTime:F2}s\n" +
                        $"Feedback Actions: {report.FeedbackActions}",
                        "Export Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error exporting metrics:\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion
    }
}