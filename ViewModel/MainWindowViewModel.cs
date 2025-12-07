using MURDOC_2024.Services;
using System;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string _selectedImagePath;
        private BitmapImage _previewImage;

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

        // Child VMs
        public ImageControlViewModel ImageControlVM { get; }
        public InputImagePaneViewModel InputImageVM { get; }
        public PreviewPaneViewModel PreviewPaneVM { get; }

        public RankNetViewModel RankNetVM { get; }
        public EfficientDetD7ViewModel EfficientDetVM { get; }

        private readonly ImageService _imageService;
        private readonly PythonModelService _python;

        public MainWindowViewModel()
        {
            _python = new PythonModelService();
            _imageService = new ImageService();

            // Create child VMs
            InputImageVM = new InputImagePaneViewModel();
            PreviewPaneVM = new PreviewPaneViewModel();

            ImageControlVM = new ImageControlViewModel(
                runModelsAction: RunModels,
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

        private void RunModels()
        {
            Console.WriteLine("Run models triggered.");
        }

        private void ResetAll()
        {
            InputImageVM.InputImage = null;
            PreviewPaneVM.PreviewImage = null;
        }

        private void AdjustInputImage(int brightness, int contrast, int saturation)
        {
            if (string.IsNullOrEmpty(SelectedImagePath))
                return;

            // Load original bitmap fully
            BitmapImage original = _imageService.LoadBitmapFully(SelectedImagePath);

            // Adjust into a NEW BitmapImage (no temp path)
            BitmapImage adjusted = _imageService.AdjustImage(original, brightness, contrast, saturation);

            // Display the adjusted image
            InputImageVM.InputImage = adjusted;
        }


        public void HandlePreviewImageChanged(string imagePath)
        {
            PreviewPaneVM.PreviewImage = new BitmapImage(new Uri(imagePath));
        }
    }
}
