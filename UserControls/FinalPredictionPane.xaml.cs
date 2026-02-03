using MURDOC_2024.Services;
using MURDOC_2024.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MURDOC_2024.UserControls
{
    /// <summary>
    /// Interaction logic for FinalPredictionPane.xaml
    /// </summary>
    public partial class FinalPredictionPane : UserControl
    {
        private FinalPredictionPaneViewModel ViewModel => DataContext as FinalPredictionPaneViewModel;
        private PolygonDrawingService _drawingService;
        private List<Ellipse> _polygonPointMarkers;
        private Polyline _currentPolyline;
        private Polygon _originalMaskPolygon;

        public FinalPredictionPane()
        {
            InitializeComponent();
            _drawingService = new PolygonDrawingService();
            _polygonPointMarkers = new List<Ellipse>();
        }

        private void EditingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || !ViewModel.IsDrawingMode)
                return;

            if (ViewModel.CurrentDrawingMode == DrawingMode.Polygon)
            {
                Point canvasPoint = e.GetPosition(EditingCanvas);

                // Convert to image coordinates
                Point imagePoint = CanvasToImageCoordinates(canvasPoint);

                // Add point to polygon (in IMAGE coordinates)
                _drawingService.AddPoint(imagePoint);

                // Add visual marker (in CANVAS coordinates)
                var marker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Brushes.Yellow,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                Canvas.SetLeft(marker, canvasPoint.X - 4);
                Canvas.SetTop(marker, canvasPoint.Y - 4);
                EditingCanvas.Children.Add(marker);
                _polygonPointMarkers.Add(marker);

                // Update polyline (convert to canvas coordinates for display)
                UpdatePolyline();

                System.Diagnostics.Debug.WriteLine($"Added point - Canvas: {canvasPoint}, Image: {imagePoint}");
            }
        }

        private void EditingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // Future: Show preview line to cursor
        }

        private void EditingCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || !ViewModel.IsDrawingMode)
                return;

            if (_drawingService.CurrentPolygon.Count >= 3)
            {
                // Complete the polygon
                CompletePolygon();
            }
            else
            {
                MessageBox.Show("Need at least 3 points to complete polygon", "Invalid Polygon",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdatePolyline()
        {
            if (_currentPolyline != null)
            {
                EditingCanvas.Children.Remove(_currentPolyline);
            }

            if (_drawingService.CurrentPolygon.Count > 1)
            {
                // Convert image coordinates to canvas coordinates for display
                var canvasPoints = _drawingService.CurrentPolygon
                    .Select(p => ImageToCanvasCoordinates(p))
                    .ToList();

                _currentPolyline = new Polyline
                {
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    Points = new PointCollection(canvasPoints)
                };

                EditingCanvas.Children.Add(_currentPolyline);
            }
        }

        private void CompletePolygon()
        {
            if (_drawingService.CurrentPolygon.Count < 3)
            {
                MessageBox.Show("Need at least 3 points to complete polygon", "Invalid Polygon",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Polygon points are already in IMAGE coordinates
                var imagePolygonPoints = new List<Point>(_drawingService.CurrentPolygon);

                // Convert polygon to mask and update ViewModel
                bool success = ViewModel.UpdateMaskFromPolygon(imagePolygonPoints);

                if (success)
                {
                    // Clear drawing
                    ClearDrawing();

                    // Exit drawing mode
                    ViewModel.DisableDrawingMode();

                    // Notify parent that ROI was completed
                    OnROICompleted();

                    MessageBox.Show(
                        "Polygon applied successfully!\n\n" +
                        "The mask has been updated with your changes.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Failed to apply polygon to mask.\n\n" +
                        "Please try again or check the debug output.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error completing polygon: {ex.Message}");
                MessageBox.Show(
                    $"Error applying polygon: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Notify that ROI was completed (for EditorControlsVM)
        /// </summary>
        private void OnROICompleted()
        {
            // This will be wired up through MainWindow later
            ROICompleted?.Invoke(this, EventArgs.Empty);
        }

        // Add this event
        public event EventHandler ROICompleted;

        private void ClearDrawing()
        {
            EditingCanvas.Children.Clear();
            _polygonPointMarkers.Clear();
            _currentPolyline = null;
            _drawingService.CancelDrawing();
        }

        public void EnablePolygonDrawing()
        {
            _drawingService.StartDrawing(DrawingMode.Polygon);
            ViewModel?.EnableDrawingMode(DrawingMode.Polygon);

            // Show the original mask outline for editing
            ShowOriginalMaskOutline();

            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Polygon drawing enabled with mask outline");
        }

        public void EnableFreehandDrawing()
        {
            _drawingService.StartDrawing(DrawingMode.Freehand);
            ViewModel?.EnableDrawingMode(DrawingMode.Freehand);
            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Freehand drawing enabled");
        }

        public void CancelDrawing()
        {
            ClearDrawing();
            ViewModel?.DisableDrawingMode();
            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Drawing cancelled");
        }

        /// <summary>
        /// Reset all drawing state and clear canvas
        /// </summary>
        public void ResetDrawing()
        {
            ClearDrawing();

            if (_originalMaskPolygon != null)
            {
                EditingCanvas.Children.Remove(_originalMaskPolygon);
                _originalMaskPolygon = null;
            }

            ViewModel?.DisableDrawingMode();

            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Drawing state reset");
        }

        public void ExportCurrentMask(string filename)
        {
            try
            {
                if (ViewModel?.BinaryMask == null)
                {
                    throw new Exception("No mask available to export");
                }

                // Save the current binary mask
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ViewModel.BinaryMask));

                using (var stream = System.IO.File.Create(filename))
                {
                    encoder.Save(stream);
                }

                System.Diagnostics.Debug.WriteLine($"Exported mask to: {filename}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting mask: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Convert canvas coordinates to image pixel coordinates
        /// </summary>
        private Point CanvasToImageCoordinates(Point canvasPoint)
        {
            if (ViewModel?.OriginalImage == null)
                return canvasPoint;

            // Get the actual rendered size and position of the image
            double canvasWidth = EditingCanvas.ActualWidth;
            double canvasHeight = EditingCanvas.ActualHeight;

            int imageWidth = ViewModel.OriginalImage.PixelWidth;
            int imageHeight = ViewModel.OriginalImage.PixelHeight;

            // Calculate the scale factor (Stretch="Uniform" maintains aspect ratio)
            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            // Calculate the actual rendered image size
            double renderedWidth = imageWidth * scale;
            double renderedHeight = imageHeight * scale;

            // Calculate offset (image is centered)
            double offsetX = (canvasWidth - renderedWidth) / 2;
            double offsetY = (canvasHeight - renderedHeight) / 2;

            // Transform canvas point to image coordinates
            double imageX = (canvasPoint.X - offsetX) / scale;
            double imageY = (canvasPoint.Y - offsetY) / scale;

            return new Point(imageX, imageY);
        }

        /// <summary>
        /// Convert image pixel coordinates to canvas coordinates
        /// </summary>
        private Point ImageToCanvasCoordinates(Point imagePoint)
        {
            if (ViewModel?.OriginalImage == null)
                return imagePoint;

            double canvasWidth = EditingCanvas.ActualWidth;
            double canvasHeight = EditingCanvas.ActualHeight;

            int imageWidth = ViewModel.OriginalImage.PixelWidth;
            int imageHeight = ViewModel.OriginalImage.PixelHeight;

            // Calculate scale
            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            // Calculate rendered size
            double renderedWidth = imageWidth * scale;
            double renderedHeight = imageHeight * scale;

            // Calculate offset
            double offsetX = (canvasWidth - renderedWidth) / 2;
            double offsetY = (canvasHeight - renderedHeight) / 2;

            // Transform image point to canvas coordinates
            double canvasX = imagePoint.X * scale + offsetX;
            double canvasY = imagePoint.Y * scale + offsetY;

            return new Point(canvasX, canvasY);
        }

        /// <summary>
        /// Show the original mask outline as editable polygon
        /// </summary>
        public void ShowOriginalMaskOutline()
        {
            if (ViewModel == null)
                return;

            // Get contour from mask
            var imageContourPoints = ViewModel.GetMaskContourPoints();

            if (imageContourPoints.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No contour points found");
                return;
            }

            // Convert to canvas coordinates
            var canvasPoints = imageContourPoints.Select(p => ImageToCanvasCoordinates(p)).ToList();

            // Create visual polygon
            if (_originalMaskPolygon != null)
            {
                EditingCanvas.Children.Remove(_originalMaskPolygon);
            }

            _originalMaskPolygon = new Polygon
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 0)), // Semi-transparent
                Points = new PointCollection(canvasPoints)
            };

            EditingCanvas.Children.Add(_originalMaskPolygon);

            // Load these points into the drawing service for editing
            _drawingService.CurrentPolygon.Clear();
            _drawingService.CurrentPolygon.AddRange(imageContourPoints); // Store in IMAGE coordinates

            System.Diagnostics.Debug.WriteLine($"Displayed mask outline with {canvasPoints.Count} points");
        }
    }
}