using MURDOC_2024.Model.MICA.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            // Run existing detection
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
