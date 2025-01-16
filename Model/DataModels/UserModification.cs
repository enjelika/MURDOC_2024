using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.DataModels
{
    public class UserModification
    {
        public byte[] ModifiedImage { get; set; }
        public double ConfidenceThreshold { get; set; }

        // Add other properties as needed

        public UserModification()
        {
            // Default constructor
        }

        public UserModification(byte[] modifiedImage, double confidenceThreshold)
        {
            ModifiedImage = modifiedImage;
            ConfidenceThreshold = confidenceThreshold;
        }
    }
}
