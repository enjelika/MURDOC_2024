using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Utils
{
    public static class ExpertiseCalculator
    {
        public static ExpertiseLevel CalculateExpertiseLevel(
            UserInteractionData interactionPattern,
            UserBackground background)
        {
            int score = 0;

            // Background assessment
            if (background.HasProgrammingExperience) score += 2;
            if (background.HasComputerVisionExperience) score += 3;
            if (background.HasAIExperience) score += 3;

            // Interaction pattern assessment
            score += AssessInteractionPatterns(interactionPattern);

            // Feature usage assessment
            score += AssessFeatureUsage(interactionPattern.FeatureUsageCount);

            return DetermineExpertiseLevel(score);
        }

        private static int AssessInteractionPatterns(UserInteractionData data)
        {
            int score = 0;

            // Advanced feature usage indicates higher expertise
            score += data.AdvancedFeatureAccessCount;

            // Efficient task completion indicates expertise
            if (data.AverageTaskCompletionTime < TimeSpan.FromMinutes(2))
                score += 2;

            return score;
        }

        private static int AssessFeatureUsage(Dictionary<string, int> featureUsage)
        {
            int score = 0;

            // Higher usage of advanced features indicates expertise
            if (featureUsage.TryGetValue("DetailedMetrics", out int detailedMetricsUsage) && detailedMetricsUsage > 5)
                score += 2;
            if (featureUsage.TryGetValue("ValidationChecks", out int validationChecksUsage) && validationChecksUsage > 3)
                score += 2;
            if (featureUsage.TryGetValue("CustomSettings", out int customSettingsUsage) && customSettingsUsage > 3)
                score += 1;

            return score;
        }

        private static ExpertiseLevel DetermineExpertiseLevel(int score)
        {
            if (score >= 10) return ExpertiseLevel.Expert;
            if (score >= 5) return ExpertiseLevel.Intermediate;
            return ExpertiseLevel.Novice;
        }
    }
}
