using MURDOC_2024.Services;
using System;
using System.Collections.ObjectModel;
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

        private void PlaceholderAll()
        {
            DetectionImage = new BitmapImage(new Uri(_placeholder));
            DetectionText = "No detection results yet.";
        }

        public string DetectionImagePath { get; set; }

        public void LoadResults(string detectionFolder, string outputsFolder, string imageName)
        {
            string placeholder = "pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png";

            // PNG detection image
            string png = Path.Combine(detectionFolder, imageName + ".png");
            DetectionImage = File.Exists(png)
                ? LoadBitmapFully(png)
                : new BitmapImage(new Uri(placeholder));

            // TXT detection results
            string txt = Path.Combine(detectionFolder, imageName + ".txt");
            if (!File.Exists(txt))
                txt = Path.Combine(outputsFolder, imageName + ".txt");

            DetectionText = File.Exists(txt)
                ? File.ReadAllText(txt)
                : "No detections.";
        }

        private BitmapImage LoadBitmapFully(string path)
        {
            var bmp = new BitmapImage();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = fs;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
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
