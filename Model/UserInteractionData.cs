using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model
{
    public class UserInteractionData
    {
        public List<InteractionRecord> Interactions { get; set; }
        public UserProfile Profile { get; set; }
        public Dictionary<string, int> FeatureUsageCount { get; set; }
        public TimeSpan AverageTaskCompletionTime { get; set; }
        public int AdvancedFeatureAccessCount { get; set; }

        public class InteractionRecord
        {
            public InteractionType Type { get; set; }
            public DateTime Timestamp { get; set; }
            public string Details { get; set; }
        }
    }
}
