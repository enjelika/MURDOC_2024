using MURDOC_2024.Services;
using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;

namespace MURDOC_2024.ViewModel
{
    public class FinalPredictionPaneViewModel : ViewModelBase
    {
        private BitmapImage _originalImage;      // Base layer: original input
        private BitmapImage _binaryMask;         // Binary detection mask
        private BitmapImage _rankMap;            // Confidence/fixation heatmap
        
        private ImageSource _overlayImage;       // Colored rank map with transparency

        private BitmapImage _originalBinaryMask; // Store original before modifications
        private bool _hasModifications;

        public bool HasModifications
        {
            get => _hasModifications;
            set => SetProperty(ref _hasModifications, value);
        }

        private bool _hasResult;
        private double _overlayOpacity = 0.7;

        private bool _isDrawingMode;
        private DrawingMode _currentDrawingMode;

        public bool IsDrawingMode
        {
            get => _isDrawingMode;
            set => SetProperty(ref _isDrawingMode, value);
        }

        public DrawingMode CurrentDrawingMode
        {
            get => _currentDrawingMode;
            set => SetProperty(ref _currentDrawingMode, value);
        }

        // Add these methods
        public void EnableDrawingMode(DrawingMode mode)
        {
            IsDrawingMode = true;
            CurrentDrawingMode = mode;
            System.Diagnostics.Debug.WriteLine($"Drawing mode enabled: {mode}");
        }

        public void DisableDrawingMode()
        {
            IsDrawingMode = false;
            CurrentDrawingMode = DrawingMode.None;
            System.Diagnostics.Debug.WriteLine("Drawing mode disabled");
        }

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

                string outputsDir = Path.Combine(exeDir, "outputs", fileNameWithoutExtension);
                string binaryMaskPath = Path.Combine(outputsDir, "binary_image.png");
                string rankMapPath = Path.Combine(outputsDir, "fixation_image.png");

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

                if (File.Exists(binaryMaskPath))
                {
                    BinaryMask = LoadImage(binaryMaskPath);
                    _originalBinaryMask = LoadImage(binaryMaskPath); // SAVE ORIGINAL
                    HasModifications = false; // Reset modification flag
                    System.Diagnostics.Debug.WriteLine($"Loaded binary mask: {binaryMaskPath}");
                    System.Diagnostics.Debug.WriteLine($"  Dimensions: {BinaryMask.PixelWidth}x{BinaryMask.PixelHeight}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Binary mask not found: {binaryMaskPath}");
                }

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

        /// <summary>
        /// Extract polygon points from binary mask contour using contour tracing
        /// </summary>
        public List<Point> GetMaskContourPoints()
        {
            if (BinaryMask == null)
                return new List<Point>();

            try
            {
                int width = BinaryMask.PixelWidth;
                int height = BinaryMask.PixelHeight;

                // Convert to grayscale byte array
                var grayBitmap = new FormatConvertedBitmap(BinaryMask, PixelFormats.Gray8, null, 0);
                byte[] pixels = new byte[width * height];
                grayBitmap.CopyPixels(pixels, width, 0);

                // Find all boundary pixels
                List<Point> boundaryPixels = new List<Point>();

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        int idx = y * width + x;

                        if (pixels[idx] > 128) // White pixel (object)
                        {
                            // Check if any 8-connected neighbor is background
                            bool isBoundary =
                                pixels[idx - 1] < 128 ||           // left
                                pixels[idx + 1] < 128 ||           // right
                                pixels[idx - width] < 128 ||       // top
                                pixels[idx + width] < 128 ||       // bottom
                                pixels[idx - width - 1] < 128 ||   // top-left
                                pixels[idx - width + 1] < 128 ||   // top-right
                                pixels[idx + width - 1] < 128 ||   // bottom-left
                                pixels[idx + width + 1] < 128;     // bottom-right

                            if (isBoundary)
                            {
                                boundaryPixels.Add(new Point(x, y));
                            }
                        }
                    }
                }

                if (boundaryPixels.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No boundary pixels found");
                    return new List<Point>();
                }

                // Order points by tracing the contour
                List<Point> orderedContour = TraceContour(boundaryPixels, width, height);

                // Simplify contour (reduce points while maintaining shape)
                List<Point> simplifiedContour = SimplifyContour(orderedContour, tolerance: 2.0);

                System.Diagnostics.Debug.WriteLine($"Extracted {boundaryPixels.Count} boundary pixels, " +
                    $"ordered to {orderedContour.Count} points, " +
                    $"simplified to {simplifiedContour.Count} points");

