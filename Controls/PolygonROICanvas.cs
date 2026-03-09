using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using MURDOC_2024.Model;
using System.Collections.ObjectModel;

namespace MURDOC_2024.Controls
{
    /// <summary>
    /// Custom WPF Canvas supporting polygon and freehand ROI drawing for operator region-of-interest selection.
    /// Left-click adds polygon vertices; right-click or Enter closes the polygon; Escape cancels.
    /// Completed ROIs are stored in <see cref="ROIs"/> and raise <see cref="ROIAdded"/>.
    /// </summary>
    public class PolygonROICanvas : Canvas
    {
        private List<Point> _currentPolygonPoints = new List<Point>();
        private Polyline _currentPolyline;
        private bool _isDrawingPolygon;
        private ROIDrawMode _drawMode = ROIDrawMode.Polygon;

        public ObservableCollection<PolygonROI> ROIs { get; set; } = new ObservableCollection<PolygonROI>();

        public ROIDrawMode DrawMode
        {
            get => _drawMode;
            set
            {
                _drawMode = value;
                CancelCurrentDrawing();
            }
        }

        /// <summary>
        /// Initializes the canvas with a transparent background and wires up mouse and keyboard handlers.
        /// </summary>
        public PolygonROICanvas()
        {
            Background = Brushes.Transparent;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseRightButtonDown += OnMouseRightButtonDown;
            MouseMove += OnMouseMove;
            KeyDown += OnKeyDown;
        }

        /// <summary>Dispatches a left-click to the active drawing mode (polygon vertex or freehand start).</summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_drawMode == ROIDrawMode.Polygon)
            {
                DrawPolygonPoint(e.GetPosition(this));
            }
            else if (_drawMode == ROIDrawMode.Freehand)
            {
                StartFreehandDrawing(e.GetPosition(this));
            }
        }

        /// <summary>Adds a vertex to the current polygon and draws a yellow point marker on the canvas.</summary>
        private void DrawPolygonPoint(Point point)
        {
            _currentPolygonPoints.Add(point);

            if (_currentPolyline == null)
            {
                _currentPolyline = new Polyline
                {
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round
                };
                Children.Add(_currentPolyline);
            }

            _currentPolyline.Points.Add(point);

            // Add visual point marker
            var marker = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Brushes.Yellow
            };
            Canvas.SetLeft(marker, point.X - 3);
            Canvas.SetTop(marker, point.Y - 3);
            Children.Add(marker);

