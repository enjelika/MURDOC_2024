using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MURDOC_2024.Model.MICA
{
    /// <summary>
    /// Provides validation functionality by comparing detection results against ground truth data
    /// </summary>
    public class ValidationSystem
    {
        /// <summary>
        /// Stores the path to the ground truth data directory
        /// </summary>
        private readonly string _groundTruthPath;

        /// <summary>
        /// Initializes a new instance of the ValidationSystem class
        /// </summary>
        /// <param name="groundTruthPath">Directory path containing ground truth data</param>
        public ValidationSystem(string groundTruthPath)
        {
            _groundTruthPath = groundTruthPath;
        }

        /// <summary>
        /// Validates detection results against ground truth data
        /// </summary>
        /// <param name="detectionResult">The detection result to validate</param>
        /// <returns>A ValidationResult containing comparison metrics</returns>
        public async Task<ValidationResult> ValidateDetection(DetectionResult detectionResult)
        {
            var groundTruthFile = Path.Combine(_groundTruthPath,
                Path.GetFileNameWithoutExtension(detectionResult.FilePath) + ".json");

            if (!File.Exists(groundTruthFile))
            {
                return new ValidationResult
                {
                    IsValidated = false,
                    Message = "Ground truth data not found",
                    Metrics = new Dictionary<string, float>()
                };
            }

            var groundTruth = await LoadGroundTruth(groundTruthFile);
            var metrics = CalculateMetrics(detectionResult, groundTruth);

            return new ValidationResult
            {
                IsValidated = true,
                Message = "Validation completed",
                Metrics = metrics
            };
        }

        /// <summary>
        /// Loads ground truth data from a JSON file
        /// </summary>
        /// <param name="groundTruthFile">Path to the ground truth JSON file</param>
        /// <returns>Deserialized ground truth data</returns>
        private async Task<GroundTruth> LoadGroundTruth(string groundTruthFile)
        {
            // Use Task.Run since File.ReadAllText is synchronous in C# 7.3
            var json = await Task.Run(() => File.ReadAllText(groundTruthFile));
            return JsonConvert.DeserializeObject<GroundTruth>(json);
        }

        /// <summary>
        /// Calculates validation metrics by comparing detection results with ground truth
        /// </summary>
        /// <param name="detection">Detection results to validate</param>
        /// <param name="groundTruth">Ground truth data for comparison</param>
        /// <returns>Dictionary of metric names and values</returns>
        private Dictionary<string, float> CalculateMetrics(DetectionResult detection, GroundTruth groundTruth)
        {
            var metrics = new Dictionary<string, float>();

            // Calculate Intersection over Union (IoU) for weak areas
            metrics["IoU"] = CalculateIoU(detection.WeakAreas, groundTruth.WeakAreas);

            // Calculate part detection accuracy
            metrics["PartAccuracy"] = CalculatePartAccuracy(detection.DetectedParts, groundTruth.Parts);

            // Calculate overall detection accuracy
            metrics["DetectionAccuracy"] = detection.HasDetectedObject == groundTruth.HasObject ? 1.0f : 0.0f;

            return metrics;
        }

        /// <summary>
        /// Calculates Intersection over Union for weak areas
        /// </summary>
        /// <param name="detectedAreas">List of detected weak areas</param>
        /// <param name="groundTruthAreas">List of ground truth weak areas</param>
        /// <returns>IoU score between 0 and 1</returns>
        private float CalculateIoU(List<WeakArea> detectedAreas, List<WeakArea> groundTruthAreas)
        {
            float totalIoU = 0;
            int pairs = 0;

            foreach (var detected in detectedAreas)
            {
                foreach (var truth in groundTruthAreas)
                {
                    var intersection = CalculateIntersectionArea(detected, truth);
                    var union = CalculateUnionArea(detected, truth);

                    if (union > 0)
                    {
                        totalIoU += intersection / union;
                        pairs++;
                    }
                }
            }

            return pairs > 0 ? totalIoU / pairs : 0;
        }

        /// <summary>
        /// Calculates the intersection area of two weak areas
        /// </summary>
        private float CalculateIntersectionArea(WeakArea a, WeakArea b)
        {
            var xLeft = Math.Max(a.X1, b.X1);
            var yTop = Math.Max(a.Y1, b.Y1);
            var xRight = Math.Min(a.X2, b.X2);
            var yBottom = Math.Min(a.Y2, b.Y2);

            if (xRight < xLeft || yBottom < yTop)
                return 0;

            return (xRight - xLeft) * (yBottom - yTop);
        }

        /// <summary>
        /// Calculates the union area of two weak areas
        /// </summary>
        private float CalculateUnionArea(WeakArea a, WeakArea b)
        {
            var areaA = (a.X2 - a.X1) * (a.Y2 - a.Y1);
            var areaB = (b.X2 - b.X1) * (b.Y2 - b.Y1);
            var intersection = CalculateIntersectionArea(a, b);

            return areaA + areaB - intersection;
        }

        /// <summary>
        /// Calculates accuracy of part detection
        /// </summary>
        private float CalculatePartAccuracy(Dictionary<string, float> detectedParts, Dictionary<string, float> groundTruthParts)
        {
            float correctParts = 0;

            foreach (var part in groundTruthParts)
            {
                if (detectedParts.ContainsKey(part.Key))
                {
                    // Consider a part correctly detected if confidence is within 20% of ground truth
                    if (Math.Abs(detectedParts[part.Key] - part.Value) <= 0.2)
                    {
                        correctParts++;
                    }
                }
            }

            return groundTruthParts.Count > 0 ? correctParts / groundTruthParts.Count : 0;
        }
    }

    /// <summary>
    /// Represents the validation results including metrics
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Gets or sets whether validation was performed
        /// </summary>
        public bool IsValidated { get; set; }

        /// <summary>
        /// Gets or sets the validation result message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of validation metrics
        /// </summary>
        public Dictionary<string, float> Metrics { get; set; }
    }

    /// <summary>
    /// Represents ground truth data for validation
    /// </summary>
    public class GroundTruth
    {
        /// <summary>
        /// Gets or sets whether an object exists in the ground truth
        /// </summary>
        public bool HasObject { get; set; }

        /// <summary>
        /// Gets or sets the list of ground truth weak areas
        /// </summary>
        public List<WeakArea> WeakAreas { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of ground truth parts and their confidence scores
        /// </summary>
        public Dictionary<string, float> Parts { get; set; }
    }
}