using MURDOC_2024.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class MainWindowViewModel : ViewModelBase
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

                    // Update command can execute state
                    ImageControlVM?.UpdateCommandStates();
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

        public MainWindowViewModel()
        {
            _python = new PythonModelService();
            _imageService = new ImageService();

            // Create child VMs
            InputImageVM = new InputImagePaneViewModel();
            PreviewPaneVM = new PreviewPaneViewModel();
            IAIOutputVM = new IAIOutputPaneViewModel();

            ImageControlVM = new ImageControlViewModel(
                runModelsAction: () => RunModelsCommand(), // Changed from async void
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

        /// <summary>
        /// Properly async method for running models
        /// </summary>
        private async Task RunModelsAsync()
        {
            if (IsRunningModels)
                return;

            try
            {
                IsRunningModels = true;

                // Set cursor on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                });

                string imageToProcess = GetImageToUse();
                string iaiMessage = null;

                // Run Python models on background thread
                await Task.Run(async () =>
                {
                    try
                    {
                        // Run Python model (this will handle GIL internally)
                        iaiMessage = await RunPythonModelAsync(imageToProcess);

                        // Load output images on background thread
                        await Task.Run(() => LoadModelOutputs());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in model execution: {ex.Message}");
                        throw;
                    }
                });

                // Update UI on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IAIOutputVM.IAIOutputMessage = iaiMessage ?? "Model execution completed.";
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RunModelsAsync: {ex.Message}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IAIOutputVM.IAIOutputMessage = $"Error: {ex.Message}";
                });

                throw;
            }
            finally
            {
                // Always restore cursor and state on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Mouse.OverrideCursor = null;
                    IsRunningModels = false;
                });
            }
        }

        /// <summary>
        /// Wrapper for Python execution to handle async properly
        /// </summary>
        private Task<string> RunPythonModelAsync(string imagePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    // This runs on a background thread
                    return _python.RunIAIModels(imagePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Python execution error: {ex.Message}");
                    throw;
                }
            });
        }

        private void ResetAll()
        {
            SelectedImagePath = null;
            _adjustedInputImage = null;
            _hasAdjustedImage = false;
            _latestAdjustedImagePath = null;

            InputImageVM.InputImage = null;
            PreviewPaneVM.PreviewImage = null;
            IAIOutputVM.IAIOutputMessage = string.Empty;

            RankNetVM.Clear();
            EfficientDetVM.Clear();
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
                InputImageVM.InputImage = adjusted;
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

                // Load results on background thread, but UI updates will be handled by ViewModels
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RankNetVM.LoadResults(offrampsFolderPath, outputsFolderPath);
                    EfficientDetVM.LoadResults(detectionFolderPath, outputsFolderPath, selectedName);
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
    }
}