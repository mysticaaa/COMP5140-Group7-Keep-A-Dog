using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private float lastSharpTurn = float.NegativeInfinity;

        private Vector3 PredictFootLanding(Foot foot, Vector3 travel, float yaw, float distance,
            float strideLength, float stance)
        {
            float forecast = strideLength * (1f - stance * .5f) / Mathf.Max(distance, .0001f);
            float anticipatedYaw = Mathf.Clamp(yaw * forecast * .78f, -19f, 19f);
            float maximumAdvance = Mathf.Lerp(.37f, .53f, trotBlend);
            if (lastWalkFrame == Time.frameCount && remainingWalkDistance < .31f)
            {
                maximumAdvance = Mathf.Min(maximumAdvance, remainingWalkDistance);
                anticipatedYaw = Mathf.Clamp(anticipatedYaw, Mathf.Min(0f, remainingWalkAngle), Mathf.Max(0f, remainingWalkAngle));
            }
            // Integrate a short circular arc rather than project a straight step across the bend.
            // This gives the inside and outside paws their own landing paths around the same body arc.
            float halfAngle = anticipatedYaw * Mathf.Deg2Rad * .5f;
            float chord = Mathf.Abs(halfAngle) < .0001f ? 1f : Mathf.Sin(halfAngle) / halfAngle;
            Vector3 advance = Quaternion.AngleAxis(anticipatedYaw * .35f, Vector3.up) *
                Vector3.ClampMagnitude(travel * (forecast * chord), maximumAdvance);
            Vector3 landing = transform.position + Quaternion.AngleAxis(anticipatedYaw, Vector3.up) *
                (FootGoal(foot) - transform.position) + advance;
            return KeepFootOnOwnSide(foot, landing);
        }

        private Foot EarlyTurnStep(Vector3 travel, float yaw, float turnRate, float dt,
            float lastCycle, float previousTrotBlend)
        {
            if (dt <= 0f) return null;
            if (turnRate >= 9f) lastSharpTurn = Time.time;
            if (Time.time - lastSharpTurn > 1.9f) return null;

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
                float laneOverrun = Mathf.Abs(neutralX) * .65f - Mathf.Sign(neutralX) * where.x;
                // A pivot can also overreach sideways without crossing the centerline.
                float reachOverrun = Mathf.Abs(where.x - neutralX) * .9f + Mathf.Abs(where.z - neutralZ) * .62f - .110f * bodyScale;
                if (Time.time - foot.LandedAt < .23f && where.x * Mathf.Sign(neutralX) > Mathf.Abs(neutralX) * .48f)
                    continue;
                float supportError = foot.SupportError - .065f * bodyScale;
                // A long planted contact can go out of reach after steering has already eased.
                // Recover on the next available swing without adding footfalls on straight travel.
                if (turnRate < 25f && supportError <= 0f) continue;
                float urgency = Mathf.Max(Mathf.Max(laneOverrun, reachOverrun), supportError);
                if (urgency <= mostUrgent) continue;
                mostUrgent = urgency;
                candidate = foot;
            }
            return candidate;
        }

        private bool IsScheduledGaitStep(Foot foot, float lastCycle, float previousTrotBlend)
        {
            if (Time.time - foot.LandedAt < .16f) return false;
            float diagonalOffset = foot.Front == foot.Right ? (foot.Right ? 1f : 0f) : .5f;
            float previousOffset = Mathf.Lerp(foot.StepOffset, diagonalOffset, previousTrotBlend);
            float stepOffset = Mathf.Lerp(foot.StepOffset, diagonalOffset, trotBlend);
            return Mathf.Floor(gaitCycle - stepOffset) > Mathf.Floor(lastCycle - previousOffset);
        }
    }
}
