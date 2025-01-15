using MURDOC_2024.Model.MICA;
using MURDOC_2024.Model.MICA.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MURDOC_2024.Model
{
    /// <summary>
    /// Manages the detection pipeline for processing images through IAI Decision Hierarchy and MICA
    /// </summary>
    public class DetectionPipeline
    {
        private readonly IIAIDecisionService _iaiDecisionService;
        private readonly IMICAService _micaService;
        private readonly ValidationSystem _validationSystem;

        /// <summary>
        /// Initializes a new instance of the DetectionPipeline class
        /// </summary>
        /// <param name="iaiDecisionService">Service for IAI decision hierarchy processing</param>
        /// <param name="micaService">Service for MICA processing</param>
        public DetectionPipeline(
            IIAIDecisionService iaiDecisionService,
            IMICAService micaService,
            ValidationSystem validationSystem)
        {
            _iaiDecisionService = iaiDecisionService;
            _micaService = micaService;
            _validationSystem = validationSystem;
        }

        /// <summary>
        /// Processes an image through the detection pipeline
        /// </summary>
        /// <param name="imagePath">Path to the image file to process</param>
        /// <returns>A DetectionResult containing analysis results</returns>
        public async Task<DetectionResult> ProcessImage(string imagePath)
        {
            var iaiDecisionResult = await _iaiDecisionService.ProcessImage(imagePath);
            var iaiResults = ParseIAIResults(iaiDecisionResult);

            if (!iaiResults.ObjectPresent)
            {
                return new DetectionResult
                {
                    HasDetectedObject = false,
                    DetectionMessage = iaiResults.Message,
                    FilePath = imagePath,
                    ProcessedTime = System.DateTime.Now
                };
            }

            var micaResult = await _micaService.ProcessResults(iaiResults);

            // Validate results if ground truth is available
            var validationResult = await _validationSystem.ValidateDetection(micaResult);
            micaResult.DetectionMessage += "\n" + validationResult.Message;

            return micaResult;
        }

        /// <summary>
        /// Parses the output from the Python IAI Decision Hierarchy script
        /// </summary>
        /// <param name="pythonOutput">Raw output string from Python script</param>
        /// <returns>Structured IAIResults object containing parsed data</returns>
        private IAIResults ParseIAIResults(string pythonOutput)
        {
            var results = new IAIResults();

            if (pythonOutput.Contains("No object present"))
            {
                results.ObjectPresent = false;
            }
            else
            {
                results.ObjectPresent = true;
                ParseWeakAreas(pythonOutput, results);
                ParseDetectedParts(pythonOutput, results);
            }
            results.Message = pythonOutput;
            return results;
        }

        /// <summary>
        /// Parses weak areas from the Python script output
        /// </summary>
        /// <param name="output">Raw output string containing weak area information</param>
        /// <param name="results">IAIResults object to populate with weak areas</param>
        private void ParseWeakAreas(string output, IAIResults results)
        {
            var weakAreas = new List<WeakArea>();
            // Add parsing logic here
            results.WeakAreas = weakAreas;
        }

        /// <summary>
        /// Parses detected parts from the Python script output
        /// </summary>
        /// <param name="output">Raw output string containing detected parts information</param>
        /// <param name="results">IAIResults object to populate with detected parts</param>
        private void ParseDetectedParts(string output, IAIResults results)
        {
            var detectedParts = new Dictionary<string, float>();
            // Add parsing logic here
            results.DetectedParts = detectedParts;
        }
    }

    /// <summary>
    /// Contains the results from the IAI Decision Hierarchy analysis
    /// </summary>
    public class IAIResults
    {
        /// <summary>
        /// Gets or sets whether an object was detected
        /// </summary>
        public bool ObjectPresent { get; set; }

        /// <summary>
        /// Gets or sets the analysis message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the list of detected weak areas
        /// </summary>
        public List<WeakArea> WeakAreas { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of detected parts and their confidence scores
        /// </summary>
        public Dictionary<string, float> DetectedParts { get; set; }

        /// <summary>
        /// Initializes a new instance of the IAIResults class
        /// </summary>
        public IAIResults()
        {
            WeakAreas = new List<WeakArea>();
            DetectedParts = new Dictionary<string, float>();
            Message = string.Empty;
        }
    }

    /// <summary>
    /// Represents a weak area in the camouflage detection
    /// </summary>
    public class WeakArea
    {
        /// <summary>
        /// Gets or sets the X coordinate of the top-left corner
        /// </summary>
        public int X1 { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate of the top-left corner
        /// </summary>
        public int Y1 { get; set; }

        /// <summary>
        /// Gets or sets the X coordinate of the bottom-right corner
        /// </summary>
        public int X2 { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate of the bottom-right corner
        /// </summary>
        public int Y2 { get; set; }
    }
}