using MURDOC_2024.Model.MICA.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.AdaptiveInterface
{
    public class AdaptiveDisplayManager
    {
        private readonly IVisualizationService _visualizationService;
        private readonly ExpertiseDetector _expertiseDetector;

        public void UpdateDisplay(DetectionResult result)
        {
            var expertise = _expertiseDetector.DetermineExpertise(_currentUserData);
            var visualizationLevel = _visualizationService.GetVisualizationForExpertise(expertise);

            UpdateDisplayComponents(result, visualizationLevel);
        }
    }
}
