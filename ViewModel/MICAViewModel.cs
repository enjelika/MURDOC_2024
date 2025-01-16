using MURDOC_2024.Model;
using MURDOC_2024.Model.MICA.Services;
using System.Threading.Tasks;

namespace MURDOC_2024.ViewModel
{
    public class MICAViewModel
    {
        private readonly IAIDecisionService _aiDecisionService;
        private readonly IMICAService _micaService;
        private readonly IVisualizationService _visualizationService;

        public MICAViewModel(IAIDecisionService aiDecisionService, IMICAService micaService, IVisualizationService visualizationService)
        {
            _aiDecisionService = aiDecisionService;
            _micaService = micaService;
            _visualizationService = visualizationService;
        }

        public async Task ProcessDetectionResult(DetectionResult result)
        {
            // Add uncertainty metrics
            var uncertaintyMetrics = await _micaService.CalculateUncertainty(result);

            // Enable validation if ground truth available
            if (_micaService.HasGroundTruth())
            {
                _visualizationService.EnableValidationInterface();
            }

            // Perform AI decision based on the detection result and uncertainty metrics
            var aiDecision = await _aiDecisionService.MakeDecision(result, uncertaintyMetrics);

            // Update the visualization based on the AI decision
            _visualizationService.UpdateVisualization(aiDecision);
        }
    }
}