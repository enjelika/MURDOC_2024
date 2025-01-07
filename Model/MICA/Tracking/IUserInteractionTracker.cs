using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Tracking
{
    public interface IUserInteractionTracker
    {
        UserInteractionData GetInteractionPattern();
        UserBackground GetUserBackground();
        void TrackInteraction(InteractionType type, DateTime timestamp);
        void UpdateUserProfile(UserProfile profile);
    }
}
