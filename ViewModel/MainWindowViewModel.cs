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
                imageSelectedAction: path =>
                {
                    SelectedImagePath = path;
                    InputImageVM.LoadImage(path);
                },
                slidersChangedAction: (b, c, s) => AdjustInputImage(b, c, s)
            );

            RankNetVM = new RankNetViewModel(previewPath => HandlePreviewImageChanged(previewPath));
            EfficientDetVM = new EfficientDetD7ViewModel(HandlePreviewImageChanged);
            FinalPredictionVM = new FinalPredictionPaneViewModel();

            MICAControlVM = new MICAControlViewModel(
                pythonService: _python,             // ideally via IPythonService
                runModelsAction: () => RunModelsCommand()
            );

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
                // Set running state - this will disable buttons
                IsRunningModels = true;
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
                // This ensures property change notifications work correctly
                IsRunningModels = false;
                LogStateChange("After setting IsRunningModels = false");

                // Additional safety: Force a final command update after a small delay
                await Task.Delay(100);
                LogStateChange("After delay, forcing command update");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ImageControlVM?.UpdateCommandStates();
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
            PreviewPaneVM.PreviewImage = null;
            IAIOutputVM.IAIOutputMessage = string.Empty;

            RankNetVM.Clear();
            EfficientDetVM.Clear();
            FinalPredictionVM.Clear();

            // Force command updates after reset
            ImageControlVM?.UpdateCommandStates();
            CommandManager.InvalidateRequerySuggested();
            MICAControlVM.ResetAll();
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