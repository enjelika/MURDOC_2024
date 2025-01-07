using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model
{
    public class DetectionResult
    {
        public string ImagePath { get; set; }
        public double ConfidenceScore { get; set; }
        public Dictionary<string, double> PartConfidences { get; set; }
        public byte[,] BinaryMask { get; set; }
        public byte[,] UncertaintyMap { get; set; }
        public List<DetectedObject> DetectedObjects { get; set; }
        public DateTime ProcessedTime { get; set; }

        public class DetectedObject
        {
            public string ObjectType { get; set; }
            public double Confidence { get; set; }
            public Rectangle BoundingBox { get; set; }
            public Dictionary<string, double> PartScores { get; set; }
        }
    }
}
