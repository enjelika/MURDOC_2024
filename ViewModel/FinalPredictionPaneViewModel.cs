using MURDOC_2024.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq; // For OrderByDescending
using MURDOC_2024.Model;
using IOPath = System.IO.Path;  // CREATE ALIAS to avoid conflict with System.Windows.Shapes.Path
using System.IO;

namespace MURDOC_2024.ViewModel
{
    public class FinalPredictionPaneViewModel : ViewModelBase
    {
        private BitmapImage _originalImage;      // Base layer: original input
        private BitmapImage _binaryMask;         // Binary detection mask
        private BitmapImage _rankMap;            // Confidence/fixation heatmap
        
        private ImageSource _overlayImage;       // Colored rank map with transparency
        private string _originalImagePath;
        private BitmapImage _originalBinaryMask; // Store original before modifications

        // Session tracking
        private string _sessionId;
        private DateTime _sessionStartTime;
        private bool _hasModifications;

        public bool HasAnyModifications => HasModifications || _hasRankModifications;

        /// <summary>
        /// Whether the rank map has been modified independently (brush edits only).
        /// Used by session recording to distinguish mask edits from rank edits.
        /// </summary>
        public bool HasRankModifications => _hasRankModifications;

        private byte[] _modifiedRankData; // Store modified rank values
        private bool _hasRankModifications = false;

        public bool HasModifications
        {
            get => _hasModifications;
            set => SetProperty(ref _hasModifications, value);
        }

        /// <summary>
        /// Sets the canonical session ID from an external source (e.g. MainWindowViewModel),
        /// ensuring all images analyzed in a single session share the same folder root.
        /// Must be called before the first image is loaded.
        /// </summary>
        public void SetSessionId(string sessionId)
        {
            _sessionId = sessionId;
            System.Diagnostics.Debug.WriteLine($"[FinalPredictionPane] Session ID set externally: {_sessionId}");
        }

        /// <summary>
        /// Initializes a new editing session only if no session ID has been assigned yet.
        /// Call SetSessionId() from MainWindowViewModel instead of relying on this fallback.
        /// </summary>
        private void InitializeSession()
        {
            if (!string.IsNullOrEmpty(_sessionId))
            {
                System.Diagnostics.Debug.WriteLine($"[FinalPredictionPane] Session already active: {_sessionId} — skipping re-init");
                return;
            }

            _sessionStartTime = DateTime.Now;
            _sessionId = _sessionStartTime.ToString("yyyyMMdd_HHmmss");
            System.Diagnostics.Debug.WriteLine($"[FinalPredictionPane] Initialized fallback session: {_sessionId}");
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

        private string _zoomLevelText = "100%";

        public string ZoomLevelText
        {
            get => _zoomLevelText;
            set => SetProperty(ref _zoomLevelText, value);
        }

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
                    _originalBinaryMask = LoadImage(binaryMaskPath);
                    HasModifications = false;
                    _hasRankModifications = false;
                    _modifiedRankData = null;

                    System.Diagnostics.Debug.WriteLine("Created colored overlay");
                }

                HasResult = OriginalImage != null && OverlayImage != null;

                if (!HasResult)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load complete result for: {fileNameWithoutExtension}");
                }

                _originalImagePath = originalImagePath;

                // Initialize session only if no external session ID was provided.
                // MainWindowViewModel calls SetSessionId() before the first image loads,
                // so this acts as a fallback only.
                InitializeSession();

                // Check if there's an existing session with modifications
                LoadExistingSessionIfAvailable(fileNameWithoutExtension);

