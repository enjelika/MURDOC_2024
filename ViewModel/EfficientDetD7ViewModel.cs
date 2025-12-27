using MURDOC_2024.Services;
using System;
using System.IO;
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

        private readonly Action<string> _previewCallback;
        private readonly ImageService _imageService = new ImageService();

        private readonly string _placeholder =
            "pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png";

        public EfficientDetD7ViewModel(Action<string> previewCallback)
        {
            _previewCallback = previewCallback;

            // Set default placeholder
            PlaceholderAll();
        }

        // ----------------------------------------------------
        // INTERNAL HELPERS
        // ----------------------------------------------------
        private BitmapImage LoadOrPlaceholder(string path)
        {
            if (File.Exists(path))
                return _imageService.LoadBitmapFully(path);

            return new BitmapImage(new Uri(_placeholder));
        }

        private void PlaceholderAll()
        {
            DetectionImage = new BitmapImage(new Uri(_placeholder));
            DetectionText = "No detection results yet.";
        }

        public string DetectionImagePath { get; set; }

        public void LoadResults(string detectionFolder, string outputsFolder, string imageName)
        {
            // PNG detection image - use the helper method
            string png = Path.Combine(detectionFolder, imageName + ".png");
            DetectionImage = LoadOrPlaceholder(png);
            DetectionImagePath = png;

            // TXT detection results
            string txt = Path.Combine(detectionFolder, imageName + ".txt");
            if (!File.Exists(txt))
                txt = Path.Combine(outputsFolder, imageName + ".txt");

            DetectionText = File.Exists(txt)
                ? File.ReadAllText(txt)
                : "No detections.";
        }

        public void Clear()
        {
            PlaceholderAll();
            OnPropertyChanged(nameof(DetectionImage));
            OnPropertyChanged(nameof(DetectionText));
        }

        // ----------------------------------------------------
        // MOUSEOVER SUPPORT
        // Called by UserControl when user hovers over image.
        // ----------------------------------------------------
        public void OnMouseOverImage(string imagePath)
        {
            _previewCallback?.Invoke(imagePath);
        }
    }
}
