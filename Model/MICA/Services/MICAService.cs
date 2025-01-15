using System;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Services
{
    /// <summary>
    /// Service class that implements the IMICAService interface to process camouflage detection results
    /// </summary>
    public class MICAService : IMICAService
    {
        /// <summary>
        /// Processes the results from the IAI decision hierarchy
        /// </summary>
        /// <param name="iaiResults">Results from the IAI decision hierarchy containing detection information</param>
        /// <returns>A Task containing the processed DetectionResult with combined analysis data</returns>
        public Task<DetectionResult> ProcessResults(IAIResults iaiResults)
        {
            var result = new DetectionResult
            {
                HasDetectedObject = iaiResults.ObjectPresent,
                DetectionMessage = iaiResults.Message,
                WeakAreas = iaiResults.WeakAreas,
                DetectedParts = iaiResults.DetectedParts,
                ProcessedTime = DateTime.Now
            };

            return Task.FromResult(result);
        }
    }
}