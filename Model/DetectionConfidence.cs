using System;
using System.Drawing;

namespace MURDOC_2024.Model
{
    public class DetectionConfidence
    {
        public double RankNetConfidence { get; set; }
        public double EfficientDetConfidence { get; set; }
        public double OverallConfidence { get; set; }

        private string _binaryMaskPath;
        private string _uncertaintyMapPath;

        public DetectionConfidence(double rankNetConfidence, double efficientDetConfidence)
        {
            RankNetConfidence = rankNetConfidence;
            EfficientDetConfidence = efficientDetConfidence;
            CalculateOverallConfidence();
        }

        private void CalculateOverallConfidence()
        {
            // Calculate the overall confidence based on RankNet and EfficientDet confidences
            // You can use a weighted average or any other desired formula
            OverallConfidence = (RankNetConfidence + EfficientDetConfidence) / 2;
        }

        public void UpdateRankNetConfidence(double newConfidence)
        {
            RankNetConfidence = newConfidence;
            CalculateOverallConfidence();
        }

        public void UpdateEfficientDetConfidence(double newConfidence)
        {
            EfficientDetConfidence = newConfidence;
            CalculateOverallConfidence();
        }

        public string GetConfidenceLevel()
        {
            if (OverallConfidence >= 0.8)
                return "High";
            else if (OverallConfidence >= 0.6)
                return "Medium";
            else
                return "Low";
        }

        public Image GetBinaryMask()
        {
            try
            {
                return Image.FromFile(_binaryMaskPath);
            }
            catch (Exception ex)
            {
                // Handle the exception appropriately (e.g., log the error, return a default image, or throw a custom exception)
                throw new Exception($"Failed to load binary mask image: {ex.Message}");
            }
        }

        public object GetFixationMask()
        {
            try
            {
                return Image.FromFile(_uncertaintyMapPath);
            }
            catch (Exception ex)
            {
                // Handle the exception appropriately (e.g., log the error, return a default image, or throw a custom exception)
                throw new Exception($"Failed to load uncertainty map image: {ex.Message}");
            }
        }
    }
}
