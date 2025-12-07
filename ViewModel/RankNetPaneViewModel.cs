using System;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class RankNetViewModel : ViewModelBase
    {
        public BitmapImage X1 { get => _x1; set => SetProperty(ref _x1, value); }
        private BitmapImage _x1;

        public BitmapImage X2 { get => _x2; set => SetProperty(ref _x2, value); }
        private BitmapImage _x2;

        public BitmapImage X3 { get => _x3; set => SetProperty(ref _x3, value); }
        private BitmapImage _x3;

        public BitmapImage X4 { get => _x4; set => SetProperty(ref _x4, value); }
        private BitmapImage _x4;

        public BitmapImage X2_2 { get => _x2_2; set => SetProperty(ref _x2_2, value); }
        private BitmapImage _x2_2;

        public BitmapImage X3_2 { get => _x3_2; set => SetProperty(ref _x3_2, value); }
        private BitmapImage _x3_2;

        public BitmapImage X4_2 { get => _x4_2; set => SetProperty(ref _x4_2, value); }
        private BitmapImage _x4_2;

        public BitmapImage FixationDecoder { get; set; }
        public BitmapImage FixationGradCAM { get; set; }

        public BitmapImage CamouflageDecoder { get; set; }
        public BitmapImage CamouflageGradCAM { get; set; }

        public BitmapImage RefPred { get; set; }

        public BitmapImage Placeholder { get; }      

        private readonly Action<string> _previewCallback;

        public RankNetViewModel(Action<string> previewCallback)
        {
            _previewCallback = previewCallback
                ?? throw new ArgumentNullException(nameof(previewCallback));

            // Set default placeholder
            Placeholder = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));

            X1 = Placeholder;
            X2 = Placeholder;
            X3 = Placeholder;
            X4 = Placeholder;
            FixationDecoder = Placeholder;
            FixationGradCAM = Placeholder;

            X2_2 = Placeholder;
            X3_2 = Placeholder;
            X4_2 = Placeholder;
            CamouflageDecoder = Placeholder;
            CamouflageGradCAM = Placeholder;
        }

        // Helper method so Python service can inject images safely
        public void LoadImage(out BitmapImage target, string path)
        {
            target = string.IsNullOrEmpty(path)
                ? null
                : new BitmapImage(new Uri(path));
        }

        // MOUSEOVER BINDING
        public void OnMouseOverImage(string imagePath)
        {
            _previewCallback?.Invoke(imagePath);
        }
    }
}
