using System;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class EfficientDetD7ViewModel : ViewModelBase
    {
        private BitmapImage _detectionImage;
        public BitmapImage DetectionImage
        {
            get => _detectionImage;
            set => SetProperty(ref _detectionImage, value);
        }

        private string _detectionText;
        public string DetectionText
        {
            get => _detectionText;
            set => SetProperty(ref _detectionText, value);
        }

        public BitmapImage Placeholder { get; }

        private readonly Action<string> _previewCallback;

        public EfficientDetD7ViewModel(Action<string> previewCallback)
        {
            _previewCallback = previewCallback
                ?? throw new ArgumentNullException(nameof(previewCallback));

            // Set default placeholder
            Placeholder = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));

            DetectionImage = Placeholder;
        }

        public string DetectionImagePath { get; set; }

        public void LoadDetectionImage(string path)
        {
            DetectionImage = string.IsNullOrEmpty(path)
                ? null
                : new BitmapImage(new Uri(path));
        }

        public void LoadDetectionText(string path)
        {
            DetectionText = string.IsNullOrEmpty(path)
                ? ""
                : System.IO.File.ReadAllText(path);
        }

        // MOUSEOVER BINDING
        public void OnMouseOverImage(string imagePath)
        {
            _previewCallback?.Invoke(imagePath);
        }
    }
}
