using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model
{
    public class DetectionConfidence
    {
        public double Confidence { get; set; }

        // Add other properties and methods as needed

        public byte[] GetBinaryMask()
        {
            // Implement the logic to get the binary mask
            // Return the binary mask as a byte array
            throw new NotImplementedException();
        }

        public byte[] GetUncertaintyMap()
        {
            // Implement the logic to get the uncertainty map
            // Return the uncertainty map as a byte array
            throw new NotImplementedException();
        }
    }
}
