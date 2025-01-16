using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.DataModels
{
    public class RankNetInput
    {
        public byte[] Image { get; set; }

        // Add other properties as needed

        public RankNetInput()
        {
            // Default constructor
        }

        public RankNetInput(byte[] image)
        {
            Image = image;
        }
    }
}
