using System.Threading.Tasks;
using MURDOC_2024.Model.DataModels;
using MURDOC_2024.Model.MICA.Utils;

namespace MURDOC_2024.Model.MICA.Services
{
    /// <summary>
    /// Interface defining the contract for Mixed-Initiative Camouflage Analysis (MICA) services
    /// </summary>
    public interface IMICAService
    {
        /// <summary>
        /// Processes the results from the IAI decision hierarchy analysis
        /// </summary>
        /// <param name="iaiResults">The analysis results from the IAI decision hierarchy process</param>
        /// <returns>A Task containing the processed DetectionResult with combined analysis data</returns>
        Task<DetectionResult> ProcessResults(IAIResults iaiResults);

        /// <summary>
        /// Calculates the uncertainty metrics for a given detection result
        /// </summary>
        /// <param name="result">The detection result to calculate uncertainty metrics for</param>
        /// <returns>A Task containing the calculated UncertaintyMetrics</returns>
        Task<UncertaintyMetrics> CalculateUncertainty(DetectionResult result);

        /// <summary>
        /// Checks if ground truth data is available for validation
        /// </summary>
        /// <returns>True if ground truth data is available, otherwise false</returns>
        bool HasGroundTruth();
    }

    public class MICAService : IMICAService
    {
        private readonly UncertaintyMetrics _uncertaintyMetrics;
        private readonly ValidationSystem _validationSystem;

        public MICAService(UncertaintyMetrics uncertaintyMetrics, ValidationSystem validationSystem)
        {
            _uncertaintyMetrics = uncertaintyMetrics;
            _validationSystem = validationSystem;
        }

        public async Task<DetectionResult> ProcessResults(IAIResults iaiResults)
        {
            // Process the IAI results and combine them into a DetectionResult
            // ...

            return new DetectionResult();
        }

        public async Task<UncertaintyMetrics> CalculateUncertainty(DetectionResult result)
        {
            // Calculate the uncertainty metrics using the UncertaintyMetrics class
            var uncertaintyMetrics = await _uncertaintyMetrics.Calculate(result);

            return uncertaintyMetrics;
        }

        public bool HasGroundTruth()
        {
            // Check if ground truth data is available using the ValidationSystem class
            return _validationSystem.HasGroundTruth();
        }
    }
}