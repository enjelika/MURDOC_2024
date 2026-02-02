using MURDOC_2024.Services;
using MURDOC_2024.ViewModel;
using System;
using System.Collections.Generic;
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
                Point clickPoint = e.GetPosition(EditingCanvas);

                // Add point to polygon
                _drawingService.AddPoint(clickPoint);

                // Add visual marker
                var marker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Brushes.Yellow,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };

                Canvas.SetLeft(marker, clickPoint.X - 4);
                Canvas.SetTop(marker, clickPoint.Y - 4);
                EditingCanvas.Children.Add(marker);
                _polygonPointMarkers.Add(marker);

                // Update polyline
                UpdatePolyline();

                System.Diagnostics.Debug.WriteLine($"Added polygon point: {clickPoint}");
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
                _currentPolyline = _drawingService.CreateVisualPolyline(
                    _drawingService.CurrentPolygon,
                    Brushes.Yellow,
                    2
                );

                EditingCanvas.Children.Add(_currentPolyline);
            }
        }

        private void CompletePolygon()
        {
            // Convert polygon to mask and update
            ViewModel.UpdateMaskFromPolygon(new List<Point>(_drawingService.CurrentPolygon));

            // Clear drawing
            ClearDrawing();

            // Exit drawing mode
            ViewModel.DisableDrawingMode();

            MessageBox.Show("Polygon applied to mask!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

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
            System.Diagnostics.Debug.WriteLine("FinalPredictionPane: Polygon drawing enabled");
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
    }
}