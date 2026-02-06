using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MURDOC_2024.Model;

namespace MURDOC_2024.Services
{
    public class RankBrushService
    {
        public RankBrushMode CurrentMode { get; set; }
        public double BrushSize { get; set; } = 20;
        public double BrushStrength { get; set; } = 0.5;

        /// <summary>
        /// Apply brush stroke to rank map data
        /// </summary>
        public byte[] ApplyBrushStroke(byte[] rankData, int width, int height, Point point)
        {
            if (rankData == null || rankData.Length != width * height)
                return rankData;

            byte[] modified = new byte[rankData.Length];
            Array.Copy(rankData, modified, rankData.Length);

            int centerX = (int)point.X;
            int centerY = (int)point.Y;
            int radius = (int)(BrushSize / 2);

            // Apply circular brush with falloff
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
                        double currentValue = modified[idx] / 255.0;

                        // Apply adjustment
                        double adjustment = CurrentMode == RankBrushMode.Increase
                            ? effectiveStrength * 0.2  // Increase by up to 20%
                            : -effectiveStrength * 0.2; // Decrease by up to 20%

                        double newValue = Math.Max(0, Math.Min(1, currentValue + adjustment));
                        modified[idx] = (byte)(newValue * 255);
                    }
                }
            }

            return modified;
        }
    }
}