            _isDrawingPolygon = true;
        }

        /// <summary>Closes and finalizes the current polygon when right-clicked with at least 3 points.</summary>
        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right-click or double-click to finish polygon
            if (_isDrawingPolygon && _currentPolygonPoints.Count >= 3)
            {
                FinishPolygon();
            }
        }

        /// <summary>Handles Enter (finish polygon) and Escape (cancel drawing) keyboard shortcuts.</summary>
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _isDrawingPolygon && _currentPolygonPoints.Count >= 3)
            {
                FinishPolygon();
            }
            else if (e.Key == Key.Escape)
            {
                CancelCurrentDrawing();
            }
        }

        /// <summary>
        /// Converts the accumulated points into a filled yellow polygon, adds a context menu
        /// (Delete / Export Mask), creates a <see cref="PolygonROI"/> object, and raises <see cref="ROIAdded"/>.
        /// </summary>
        private void FinishPolygon()
        {
            if (_currentPolygonPoints.Count < 3) return;

            // Close the polygon
            _currentPolygonPoints.Add(_currentPolygonPoints[0]);

            // Create filled polygon for visualization
            var polygon = new Polygon
            {
                Points = new PointCollection(_currentPolygonPoints),
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0))
            };

            // Remove the polyline and markers, add filled polygon
            Children.Clear();
            Children.Add(polygon);

            // Create ROI object
            var roi = new PolygonROI
            {
                Points = new List<Point>(_currentPolygonPoints),
                Visual = polygon,
                Priority = 1.0
            };

            // Add context menu
            var contextMenu = new ContextMenu();
            var deleteItem = new MenuItem { Header = "Delete ROI" };
            deleteItem.Click += (s, args) => RemoveROI(roi);
            var exportItem = new MenuItem { Header = "Export Mask" };
            exportItem.Click += (s, args) => ExportROIMask(roi);
            contextMenu.Items.Add(deleteItem);
            contextMenu.Items.Add(exportItem);
            polygon.ContextMenu = contextMenu;

            ROIs.Add(roi);
            ROIAdded?.Invoke(this, roi);

            // Reset for next polygon
            _currentPolygonPoints.Clear();
            _currentPolyline = null;
            _isDrawingPolygon = false;
        }

        /// <summary>Discards the in-progress polygon and removes all temporary visual elements from the canvas.</summary>
        private void CancelCurrentDrawing()
        {
            _currentPolygonPoints.Clear();
            _currentPolyline = null;
            _isDrawingPolygon = false;

            // Remove temporary drawing elements
            var elementsToRemove = Children.OfType<UIElement>()
                .Where(e => e is Polyline || (e is Ellipse ellipse && ellipse.Width == 6))
                .ToList();
            foreach (var element in elementsToRemove)
            {
                Children.Remove(element);
            }
        }

        /// <summary>Begins a freehand stroke, capturing the mouse to track continuous movement.</summary>
        private void StartFreehandDrawing(Point point)
        {
            _currentPolygonPoints.Clear();
            _currentPolygonPoints.Add(point);

            _currentPolyline = new Polyline
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            Children.Add(_currentPolyline);
            _currentPolyline.Points.Add(point);

            _isDrawingPolygon = true;
            CaptureMouse();
        }

        /// <summary>Appends points to the freehand polyline while the left button is held.</summary>
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_drawMode == ROIDrawMode.Freehand && _isDrawingPolygon && e.LeftButton == MouseButtonState.Pressed)
            {
                var point = e.GetPosition(this);
                _currentPolygonPoints.Add(point);
                _currentPolyline.Points.Add(point);
            }
        }

        /// <summary>Releases the mouse capture and finalizes the freehand polygon on button release.</summary>
        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_drawMode == ROIDrawMode.Freehand && _isDrawingPolygon)
            {
                ReleaseMouseCapture();
                if (_currentPolygonPoints.Count >= 3)
                {
                    FinishPolygon();
                }
            }
        }

        /// <summary>Removes an ROI's visual from the canvas and raises <see cref="ROIRemoved"/>.</summary>
        private void RemoveROI(PolygonROI roi)
        {
            Children.Remove(roi.Visual);
            ROIs.Remove(roi);
            ROIRemoved?.Invoke(this, roi);
        }

        /// <summary>Generates a binary mask for the ROI and prompts the user to save it as a PNG file.</summary>
        private void ExportROIMask(PolygonROI roi)
        {
            var mask = roi.CreateBinaryMask((int)ActualWidth, (int)ActualHeight);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                DefaultExt = ".png",
                FileName = $"roi_mask_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog() == true)
            {
                SaveBitmapSourceAsPng(mask, dialog.FileName);
            }
        }

        /// <summary>Encodes a <see cref="BitmapSource"/> as PNG and writes it to the specified file path.</summary>
        private void SaveBitmapSourceAsPng(BitmapSource bitmap, string filepath)
        {
            using (var fileStream = new System.IO.FileStream(filepath, System.IO.FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fileStream);
            }
        }

        public event EventHandler<PolygonROI> ROIAdded;
        public event EventHandler<PolygonROI> ROIRemoved;
    }

    public enum ROIDrawMode
    {
        Polygon,    // Click points to create vertices
        Freehand,   // Draw freely with mouse
        Rectangle   // Simple rectangle (for backward compatibility)
    }
}
