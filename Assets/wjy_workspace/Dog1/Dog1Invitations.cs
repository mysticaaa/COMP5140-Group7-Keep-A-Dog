using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private void OpenPlayInvitation(bool preserveReply = false)
        {
            pendingInvitation = true;
            if (agent.CanGiveFeedback)
            {
                invitationResponseUntil = Time.unscaledTime + 26f;
                if (!preserveReply) reply = "麦粒伏低身子，等你选陪玩还是歇一会儿。";
            }
            else
            {
                OpenTrickReward();
                if (!preserveReply) reply = "麦粒前身伏低，望着你等回应。可以陪它玩，也可以表扬这个动作。";
            }
            dog.HoldForInvitation(5.5f);
        }
    }
}
