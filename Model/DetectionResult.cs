using System;
using System.Collections.Generic;

namespace MURDOC_2024.Model
{
    /// <summary>
    /// Represents the final result of the camouflage detection analysis process
    /// </summary>
    public class DetectionResult
    {
        /// <summary>
        /// Gets or sets whether an object was detected in the image
        /// </summary>
        public bool HasDetectedObject { get; set; }

        /// <summary>
        /// Gets or sets the message describing the detection results
        /// </summary>
        public string DetectionMessage { get; set; }

        /// <summary>
        /// Gets or sets the list of weak areas identified in the camouflage
        /// </summary>
        public List<WeakArea> WeakAreas { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of detected object parts and their confidence scores
        /// </summary>
        public Dictionary<string, float> DetectedParts { get; set; }

        /// <summary>
        /// Gets or sets the file path of the analyzed image
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the analysis was completed
        /// </summary>
        public DateTime ProcessedTime { get; set; }

        /// <summary>
        /// Gets or sets the file path of the ground truth image
        /// </summary>
        public string GroundTruthImagePath { get; set; }

        /// <summary>
        /// Gets or sets the file path of the prediction image
        /// </summary>
        public string PredictionImagePath { get; set; }

        /// <summary>
        /// Gets or sets the file path of the uncertainty map image
        /// </summary>
        public string UncertaintyMapPath { get; set; }

        /// <summary>
        /// Initializes a new instance of the DetectionResult class with default values
        /// </summary>
        public DetectionResult()
        {
            HasDetectedObject = false;
            DetectionMessage = string.Empty;
            FilePath = string.Empty;
            WeakAreas = new List<WeakArea>();
            DetectedParts = new Dictionary<string, float>();
            ProcessedTime = DateTime.Now;
            GroundTruthImagePath = string.Empty;
            PredictionImagePath = string.Empty;
            UncertaintyMapPath = string.Empty;
        }
    }
}