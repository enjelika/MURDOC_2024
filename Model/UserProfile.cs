using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model
{
    public class UserProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Department { get; set; }
        public UserBackground Background { get; set; }
        public DateTime LastLoginDate { get; set; }
        public Dictionary<string, int> FeatureUsageHistory { get; set; }
        public PreferenceSettings Preferences { get; set; }

        public UserProfile()
        {
            FeatureUsageHistory = new Dictionary<string, int>();
            Preferences = new PreferenceSettings();
        }
    }

    public class UserBackground
    {
        public bool HasProgrammingExperience { get; set; }
        public bool HasComputerVisionExperience { get; set; }
        public bool HasAIExperience { get; set; }
        public int YearsOfExperience { get; set; }
        public string[] TechnicalSkills { get; set; }
        public string[] Certifications { get; set; }
        public string EducationLevel { get; set; }
    }

    public class PreferenceSettings
    {
        public bool ShowDetailedMetrics { get; set; }
        public bool EnableAdvancedFeatures { get; set; }
        public bool AutoAdjustInterface { get; set; }
        public string PreferredVisualizationMode { get; set; }
        public Dictionary<string, bool> FeatureToggles { get; set; }

        public PreferenceSettings()
        {
            FeatureToggles = new Dictionary<string, bool>();
        }
    }
}
