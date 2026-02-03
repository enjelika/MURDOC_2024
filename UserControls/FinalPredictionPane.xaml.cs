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

        // Zoom and Pan state
        private double _zoomLevel = 1.0;
        private Point _panOffset = new Point(0, 0);
        private bool _isPanning = false;
        private Point _lastPanPosition;
        private const double MIN_ZOOM = 0.5;
        private const double MAX_ZOOM = 5.0;
        private const double ZOOM_STEP = 0.1;

        // Add ScaleTransform and TranslateTransform for the canvas
        private ScaleTransform _scaleTransform;
        private TranslateTransform _translateTransform;
        private TransformGroup _transformGroup;

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

            // Initialize transforms
            _scaleTransform = new ScaleTransform(1.0, 1.0);
            _translateTransform = new TranslateTransform(0, 0);
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);
        }

        /// <summary>
        /// Set up zoom and pan when entering drawing mode
        /// </summary>
        private void InitializeZoomPan()
        {
            // Apply transforms to the ENTIRE GRID (not just canvas)
            LayeredImageGrid.RenderTransform = _transformGroup;
            LayeredImageGrid.RenderTransformOrigin = new Point(0.5, 0.5);

            // Subscribe to mouse wheel for zoom on the parent grid
            LayeredImageGrid.MouseWheel += EditingCanvas_MouseWheel;

            // Pan handlers stay on EditingCanvas for better control
            EditingCanvas.MouseDown += EditingCanvas_MouseDown_Pan;
            EditingCanvas.MouseUp += EditingCanvas_MouseUp_Pan;
            EditingCanvas.MouseMove += EditingCanvas_MouseMove_Pan;

            System.Diagnostics.Debug.WriteLine("Zoom/Pan initialized on entire image grid");
        }

        /// <summary>
        /// Clean up zoom and pan
        /// </summary>
        private void CleanupZoomPan()
        {
            LayeredImageGrid.MouseWheel -= EditingCanvas_MouseWheel;
            EditingCanvas.MouseDown -= EditingCanvas_MouseDown_Pan;
            EditingCanvas.MouseUp -= EditingCanvas_MouseUp_Pan;
            EditingCanvas.MouseMove -= EditingCanvas_MouseMove_Pan;

            // Reset transforms
            _zoomLevel = 1.0;
            _panOffset = new Point(0, 0);
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;

            LayeredImageGrid.RenderTransform = null;

            System.Diagnostics.Debug.WriteLine("Zoom/Pan cleaned up");
        }

        /// <summary>
        /// Handle mouse wheel zoom
        /// </summary>
        private void EditingCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                return; // Only zoom with Ctrl held

            double zoomDelta = e.Delta > 0 ? ZOOM_STEP : -ZOOM_STEP;
            double newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, _zoomLevel + zoomDelta));

            if (newZoom != _zoomLevel)
            {
                // Get mouse position relative to the LayeredImageGrid
                Point mousePos = e.GetPosition(LayeredImageGrid);

                // Calculate zoom factor
                double zoomFactor = newZoom / _zoomLevel;

                // Adjust pan to zoom towards mouse position
                _panOffset.X = mousePos.X - (mousePos.X - _panOffset.X) * zoomFactor;
                _panOffset.Y = mousePos.Y - (mousePos.Y - _panOffset.Y) * zoomFactor;

                _zoomLevel = newZoom;

                // Apply transforms
                _scaleTransform.ScaleX = _zoomLevel;
                _scaleTransform.ScaleY = _zoomLevel;
                _translateTransform.X = _panOffset.X;
                _translateTransform.Y = _panOffset.Y;

                // Update zoom display
                if (ViewModel != null)
                    ViewModel.ZoomLevelText = $"{(_zoomLevel * 100):F0}%";

                System.Diagnostics.Debug.WriteLine($"Zoom: {_zoomLevel:F2}x");
            }

            e.Handled = true;
        }

        /// <summary>
        /// Start panning with middle mouse button
        /// </summary>
        private void EditingCanvas_MouseDown_Pan(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
            {
                _isPanning = true;
                _lastPanPosition = e.GetPosition(this);
                EditingCanvas.CaptureMouse();
                EditingCanvas.Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Stop panning
        /// </summary>
        private void EditingCanvas_MouseUp_Pan(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
            {
                _isPanning = false;
                EditingCanvas.ReleaseMouseCapture();
                EditingCanvas.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handle panning motion
        /// </summary>
        private void EditingCanvas_MouseMove_Pan(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point currentPos = e.GetPosition(this);
                Vector delta = currentPos - _lastPanPosition;

                _panOffset.X += delta.X;
                _panOffset.Y += delta.Y;

                _translateTransform.X = _panOffset.X;
                _translateTransform.Y = _panOffset.Y;

                _lastPanPosition = currentPos;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Zoom in programmatically
        /// </summary>
        public void ZoomIn()
        {
            double newZoom = Math.Min(MAX_ZOOM, _zoomLevel + ZOOM_STEP * 2);
            SetZoom(newZoom);
        }

        /// <summary>
        /// Zoom out programmatically
        /// </summary>
        public void ZoomOut()
        {
            double newZoom = Math.Max(MIN_ZOOM, _zoomLevel - ZOOM_STEP * 2);
            SetZoom(newZoom);
        }

        /// <summary>
        /// Reset zoom to 100%
        /// </summary>
        public void ResetZoom()
        {
            SetZoom(1.0);
            _panOffset = new Point(0, 0);
            _translateTransform.X = 0;
            _translateTransform.Y = 0;
        }

        /// <summary>
        /// Set specific zoom level
        /// </summary>
        private void SetZoom(double zoom)
        {
            _zoomLevel = zoom;
            _scaleTransform.ScaleX = _zoomLevel;
            _scaleTransform.ScaleY = _zoomLevel;
            ViewModel.ZoomLevelText = $"{(_zoomLevel * 100):F0}%";
            System.Diagnostics.Debug.WriteLine($"Zoom set to: {_zoomLevel:F2}x");
        }

        /// <summary>
        /// Convert canvas coordinates to image pixel coordinates (accounting for zoom/pan)
        /// </summary>
        private Point CanvasToImageCoordinates(Point canvasPoint)
        {
            if (ViewModel?.OriginalImage == null)
                return canvasPoint;

            // First, get the position relative to the LayeredImageGrid
            // (This accounts for zoom/pan since the grid is transformed)

            double canvasWidth = LayeredImageGrid.ActualWidth;
            double canvasHeight = LayeredImageGrid.ActualHeight;

            int imageWidth = ViewModel.OriginalImage.PixelWidth;
            int imageHeight = ViewModel.OriginalImage.PixelHeight;

            // Calculate scale for Stretch="Uniform"
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
        /// Convert image pixel coordinates to canvas coordinates (accounting for zoom/pan)
        /// </summary>
        private Point ImageToCanvasCoordinates(Point imagePoint)
        {
            if (ViewModel?.OriginalImage == null)
                return imagePoint;

            double canvasWidth = LayeredImageGrid.ActualWidth;
            double canvasHeight = LayeredImageGrid.ActualHeight;

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

        public void EnablePolygonDrawing()
        {
            _drawingService.StartDrawing(DrawingMode.Polygon);
            ViewModel?.EnableDrawingMode(DrawingMode.Polygon);

            InitializeZoomPan(); // INITIALIZE ZOOM/PAN
            ShowOriginalMaskOutline();

            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Polygon drawing enabled with editable outline and zoom/pan");
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

        public void EnableFreehandDrawing()
        {
            _drawingService.StartDrawing(DrawingMode.Freehand);
            ViewModel?.EnableDrawingMode(DrawingMode.Freehand);
            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Freehand drawing enabled");
        }

        public void CancelDrawing()
        {
            ClearDrawing();
            CleanupZoomPan(); // CLEANUP ZOOM/PAN
            ViewModel?.DisableDrawingMode();
            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Drawing cancelled");
        }

        public void ResetDrawing()
        {
            ClearDrawing();
            CleanupZoomPan(); // CLEANUP ZOOM/PAN

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

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomIn();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomOut();
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
        }
    }
}