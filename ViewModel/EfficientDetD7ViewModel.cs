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

        public BitmapImage Placeholder { get; }

        private readonly Action<string> _previewCallback;
        private readonly ImageService _imageService = new ImageService();

        public EfficientDetD7ViewModel(Action<string> previewCallback)
        {
            _previewCallback = previewCallback
                ?? throw new ArgumentNullException(nameof(previewCallback));

            // Set default placeholder
            PlaceholderAll();
        }

        private void PlaceholderAll()
        {
            string ph = "pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png";
            DetectionImage = new BitmapImage(new Uri(ph));
            DetectionText = "No detection results yet.";
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

        // MOUSEOVER BINDING
        public void OnMouseOverImage(string imagePath)
        {
            _previewCallback?.Invoke(imagePath);
        }
    }
}
