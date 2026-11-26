using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private void ConfigureOwnerReachability()
        {
            if (dog != null) dog.OwnerGroundHeightProvider = PlayerGroundHeight;
        }

        private float PlayerGroundHeight()
        {
            if (viewCamera == null) return float.NaN;
            // Floor-tracked head height changes when the player crouches; the origin is the floor.
            if (HeadsetRig != null && viewCamera == HeadsetRig.HeadCamera)
                return HeadsetRig.Origin != null ? HeadsetRig.Origin.position.y : float.NaN;
            // The desktop view has the fixed eye height also used by PlaceHeadset and apartment lift.
            return viewCamera.transform.position.y - 1.65f;
        }
    }
}
