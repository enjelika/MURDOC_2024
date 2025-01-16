using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.DataModels
{
    public class RankNetOutput
    {
        public byte[] SegmentationMask { get; set; }
        public double[] ConfidenceScores { get; set; }

        // Add other properties as needed

        public RankNetOutput()
        {
            // Default constructor
        }

        public RankNetOutput(byte[] segmentationMask, double[] confidenceScores)
        {
            SegmentationMask = segmentationMask;
            ConfidenceScores = confidenceScores;
        }
    }
}