                return simplifiedContour;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting contour: {ex.Message}");
                return new List<Point>();
            }
        }

        /// <summary>
        /// Trace contour by finding nearest neighbors
        /// </summary>
        private List<Point> TraceContour(List<Point> boundaryPixels, int width, int height)
        {
            if (boundaryPixels.Count == 0)
                return new List<Point>();

            HashSet<Point> unvisited = new HashSet<Point>(boundaryPixels);
            List<Point> contour = new List<Point>();

            // Start with leftmost, topmost point
            Point start = boundaryPixels.OrderBy(p => p.Y).ThenBy(p => p.X).First();
            Point current = start;

            contour.Add(current);
            unvisited.Remove(current);

            // Trace contour by finding nearest unvisited neighbor
            while (unvisited.Count > 0)
            {
                Point nearest = FindNearestNeighbor(current, unvisited);

                if (nearest.X == -1) // No more neighbors found
                    break;

                contour.Add(nearest);
                unvisited.Remove(nearest);
                current = nearest;
            }

            return contour;
        }

        /// <summary>
        /// Find nearest unvisited neighbor (within distance 3)
        /// </summary>
        private Point FindNearestNeighbor(Point current, HashSet<Point> unvisited)
        {
            double minDist = double.MaxValue;
            Point nearest = new Point(-1, -1);

            // Search in expanding radius (1, 2, 3 pixels)
            for (int radius = 1; radius <= 3; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        Point candidate = new Point(current.X + dx, current.Y + dy);

                        if (unvisited.Contains(candidate))
                        {
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            if (dist < minDist)
                            {
                                minDist = dist;
                                nearest = candidate;
                            }
                        }
                    }
                }

                if (nearest.X != -1)
                    return nearest;
            }

            return nearest;
        }

        /// <summary>
        /// Simplify contour using Ramer-Douglas-Peucker algorithm
        /// </summary>
        private List<Point> SimplifyContour(List<Point> points, double tolerance)
        {
            if (points.Count < 3)
                return new List<Point>(points);

            // Keep every Nth point for performance (adjust as needed)
            int step = Math.Max(1, points.Count / 200); // Target ~200 points

            List<Point> simplified = new List<Point>();
            for (int i = 0; i < points.Count; i += step)
            {
                simplified.Add(points[i]);
            }

            // Make sure we include the last point to close the loop
            if (simplified[simplified.Count - 1] != points[points.Count - 1])
            {
                simplified.Add(points[points.Count - 1]);
            }

            return simplified;
        }

        /// <summary>
        /// Update binary mask with new polygon
        /// </summary>
        public bool UpdateMaskFromPolygon(List<Point> polygonPoints)
        {
            if (polygonPoints == null || polygonPoints.Count < 3 || OriginalImage == null)
            {
                System.Diagnostics.Debug.WriteLine("Invalid polygon or no original image");
                return false;
            }

            try
            {
                int width = OriginalImage.PixelWidth;
                int height = OriginalImage.PixelHeight;

                System.Diagnostics.Debug.WriteLine($"Creating mask from polygon with {polygonPoints.Count} points");
                System.Diagnostics.Debug.WriteLine($"Image dimensions: {width}x{height}");

                var polygonService = new PolygonDrawingService();
                polygonService.SetImageDimensions(width, height);

                byte[] newMaskData = polygonService.ConvertPolygonToMask(polygonPoints, width, height);

                if (newMaskData == null)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to convert polygon to mask");
                    return false;
                }

                var newMask = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
                newMask.WritePixels(new Int32Rect(0, 0, width, height), newMaskData, width, 0);
                newMask.Freeze();

                BinaryMask = ConvertWriteableToBitmapImage(newMask);
                HasModifications = true; // MARK AS MODIFIED

                System.Diagnostics.Debug.WriteLine("Updated BinaryMask with new polygon");

                if (RankMap != null)
                {
                    OverlayImage = CreateColoredOverlay(RankMap, BinaryMask);
                    System.Diagnostics.Debug.WriteLine("Recreated overlay with new mask");
                }

                SaveModifiedMask(newMaskData, width, height);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating mask from polygon: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public bool RestoreOriginalMask()
        {
            if (_originalBinaryMask == null)
            {
                System.Diagnostics.Debug.WriteLine("No original mask stored");
                return false;
            }

            try
            {
                BinaryMask = _originalBinaryMask;
                HasModifications = false;

                System.Diagnostics.Debug.WriteLine("Restored original binary mask");

                if (RankMap != null)
                {
                    OverlayImage = CreateColoredOverlay(RankMap, BinaryMask);
                    System.Diagnostics.Debug.WriteLine("Recreated overlay with original mask");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring original mask: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save modified mask to outputs directory
        /// </summary>
        private void SaveModifiedMask(byte[] maskData, int width, int height)
        {
            try
            {
                if (string.IsNullOrEmpty(OriginalImage?.UriSource?.LocalPath))
                {
                    System.Diagnostics.Debug.WriteLine("No original image path available for saving mask");
                    return;
                }

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(OriginalImage.UriSource.LocalPath);
                string outputDir = System.IO.Path.Combine(exeDir, "outputs", fileName);

                // Ensure directory exists
                System.IO.Directory.CreateDirectory(outputDir);

                // Save modified binary mask
                string modifiedMaskPath = System.IO.Path.Combine(outputDir, "binary_image_modified.png");

                var writeable = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
                writeable.WritePixels(new Int32Rect(0, 0, width, height), maskData, width, 0);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(writeable));

                using (var stream = System.IO.File.Create(modifiedMaskPath))
                {
                    encoder.Save(stream);
                }

                System.Diagnostics.Debug.WriteLine($"Saved modified mask to: {modifiedMaskPath}");

                // Also save with timestamp for history
                string timestampPath = System.IO.Path.Combine(
                    outputDir,
                    $"binary_image_modified_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                using (var stream = System.IO.File.Create(timestampPath))
                {
                    encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(writeable));
                    encoder.Save(stream);
                }

                System.Diagnostics.Debug.WriteLine($"Saved timestamped mask to: {timestampPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving modified mask: {ex.Message}");
            }
        }

        private BitmapImage ConvertWriteableToBitmapImage(WriteableBitmap writeable)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(writeable));

            using (var stream = new System.IO.MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
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
            _originalBinaryMask = null;
            HasModifications = false;
        }
    }
}