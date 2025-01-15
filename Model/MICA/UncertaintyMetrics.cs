using System;
using System.Drawing;

namespace MURDOC_2024.Model.MICA
{
    public class UncertaintyMetrics
    {
        public double ComputeIoU(Bitmap groundTruth, Bitmap prediction)
        {
            if (groundTruth == null || prediction == null)
                throw new ArgumentNullException("Both ground truth and prediction images must be provided.");

            if (groundTruth.Size != prediction.Size)
                throw new ArgumentException("Ground truth and prediction images must have the same dimensions.");

            int intersection = 0;
            int union = 0;

            for (int y = 0; y < groundTruth.Height; y++)
            {
                for (int x = 0; x < groundTruth.Width; x++)
                {
                    Color gtPixel = groundTruth.GetPixel(x, y);
                    Color predPixel = prediction.GetPixel(x, y);

                    bool isGTPixel = gtPixel.R > 128; // Assuming binary mask (white for object, black for background)
                    bool isPredPixel = predPixel.R > 128;

                    if (isGTPixel && isPredPixel)
                        intersection++;

                    if (isGTPixel || isPredPixel)
                        union++;
                }
            }

            return union == 0 ? 0 : (double)intersection / union;
        }

        public double ComputeUncertainty(Bitmap prediction, Bitmap uncertaintyMap)
        {
            if (prediction == null || uncertaintyMap == null)
                throw new ArgumentNullException("Prediction and uncertainty map images must be provided.");

            if (prediction.Size != uncertaintyMap.Size)
                throw new ArgumentException("Prediction and uncertainty map images must have the same dimensions.");

            double uncertaintySum = 0;
            int pixelCount = prediction.Width * prediction.Height;

            for (int y = 0; y < prediction.Height; y++)
            {
                for (int x = 0; x < prediction.Width; x++)
                {
                    Color predPixel = prediction.GetPixel(x, y);
                    Color uncertaintyPixel = uncertaintyMap.GetPixel(x, y);

                    if (predPixel.R > 128) // Object prediction
                        uncertaintySum += uncertaintyPixel.R / 255.0; // Normalize uncertainty
                }
            }

            return uncertaintySum / pixelCount; // Average uncertainty
        }

        public double ComputePixelAccuracy(Bitmap groundTruth, Bitmap prediction)
        {
            if (groundTruth == null || prediction == null)
                throw new ArgumentNullException("Both ground truth and prediction images must be provided.");

            if (groundTruth.Size != prediction.Size)
                throw new ArgumentException("Ground truth and prediction images must have the same dimensions.");

            int correctPixels = 0;
            int totalPixels = groundTruth.Width * groundTruth.Height;

            for (int y = 0; y < groundTruth.Height; y++)
            {
                for (int x = 0; x < groundTruth.Width; x++)
                {
                    Color gtPixel = groundTruth.GetPixel(x, y);
                    Color predPixel = prediction.GetPixel(x, y);

                    if ((gtPixel.R > 128 && predPixel.R > 128) || (gtPixel.R <= 128 && predPixel.R <= 128))
                        correctPixels++;
                }
            }

            return (double)correctPixels / totalPixels;
        }
    }
}
