using System;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class PreviewPaneViewModel : ViewModelBase
    {
        private BitmapImage _previewImage;

        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        public void UpdatePreview(string path)
        {
            PreviewImage = new BitmapImage(new Uri(path));
        }
    }
}
