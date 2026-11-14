using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private Foot EarlyTurnStep(Vector3 travel, float yaw, float turnRate, float dt,
            float lastCycle, float previousTrotBlend)
        {
            if (turnRate < 42f || dt <= 0f) return null;

            // Sample slightly ahead of the body. It must still be a step, not a sliding planted correction.
            float framesAhead = .064f / dt;
            Vector3 futurePosition = transform.position + travel * framesAhead;
            float aheadYaw = Mathf.Clamp(yaw * framesAhead, -25f, 25f);
            Quaternion inverseHeading = Quaternion.Inverse(Quaternion.AngleAxis(aheadYaw, Vector3.up) * transform.rotation);
            Foot candidate = null;
            float mostUrgent = 0f;
            int expectedSwing = 0;
            foreach (Foot foot in feet)
            {
                if (!foot.Planted || IsScheduledGaitStep(foot, lastCycle, previousTrotBlend))
                {
                    if (++expectedSwing >= 2) return null;
                    continue;
                }

                Vector3 where = inverseHeading * (foot.Contact - futurePosition);
                float neutralX = foot.Neutral.x * transform.lossyScale.x;
                float neutralZ = foot.Neutral.z * transform.lossyScale.z;
                float laneOverrun = Mathf.Abs(neutralX) * .74f - Mathf.Sign(neutralX) * where.x;
                // A pivot can also overreach sideways without crossing the centerline.
                float reachOverrun = Mathf.Abs(where.x - neutralX) * .9f + Mathf.Abs(where.z - neutralZ) * .62f - .132f;
                float urgency = Mathf.Max(laneOverrun, reachOverrun);
                if (urgency <= mostUrgent) continue;
                mostUrgent = urgency;
                candidate = foot;
            }
            return candidate;
        }

        private bool IsScheduledGaitStep(Foot foot, float lastCycle, float previousTrotBlend)
        {
            float diagonalOffset = foot.Front == foot.Right ? (foot.Right ? 1f : 0f) : .5f;
            float previousOffset = Mathf.Lerp(foot.StepOffset, diagonalOffset, previousTrotBlend);
            float stepOffset = Mathf.Lerp(foot.StepOffset, diagonalOffset, trotBlend);
            return Mathf.Floor(gaitCycle - stepOffset) > Mathf.Floor(lastCycle - previousOffset);
        }
    }
}
