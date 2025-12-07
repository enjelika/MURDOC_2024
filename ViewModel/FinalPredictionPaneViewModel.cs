using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class FinalPredictionPaneViewModel : ViewModelBase
    {
        private BitmapImage _finalImagePrediction;
        public BitmapImage FinalImagePrediction
        {
            get => _finalImagePrediction;
            set => SetProperty(ref _finalImagePrediction, value);
        }

        public void LoadImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                FinalImagePrediction = null;
                return;
            }

            FinalImagePrediction = new BitmapImage(new Uri(imagePath));
        }

        // ----------------------------------------------------
        // LOAD RESULTS FROM PYTHON OUTPUT
        // ----------------------------------------------------
        public void LoadResult(string imageFolder, string imageName)
        {
            FinalImagePrediction = new BitmapImage(new Uri(Path.Combine(imageFolder, imageName+".png")));
        }
    }
}
