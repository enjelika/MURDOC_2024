using MURDOC_2024.Model.MICA.Services;
using System.Threading.Tasks;

namespace MURDOC_2024.Model
{
    public class DetectionPipeline
    {
        private readonly IRankNetService _rankNetService;
        private readonly IEfficientDetService _efficientDetService;
        private readonly IMICAService _micaService;

        public async Task<DetectionResult> ProcessImage(string imagePath)
        {
            // Run existing detection : TODO - May need to revise this strategy to use the IAI_Decision_Hierarchy.py script
            var rankNetResult = await _rankNetService.Detect(imagePath);
            var efficientDetResult = await _efficientDetService.Detect(imagePath);

            // Process through MICA
            var micaResult = await _micaService.ProcessResults(
                rankNetResult,
                efficientDetResult
            );

            return micaResult;
        }
    }
}