                System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Result loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading result layers: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                Clear();
            }
        }

        /// <summary>
        /// Apply brush stroke to rank map at image coordinates
        /// </summary>
        public void ApplyRankBrush(Point imagePoint, RankBrushMode mode, double brushSize, double brushStrength)
        {
            if (RankMap == null || OriginalImage == null)
                return;

            try
            {
                int width = OriginalImage.PixelWidth;
                int height = OriginalImage.PixelHeight;

                // Initialize modified data if first edit or dimensions changed
                if (_modifiedRankData == null || _modifiedRankData.Length != width * height)
                {
                    // RankMap may be different size than OriginalImage - must scale first
                    WriteableBitmap scaled;
                    if (RankMap.PixelWidth != width || RankMap.PixelHeight != height)
                    {
                        var transform = new ScaleTransform(
                            (double)width / RankMap.PixelWidth,
                            (double)height / RankMap.PixelHeight);
                        scaled = new WriteableBitmap(new TransformedBitmap(RankMap, transform));
                        System.Diagnostics.Debug.WriteLine($"Scaled RankMap from {RankMap.PixelWidth}x{RankMap.PixelHeight} to {width}x{height}");
                    }
                    else
                    {
                        scaled = new WriteableBitmap(RankMap);
                    }

                    var grayBitmap = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
                    _modifiedRankData = new byte[width * height];
                    grayBitmap.CopyPixels(_modifiedRankData, width, 0);
                    System.Diagnostics.Debug.WriteLine($"Initialized _modifiedRankData: {_modifiedRankData.Length} bytes for {width}x{height}");
                }

                // Apply brush
                var brushService = new RankBrushService
                {
                    CurrentMode = mode,
                    BrushSize = brushSize,
                    BrushStrength = brushStrength
                };

                _modifiedRankData = brushService.ApplyBrushStroke(_modifiedRankData, width, height, imagePoint);
                _hasRankModifications = true;

                // Update RankMap with modified data
                var newRankMap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
                newRankMap.WritePixels(new Int32Rect(0, 0, width, height), _modifiedRankData, width, 0);
                newRankMap.Freeze();

                RankMap = ConvertWriteableToBitmapImage(newRankMap);

                // Regenerate overlay
                if (BinaryMask != null)
                {
                    OverlayImage = CreateColoredOverlay(RankMap, BinaryMask);
                }

                System.Diagnostics.Debug.WriteLine($"Applied {mode} brush at {imagePoint}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying rank brush: {ex.Message}");
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
                _originalBinaryMask = BinaryMask;  // ← THIS IS THE KEY FIX
                HasModifications = true;

                System.Diagnostics.Debug.WriteLine("Updated BinaryMask AND _originalBinaryMask with new polygon");

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
                string fileName = IOPath.GetFileNameWithoutExtension(OriginalImage.UriSource.LocalPath);
                string outputDir = IOPath.Combine(exeDir, "outputs", fileName);

                // Ensure directory exists
                System.IO.Directory.CreateDirectory(outputDir);

                // Save modified binary mask
                string modifiedMaskPath = IOPath.Combine(outputDir, "binary_image_modified.png");

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
                string timestampPath = IOPath.Combine(
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

        /// <summary>
        /// Save all modifications to LoRA training format.
        /// All images analyzed within a single session share the same root folder,
        /// identified by the session ID set via SetSessionId() from MainWindowViewModel.
        /// Structure: session_YYYYMMDD_HHMMSS/
        ///   ├── bin_gt/    (edited binary masks)
        ///   ├── fix_gt/    (edited rank maps)
        ///   └── img/       (original input images)
        /// </summary>
        public void SaveAllModifications()
        {
            if (!HasAnyModifications)
            {
                System.Diagnostics.Debug.WriteLine("No modifications to save");
                return;
            }

            try
            {
                // All images in a session share the same root folder via _sessionId.
                // _sessionId is set externally by MainWindowViewModel.SetSessionId()
                // so that multiple images map to the same training_sessions/session_{id}/ tree.
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string sessionFolder = IOPath.Combine(exeDir, "training_sessions", $"session_{_sessionId}");
                Directory.CreateDirectory(sessionFolder);

                // Create subfolders (created here idempotently — safe across multiple images)
                string biGtFolder = IOPath.Combine(sessionFolder, "bi_gt");
                string fixFolder = IOPath.Combine(sessionFolder, "fix");
                string imgFolder = IOPath.Combine(sessionFolder, "img");

                Directory.CreateDirectory(biGtFolder);
                Directory.CreateDirectory(fixFolder);
                Directory.CreateDirectory(imgFolder);

                List<string> savedFiles = new List<string>();
                string imageName = IOPath.GetFileNameWithoutExtension(_originalImagePath);

                // Save binary mask if modified
                if (HasModifications)
                {
                    string maskPath = IOPath.Combine(biGtFolder, $"{imageName}.png");
                    SaveBinaryMaskToPNG(maskPath);
                    savedFiles.Add($"Binary Mask: {maskPath}");

                    // Update _originalBinaryMask from current in-memory BinaryMask
                    // instead of reloading from file (avoids WPF image cache issues)
                    _originalBinaryMask = BinaryMask;

                    System.Diagnostics.Debug.WriteLine($"Saved binary mask and updated original reference: {maskPath}");
                }

                // Save rank map if modified
                if (_hasRankModifications && _modifiedRankData != null && RankMap != null)
                {
                    string rankPath = IOPath.Combine(fixFolder, $"{imageName}.png");
                    SaveRankMapAsPNG(rankPath, OriginalImage.PixelWidth, OriginalImage.PixelHeight);

                    // Only reload if save succeeded
                    if (File.Exists(rankPath))
                    {
                        savedFiles.Add($"Rank Map: {rankPath}");
                        ReloadRankMapFromFile(rankPath);
                        System.Diagnostics.Debug.WriteLine($"Saved and reloaded rank map: {rankPath}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Rank map save was skipped due to buffer mismatch");
                    }
                }

                // Copy original image
                if (!string.IsNullOrEmpty(_originalImagePath) && File.Exists(_originalImagePath))
                {
                    string extension = IOPath.GetExtension(_originalImagePath);
                    string imgPath = IOPath.Combine(imgFolder, $"{imageName}{extension}");
                    File.Copy(_originalImagePath, imgPath, overwrite: true);
                    savedFiles.Add($"Original Image: {imgPath}");
                }

                // Save metadata
                SaveSessionMetadata(sessionFolder, savedFiles, imageName);

                System.Diagnostics.Debug.WriteLine($"Session saved: {sessionFolder}");
                System.Diagnostics.Debug.WriteLine($"Files saved: {savedFiles.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving modifications: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Reload binary mask from saved file back into memory
        /// </summary>
        private void ReloadBinaryMaskFromFile(string filepath)
        {
            try
            {
                var uri = new Uri(filepath);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();

                BinaryMask = bitmap;
                _originalBinaryMask = bitmap;

                System.Diagnostics.Debug.WriteLine("Reloaded binary mask from saved file");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reloading binary mask: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Reload rank map from saved file back into memory
        /// </summary>
        private void ReloadRankMapFromFile(string filepath)
        {
            try
            {
                var uri = new Uri(filepath);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();

                RankMap = bitmap;

                // Always convert to Gray8 for consistent rank data buffer,
                // even if the PNG reloads as Bgr24 or another format
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                var grayBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
                int stride = width; // Gray8 = 1 byte per pixel
                _modifiedRankData = new byte[height * stride];
                grayBitmap.CopyPixels(_modifiedRankData, stride, 0);

                OverlayImage = CreateColoredOverlay(RankMap, BinaryMask);

                System.Diagnostics.Debug.WriteLine($"Reloaded rank map from saved file ({bitmap.Format} -> Gray8)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reloading rank map: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Check if there's an existing session with modifications for this image
        /// </summary>
        private void LoadExistingSessionIfAvailable(string imageName)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string sessionsDir = IOPath.Combine(exeDir, "training_sessions");

                if (!Directory.Exists(sessionsDir))
                    return;

                // Find the most recent session for this image
                var sessionFolders = Directory.GetDirectories(sessionsDir, "session_*")
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToList();

                foreach (var sessionFolder in sessionFolders)
                {
                    // Check if this session has modifications for our image
                    string biGtPath = IOPath.Combine(sessionFolder, "bi_gt", $"{imageName}.png");
                    string fixPath = IOPath.Combine(sessionFolder, "fix", $"{imageName}.png");

                    bool hasBinaryMask = File.Exists(biGtPath);
                    bool hasRankMap = File.Exists(fixPath);

                    if (hasBinaryMask || hasRankMap)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found existing session: {sessionFolder}");

                        // Load the saved binary mask
                        if (hasBinaryMask)
                        {
                            ReloadBinaryMaskFromFile(biGtPath);
                            HasModifications = true;
                            System.Diagnostics.Debug.WriteLine("Loaded existing binary mask modifications");
                        }

                        // Load the saved rank map
                        if (hasRankMap)
                        {
                            ReloadRankMapFromFile(fixPath);
                            _hasRankModifications = true;
                            System.Diagnostics.Debug.WriteLine("Loaded existing rank map modifications");
                        }

                        // Use this session ID to continue the session
                        string sessionName = IOPath.GetFileName(sessionFolder);
                        _sessionId = sessionName.Replace("session_", "");

                        break; // Use the most recent session
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading existing session: {ex.Message}");
                // Don't throw - just continue with fresh start if session load fails
            }
        }

        /// <summary>
        /// Save session metadata as JSON
        /// </summary>
        private void SaveSessionMetadata(string sessionFolder, List<string> savedFiles, string imageName)
        {
            try
            {
                var metadata = new
                {
                    SessionId = _sessionId,
                    StartTime = _sessionStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ImageName = imageName,
                    ModificationsSaved = savedFiles,
                    HasBinaryMaskEdit = HasModifications,
                    HasRankMapEdit = _hasRankModifications,
                    LoRATrainingReady = true,
                    FolderStructure = new
                    {
                        BiGt = "bi_gt/ - Binary ground truth masks",
                        Fix = "fix/ - Fixation/rank maps",
                        Img = "img/ - Original input images"
                    }
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(metadata, Newtonsoft.Json.Formatting.Indented);
                string metadataPath = IOPath.Combine(sessionFolder, "session_metadata.json");
                System.IO.File.WriteAllText(metadataPath, json);

                System.Diagnostics.Debug.WriteLine($"Saved session metadata to: {metadataPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Save binary mask as PNG
        /// </summary>
        private void SaveBinaryMaskToPNG(string filepath)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BinaryMask));

            using (var stream = System.IO.File.Create(filepath))
            {
                encoder.Save(stream);
            }
        }

        /// <summary>
        /// Save rank map as PNG (0-255 byte values)
        /// </summary>
        private void SaveRankMapAsPNG(string filepath, int width, int height)
        {
            if (_modifiedRankData == null)
            {
                System.Diagnostics.Debug.WriteLine("No modified rank data to save");
                return;
            }

            // Verify buffer matches requested dimensions
            int expectedSize = width * height;
            if (_modifiedRankData.Length != expectedSize)
            {
                // Dimensions mismatch - use actual data dimensions
                int actualWidth = OriginalImage?.PixelWidth ?? width;
                int actualHeight = OriginalImage?.PixelHeight ?? height;
                if (_modifiedRankData.Length == actualWidth * actualHeight)
                {
                    width = actualWidth;
                    height = actualHeight;
                    System.Diagnostics.Debug.WriteLine($"Corrected rank save dimensions to {width}x{height}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Buffer size mismatch: {_modifiedRankData.Length} vs expected {expectedSize}");
                    return;
                }
            }

            var writeable = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
            writeable.WritePixels(new Int32Rect(0, 0, width, height), _modifiedRankData, width, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(writeable));

            using (var stream = System.IO.File.Create(filepath))
            {
                encoder.Save(stream);
            }

            System.Diagnostics.Debug.WriteLine($"Saved rank map: {filepath} ({width}x{height})");
        }

        /// <summary>
        /// Legacy method - now redirects to unified save
        /// </summary>
        public void SaveModifiedRankMap()
        {
            SaveAllModifications();
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