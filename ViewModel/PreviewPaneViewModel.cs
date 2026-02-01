using System;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class PreviewPaneViewModel : ViewModelBase
    {
        private BitmapImage _previewImage;
        private readonly BitmapImage _placeholderImage;

        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        public PreviewPaneViewModel()
        {
            // Load placeholder image from Assets folder
            _placeholderImage = LoadPlaceholderImage();

            // Set placeholder as default
            PreviewImage = _placeholderImage;
        }

        private BitmapImage LoadPlaceholderImage()
        {
            try
            {
                // Option 1: If image_placeholder.png is set as Resource
                var uri = new Uri("pack://application:,,,/Assets/image_placeholder.png");

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Important for cross-thread access

                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load placeholder image: {ex.Message}");

                // Fallback: Create a simple gray placeholder
                return CreateDefaultPlaceholder();
            }
        }

        private BitmapImage CreateDefaultPlaceholder()
        {
            // Create a simple 300x300 gray bitmap as fallback
            var width = 300;
            var height = 300;
            var dpi = 96.0;

            var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);

            var visual = new System.Windows.Media.DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(
                    System.Windows.Media.Brushes.LightGray,
                    null,
                    new System.Windows.Rect(0, 0, width, height));
            }

            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var memoryStream = new System.IO.MemoryStream();
            encoder.Save(memoryStream);
            memoryStream.Position = 0;

            var result = new BitmapImage();
            result.BeginInit();
            result.StreamSource = memoryStream;
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.EndInit();
            result.Freeze();

            return result;
        }

        public void UpdatePreview(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    PreviewImage = new BitmapImage(new Uri(path));
                }
                else
                {
                    // Reset to placeholder if path is invalid
                    ResetToPlaceholder();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating preview: {ex.Message}");
                ResetToPlaceholder();
            }
        }

        /// <summary>
        /// Resets preview to the placeholder image
        /// Call this when mouse leaves the hoverable image
        /// </summary>
        public void ResetToPlaceholder()
        {
            PreviewImage = _placeholderImage;
        }

        /// <summary>
        /// Clears the preview (sets to null)
        /// </summary>
        public void ClearPreview()
        {
            ResetToPlaceholder();
        }
    }
}