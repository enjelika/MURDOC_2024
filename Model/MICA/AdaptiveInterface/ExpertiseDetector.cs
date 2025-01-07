using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.AdaptiveInterface
{
    public class ExpertiseDetector
    {
        private readonly IUserInteractionTracker _interactionTracker;

        public ExpertiseLevel DetermineExpertise(UserInteractionData data)
        {
            var interactionPattern = _interactionTracker.GetInteractionPattern();
            var technicalBackground = _interactionTracker.GetUserBackground();

            return CalculateExpertiseLevel(interactionPattern, technicalBackground);
        }
    }
}
