using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        public Transform PerceptionHead => head;
        public bool CanOfferMicroAction => Ready && CanPlayMicroPose() && !IsPlayingMicroAction;

        public bool CanOfferMicroActionId(string id)
        {
            if (!CanOfferMicroAction) return false;
            foreach (DogMicroAction action in MicroActions)
            {
                if (action.Id != id) continue;
                if ((id == "ear_perk" || id == "ear_flick") &&
                    (Bone("DEF-ear.01.L") == null || Bone("DEF-ear.01.R") == null)) return false;
                return id != "gaze_exchange" ||
                    (TryLookAngles(user, 6f, 88f, out _) && TryLookAngles(ball, 4f, 72f, out _));
            }
            return false;
        }
    }
}
