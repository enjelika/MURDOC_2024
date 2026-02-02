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
        private bool _hasAdjustedImage;
        private BitmapImage _adjustedInputImage;

        // Performance metrics
        private PerformanceMetricsService _metricsService;
        private Stopwatch _modelExecutionTimer;

        // Detection tracking
        private List<DetectionResult> _currentDetections;

        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set => SetProperty(ref _selectedImagePath, value);
        }

        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
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

        public event EventHandler PolygonModeRequested;
        public event EventHandler FreehandModeRequested;
        public event EventHandler ClearROIsRequested;
        public event EventHandler<string> ROIMaskExportRequested;

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

            EditorControlsVM = new EditorControlsPaneViewModel();

            // Subscribe to EditorControls events
            EditorControlsVM.CorrectionModeToggled += OnCorrectionModeToggled;
            EditorControlsVM.FeedbackHistoryViewRequested += OnFeedbackHistoryViewRequested;
            EditorControlsVM.FeedbackExportRequested += OnFeedbackExportRequested;
            EditorControlsVM.SessionResetRequested += OnSessionResetRequested;
            EditorControlsVM.DetectionFeedbackProvided += OnDetectionFeedbackProvided;
            EditorControlsVM.PolygonModeRequested += OnPolygonModeRequested;
            EditorControlsVM.FreehandModeRequested += OnFreehandModeRequested;
            EditorControlsVM.ClearROIsRequested += OnClearROIsRequested;
            EditorControlsVM.ExportROIMasksRequested += OnExportROIMasksRequested;
        }

        #region EditorControls Event Handlers

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
            // Log feedback to metrics
            _metricsService.LogFeedback(feedback.FeedbackType.ToString(), feedback.DetectionId);
        }

        private void OnPolygonModeRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Polygon mode requested");

            // Notify the view to enable polygon drawing on FinalPredictionPane
            PolygonModeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnFreehandModeRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Freehand mode requested");

            // Notify the view to enable freehand drawing on FinalPredictionPane
            FreehandModeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnClearROIsRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Clear ROIs requested");

            // Notify the view to clear all drawing
            ClearROIsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnExportROIMasksRequested(object sender, EventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG files (*.png)|*.png|All files (*.*)|*.*",
                    DefaultExt = ".png",
                    FileName = $"roi_mask_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };

                if (dialog.ShowDialog() == true)
                {
                    // Export current mask from FinalPredictionPane
                    ROIMaskExportRequested?.Invoke(this, dialog.FileName);

                    MessageBox.Show(
                        "ROI mask exported successfully!",
                        "Export Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error exporting ROI mask:\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Detection Handling

        public void AddDetectionFeedback(DetectionFeedback feedback)
        {
            EditorControlsVM.AddFeedback(feedback);
        }

        /// <summary>
        /// Handle detection clicks from FinalPredictionPane
        /// </summary>
        public void OnDetectionClicked(DetectionResult detection)
        {
            if (detection == null) return;

            // Log the click
            _metricsService.LogDetectionClick(
                detection.Id,
                detection.Label,
                detection.Confidence);

            // Update editor controls
            EditorControlsVM.SelectDetection(detection);
        }

        /// <summary>
        /// Parse consolidated detections from IAI output and auto-select the first one
        /// </summary>
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
                    // Look for consolidated regions section
                    if (line.Contains("consolidated region"))
                    {
                        inConsolidatedSection = true;
                        continue;
                    }

                    // Parse detection lines (format: "1. Object (leg), Confidence: 70.98% (2 parts)")
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

                // AUTO-SELECT: If we have any detections, auto-select the first one (highest confidence)
                if (_currentDetections.Count > 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        EditorControlsVM.SelectDetection(_currentDetections[0]);
                        System.Diagnostics.Debug.WriteLine($"Auto-selected detection: {_currentDetections[0].Label}");
                    });
                }
                else
                {
                    // No detections parsed - create a generic one from "Object present" message
                    if (iaiOutput.Contains("Object present"))
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing detections: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract first confidence value from output (fallback method)
        /// </summary>
        private double ExtractFirstConfidenceFromOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
                return 0.5;

            try
            {
                // Look for patterns like "Confidence: 70.98%"
                var match = Regex.Match(output, @"Confidence:\s*([\d.]+)%");
                if (match.Success)
                {
                    return double.Parse(match.Groups[1].Value) / 100.0;
                }
            }
            catch { }

            return 0.5; // Default fallback
        }

        /// <summary>
        /// Parse a single detection line
        /// Format: "1. Object (leg), Confidence: 70.98% (2 parts)"
        /// </summary>
        private DetectionResult ParseDetectionLine(string line, int index)
        {
            try
            {
                // Remove leading number and dot
                var content = Regex.Replace(line, @"^\d+\.\s*", "").Trim();

                // Split by comma
                var parts = content.Split(',');
                if (parts.Length < 2)
                    return null;

                // Extract label (e.g., "Object (leg)")
                var label = parts[0].Trim();

                // Extract confidence (e.g., "Confidence: 70.98%")
                var confidenceMatch = Regex.Match(parts[1], @"([\d.]+)%");
                if (!confidenceMatch.Success)
                    return null;

                var confidence = double.Parse(confidenceMatch.Groups[1].Value) / 100.0;

                // Extract part count if present (e.g., "(2 parts)")
                int partCount = 1;
                var partCountMatch = Regex.Match(content, @"\((\d+)\s+parts?\)");
                if (partCountMatch.Success)
                {
                    partCount = int.Parse(partCountMatch.Groups[1].Value);
                }

                // Extract parts breakdown from next line if available
                // (This would require reading ahead in the parsing logic)

                var detection = new DetectionResult
                {
                    Id = Guid.NewGuid().ToString(),
                    Label = label,
                    Confidence = confidence,
                    PartCount = partCount,
                    ImagePath = SelectedImagePath,
                    // Note: BoundingBox coordinates would need to come from
                    // a separate JSON file or modified Python output
                    // For now, we'll work with just the label and confidence
                };

                return detection;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing detection line '{line}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get current detections (for UI binding or other uses)
        /// </summary>
        public List<DetectionResult> GetCurrentDetections()
        {
            return _currentDetections;
        }

        #endregion

        #region Metrics Export

        /// <summary>
        /// Export performance metrics
        /// </summary>
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

                    // Also generate and display report
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

        private void OnImageSelected(string path)
        {
            SelectedImagePath = path;
            InputImageVM.LoadImage(path);
            MICAControlVM.OnImageSelected(path);

            // Track task start
            _metricsService.StartTask(path);
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

        private void LogStateChange(string context)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {context}:");
            System.Diagnostics.Debug.WriteLine($"  IsRunningModels: {IsRunningModels}");
            System.Diagnostics.Debug.WriteLine($"  IsNotRunningModels: {IsNotRunningModels}");
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
                string iaiMessage = null;

                // Start timing model execution
                _modelExecutionTimer.Restart();

                // Run Python model
                iaiMessage = await RunPythonModelAsync(imageToProcess);

                // Stop timing
                _modelExecutionTimer.Stop();
                _metricsService.LogModelRun(_modelExecutionTimer.Elapsed.TotalSeconds);

                // Parse detections from output
                ParseDetections(iaiMessage);

                // Load outputs
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

        private void ResetAll()
        {
            SelectedImagePath = string.Empty;
            _adjustedInputImage = null;
            _hasAdjustedImage = false;
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

            // Clear the editor state
            EditorControlsVM.ClearSelection();
            EditorControlsVM.CurrentDetections?.Clear();

            ImageControlVM?.SetButtonStates(false);
            MICAControlVM?.SetButtonStates(false);

            ImageControlVM?.UpdateCommandStates();
            MICAControlVM?.UpdateCommandStates();
            EditorControlsVM?.UpdateCommandStates();
            CommandManager.InvalidateRequerySuggested();
        }

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
                _adjustedInputImage = adjusted;
                _hasAdjustedImage = true;

                InputImageVM.InputImage = new BitmapImage(new Uri(_latestAdjustedImagePath));

                // Log parameter changes
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

        private void LoadModelOutputs()
        {
            if (string.IsNullOrEmpty(SelectedImagePath))
                return;

            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string selectedName = Path.GetFileNameWithoutExtension(SelectedImagePath);

                string offrampsFolderPath = Path.Combine(exeDir, "offramp_output_images");
                string outputsFolderPath = Path.Combine(exeDir, "outputs", selectedName);
                string detectionFolderPath = Path.Combine(exeDir, "detection_results");
                string predictionFolderPath = Path.Combine(exeDir, "results");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    RankNetVM.LoadResults(offrampsFolderPath, outputsFolderPath);
                    EfficientDetVM.LoadResults(detectionFolderPath, outputsFolderPath, selectedName);

                    // Pass the original image path so it can be found
                    FinalPredictionVM.LoadResult(predictionFolderPath, selectedName, SelectedImagePath);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading model outputs: {ex.Message}");
            }
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

        public void Dispose()
        {
            _metricsService?.EndSession();
            (_python as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}