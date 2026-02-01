using MURDOC_2024.Model;
using MURDOC_2024.Services;
using System;
using System.IO;
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
                    // Notify the UI that the running state has changed
                    OnPropertyChanged(nameof(IsNotRunningModels));

                    // Force command state updates on UI thread
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ImageControlVM?.UpdateCommandStates();
                        MICAControlVM?.UpdateCommandStates();  // ADD THIS
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

        public MainWindowViewModel()
        {
            _python = new PythonModelService();
            _python.SetDetectionParameters(1.5, 0.0);
            _imageService = new ImageService();

            // Create child VMs
            InputImageVM = new InputImagePaneViewModel();
            PreviewPaneVM = new PreviewPaneViewModel();
            IAIOutputVM = new IAIOutputPaneViewModel();

            ImageControlVM = new ImageControlViewModel(
                runModelsAction: () => RunModelsCommand(),
                resetAction: ResetAll,
                imageSelectedAction: path => OnImageSelected(path),  // CHANGED: Use dedicated method
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
        }

        #region EditorControls Event Handlers

        private void OnCorrectionModeToggled(object sender, EventArgs e)
        {
            // TODO: Enable/disable correction drawing mode on the main canvas
            bool isActive = EditorControlsVM.IsCorrectionModeActive;
            System.Diagnostics.Debug.WriteLine($"Correction mode: {(isActive ? "ACTIVE" : "INACTIVE")}");

            // You'll wire this up to your polygon drawing canvas later
        }

        private void OnFeedbackHistoryViewRequested(object sender, EventArgs e)
        {
            // TODO: Show feedback history window/dialog
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

                    System.IO.File.WriteAllText(dialog.FileName, json);

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
                // TODO: Clear ROIs when implemented

                MessageBox.Show(
                    "Session reset complete.",
                    "Reset Session",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        #endregion

        public void AddDetectionFeedback(DetectionFeedback feedback)
        {
            EditorControlsVM.AddFeedback(feedback);
        }

        /// <summary>
        /// Called when an image is selected via the Browse button
        /// </summary>
        private void OnImageSelected(string path)
        {
            SelectedImagePath = path;
            InputImageVM.LoadImage(path);

            // CRITICAL: Notify MICA Control that an image has been selected
            // This enables the Run and Reset buttons
            MICAControlVM.OnImageSelected(path);
        }

        /// <summary>
        /// Wrapper method to safely call async RunModels
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

        private void LogStateChange(string context)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {context}:");
            System.Diagnostics.Debug.WriteLine($"  IsRunningModels: {IsRunningModels}");
            System.Diagnostics.Debug.WriteLine($"  IsNotRunningModels: {IsNotRunningModels}");
            System.Diagnostics.Debug.WriteLine($"  Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            System.Diagnostics.Debug.WriteLine($"  IsUIThread: {Application.Current.Dispatcher.CheckAccess()}");
        }

        /// <summary>
        /// Properly async method for running models
        /// </summary>
        private async Task RunModelsAsync()
        {
            if (IsRunningModels)
                return;

            try
            {
                LogStateChange("Before setting IsRunningModels = true");

                // Set running state - this will disable buttons in BOTH ViewModels
                IsRunningModels = true;

                // UPDATE: Explicitly disable buttons in both ViewModels
                ImageControlVM?.SetButtonStates(true);
                MICAControlVM?.SetButtonStates(true);

                LogStateChange("After setting IsRunningModels = true");

                // Set wait cursor on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                });

                string imageToProcess = GetImageToUse();
                string iaiMessage = null;

                // Run Python model (long-running operation)
                iaiMessage = await RunPythonModelAsync(imageToProcess);

                // Load outputs
                await Task.Run(() => LoadModelOutputs());

                // Update UI with results
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

                // Restore cursor on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = null;
                });

                // IMPORTANT: Set IsRunningModels to false OUTSIDE of Dispatcher.Invoke
                IsRunningModels = false;

                // UPDATE: Explicitly re-enable buttons in both ViewModels
                ImageControlVM?.SetButtonStates(false);
                MICAControlVM?.SetButtonStates(false);

                LogStateChange("After setting IsRunningModels = false");

                // Additional safety: Force a final command update after a small delay
                await Task.Delay(100);
                LogStateChange("After delay, forcing command update");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ImageControlVM?.UpdateCommandStates();
                    MICAControlVM?.UpdateCommandStates();  // ADD THIS
                    CommandManager.InvalidateRequerySuggested();
                    LogStateChange("After forcing command update");
                });
            }
        }

        /// <summary>
        /// Simplified wrapper: Directly returns the Task created by the PythonModelService.
        /// </summary>
        private async Task<string> RunPythonModelAsync(string imagePath)
        {
            // If you add a dedicated UseMICA toggle later, replace this condition.
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

            // UPDATE: Reset both ViewModels
            ImageControlVM?.SetButtonStates(false);
            // Note: MICAControlVM.ResetAll() is called internally via its ExecuteReset
            // But we should also reset its button states here for consistency
            MICAControlVM?.SetButtonStates(false);

            // Force command updates after reset
            ImageControlVM?.UpdateCommandStates();
            MICAControlVM?.UpdateCommandStates();
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

                // Save adjusted bitmap to temp file for Python
                _latestAdjustedImagePath = _imageService.SaveBitmapToTemp(adjusted);
                _adjustedInputImage = adjusted;
                _hasAdjustedImage = true;

                // Update UI
                InputImageVM.InputImage = new BitmapImage(new Uri(_latestAdjustedImagePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adjusting image: {ex.Message}");
            }
        }

        private string GetImageToUse()
        {
            // If user adjusted the image, use the latest adjusted temp file
            if (!string.IsNullOrEmpty(_latestAdjustedImagePath) && File.Exists(_latestAdjustedImagePath))
                return _latestAdjustedImagePath;

            // Otherwise use the original
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

                // Load results on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RankNetVM.LoadResults(offrampsFolderPath, outputsFolderPath);
                    EfficientDetVM.LoadResults(detectionFolderPath, outputsFolderPath, selectedName);
                    FinalPredictionVM.LoadResult(predictionFolderPath, "segmented_" + selectedName);
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
            // Safely dispose of the Python service
            (_python as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}