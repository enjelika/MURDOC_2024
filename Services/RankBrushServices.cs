using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MURDOC_2024.Model;

namespace MURDOC_2024.Services
{
    /// <summary>
    /// Service that applies circular brush strokes to rank map byte arrays.
    /// Supports Increase and Decrease modes with configurable size and Gaussian falloff strength.
    /// </summary>
    public class RankBrushService
    {
        public RankBrushMode CurrentMode { get; set; }
        public double BrushSize { get; set; } = 20;
        public double BrushStrength { get; set; } = 0.5;

        /// <summary>
        /// Apply brush stroke to rank map data.
        /// Modifies the array in-place and returns the same reference to avoid
        /// allocating a new ~600KB array on every MouseMove event.
        /// </summary>
        public byte[] ApplyBrushStroke(byte[] rankData, int width, int height, Point point)
        {
            if (rankData == null || rankData.Length != width * height)
                return rankData;

            int centerX = (int)point.X;
            int centerY = (int)point.Y;
            int radius = (int)(BrushSize / 2);

            // Apply circular brush with falloff (in-place)
            for (int y = Math.Max(0, centerY - radius); y < Math.Min(height, centerY + radius); y++)
            {
                for (int x = Math.Max(0, centerX - radius); x < Math.Min(width, centerX + radius); x++)
                {
                    double distance = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));

                    if (distance <= radius)
                    {
                        // Smooth falloff from center to edge
                        double falloff = 1.0 - (distance / radius);
                        double effectiveStrength = BrushStrength * falloff;

                        int idx = y * width + x;
                        double currentValue = rankData[idx] / 255.0;

                        // Apply adjustment
                        double adjustment = CurrentMode == RankBrushMode.Increase
                            ? effectiveStrength * 0.2  // Increase by up to 20%
                            : -effectiveStrength * 0.2; // Decrease by up to 20%

                        double newValue = Math.Max(0, Math.Min(1, currentValue + adjustment));
                        rankData[idx] = (byte)(newValue * 255);
                    }
                }
            }

            return rankData;
        }
    }
}