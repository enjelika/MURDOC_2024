using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.Model
{
    public class DetectionFeedback
    {
        public string DetectionId { get; set; }
        public FeedbackType Type { get; set; }

        // Support both bounding box and polygon feedback
        public BoundingBox BoundingBox { get; set; }
        public List<Point> PolygonPoints { get; set; }
        public byte[] MaskData { get; set; }  // Binary mask if available

        public double OriginalConfidence { get; set; }
        public DateTime Timestamp { get; set; }
        public string ImagePath { get; set; }

        // Metadata
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public GeometryType GeometryType { get; set; }

        /// <summary>
        /// Create feedback from bounding box (backward compatibility)
        /// </summary>
        public static DetectionFeedback FromBoundingBox(
            string detectionId,
            FeedbackType type,
            BoundingBox bbox,
            double confidence,
            string imagePath)
        {
            return new DetectionFeedback
            {
                DetectionId = detectionId,
                Type = type,
                BoundingBox = bbox,
                GeometryType = GeometryType.BoundingBox,
                OriginalConfidence = confidence,
                Timestamp = DateTime.Now,
                ImagePath = imagePath
            };
        }

        /// <summary>
        /// Create feedback from polygon points
        /// </summary>
        public static DetectionFeedback FromPolygon(
            string detectionId,
            FeedbackType type,
            List<Point> points,
            double confidence,
            string imagePath,
            int imageWidth,
            int imageHeight)
        {
            return new DetectionFeedback
            {
                DetectionId = detectionId,
                Type = type,
                PolygonPoints = new List<Point>(points),
                GeometryType = GeometryType.Polygon,
                OriginalConfidence = confidence,
                Timestamp = DateTime.Now,
                ImagePath = imagePath,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight
            };
        }

        /// <summary>
        /// Create feedback with binary mask
        /// </summary>
        public static DetectionFeedback FromMask(
            string detectionId,
            FeedbackType type,
            byte[] maskData,
            double confidence,
            string imagePath,
            int imageWidth,
            int imageHeight)
        {
            return new DetectionFeedback
            {
                DetectionId = detectionId,
                Type = type,
                MaskData = maskData,
                GeometryType = GeometryType.Mask,
                OriginalConfidence = confidence,
                Timestamp = DateTime.Now,
                ImagePath = imagePath,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight
            };
        }

        /// <summary>
        /// Get feedback geometry as normalized coordinates for Python
        /// </summary>
        public object GetNormalizedGeometry()
        {
            switch (GeometryType)
            {
                case GeometryType.BoundingBox:
                    return new
                    {
                        type = "bbox",
                        x = BoundingBox.X / ImageWidth,
                        y = BoundingBox.Y / ImageHeight,
                        width = BoundingBox.Width / ImageWidth,
                        height = BoundingBox.Height / ImageHeight
                    };

                case GeometryType.Polygon:
                    return new
                    {
                        type = "polygon",
                        points = PolygonPoints.Select(p => new[]
                        {
                            p.X / ImageWidth,
                            p.Y / ImageHeight
                        }).ToList()
                    };

                case GeometryType.Mask:
                    return new
                    {
                        type = "mask",
                        mask_data = MaskData,
                        width = ImageWidth,
                        height = ImageHeight
                    };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Convert polygon to binary mask if needed
        /// </summary>
        public byte[] GetAsMask()
        {
            if (GeometryType == GeometryType.Mask && MaskData != null)
            {
                return MaskData;
            }

            if (GeometryType == GeometryType.Polygon && PolygonPoints != null)
            {
                return CreateMaskFromPolygon(PolygonPoints, ImageWidth, ImageHeight);
            }

            if (GeometryType == GeometryType.BoundingBox && BoundingBox != null)
            {
                return CreateMaskFromBoundingBox(BoundingBox, ImageWidth, ImageHeight);
            }

            return null;
        }

        private byte[] CreateMaskFromPolygon(List<Point> points, int width, int height)
        {
            var drawingVisual = new System.Windows.Media.DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                context.DrawRectangle(System.Windows.Media.Brushes.Black, null,
                    new Rect(0, 0, width, height));

                var geometry = new System.Windows.Media.PathGeometry(new[]
                {
                    new System.Windows.Media.PathFigure(
                        points[0],
                        points.Skip(1).Select(p => new System.Windows.Media.LineSegment(p, true)),
                        true
                    )
                });
                context.DrawGeometry(System.Windows.Media.Brushes.White, null, geometry);
            }

            var renderTarget = new RenderTargetBitmap(
                width, height, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32
            );
            renderTarget.Render(drawingVisual);

            var stride = width;
            var pixels = new byte[height * stride];
            var formatConverted = new FormatConvertedBitmap(
                renderTarget,
                System.Windows.Media.PixelFormats.Gray8,
                null, 0
            );
            formatConverted.CopyPixels(pixels, stride, 0);

            return pixels;
        }

        private byte[] CreateMaskFromBoundingBox(BoundingBox bbox, int width, int height)
        {
            var mask = new byte[width * height];

            int x1 = (int)bbox.X;
            int y1 = (int)bbox.Y;
            int x2 = (int)(bbox.X + bbox.Width);
            int y2 = (int)(bbox.Y + bbox.Height);

            for (int y = y1; y < y2 && y < height; y++)
            {
                for (int x = x1; x < x2 && x < width; x++)
                {
                    mask[y * width + x] = 255;
                }
            }

            return mask;
        }
    }

    public enum FeedbackType
    {
        Confirmation,    // User says detection is correct
        Rejection,       // User says detection is false positive
        Correction       // User adds missed object (typically with polygon/mask)
    }

    public enum GeometryType
    {
        BoundingBox,     // Simple rectangular region
        Polygon,         // Polygon vertices
        Mask             // Binary mask data
    }

    public class BoundingBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public static BoundingBox FromPolygon(List<Point> points)
        {
            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxX = points.Max(p => p.X);
            var maxY = points.Max(p => p.Y);

            return new BoundingBox
            {
                X = minX,
                Y = minY,
                Width = maxX - minX,
                Height = maxY - minY
            };
        }
    }
}