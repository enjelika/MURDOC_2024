using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Services
{
    public interface IVisualizationService
    {
        Task<VisualizationLevel> GetVisualizationForExpertise(ExpertiseLevel level);
        void UpdateConfidenceDisplay(DetectionConfidence confidence);
        void ShowUncertaintyMetrics(UncertaintyData data);
    }
}
