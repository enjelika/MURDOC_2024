using MURDOC_2024.Model;
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
        private bool _isZoomPanInitialized = false;

        // Transform objects for zoom/pan
        private ScaleTransform _scaleTransform;
        private TranslateTransform _translateTransform;
        private TransformGroup _transformGroup;

        // Polygon editing state
        private bool _isDraggingPoint;
        private int _draggedPointIndex;
        private Ellipse _draggedMarker;
        private PointEditMode _currentPointEditMode = PointEditMode.Add;

        // Rank editing state
        private bool _isRankBrushMode;
        private RankBrushMode _currentBrushMode;
        private double _currentBrushSize = 20;
        private double _currentBrushStrength = 0.5;
        private bool _isRankPainting;

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
        /// Public entry point: apply polygon, save rank, save to LoRA folders.
        /// Called from MainWindow on Save.
        /// </summary>
        public void SaveAllChanges()
        {
            System.Diagnostics.Debug.WriteLine("=== SaveAllChanges START ===");

            // Apply polygon to binary mask (once)
            bool polyResult = ApplyPolygonChangesIfModified();
            System.Diagnostics.Debug.WriteLine($"Polygon applied: {polyResult}");

            // Save all modifications to LoRA training folders (once)
            ViewModel?.SaveAllModifications();
            System.Diagnostics.Debug.WriteLine("=== SaveAllChanges END ===");
        }

        #region Unified Edit Mode (Current)

        /// <summary>
        /// Enter unified edit mode (polygon + rank brush)
        /// </summary>
        public void EnterUnifiedEditMode(RankBrushMode brushMode, double brushSize, double brushStrength)
        {
            // Enable polygon drawing
            _drawingService.StartDrawing(DrawingMode.Polygon);
            ViewModel?.EnableDrawingMode(DrawingMode.Polygon);

            // Enable rank brush
            _isRankBrushMode = true;
            _currentBrushMode = brushMode;
            _currentBrushSize = brushSize;
            _currentBrushStrength = brushStrength;

            // Initialize zoom/pan
            InitializeZoomPan();

            // Show canvas and original outline
            EditingCanvas.Visibility = Visibility.Visible;
            EditingCanvas.Cursor = Cursors.Cross;
            ShowOriginalMaskOutline();

            // Default to polygon editing mode
            _currentEditSubMode = EditSubMode.PolygonEditing;

            System.Diagnostics.Debug.WriteLine("Entered unified edit mode (polygon + rank)");
        }

        /// <summary>
        /// Apply polygon modifications to the binary mask (if any)
        /// </summary>
        public bool ApplyPolygonChangesIfModified()
        {
            // Check if we're in unified edit mode with polygon points
            if (!ViewModel.IsDrawingMode || _drawingService.CurrentPolygon.Count < 3)
            {
                System.Diagnostics.Debug.WriteLine("No polygon modifications to apply");
                return true; // No polygon to apply, that's okay
            }

            try
            {
                // Check if polygon has been modified from original
                var currentPolygon = new List<Point>(_drawingService.CurrentPolygon);

                System.Diagnostics.Debug.WriteLine($"Applying polygon with {currentPolygon.Count} points to binary mask");

                // Convert polygon to mask and update ViewModel
                bool success = ViewModel.UpdateMaskFromPolygon(currentPolygon);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Successfully applied polygon changes to binary mask");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply polygon changes");
                    MessageBox.Show(
                        "Failed to apply polygon changes to mask.\n\nPlease try again.",
                        "Polygon Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying polygon changes: {ex.Message}");
                MessageBox.Show(
                    $"Error applying polygon changes:\n{ex.Message}",
                    "Polygon Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Exit unified edit mode
        /// </summary>
        public void ExitUnifiedEditMode()
        {
            // Disable polygon drawing
            _drawingService.CancelDrawing();
            ViewModel?.DisableDrawingMode();

            // Disable rank brush
            _isRankBrushMode = false;
            _isRankPainting = false;

            // Clear sub-mode
            _currentEditSubMode = EditSubMode.None;

            // Cleanup
            ClearDrawing();
            CleanupZoomPan();

            EditingCanvas.Visibility = Visibility.Collapsed;
            EditingCanvas.Cursor = Cursors.Arrow;

            if (EditingCanvas.IsMouseCaptured)
            {
                EditingCanvas.ReleaseMouseCapture();
            }

            System.Diagnostics.Debug.WriteLine("Exited unified edit mode");
        }

        /// <summary>
        /// Set point editing mode (Add or Remove)
        /// </summary>
        public void SetPointEditMode(PointEditMode mode)
        {
            _currentPointEditMode = mode;

            // Switch to polygon editing sub-mode
            _currentEditSubMode = EditSubMode.PolygonEditing;
            EditingCanvas.Cursor = Cursors.Hand; // Hand cursor for point editing

            System.Diagnostics.Debug.WriteLine($"Point edit mode set to: {mode} (Polygon editing active)");
        }

        /// <summary>
        /// Switch the active editing tool between polygon editing and rank painting.
        /// </summary>
        public void SetEditingToolMode(string mode)
        {
            if (mode == "Polygon")
            {
                _currentEditSubMode = EditSubMode.PolygonEditing;
                EditingCanvas.Cursor = Cursors.Hand;
                System.Diagnostics.Debug.WriteLine("Switched to Polygon Editing tool");
            }
            else if (mode == "RankPaint")
            {
                _currentEditSubMode = EditSubMode.RankBrushing;
                EditingCanvas.Cursor = Cursors.Cross;
                System.Diagnostics.Debug.WriteLine("Switched to Rank Painting tool");
            }
        }

        /// <summary>
        /// Update rank brush parameters while in edit mode
        /// </summary>
        public void UpdateRankBrush(RankBrushMode mode, double brushSize, double brushStrength)
        {
            _currentBrushMode = mode;
            _currentBrushSize = brushSize;
            _currentBrushStrength = brushStrength;

            // Switch to rank brushing sub-mode
            _currentEditSubMode = EditSubMode.RankBrushing;
            EditingCanvas.Cursor = Cursors.Cross; // Cross cursor for painting

            string modeText = mode == RankBrushMode.Increase ? "Increase" : "Decrease";
            System.Diagnostics.Debug.WriteLine($"Brush updated: {modeText}, Size: {brushSize}px, Strength: {brushStrength:P0} (Rank brushing active)");
        }

        /// <summary>
        /// Save all modifications to disk (applies polygon changes first)
        /// </summary>
        public void SaveModifiedRankMap()
        {
            if (ViewModel == null)
            {
                throw new Exception("ViewModel is null");
            }

            try
            {
                // STEP 1: Apply polygon modifications to binary mask (if any)
                bool polygonApplied = ApplyPolygonChangesIfModified();

                if (!polygonApplied)
                {
                    // User was notified by ApplyPolygonChangesIfModified
                    return;
                }

                // STEP 2: Save all modifications (binary mask + rank map)
                ViewModel.SaveAllModifications();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving modifications: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Zoom and Pan

        /// <summary>
        /// Set up zoom and pan when entering edit mode
        /// </summary>
        private void InitializeZoomPan()
        {
            if (_isZoomPanInitialized)
            {
                System.Diagnostics.Debug.WriteLine("Zoom/Pan already initialized, skipping");
                return;
            }

            LayeredImageGrid.RenderTransform = _transformGroup;
            LayeredImageGrid.RenderTransformOrigin = new Point(0.5, 0.5);

            LayeredImageGrid.MouseWheel += EditingCanvas_MouseWheel;
            EditingCanvas.MouseDown += EditingCanvas_MouseDown_Pan;
            EditingCanvas.MouseUp += EditingCanvas_MouseUp_Pan;
            EditingCanvas.MouseMove += EditingCanvas_MouseMove_Pan;

            _isZoomPanInitialized = true;
            System.Diagnostics.Debug.WriteLine("Zoom/Pan initialized");
        }

        /// <summary>
        /// Clean up zoom and pan
        /// </summary>
        private void CleanupZoomPan()
        {
            if (!_isZoomPanInitialized)
            {
                System.Diagnostics.Debug.WriteLine("Zoom/Pan not initialized, skipping cleanup");
                return;
            }

            LayeredImageGrid.MouseWheel -= EditingCanvas_MouseWheel;
            EditingCanvas.MouseDown -= EditingCanvas_MouseDown_Pan;
            EditingCanvas.MouseUp -= EditingCanvas_MouseUp_Pan;
            EditingCanvas.MouseMove -= EditingCanvas_MouseMove_Pan;

            _zoomLevel = 1.0;
            _panOffset = new Point(0, 0);
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;

            LayeredImageGrid.RenderTransform = null;

            _isZoomPanInitialized = false;
            System.Diagnostics.Debug.WriteLine("Zoom/Pan cleaned up");
        }

        private void EditingCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                return;

            double zoomDelta = e.Delta > 0 ? ZOOM_STEP : -ZOOM_STEP;
            double newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, _zoomLevel + zoomDelta));

            if (newZoom != _zoomLevel)
            {
                Point mousePos = e.GetPosition(LayeredImageGrid);
                double zoomFactor = newZoom / _zoomLevel;

                _panOffset.X = mousePos.X - (mousePos.X - _panOffset.X) * zoomFactor;
                _panOffset.Y = mousePos.Y - (mousePos.Y - _panOffset.Y) * zoomFactor;

                _zoomLevel = newZoom;

                _scaleTransform.ScaleX = _zoomLevel;
                _scaleTransform.ScaleY = _zoomLevel;
                _translateTransform.X = _panOffset.X;
                _translateTransform.Y = _panOffset.Y;

                if (ViewModel != null)
                    ViewModel.ZoomLevelText = $"{(_zoomLevel * 100):F0}%";

                System.Diagnostics.Debug.WriteLine($"Zoom: {_zoomLevel:F2}x");
            }

            e.Handled = true;
        }

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

        private void EditingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // =================================================================
            // RANK BRUSH PAINTING (only if in rank brushing sub-mode)
            // =================================================================
            if (_isRankPainting && _isRankBrushMode && _currentEditSubMode == EditSubMode.RankBrushing)
            {
                Point canvasPoint = e.GetPosition(EditingCanvas);
                Point imagePoint = CanvasToImageCoordinates(canvasPoint);

                ViewModel.ApplyRankBrush(imagePoint, _currentBrushMode, _currentBrushSize, _currentBrushStrength);

                e.Handled = true;
                return;
            }

            // =================================================================
            // PANNING (works in both modes if Space is held)
            // =================================================================
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
                return;
            }

            // =================================================================
            // POLYGON POINT DRAGGING (only if in polygon editing sub-mode)
            // =================================================================
            if (_isDraggingPoint && _draggedPointIndex >= 0 && _draggedMarker != null &&
                _currentEditSubMode == EditSubMode.PolygonEditing)
            {
                Point canvasPoint = e.GetPosition(EditingCanvas);
                Point imagePoint = CanvasToImageCoordinates(canvasPoint);

                if (_draggedPointIndex < _drawingService.CurrentPolygon.Count)
                {
                    _drawingService.CurrentPolygon[_draggedPointIndex] = imagePoint;
                }

                Canvas.SetLeft(_draggedMarker, canvasPoint.X - 6);
                Canvas.SetTop(_draggedMarker, canvasPoint.Y - 6);

                UpdatePolyline();

                e.Handled = true;
            }
        }

        public void ZoomIn()
        {
            double newZoom = Math.Min(MAX_ZOOM, _zoomLevel + ZOOM_STEP * 2);
            SetZoom(newZoom);
        }

        public void ZoomOut()
        {
            double newZoom = Math.Max(MIN_ZOOM, _zoomLevel - ZOOM_STEP * 2);
            SetZoom(newZoom);
        }

        public void ResetZoom()
        {
            SetZoom(1.0);
            _panOffset = new Point(0, 0);
            _translateTransform.X = 0;
            _translateTransform.Y = 0;
        }

        private void SetZoom(double zoom)
        {
            _zoomLevel = zoom;
            _scaleTransform.ScaleX = _zoomLevel;
            _scaleTransform.ScaleY = _zoomLevel;
            if (ViewModel != null)
                ViewModel.ZoomLevelText = $"{(_zoomLevel * 100):F0}%";
            System.Diagnostics.Debug.WriteLine($"Zoom set to: {_zoomLevel:F2}x");
        }

        private enum EditSubMode
        {
            None,
            PolygonEditing,
            RankBrushing
        }

        private EditSubMode _currentEditSubMode = EditSubMode.None;

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
        private void ResetZoom_Click(object sender, RoutedEventArgs e) => ResetZoom();

        #endregion

        #region Coordinate Conversion

        private Point CanvasToImageCoordinates(Point canvasPoint)
        {
            if (ViewModel?.OriginalImage == null)
                return canvasPoint;

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

            double imageX = (canvasPoint.X - offsetX) / scale;
            double imageY = (canvasPoint.Y - offsetY) / scale;

            return new Point(imageX, imageY);
        }

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

        #endregion

        #region Mouse Event Handlers

        private void EditingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null)
                return;

            // Don't interfere with panning
            if (Keyboard.IsKeyDown(Key.Space))
                return;

            Point canvasPoint = e.GetPosition(EditingCanvas);

            // =================================================================
            // UNIFIED EDIT MODE - Check which sub-mode is active
            // =================================================================
            if (_isRankBrushMode && ViewModel.IsDrawingMode)
            {
                // In unified mode - check sub-mode
                if (_currentEditSubMode == EditSubMode.RankBrushing)
                {
                    // RANK BRUSH PAINTING
                    _isRankPainting = true;
                    Point imagePoint = CanvasToImageCoordinates(canvasPoint);

                    ViewModel.ApplyRankBrush(imagePoint, _currentBrushMode, _currentBrushSize, _currentBrushStrength);

                    EditingCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                else if (_currentEditSubMode == EditSubMode.PolygonEditing)
                {
                    // POLYGON POINT EDITING
                    HandlePolygonPointClick(canvasPoint);
                    e.Handled = true;
                    return;
                }
            }

            // =================================================================
            // LEGACY: Separate modes (shouldn't happen in unified mode)
            // =================================================================
            if (_isRankBrushMode)
            {
                _isRankPainting = true;
                Point imagePoint = CanvasToImageCoordinates(canvasPoint);
                ViewModel.ApplyRankBrush(imagePoint, _currentBrushMode, _currentBrushSize, _currentBrushStrength);
                EditingCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (ViewModel.IsDrawingMode)
            {
                HandlePolygonPointClick(canvasPoint);
                e.Handled = true;
                return;
            }
        }

        /// <summary>
        /// Handle polygon point clicking (add, remove, or drag)
        /// </summary>
        private void HandlePolygonPointClick(Point canvasPoint)
        {
            // Check if clicking near an existing point marker (within 10 pixels)
            for (int i = 0; i < _polygonPointMarkers.Count; i++)
            {
                var marker = _polygonPointMarkers[i];
                double markerX = Canvas.GetLeft(marker) + 5;
                double markerY = Canvas.GetTop(marker) + 5;

                double distance = Math.Sqrt(
                    Math.Pow(canvasPoint.X - markerX, 2) +
                    Math.Pow(canvasPoint.Y - markerY, 2));

                if (distance < 10)
                {
                    // REMOVE MODE: Delete the point
                    if (_currentPointEditMode == PointEditMode.Remove)
                    {
                        if (_polygonPointMarkers.Count <= 3)
                        {
                            MessageBox.Show("Cannot remove point - polygon needs at least 3 points",
                                "Minimum Points", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        EditingCanvas.Children.Remove(marker);
                        _polygonPointMarkers.RemoveAt(i);
                        _drawingService.CurrentPolygon.RemoveAt(i);
                        UpdatePolyline();

                        System.Diagnostics.Debug.WriteLine($"Removed point {i}");
                        return;
                    }

                    // ADD MODE: Start dragging existing point
                    _isDraggingPoint = true;
                    _draggedPointIndex = i;
                    _draggedMarker = marker;

                    marker.Fill = Brushes.Orange;
                    marker.Width = 12;
                    marker.Height = 12;
                    Canvas.SetLeft(marker, markerX - 6);
                    Canvas.SetTop(marker, markerY - 6);

                    EditingCanvas.CaptureMouse();
                    System.Diagnostics.Debug.WriteLine($"Started dragging existing point {i}");

                    return;
                }
            }

            // CREATE NEW POINT (Add mode only)
            if (_currentPointEditMode == PointEditMode.Add && ViewModel.CurrentDrawingMode == DrawingMode.Polygon)
            {
                Point imagePoint = CanvasToImageCoordinates(canvasPoint);

                _drawingService.AddPoint(imagePoint);

                var marker = CreatePointMarker(canvasPoint);

                _isDraggingPoint = true;
                _draggedPointIndex = _drawingService.CurrentPolygon.Count - 1;
                _draggedMarker = marker;

                marker.Fill = Brushes.Orange;
                marker.Width = 12;
                marker.Height = 12;
                Canvas.SetLeft(marker, canvasPoint.X - 6);
                Canvas.SetTop(marker, canvasPoint.Y - 6);

                EditingCanvas.Children.Add(marker);
                _polygonPointMarkers.Add(marker);
                EditingCanvas.CaptureMouse();
                UpdatePolyline();

                System.Diagnostics.Debug.WriteLine($"Created new point {_draggedPointIndex} and started dragging");
            }
        }

        private void EditingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Stop rank painting
            if (_isRankPainting)
            {
                _isRankPainting = false;
                EditingCanvas.ReleaseMouseCapture();
                System.Diagnostics.Debug.WriteLine("Stopped rank painting");
                e.Handled = true;
                return;
            }

            // Stop polygon point dragging
            if (_isDraggingPoint)
            {
                try
                {
                    Point finalCanvasPoint = e.GetPosition(EditingCanvas);
                    Point finalImagePoint = CanvasToImageCoordinates(finalCanvasPoint);

                    if (_draggedPointIndex >= 0 && _draggedPointIndex < _drawingService.CurrentPolygon.Count)
                    {
                        _drawingService.CurrentPolygon[_draggedPointIndex] = finalImagePoint;
                    }

                    if (_draggedMarker != null)
                    {
                        _draggedMarker.Fill = Brushes.Yellow;
                        _draggedMarker.Width = 10;
                        _draggedMarker.Height = 10;

                        Canvas.SetLeft(_draggedMarker, finalCanvasPoint.X - 5);
                        Canvas.SetTop(_draggedMarker, finalCanvasPoint.Y - 5);
                    }

                    UpdatePolyline();
                }
                finally
                {
                    _isDraggingPoint = false;
                    _draggedPointIndex = -1;
                    _draggedMarker = null;

                    if (EditingCanvas.IsMouseCaptured)
                    {
                        EditingCanvas.ReleaseMouseCapture();
                    }
                }

                e.Handled = true;
            }
        }

        private void EditingCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Exit unified edit mode on right-click
            if (ViewModel?.IsDrawingMode == true || _isRankBrushMode)
            {
                bool hasPolygonChanges = _drawingService.CurrentPolygon.Count >= 3;
                bool hasRankChanges = ViewModel.HasAnyModifications;

                if (hasPolygonChanges || hasRankChanges)
                {
                    string warningMessage = "You have unsaved changes:\n\n";
                    if (hasPolygonChanges)
                        warningMessage += "⚠️ Polygon edits\n";
                    if (hasRankChanges)
                        warningMessage += "⚠️ Rank map edits\n";

                    warningMessage += "\nSave changes before exiting?";

                    var result = MessageBox.Show(
                        warningMessage,
                        "Save Changes?",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Save, then exit
                        SaveAllChanges();
                        ExitUnifiedEditMode();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var mainWindow = Application.Current.MainWindow as MainWindow;
                            mainWindow?.ViewModel?.EditorControlsVM?.ExitEditMode();
                        });
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        // Exit without saving
                        ExitUnifiedEditMode();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var mainWindow = Application.Current.MainWindow as MainWindow;
                            mainWindow?.ViewModel?.EditorControlsVM?.ExitEditMode();
                        });
                    }
                    // Cancel = stay in edit mode, do nothing
                }
                else
                {
                    // No changes, just exit
                    ExitUnifiedEditMode();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var mainWindow = Application.Current.MainWindow as MainWindow;
                        mainWindow?.ViewModel?.EditorControlsVM?.ExitEditMode();
                    });
                }

                e.Handled = true;
                return;
            }
        }
        #endregion

        #region Polygon Helpers

        private Ellipse CreatePointMarker(Point canvasPoint)
        {
            var marker = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Yellow,
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Cursor = Cursors.Hand
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
                var canvasPoints = _drawingService.CurrentPolygon
                    .Select(p => ImageToCanvasCoordinates(p))
                    .ToList();

                _currentPolyline = new Polyline
                {
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    Points = new PointCollection(canvasPoints)
                };

                Canvas.SetZIndex(_currentPolyline, -1);

                EditingCanvas.Children.Add(_currentPolyline);
            }
        }

        private void ShowOriginalMaskOutline()
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

            // Add editable point markers
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

        #endregion
    }
}