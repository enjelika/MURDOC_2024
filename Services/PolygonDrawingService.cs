using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MURDOC_2024.Services
{
    public enum DrawingMode
    {
        None,
        Polygon,
        Freehand,
        Eraser
    }

    /// <summary>
    /// Stateful service managing polygon and freehand drawing operations for the MICA editor.
    /// Converts completed polygon point lists into binary masks and WPF visual shapes.
    /// </summary>
    public class PolygonDrawingService
    {
        public DrawingMode CurrentMode { get; set; } = DrawingMode.None;
        public List<Point> CurrentPolygon { get; private set; } = new List<Point>();
        public bool IsDrawing => CurrentPolygon.Count > 0;

        private int _imageWidth;
        private int _imageHeight;

        /// <summary>Sets the reference image dimensions used when converting polygon points to masks.</summary>
        public void SetImageDimensions(int width, int height)
        {
            _imageWidth = width;
            _imageHeight = height;
        }

        /// <summary>Activates the specified drawing mode and clears any previously accumulated points.</summary>
        public void StartDrawing(DrawingMode mode)
        {
            CurrentMode = mode;
            CurrentPolygon.Clear();
        }

        /// <summary>Appends a point to the current polygon if the drawing mode is Polygon.</summary>
        public void AddPoint(Point point)
        {
            if (CurrentMode == DrawingMode.Polygon)
            {
                CurrentPolygon.Add(point);
            }
        }

        /// <summary>Closes the polygon if at least 3 points have been placed, then resets the drawing mode.</summary>
        public void CompletePolygon()
        {
            // Close the polygon if it has at least 3 points
            if (CurrentPolygon.Count >= 3)
            {
                // Polygon is complete, ready to convert to mask
                CurrentMode = DrawingMode.None;
            }
        }

        /// <summary>Discards all accumulated polygon points and resets the drawing mode to None.</summary>
        public void CancelDrawing()
        {
            CurrentPolygon.Clear();
            CurrentMode = DrawingMode.None;
        }

        /// <summary>
        /// Convert polygon points to binary mask
        /// </summary>
        public byte[] ConvertPolygonToMask(List<Point> points, int width, int height)
        {
            if (points == null || points.Count < 3)
                return null;

            // Create a drawing visual
            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                // Draw black background
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

                // Draw white polygon
                var geometry = new PathGeometry(new[]
                {
                    new PathFigure(
                        points[0],
                        points.Skip(1).Select(p => new LineSegment(p, true)),
                        true // Close the path
                    )
                });

                context.DrawGeometry(Brushes.White, null, geometry);
            }

            // Render to bitmap
            var renderTarget = new RenderTargetBitmap(
                width, height, 96, 96,
                PixelFormats.Pbgra32
            );
            renderTarget.Render(drawingVisual);

            // Convert to grayscale byte array
            var grayBitmap = new FormatConvertedBitmap(renderTarget, PixelFormats.Gray8, null, 0);

            int stride = width;
            byte[] pixels = new byte[height * stride];
            grayBitmap.CopyPixels(pixels, stride, 0);

            return pixels;
        }

        /// <summary>
        /// Create visual polygon for display on canvas
        /// </summary>
        public Polygon CreateVisualPolygon(List<Point> points, Brush stroke, double strokeThickness = 2)
        {
            if (points == null || points.Count == 0)
                return null;

            var polygon = new Polygon
            {
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0)), // Semi-transparent yellow
                Points = new PointCollection(points)
            };

            return polygon;
        }

        /// <summary>
        /// Create polyline for incomplete polygon (during drawing)
        /// </summary>
        public Polyline CreateVisualPolyline(List<Point> points, Brush stroke, double strokeThickness = 2)
        {
            if (points == null || points.Count == 0)
                return null;

            var polyline = new Polyline
            {
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Points = new PointCollection(points)
            };

            return polyline;
        }
    }
}