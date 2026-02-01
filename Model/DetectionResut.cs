using System;
using System.Collections.Generic;
using System.Windows;

namespace MURDOC_2024.Model
{
    public class DetectionResult
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public double Confidence { get; set; }
        public BoundingBox BoundingBox { get; set; }
        public List<Point> PolygonPoints { get; set; }
        public byte[] SegmentationMask { get; set; }

        // For consolidated detections
        public int PartCount { get; set; }
        public Dictionary<string, int> Parts { get; set; }
        public double AvgConfidence { get; set; }

        // Feedback state
        public bool IsConfirmed { get; set; }
        public bool IsRejected { get; set; }
        public bool HasFeedback => IsConfirmed || IsRejected;

        // Source image info
        public string ImagePath { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        // Display properties
        public string DisplayLabel => $"{Label} ({Confidence:P0})";
        public string ConfidenceText => $"{Confidence:P2}";

        public string PartsBreakdown
        {
            get
            {
                if (Parts == null || Parts.Count == 0)
                    return string.Empty;

                var parts = new List<string>();
                foreach (var kvp in Parts)
                {
                    parts.Add($"{kvp.Key}({kvp.Value})");
                }
                return string.Join(", ", parts);
            }
        }

        /// <summary>
        /// Convert this detection to feedback
        /// </summary>
        public DetectionFeedback ToFeedback(FeedbackType type)
        {
            // Use polygon if available
            if (PolygonPoints != null && PolygonPoints.Count > 0)
            {
                return DetectionFeedback.FromPolygon(
                    Id,
                    type,
                    PolygonPoints,
                    Confidence,
                    ImagePath,
                    ImageWidth,
                    ImageHeight
                );
            }

            // Use mask if available
            if (SegmentationMask != null)
            {
                return DetectionFeedback.FromMask(
                    Id,
                    type,
                    SegmentationMask,
                    Confidence,
                    ImagePath,
                    ImageWidth,
                    ImageHeight
                );
            }

            // Fallback to bounding box
            if (BoundingBox != null)
            {
                return DetectionFeedback.FromBoundingBox(
                    Id,
                    type,
                    BoundingBox,
                    Confidence,
                    ImagePath
                );
            }

            return null;
        }
    }
}