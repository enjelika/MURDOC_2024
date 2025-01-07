using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model
{
    public enum ExpertiseLevel
    {
        Novice,
        Intermediate,
        Expert
    }

    public enum InteractionType
    {
        ImageLoad,
        DetectionRun,
        SettingAdjust,
        ValidationCheck,
        DetailedViewAccess,
        AdvancedFeatureUse
    }

    public enum VisualizationLevel
    {
        Basic,      // Simple display with binary mask and confidence score
        Standard,   // Adds part detection and basic uncertainty visualization
        Detailed    // Full metrics, advanced controls, and detailed uncertainty display
    }
}
