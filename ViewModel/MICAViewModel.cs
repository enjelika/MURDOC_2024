using MURDOC_2024.Model;
using MURDOC_2024.Model.MICA.Services;
using System.Threading.Tasks;

namespace MURDOC_2024.ViewModel
{
    public class MICAViewModel
    {
        private readonly IDetectionService _detectionService;

        public async Task ProcessDetectionResult(DetectionResult result)
        {
            // Add uncertainty metrics
            var uncertaintyMetrics = CalculateUncertainty(result);

            // Enable validation if ground truth available
            if (HasGroundTruth)
            {
                EnableValidationInterface();
            }
        }
    }
}
