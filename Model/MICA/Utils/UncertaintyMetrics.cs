using System;
using System.Drawing;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Utils
{
    /// <summary>
    /// A class for computing uncertainty metrics in camouflage detection.
    /// </summary>
    public class UncertaintyMetrics
    {
        /// <summary>
        /// Gets or sets the Intersection over Union (IoU) metric.
        /// </summary>
        public double IoU { get; set; }

        /// <summary>
        /// Gets or sets the uncertainty metric.
        /// </summary>
        public double Uncertainty { get; set; }

        /// <summary>
        /// Gets or sets the pixel accuracy metric.
        /// </summary>
        public double PixelAccuracy { get; set; }

        /// <summary>
        /// Computes the Intersection over Union (IoU) between the ground truth and prediction images.
        /// </summary>
        /// <param name="groundTruth">The ground truth image.</param>
        /// <param name="prediction">The prediction image.</param>
        /// <returns>The IoU value between 0 and 1.</returns>
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

        // <summary>
        /// Computes the uncertainty of the prediction based on the prediction and uncertainty map images.
        /// </summary>
        /// <param name="prediction">The prediction image.</param>
        /// <param name="uncertaintyMap">The uncertainty map image.</param>
        /// <returns>The average uncertainty value between 0 and 1.</returns>
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

        /// <summary>
        /// Computes the pixel accuracy between the ground truth and prediction images.
        /// </summary>
        /// <param name="groundTruth">The ground truth image.</param>
        /// <param name="prediction">The prediction image.</param>
        /// <returns>The pixel accuracy value between 0 and 1.</returns>
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

        /// <summary>
        /// Calculates the uncertainty metrics for a given detection result.
        /// </summary>
        /// <param name="result">The detection result containing image paths.</param>
        /// <returns>The current instance of UncertaintyMetrics with calculated values.</returns>
        public async Task<UncertaintyMetrics> Calculate(DetectionResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result), "Detection result must be provided.");

            var groundTruthImage = await LoadImage(result.GroundTruthImagePath);
            var predictionImage = await LoadImage(result.PredictionImagePath);
            var uncertaintyMapImage = await LoadImage(result.UncertaintyMapPath);

            IoU = ComputeIoU(groundTruthImage, predictionImage);
            Uncertainty = ComputeUncertainty(predictionImage, uncertaintyMapImage);
            PixelAccuracy = ComputePixelAccuracy(groundTruthImage, predictionImage);

            return this;
        }

        /// <summary>
        /// Loads an image from the specified file path.
        /// </summary>
        /// <param name="imagePath">The path to the image file.</param>
        /// <returns>The loaded Bitmap image, or null if the path is invalid.</returns>
        private async Task<Bitmap> LoadImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            return await Task.Run(() => new Bitmap(imagePath));
        }
    }
}
