using MURDOC_2024.Model.MICA.Services;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA
{
    public class VisualizationService : IVisualizationService // Multi-level visualization system
    {
        private readonly IDetectionService _detectionService;

        public void UpdateConfidenceDisplay(DetectionConfidence confidence)
        {
            // Update visualization based on confidence levels
            var binaryMask = confidence.GetBinaryMask();
            var uncertaintyMap = confidence.GetUncertaintyMap();

            // Update display elements
            UpdateVisualizationLayers(binaryMask, uncertaintyMap);
        }
    }
}
