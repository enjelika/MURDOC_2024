using System;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class InputImagePaneViewModel : ViewModelBase
    {
        private BitmapImage _inputImage;
        public BitmapImage InputImage
        {
            get => _inputImage;
            set => SetProperty(ref _inputImage, value);
        }

        public void LoadImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                InputImage = null;
                return;
            }

            InputImage = new BitmapImage(new Uri(imagePath));
        }
    }
}
