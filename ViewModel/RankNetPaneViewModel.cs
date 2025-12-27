using MURDOC_2024.Services;
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class RankNetViewModel : ViewModelBase
    {
        private readonly ImageService _imageService = new ImageService();
        private readonly Action<string> _previewCallback;

        private readonly string _placeholder =
            "pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png";

        public RankNetViewModel(Action<string> previewCallback)
        {
            _previewCallback = previewCallback;

            PlaceholderAll();
        }

        // --------------------------
        // Properties for UI binding
        // --------------------------
        public BitmapImage X1 { get => _x1; set => SetProperty(ref _x1, value); }
        private BitmapImage _x1;

        public BitmapImage X2 { get => _x2; set => SetProperty(ref _x2, value); }
        private BitmapImage _x2;

        public BitmapImage X3 { get => _x3; set => SetProperty(ref _x3, value); }
        private BitmapImage _x3;

        public BitmapImage X4 { get => _x4; set => SetProperty(ref _x4, value); }
        private BitmapImage _x4;

        public BitmapImage FixationDecoder { get => _fixDec; set => SetProperty(ref _fixDec, value); }
        private BitmapImage _fixDec;

        public BitmapImage FixationGradCAM { get => _fixGrad; set => SetProperty(ref _fixGrad, value); }
        private BitmapImage _fixGrad;

        public BitmapImage X2_2 { get => _x22; set => SetProperty(ref _x22, value); }
        private BitmapImage _x22;

        public BitmapImage X3_2 { get => _x32; set => SetProperty(ref _x32, value); }
        private BitmapImage _x32;

        public BitmapImage X4_2 { get => _x42; set => SetProperty(ref _x42, value); }
        private BitmapImage _x42;

        public BitmapImage RefPred { get => _refPred; set => SetProperty(ref _refPred, value); }
        private BitmapImage _refPred;

        public BitmapImage CamouflageDecoder { get => _camDec; set => SetProperty(ref _camDec, value); }
        private BitmapImage _camDec;

        public BitmapImage CamouflageGradCAM { get => _camGrad; set => SetProperty(ref _camGrad, value); }
        private BitmapImage _camGrad;

        // ----------------------------------------------------
        // MOUSEOVER SUPPORT
        // Called by UserControl when user hovers over image.
        // ----------------------------------------------------
        public void OnMouseOverImage(string imagePath)
        {
            _previewCallback?.Invoke(imagePath);
        }

        // ----------------------------------------------------
        // LOAD RESULTS FROM PYTHON OUTPUT
        // ----------------------------------------------------
        public void LoadResults(string offrampsFolder, string outputsFolder)
        {
            X1 = LoadOrPlaceholder(Path.Combine(offrampsFolder, "x1.png"));
            X2 = LoadOrPlaceholder(Path.Combine(offrampsFolder, "x2.png"));
            X3 = LoadOrPlaceholder(Path.Combine(offrampsFolder, "x3.png"));
            X4 = LoadOrPlaceholder(Path.Combine(offrampsFolder, "x4.png"));

            FixationDecoder = LoadOrPlaceholder(Path.Combine(outputsFolder, "binary_image.png"));
            FixationGradCAM = LoadOrPlaceholder(Path.Combine(outputsFolder, "gradcam_cod.png"));

            X2_2 = LoadOrPlaceholder(Path.Combine(offrampsFolder, "x2_2.png"));
            X3_2 = LoadOrPlaceholder(Path.Combine(offrampsFolder, "x3_2.png"));
            X4_2 = LoadOrPlaceholder(Path.Combine(offrampsFolder, "x4_2.png"));

            RefPred = LoadOrPlaceholder(Path.Combine(offrampsFolder, "ref_pred.png"));
            CamouflageDecoder = LoadOrPlaceholder(Path.Combine(outputsFolder, "fixation_image.png"));
            CamouflageGradCAM = LoadOrPlaceholder(Path.Combine(outputsFolder, "gradcam_fix.png"));
        }

        public void Clear()
        {
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
            X1 = new BitmapImage(new Uri(_placeholder));
            X2 = new BitmapImage(new Uri(_placeholder));
            X3 = new BitmapImage(new Uri(_placeholder));
            X4 = new BitmapImage(new Uri(_placeholder));
            FixationDecoder = new BitmapImage(new Uri(_placeholder));
            FixationGradCAM = new BitmapImage(new Uri(_placeholder));

            X2_2 = new BitmapImage(new Uri(_placeholder));
            X3_2 = new BitmapImage(new Uri(_placeholder));
            X4_2 = new BitmapImage(new Uri(_placeholder));
            RefPred = new BitmapImage(new Uri(_placeholder));
            CamouflageDecoder = new BitmapImage(new Uri(_placeholder));
            CamouflageGradCAM = new BitmapImage(new Uri(_placeholder));
        }
    }
}
