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
    /// <summary>
    /// Main application ViewModel coordinating all child ViewModels and managing
    /// the core workflow: image selection → model execution → detection feedback → editing.
    /// Implements the MICA (Mixed-Initiative Camouflage Analysis) prototype for user study.
    /// </summary>
    public class MainWindowViewModel : ViewModelBase, IDisposable
    {
        #region Private Fields

        private string _selectedImagePath;
        private BitmapImage _previewImage;
        private readonly ImageService _imageService;
        private readonly PythonModelService _python;
        private bool _isRunningModels;
        private string _latestAdjustedImagePath;

        // Performance metrics tracking
        private PerformanceMetricsService _metricsService;
        private Stopwatch _modelExecutionTimer;

        // Detection tracking for current image
        private List<DetectionResult> _currentDetections;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the path to the currently selected image file.
        /// </summary>
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set => SetProperty(ref _selectedImagePath, value);
        }

        /// <summary>
        /// Gets or sets whether models are currently executing.
        /// When true, most UI controls are disabled to prevent interference.
        /// </summary>
        public bool IsRunningModels
        {
            get => _isRunningModels;
            set
            {
                if (SetProperty(ref _isRunningModels, value))
                {
                    OnPropertyChanged(nameof(IsNotRunningModels));

                    // Update all child ViewModels' command states
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

        /// <summary>
        /// Inverse of IsRunningModels, for binding to UI elements that should be enabled when NOT running.
        /// </summary>
        public bool IsNotRunningModels => !IsRunningModels;

        // Child ViewModels - each manages a specific UI panel
        public ImageControlViewModel ImageControlVM { get; }          // Image selection and adjustment
        public InputImagePaneViewModel InputImageVM { get; }          // Input image display
        public PreviewPaneViewModel PreviewPaneVM { get; }            // Preview pane display
        public IAIOutputPaneViewModel IAIOutputVM { get; }            // Text output from AI models
        public RankNetViewModel RankNetVM { get; }                    // RankNet model results
        public EfficientDetD7ViewModel EfficientDetVM { get; }        // EfficientDet model results
        public FinalPredictionPaneViewModel FinalPredictionVM { get; } // Combined prediction with editing
        public MICAControlViewModel MICAControlVM { get; }            // MICA detection parameters
        public EditorControlsPaneViewModel EditorControlsVM { get; }  // Detection feedback and editing
        public SessionInfoPaneViewModel SessionInfoVM { get; }        // Session tracking

        #endregion

        #region Events

        // Unified edit mode events
        public event EventHandler ResetDrawingRequested;
        public event EventHandler EnterEditModeRequested;
        public event EventHandler ExitEditModeRequested;
        public event EventHandler<PointEditMode> PointEditModeChanged;
        public event EventHandler SaveAllModificationsRequested;
        public event EventHandler<RankBrushEventArgs> RankBrushChangedRequested;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the MainWindowViewModel and all child ViewModels.
        /// Sets up event subscriptions and initializes services.
        /// </summary>
        public MainWindowViewModel()
        {
            // Initialize Python service with default detection parameters
            _python = new PythonModelService();
            _python.SetDetectionParameters(1.5, 0.0);  // Default: d'=1.5, β=0.0

            _imageService = new ImageService();

            // Initialize performance metrics tracking
            _metricsService = new PerformanceMetricsService();
            _metricsService.StartSession();
            _modelExecutionTimer = new Stopwatch();

            _currentDetections = new List<DetectionResult>();

            // Create child ViewModels
            InputImageVM = new InputImagePaneViewModel();
            PreviewPaneVM = new PreviewPaneViewModel();
            IAIOutputVM = new IAIOutputPaneViewModel();

            // Image control with callbacks for all user actions
            ImageControlVM = new ImageControlViewModel(
                runModelsAction: () => RunModelsCommand(),
                resetAction: ResetAll,
                imageSelectedAction: path => OnImageSelected(path),
                slidersChangedAction: (b, c, s) => AdjustInputImage(b, c, s)
            );

            // Model result ViewModels
            RankNetVM = new RankNetViewModel(previewPath => HandlePreviewImageChanged(previewPath));
            EfficientDetVM = new EfficientDetD7ViewModel(HandlePreviewImageChanged);
            FinalPredictionVM = new FinalPredictionPaneViewModel();

            // MICA control panel
            MICAControlVM = new MICAControlViewModel(
                pythonService: _python,
                runModelsAction: () => RunModelsCommand(),
                resetAction: ResetAll
            );

            // Session tracking
            SessionInfoVM = new SessionInfoPaneViewModel();
            SessionInfoVM.EndSessionRequested += OnEndSessionRequested;

            // Detection feedback and editing controls
            EditorControlsVM = new EditorControlsPaneViewModel();

            // Subscribe to all EditorControls events
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

        #endregion

        #region Session Management

        /// <summary>
        /// Handles the end session request from SessionInfoVM.
        /// Saves comprehensive session summary JSON and optionally queues for LoRA retraining.
        /// </summary>
        private void OnEndSessionRequested(object sender, EventArgs e)
        {
            try
            {
                if (!SessionInfoVM.HasActiveSession)
                {
                    MessageBox.Show(
                        "No active session to end.",
                        "End Session",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Get complete session data
                var summary = SessionInfoVM.GetSessionSummary();

                // Confirm with user and ask about retraining
                var result = MessageBox.Show(
                    $"End session and save summary?\n\n" +
                    $"Session Duration: {summary.TotalDuration:hh\\:mm\\:ss}\n" +
                    $"Images Analyzed: {summary.ImagesAnalyzed}\n\n" +
                    $"Queue for LoRA retraining?\n\n" +
                    $"Yes = Save & Queue for Retraining\n" +
                    $"No = Save Summary Only\n" +
                    $"Cancel = Don't End Session",
                    "End Session",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes || result == MessageBoxResult.No)
                {
                    // Save session summary JSON with all image data and parameters
                    string sessionId = summary.SessionStartTime.ToString("yyyyMMdd_HHmmss");
                    SaveSessionSummary(summary, sessionId);

                    // Queue for LoRA retraining if requested
                    if (result == MessageBoxResult.Yes)
                    {
                        QueueSessionForRetraining(sessionId);
                    }

                    // End the session (clears tracking data)
                    SessionInfoVM.EndSessionInternal();

                    MessageBox.Show(
                        $"Session saved successfully!\n\n" +
                        $"Duration: {summary.TotalDuration:hh\\:mm\\:ss}\n" +
                        $"Images: {summary.ImagesAnalyzed}\n" +
                        (result == MessageBoxResult.Yes ? "\nQueued for LoRA retraining." : ""),
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

        /// <summary>
        /// Saves comprehensive session summary as JSON file.
        /// Includes: session metadata, statistics, and per-image data with all parameters.
        /// </summary>
        /// <param name="summary">Session summary data from SessionInfoVM</param>
        /// <param name="sessionId">Unique session identifier (yyyyMMdd_HHmmss format)</param>
        private void SaveSessionSummary(SessionSummaryData summary, string sessionId)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string sessionsDir = Path.Combine(exeDir, "training_sessions");
                Directory.CreateDirectory(sessionsDir);

                string sessionFile = Path.Combine(sessionsDir, $"session_{sessionId}_summary.json");

                // Create comprehensive summary object for JSON serialization
                var summaryObject = new
                {
                    SessionId = sessionId,
                    StartTime = summary.SessionStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    EndTime = summary.SessionEndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Duration = new
                    {
                        TotalSeconds = summary.TotalDuration.TotalSeconds,
                        Formatted = $"{summary.TotalDuration:hh\\:mm\\:ss}"
                    },

                    // Aggregate statistics across all images
                    Statistics = new
                    {
                        TotalImagesAnalyzed = summary.ImagesAnalyzed,
                        ImagesWithBinaryEdits = summary.Images.Count(i => i.BinaryMaskEdited),
                        ImagesWithRankEdits = summary.Images.Count(i => i.RankMapEdited),
                        TotalConfirmed = summary.Images.Sum(i => i.ConfirmedDetections),
                        TotalRejected = summary.Images.Sum(i => i.RejectedDetections),
                        TotalCorrections = summary.Images.Sum(i => i.CorrectionsMade)
                    },

                    // Per-image data with all parameters and actions
                    Images = summary.Images.Select(img => new
                    {
                        ImageName = img.ImageName,
                        AnalyzedAt = img.AnalyzedAt.ToString("yyyy-MM-dd HH:mm:ss"),

                        // Image adjustment parameters (brightness, contrast, saturation)
                        ImageAdjustments = new
                        {
                            Brightness = img.Brightness,
                            Contrast = img.Contrast,
                            Saturation = img.Saturation
                        },

                        // Detection parameters (sensitivity d', response bias β)
                        DetectionParameters = new
                        {
                            Sensitivity_dPrime = img.Sensitivity,
                            ResponseBias_Beta = img.ResponseBias
                        },

                        // What modifications were made
                        Modifications = new
                        {
                            BinaryMaskEdited = img.BinaryMaskEdited,
                            RankMapEdited = img.RankMapEdited
                        },

                        // User feedback on detections
                        DetectionFeedback = new
                        {
                            Confirmed = img.ConfirmedDetections,
                            Rejected = img.RejectedDetections,
                            Corrections = img.CorrectionsMade,
                            Total = img.ConfirmedDetections + img.RejectedDetections + img.CorrectionsMade
                        }
                    }).ToList()
                };

                // Serialize to formatted JSON
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                    summaryObject,
                    Newtonsoft.Json.Formatting.Indented);

                File.WriteAllText(sessionFile, json);

                System.Diagnostics.Debug.WriteLine($"Saved session summary: {sessionFile}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving session summary: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Adds session to retraining queue for LoRA model updates.
        /// Creates/appends to retrain_queue.txt with session ID and timestamp.
        /// </summary>
        /// <param name="sessionId">Session identifier to queue</param>
        private void QueueSessionForRetraining(string sessionId)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string sessionsDir = Path.Combine(exeDir, "training_sessions");
                Directory.CreateDirectory(sessionsDir);

                string queueFile = Path.Combine(sessionsDir, "retrain_queue.txt");

                // Append session to queue with timestamp
                File.AppendAllLines(queueFile, new[] { $"session_{sessionId}|{DateTime.Now:yyyy-MM-dd HH:mm:ss}" });

                System.Diagnostics.Debug.WriteLine($"Queued session for retraining: session_{sessionId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error queuing session: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates session tracking with current modification status.
        /// Called after saving changes to keep UI synchronized.
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

        /// <summary>
        /// Handles correction mode toggle from EditorControls.
        /// Logs interaction for metrics tracking.
        /// </summary>
        private void OnCorrectionModeToggled(object sender, EventArgs e)
        {
            bool isActive = EditorControlsVM.IsCorrectionModeActive;
            System.Diagnostics.Debug.WriteLine($"Correction mode: {(isActive ? "ACTIVE" : "INACTIVE")}");

            _metricsService.LogInteraction("CorrectionModeToggled", new { Active = isActive });
        }

        /// <summary>
        /// Displays feedback history summary dialog.
        /// Shows counts of confirmed, rejected, and corrected detections.
        /// </summary>
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

        /// <summary>
        /// Exports detection feedback history to JSON file.
        /// Includes all feedback items with timestamps and detection details.
        /// </summary>
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

        /// <summary>
        /// Handles session reset request with confirmation dialog.
        /// Clears all detection feedback and session statistics.
        /// </summary>
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

        /// <summary>
        /// Logs detection feedback to metrics service.
        /// Called when user confirms, rejects, or corrects a detection.
        /// </summary>
        private void OnDetectionFeedbackProvided(object sender, DetectionFeedback feedback)
        {
            _metricsService.LogFeedback(feedback.FeedbackType.ToString(), feedback.DetectionId);
        }

        #endregion

        #region Unified Edit Mode Event Handlers

        /// <summary>
        /// Forwards enter edit mode request to MainWindow for UI update.
        /// </summary>
        private void OnEnterEditModeRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Enter edit mode requested");
            EnterEditModeRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Forwards exit edit mode request to MainWindow for UI update.
        /// </summary>
        private void OnExitEditModeRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Exit edit mode requested");
            ExitEditModeRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Forwards point edit mode change (Add/Remove) to MainWindow.
        /// </summary>
        private void OnPointEditModeChanged(object sender, PointEditMode mode)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow: Point edit mode changed to {mode}");
            PointEditModeChanged?.Invoke(this, mode);
        }

        /// <summary>
        /// Handles save changes request from unified edit mode.
        /// Saves both polygon (binary mask) and rank map modifications.
        /// Records all parameters and feedback for session summary.
        /// </summary>
        private void OnSaveChangesRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Save changes requested");

            try
            {
                bool hadMaskEdits = FinalPredictionVM.HasModifications;
                bool hadRankEdits = FinalPredictionVM.HasAnyModifications;

                // Trigger save on FinalPredictionPane (saves to LoRA training folders)
                SaveAllModificationsRequested?.Invoke(this, EventArgs.Empty);

                // Update session modification tracking
                UpdateSessionModifications();

                // Record complete image data for session summary
                string imageName = Path.GetFileName(SelectedImagePath);
                SessionInfoVM.RecordImageData(
                    imageName,
                    FinalPredictionVM.HasModifications,      // Binary mask edited
                    FinalPredictionVM.HasAnyModifications,   // Rank map edited
                    EditorControlsVM.ConfirmedCount,         // Detection feedback
                    EditorControlsVM.RejectedCount,
                    EditorControlsVM.CorrectionCount,
                    ImageControlVM.Brightness,               // Image adjustment parameters
                    ImageControlVM.Contrast,
                    ImageControlVM.Saturation,
                    MICAControlVM.Sensitivity,               // Detection parameters
                    MICAControlVM.ResponseBias
                );

                // Exit edit mode after successful save
                EditorControlsVM.ExitEditMode();

                // Show confirmation with what was saved
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

        /// <summary>
        /// Forwards rank brush parameter changes to FinalPredictionPane.
        /// Updates brush mode, size, and strength in real-time.
        /// </summary>
        private void OnRankBrushChanged(object sender, RankBrushEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow: Brush changed - Mode: {e.Mode}, Size: {e.BrushSize}, Strength: {e.BrushStrength}");
            RankBrushChangedRequested?.Invoke(this, e);
        }

        #endregion

        #region Detection Handling

        /// <summary>
        /// Adds detection feedback to EditorControls tracking.
        /// </summary>
        public void AddDetectionFeedback(DetectionFeedback feedback)
        {
            EditorControlsVM.AddFeedback(feedback);
        }

        /// <summary>
        /// Handles detection click from FinalPredictionPane.
        /// Logs interaction and selects detection in EditorControls.
        /// </summary>
        public void OnDetectionClicked(DetectionResult detection)
        {
            if (detection == null) return;

            _metricsService.LogDetectionClick(
                detection.Id,
                detection.Label,
                detection.Confidence);

            EditorControlsVM.SelectDetection(detection);
        }

        /// <summary>
        /// Returns list of detections for current image.
        /// </summary>
        public List<DetectionResult> GetCurrentDetections()
        {
            return _currentDetections;
        }

        /// <summary>
        /// Parses detection output from Python AI models.
        /// Extracts consolidated detection regions with confidence scores.
        /// Auto-selects first detection for user feedback.
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
                    // Look for consolidated regions section in output
                    if (line.Contains("consolidated region"))
                    {
                        inConsolidatedSection = true;
                        continue;
                    }

                    // Parse numbered detection lines (format: "1. Object (type), Confidence: XX%")
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

                // Auto-select first detection (highest confidence)
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
                    // Fallback: create generic detection if no consolidated regions found
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

        /// <summary>
        /// Extracts first confidence value from AI output as fallback.
        /// Used when consolidated detections aren't found.
        /// </summary>
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

            return 0.5; // Default fallback
        }

        /// <summary>
        /// Parses a single detection line from AI output.
        /// Format: "1. Object (leg), Confidence: 70.98% (2 parts)"
        /// </summary>
        private DetectionResult ParseDetectionLine(string line, int index)
        {
            try
            {
                // Remove leading number and dot
                var content = Regex.Replace(line, @"^\d+\.\s*", "").Trim();
                var parts = content.Split(',');
                if (parts.Length < 2)
                    return null;

                var label = parts[0].Trim();

                // Extract confidence percentage
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

        /// <summary>
        /// Handles image selection event.
        /// Starts or continues session tracking.
        /// </summary>
        private void OnImageSelected(string path)
        {
            SelectedImagePath = path;
            InputImageVM.LoadImage(path);
            MICAControlVM.OnImageSelected(path);

            _metricsService.StartTask(path);

            string imageName = Path.GetFileName(path);

            // Start new session or continue existing one
            if (!SessionInfoVM.HasActiveSession)
            {
                SessionInfoVM.StartSession(imageName);
                System.Diagnostics.Debug.WriteLine($"Started new session for: {imageName}");
            }
            else
            {
                // Continue session with new image
                SessionInfoVM.CurrentImageName = imageName;
                SessionInfoVM.UpdateModificationStatus(false, false); // Reset modification flags
                System.Diagnostics.Debug.WriteLine($"Continuing session with new image: {imageName}");
            }
        }

        /// <summary>
        /// Command wrapper for async model execution.
        /// Catches and displays any errors that occur.
        /// </summary>
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

        /// <summary>
        /// Executes AI models asynchronously on selected image.
        /// Workflow: Set UI state → Run Python models → Parse results → Load outputs → Update UI
        /// Tracks execution time for performance metrics.
        /// </summary>
        private async Task RunModelsAsync()
        {
            if (IsRunningModels)
                return;

            try
            {
                LogStateChange("Before setting IsRunningModels = true");

                // Disable UI controls during execution
                IsRunningModels = true;
                ImageControlVM?.SetButtonStates(true);
                MICAControlVM?.SetButtonStates(true);

                LogStateChange("After setting IsRunningModels = true");

                // Show wait cursor
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                });

                // Get image to process (original or adjusted)
                string imageToProcess = GetImageToUse();
                string iaiMessage = null;

                // Start performance timing
                _modelExecutionTimer.Restart();

                // Run Python AI models
                iaiMessage = await RunPythonModelAsync(imageToProcess);

                // Stop timing and log
                _modelExecutionTimer.Stop();
                _metricsService.LogModelRun(_modelExecutionTimer.Elapsed.TotalSeconds);

                // Parse detection results from output
                ParseDetections(iaiMessage);

                // Load all model output images
                await Task.Run(() => LoadModelOutputs());

                // Update UI with results
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IAIOutputVM.IAIOutputMessage = iaiMessage ?? "Model execution completed.";

                    // Increment session image counter
                    SessionInfoVM.IncrementImageCount();
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

                // Restore normal cursor
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = null;
                });

                // Re-enable UI controls
                IsRunningModels = false;
                ImageControlVM?.SetButtonStates(false);
                MICAControlVM?.SetButtonStates(false);

                LogStateChange("After setting IsRunningModels = false");

                // Small delay to ensure UI updates
                await Task.Delay(100);

                // Force command state refresh
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ImageControlVM?.UpdateCommandStates();
                    MICAControlVM?.UpdateCommandStates();
                    CommandManager.InvalidateRequerySuggested();
                });
            }
        }

        /// <summary>
        /// Runs Python model with or without MICA parameters.
        /// Routes to appropriate Python service method.
        /// </summary>
        private async Task<string> RunPythonModelAsync(string imagePath)
        {
            bool useMica = MICAControlVM?.AutoUpdate == true;

            return useMica
                ? await _python.RunIAIModelsWithMICAAsync(imagePath)
                : await _python.RunIAIModelsBypassAsync(imagePath);
        }

        /// <summary>
        /// Loads all model output images from file system.
        /// Handles both original and adjusted image filenames.
        /// Updates RankNet, EfficientDet, and FinalPrediction ViewModels.
        /// </summary>
        private void LoadModelOutputs()
        {
            if (string.IsNullOrEmpty(SelectedImagePath))
                return;

            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;

                // CRITICAL: Use processed image filename (may be temp adjusted file)
                string processedImagePath = GetImageToUse();
                string selectedName = Path.GetFileNameWithoutExtension(processedImagePath);

                // Keep original path for session tracking
                string originalPath = SelectedImagePath;

                // Build output folder paths
                string offrampsFolderPath = Path.Combine(exeDir, "offramp_output_images");
                string outputsFolderPath = Path.Combine(exeDir, "outputs", selectedName);
                string detectionFolderPath = Path.Combine(exeDir, "detection_results");
                string predictionFolderPath = Path.Combine(exeDir, "results");

                System.Diagnostics.Debug.WriteLine($"Loading outputs for: {selectedName}");
                System.Diagnostics.Debug.WriteLine($"Original image: {originalPath}");
                System.Diagnostics.Debug.WriteLine($"Processed image: {processedImagePath}");

                // Load results into child ViewModels
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RankNetVM.LoadResults(offrampsFolderPath, outputsFolderPath);
                    EfficientDetVM.LoadResults(detectionFolderPath, outputsFolderPath, selectedName);

                    // Pass original path for session tracking (not temp adjusted path)
                    FinalPredictionVM.LoadResult(predictionFolderPath, selectedName, originalPath);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading model outputs: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs state changes during model execution for debugging.
        /// </summary>
        private void LogStateChange(string context)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {context}:");
            System.Diagnostics.Debug.WriteLine($"  IsRunningModels: {IsRunningModels}");
            System.Diagnostics.Debug.WriteLine($"  IsNotRunningModels: {IsNotRunningModels}");
        }

        #endregion

        #region Image Adjustment

        /// <summary>
        /// Adjusts image brightness, contrast, and saturation.
        /// Saves adjusted image to temp file for processing.
        /// Logs parameter changes to metrics service.
        /// </summary>
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

                // Save to temp file
                _latestAdjustedImagePath = _imageService.SaveBitmapToTemp(adjusted);

                // Update display
                InputImageVM.InputImage = new BitmapImage(new Uri(_latestAdjustedImagePath));

                // Log parameter change
                _metricsService.LogParameterChange("ImageAdjustment",
                    null,
                    new { Brightness = brightness, Contrast = contrast, Saturation = saturation });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adjusting image: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the image path to use for processing.
        /// Returns adjusted image if available, otherwise original.
        /// </summary>
        private string GetImageToUse()
        {
            if (!string.IsNullOrEmpty(_latestAdjustedImagePath) && File.Exists(_latestAdjustedImagePath))
                return _latestAdjustedImagePath;

            return SelectedImagePath;
        }

        /// <summary>
        /// Updates preview pane when RankNet or EfficientDet selects a new image.
        /// </summary>
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

        /// <summary>
        /// Resets entire application state.
        /// Clears all images, results, detections, and UI state.
        /// Note: Does NOT end the session (session persists across resets).
        /// </summary>
        private void ResetAll()
        {
            SelectedImagePath = string.Empty;
            _latestAdjustedImagePath = null;

            // Clear all ViewModels
            InputImageVM.InputImage = null;
            IAIOutputVM.IAIOutputMessage = string.Empty;
            ImageControlVM.ExecuteResetCommand();
            ImageControlVM.SelectedImagePath = string.Empty;

            RankNetVM.Clear();
            EfficientDetVM.Clear();
            FinalPredictionVM.Clear();
            PreviewPaneVM.ClearPreview();

            _currentDetections.Clear();

            // Clear editor state
            EditorControlsVM.ClearSelection();
            EditorControlsVM.CurrentDetections?.Clear();

            // Exit edit mode if active
            if (EditorControlsVM.IsEditModeActive)
            {
                EditorControlsVM.ExitEditMode();
            }

            // Notify FinalPredictionPane to clear drawing
            ResetDrawingRequested?.Invoke(this, EventArgs.Empty);

            // Update button states
            ImageControlVM?.SetButtonStates(false);
            MICAControlVM?.SetButtonStates(false);

            ImageControlVM?.UpdateCommandStates();
            MICAControlVM?.UpdateCommandStates();
            EditorControlsVM?.UpdateCommandStates();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Disposes of services and closes metrics session.
        /// Called when application closes.
        /// </summary>
        public void Dispose()
        {
            _metricsService?.EndSession();
            (_python as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Metrics Export

        /// <summary>
        /// Exports performance metrics to JSON or CSV file.
        /// Includes session duration, task counts, and interaction statistics.
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
                    // Export in selected format
                    if (saveDialog.FileName.EndsWith(".csv"))
                    {
                        _metricsService.ExportCSV(saveDialog.FileName);
                    }
                    else
                    {
                        _metricsService.ExportMetrics(saveDialog.FileName);
                    }

                    // Generate and display summary report
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