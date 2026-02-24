using System;

namespace MURDOC_2024.Model
{
    public enum RankBrushMode
    {
        Increase,
        Decrease
    }

    public class RankBrushEventArgs : EventArgs
    {
        public RankBrushMode Mode { get; set; }
        public double BrushSize { get; set; }
        public double BrushStrength { get; set; }

        public RankBrushEventArgs(RankBrushMode mode, double brushSize, double brushStrength)
        {
            Mode = mode;
            BrushSize = brushSize;
            BrushStrength = brushStrength;
        }
    }
}