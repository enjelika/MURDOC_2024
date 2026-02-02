using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class FinalPredictionPaneViewModel : ViewModelBase
    {
        private BitmapImage _originalImage;      // Base layer: original input
        private BitmapImage _binaryMask;         // Binary detection mask
        private BitmapImage _rankMap;            // Confidence/fixation heatmap
        private ImageSource _overlayImage;       // Colored rank map with transparency
        private bool _hasResult;
        private double _overlayOpacity = 0.7;

        public BitmapImage OriginalImage
        {
            get => _originalImage;
            set => SetProperty(ref _originalImage, value);
        }

        public BitmapImage BinaryMask
        {
            get => _binaryMask;
            set => SetProperty(ref _binaryMask, value);
        }

        public BitmapImage RankMap
        {
            get => _rankMap;
            set => SetProperty(ref _rankMap, value);
        }

        public ImageSource OverlayImage
        {
            get => _overlayImage;
            set => SetProperty(ref _overlayImage, value);
        }

        public bool HasResult
        {
            get => _hasResult;
            set => SetProperty(ref _hasResult, value);
        }

        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set => SetProperty(ref _overlayOpacity, value);
        }

        /// <summary>
        /// Load prediction result with separate layers
        /// </summary>
        public void LoadResult(string resultsFolder, string fileNameWithoutExtension, string originalImagePath = null)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;

                // Paths to separate components
                string outputsDir = Path.Combine(exeDir, "outputs", fileNameWithoutExtension);
                string binaryMaskPath = Path.Combine(outputsDir, "binary_image.png");
                string rankMapPath = Path.Combine(outputsDir, "fixation_image.png");

                // STEP 1: Load original image FIRST (we need its dimensions)
                if (!string.IsNullOrEmpty(originalImagePath) && File.Exists(originalImagePath))
                {
                    OriginalImage = LoadImage(originalImagePath);
                    System.Diagnostics.Debug.WriteLine($"Loaded original from provided path: {originalImagePath}");
                    System.Diagnostics.Debug.WriteLine($"  Dimensions: {OriginalImage.PixelWidth}x{OriginalImage.PixelHeight}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Original image path not found: {originalImagePath}");
                    HasResult = false;
                    return;
                }

                // STEP 2: Load binary mask
                if (File.Exists(binaryMaskPath))
                {
                    BinaryMask = LoadImage(binaryMaskPath);
                    System.Diagnostics.Debug.WriteLine($"Loaded binary mask: {binaryMaskPath}");
                    System.Diagnostics.Debug.WriteLine($"  Dimensions: {BinaryMask.PixelWidth}x{BinaryMask.PixelHeight}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Binary mask not found: {binaryMaskPath}");
                }

                // STEP 3: Load rank map
                if (File.Exists(rankMapPath))
                {
                    RankMap = LoadImage(rankMapPath);
                    System.Diagnostics.Debug.WriteLine($"Loaded rank map: {rankMapPath}");
                    System.Diagnostics.Debug.WriteLine($"  Dimensions: {RankMap.PixelWidth}x{RankMap.PixelHeight}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Rank map not found: {rankMapPath}");
                }

                // STEP 4: Create overlay AFTER all images are loaded
                if (RankMap != null && BinaryMask != null && OriginalImage != null)
                {
                    OverlayImage = CreateColoredOverlay(RankMap, BinaryMask);
                    System.Diagnostics.Debug.WriteLine("Created colored overlay");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Cannot create overlay - missing components:");
                    System.Diagnostics.Debug.WriteLine($"  Original: {OriginalImage != null}");
                    System.Diagnostics.Debug.WriteLine($"  Binary: {BinaryMask != null}");
                    System.Diagnostics.Debug.WriteLine($"  Rank: {RankMap != null}");
                }

                HasResult = OriginalImage != null && OverlayImage != null;

                if (!HasResult)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load complete result for: {fileNameWithoutExtension}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading result layers: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                Clear();
            }
        }

        /// <summary>
        /// Create a colored heatmap overlay from rank map, masked to binary detection
        /// </summary>
        private ImageSource CreateColoredOverlay(BitmapImage rankMap, BitmapImage binaryMask)
        {
            if (rankMap == null || OriginalImage == null)
                return null;

            try
            {
                int width = OriginalImage.PixelWidth;
                int height = OriginalImage.PixelHeight;

                // Convert images to proper format (8-bit grayscale)
                var rankGray = ConvertToGrayscale(rankMap, width, height);
                var maskGray = ConvertToGrayscale(binaryMask, width, height);

                if (rankGray == null)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to convert rank map to grayscale");
                    return null;
                }

                // Create BGRA output
                int stride = width * 4;
                byte[] outputPixels = new byte[height * stride];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int grayIndex = y * width + x;
                        int rgbaIndex = y * stride + x * 4;

                        // Check mask
                        bool inMask = false;
                        if (maskGray != null)
                        {
                            inMask = maskGray[grayIndex] > 128; // White = object
                        }

                        if (inMask)
                        {
                            byte rankValue = rankGray[grayIndex];

                            if (rankValue > 10)
                            {
                                // Apply JET colormap
                                var (r, g, b) = ApplyJetColormap(rankValue / 255.0);

                                outputPixels[rgbaIndex + 0] = b;  // B
                                outputPixels[rgbaIndex + 1] = g;  // G
                                outputPixels[rgbaIndex + 2] = r;  // R
                                outputPixels[rgbaIndex + 3] = (byte)(255 * 0.7); // Alpha
                            }
                        }
                        // else: transparent (already 0)
                    }
                }

                var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                result.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), outputPixels, stride, 0);
                result.Freeze();

                System.Diagnostics.Debug.WriteLine($"Created overlay: {width}x{height} (matching original image)");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating colored overlay: {ex.Message}");
                return rankMap;
            }
        }

        /// <summary>
        /// Convert BitmapImage to grayscale byte array
        /// </summary>
        private byte[] ConvertToGrayscale(BitmapImage source, int targetWidth, int targetHeight)
        {
            if (source == null) return null;

            try
            {
                // Scale if needed
                WriteableBitmap scaled;
                if (source.PixelWidth != targetWidth || source.PixelHeight != targetHeight)
                {
                    var transform = new System.Windows.Media.ScaleTransform(
                        (double)targetWidth / source.PixelWidth,
                        (double)targetHeight / source.PixelHeight);
                    scaled = new WriteableBitmap(new TransformedBitmap(source, transform));
                }
                else
                {
                    scaled = new WriteableBitmap(source);
                }

                // Convert to Gray8 format
                var grayBitmap = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);

                // Read pixels
                int stride = targetWidth; // Gray8 is 1 byte per pixel
                byte[] pixels = new byte[targetHeight * stride];
                grayBitmap.CopyPixels(pixels, stride, 0);

                return pixels;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting to grayscale: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Apply JET colormap (blue -> cyan -> yellow -> red)
        /// </summary>
        private (byte r, byte g, byte b) ApplyJetColormap(double value)
        {
            // Clamp value to [0, 1]
            value = Math.Max(0, Math.Min(1, value));

            byte r, g, b;

            if (value < 0.25)
            {
                // Blue to Cyan
                double t = value * 4;
                r = 0;
                g = (byte)(255 * t);
                b = 255;
            }
            else if (value < 0.5)
            {
                // Cyan to Green
                double t = (value - 0.25) * 4;
                r = 0;
                g = 255;
                b = (byte)(255 * (1 - t));
            }
            else if (value < 0.75)
            {
                // Green to Yellow
                double t = (value - 0.5) * 4;
                r = (byte)(255 * t);
                g = 255;
                b = 0;
            }
            else
            {
                // Yellow to Red
                double t = (value - 0.75) * 4;
                r = 255;
                g = (byte)(255 * (1 - t));
                b = 0;
            }

            return (r, g, b);
        }

        private BitmapImage LoadImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load immediately
            bitmap.EndInit();
            bitmap.Freeze(); // Make thread-safe and ensure it's fully loaded
            return bitmap;
        }

        public void Clear()
        {
            OriginalImage = null;
            BinaryMask = null;
            RankMap = null;
            OverlayImage = null;
            HasResult = false;
        }
    }
}