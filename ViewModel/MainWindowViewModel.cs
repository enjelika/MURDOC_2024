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

        #region Child VMs
        // Image selection + sliders
        public ImageControlViewModel ImageControlVM { get; }
        public InputImagePaneViewModel InputImageVM { get; }
        public PreviewPaneViewModel PreviewPaneVM { get; }

        // Model output VMs
        public RankNetViewModel RankNetVM { get; }
        public EfficientDetD7ViewModel EfficientDetVM { get; }
        #endregion

        private readonly PythonModelService _python;

        public MainWindowViewModel()
        {
            _python = new PythonModelService();

            // 1Create child VMs FIRST
            InputImageVM = new InputImagePaneViewModel();
            PreviewPaneVM = new PreviewPaneViewModel();

            // THEN create ImageControlVM and pass the callbacks
            ImageControlVM = new ImageControlViewModel(
                runModelsAction: RunModels,
                resetAction: ResetAll,
                imageSelectedAction: path => InputImageVM.LoadImage(path)
            );

            RankNetVM = new RankNetViewModel(previewPath => HandlePreviewImageChanged(previewPath));
            EfficientDetVM = new EfficientDetD7ViewModel(HandlePreviewImageChanged);
        }

        // MAIN CALLBACKS
        private void RunModels()
        {
            Console.WriteLine("Run models triggered.");
        }

        private void ResetAll()
        {
            InputImageVM.InputImage = null;
            PreviewPaneVM.PreviewImage = null;
        }

        public void HandlePreviewImageChanged(string imagePath)
        {
            if (PreviewPaneVM != null)
            {
                PreviewPaneVM.PreviewImage = new BitmapImage(new Uri(imagePath));
            }
        }

    }
}
