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

        // Dragging state
        private bool _isDraggingPoint;
        private int _draggedPointIndex;
        private Ellipse _draggedMarker;

        public event EventHandler ROICompleted;

        public FinalPredictionPane()
        {
            InitializeComponent();
            _drawingService = new PolygonDrawingService();
            _polygonPointMarkers = new List<Ellipse>();
            _isDraggingPoint = false;
            _draggedPointIndex = -1;
        }

        private void EditingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || !ViewModel.IsDrawingMode)
                return;

            Point canvasPoint = e.GetPosition(EditingCanvas);

            // Check if clicking on an existing point marker (for dragging)
            for (int i = 0; i < _polygonPointMarkers.Count; i++)
            {
                var marker = _polygonPointMarkers[i];
                double markerX = Canvas.GetLeft(marker) + 6; // Center of marker (radius + 2)
                double markerY = Canvas.GetTop(marker) + 6;

                double distance = Math.Sqrt(
                    Math.Pow(canvasPoint.X - markerX, 2) +
                    Math.Pow(canvasPoint.Y - markerY, 2));

                if (distance < 10) // Click within 10 pixels of marker center
                {
                    // Start dragging this point
                    _isDraggingPoint = true;
                    _draggedPointIndex = i;
                    _draggedMarker = marker;

                    // Change marker appearance to show it's being dragged
                    marker.Fill = Brushes.Orange;
                    marker.Width = 12;
                    marker.Height = 12;
                    Canvas.SetLeft(marker, markerX - 6);
                    Canvas.SetTop(marker, markerY - 6);

                    EditingCanvas.CaptureMouse();
                    System.Diagnostics.Debug.WriteLine($"Started dragging point {i}");
                    return;
                }
            }

            // Not clicking on existing point - add new point
            if (ViewModel.CurrentDrawingMode == DrawingMode.Polygon)
            {
                // Convert to image coordinates
                Point imagePoint = CanvasToImageCoordinates(canvasPoint);

                // Add point to polygon (in IMAGE coordinates)
                _drawingService.AddPoint(imagePoint);

                // Add visual marker (in CANVAS coordinates)
                var marker = CreatePointMarker(canvasPoint);
                EditingCanvas.Children.Add(marker);
                _polygonPointMarkers.Add(marker);

                // Update polyline
                UpdatePolyline();

                System.Diagnostics.Debug.WriteLine($"Added point - Canvas: {canvasPoint}, Image: {imagePoint}");
            }
        }

        private void EditingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingPoint && _draggedPointIndex >= 0)
            {
                Point canvasPoint = e.GetPosition(EditingCanvas);
                Point imagePoint = CanvasToImageCoordinates(canvasPoint);

                // Update the point in the drawing service (IMAGE coordinates)
                _drawingService.CurrentPolygon[_draggedPointIndex] = imagePoint;

                // Update marker position (CANVAS coordinates)
                Canvas.SetLeft(_draggedMarker, canvasPoint.X - 6);
                Canvas.SetTop(_draggedMarker, canvasPoint.Y - 6);

                // Update polyline
                UpdatePolyline();
            }
        }

        private void EditingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingPoint)
            {
                // Stop dragging
                _isDraggingPoint = false;

                // Reset marker appearance
                if (_draggedMarker != null)
                {
                    _draggedMarker.Fill = Brushes.Yellow;
                    _draggedMarker.Width = 10;
                    _draggedMarker.Height = 10;

                    Point center = new Point(
                        Canvas.GetLeft(_draggedMarker) + 6,
                        Canvas.GetTop(_draggedMarker) + 6);
                    Canvas.SetLeft(_draggedMarker, center.X - 5);
                    Canvas.SetTop(_draggedMarker, center.Y - 5);
                }

                _draggedPointIndex = -1;
                _draggedMarker = null;

                EditingCanvas.ReleaseMouseCapture();
                System.Diagnostics.Debug.WriteLine("Stopped dragging point");
            }
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

        private Ellipse CreatePointMarker(Point canvasPoint)
        {
            var marker = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Yellow,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Cursor = Cursors.Hand // Show it's draggable
            };

            Canvas.SetLeft(marker, canvasPoint.X - 5);
            Canvas.SetTop(marker, canvasPoint.Y - 5);

            return marker;
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

                // Make sure polyline is behind the markers
                Canvas.SetZIndex(_currentPolyline, -1);

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
        /// Revert to original mask outline (clear user modifications)
        /// </summary>
        public void RevertToOriginalMask()
        {
            if (ViewModel == null || !ViewModel.IsDrawingMode)
            {
                System.Diagnostics.Debug.WriteLine("Not in drawing mode, nothing to revert");
                return;
            }

            try
            {
                // Clear current drawing
                ClearDrawing();

                // Reload the original mask outline
                ShowOriginalMaskOutline();

                System.Diagnostics.Debug.WriteLine("Reverted to original mask outline");

                MessageBox.Show(
                    "Reverted to original mask outline.\n\n" +
                    "Your modifications have been discarded.",
                    "Reverted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reverting to original mask: {ex.Message}");
                MessageBox.Show(
                    $"Error reverting: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Restore original mask (even after modifications were applied)
        /// </summary>
        public void RestoreOriginalMask()
        {
            if (ViewModel == null)
            {
                System.Diagnostics.Debug.WriteLine("ViewModel is null");
                return;
            }

            try
            {
                bool success = ViewModel.RestoreOriginalMask();

                if (success)
                {
                    // Clear any drawing state
                    ClearDrawing();

                    // Optionally re-enter drawing mode with original outline
                    if (ViewModel.IsDrawingMode)
                    {
                        ShowOriginalMaskOutline();
                    }

                    System.Diagnostics.Debug.WriteLine("Restored original mask successfully");

                    MessageBox.Show(
                        "Reverted to original mask.\n\n" +
                        "All modifications have been discarded.",
                        "Mask Restored",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "No original mask available to restore.",
                        "Cannot Restore",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring original mask: {ex.Message}");
                MessageBox.Show(
                    $"Error restoring mask: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearDrawing()
        {
            EditingCanvas.Children.Clear();
            _polygonPointMarkers.Clear();
            _currentPolyline = null;
            _drawingService.CancelDrawing();
            _isDraggingPoint = false;
            _draggedPointIndex = -1;
            _draggedMarker = null;
        }

        private void OnROICompleted()
        {
            ROICompleted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Convert canvas coordinates to image pixel coordinates
        /// </summary>
        private Point CanvasToImageCoordinates(Point canvasPoint)
        {
            if (ViewModel?.OriginalImage == null)
                return canvasPoint;

            double canvasWidth = EditingCanvas.ActualWidth;
            double canvasHeight = EditingCanvas.ActualHeight;

            int imageWidth = ViewModel.OriginalImage.PixelWidth;
            int imageHeight = ViewModel.OriginalImage.PixelHeight;

            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            double renderedWidth = imageWidth * scale;
            double renderedHeight = imageHeight * scale;

            double offsetX = (canvasWidth - renderedWidth) / 2;
            double offsetY = (canvasHeight - renderedHeight) / 2;

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

            double scaleX = canvasWidth / imageWidth;
            double scaleY = canvasHeight / imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            double renderedWidth = imageWidth * scale;
            double renderedHeight = imageHeight * scale;

            double offsetX = (canvasWidth - renderedWidth) / 2;
            double offsetY = (canvasHeight - renderedHeight) / 2;

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

            var imageContourPoints = ViewModel.GetMaskContourPoints();

            if (imageContourPoints.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No contour points found");
                return;
            }

            var canvasPoints = imageContourPoints.Select(p => ImageToCanvasCoordinates(p)).ToList();

            if (_originalMaskPolygon != null)
            {
                EditingCanvas.Children.Remove(_originalMaskPolygon);
            }

            _originalMaskPolygon = new Polygon
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent
            };
            _originalMaskPolygon.Points = new PointCollection(canvasPoints);
            Canvas.SetZIndex(_originalMaskPolygon, -2);

            EditingCanvas.Children.Add(_originalMaskPolygon);

            _drawingService.CurrentPolygon.Clear();
            _drawingService.CurrentPolygon.AddRange(imageContourPoints);

            // Add editable point markers for each contour point
            foreach (var imagePoint in imageContourPoints)
            {
                var canvasPoint = ImageToCanvasCoordinates(imagePoint);
                var marker = CreatePointMarker(canvasPoint);
                EditingCanvas.Children.Add(marker);
                _polygonPointMarkers.Add(marker);
            }

            UpdatePolyline();

            System.Diagnostics.Debug.WriteLine($"Displayed editable mask outline with {imageContourPoints.Count} points");
        }

        public void EnablePolygonDrawing()
        {
            _drawingService.StartDrawing(DrawingMode.Polygon);
            ViewModel?.EnableDrawingMode(DrawingMode.Polygon);

            ShowOriginalMaskOutline();

            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Polygon drawing enabled with editable outline");
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
    }
}