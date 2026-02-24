using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;

namespace MURDOC_2024.Model
{
    public class PolygonROI
    {
        public List<Point> Points { get; set; }
        public Polygon Visual { get; set; }
        public double Priority { get; set; } = 1.0;
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Creates a binary mask image (white = ROI, black = background)
        /// Similar to your ground truth image
        /// </summary>
        public BitmapSource CreateBinaryMask(int width, int height)
        {
            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                // Black background
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

                // White polygon (ROI)
                var geometry = new PathGeometry(new[]
                {
                new PathFigure(Points[0], Points.Skip(1).Select(p => new LineSegment(p, true)), true)
            });
                context.DrawGeometry(Brushes.White, null, geometry);
            }

            var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(drawingVisual);

            return renderTarget;
        }

        /// <summary>
        /// Gets binary mask as byte array (0 = background, 255 = ROI)
        /// For sending to Python backend
        /// </summary>
        public byte[] GetMaskArray(int width, int height)
        {
            var bitmap = CreateBinaryMask(width, height);

            var stride = width;
            var pixels = new byte[height * stride];

            var formatConvertedBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
            formatConvertedBitmap.CopyPixels(pixels, stride, 0);

            return pixels;
        }

        /// <summary>
        /// Converts polygon points to normalized coordinates (0-1 range)
        /// </summary>
        public List<double[]> ToNormalizedPoints(double imageWidth, double imageHeight)
        {
            return Points.Select(p => new[]
            {
            p.X / imageWidth,
            p.Y / imageHeight
        }).ToList();
        }

        /// <summary>
        /// Gets bounding box of the polygon
        /// </summary>
        public Rect GetBoundingBox()
        {
            var minX = Points.Min(p => p.X);
            var minY = Points.Min(p => p.Y);
            var maxX = Points.Max(p => p.X);
            var maxY = Points.Max(p => p.Y);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
