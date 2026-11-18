using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private float ForelegPitch(Vector3 forearm, Vector3 paw) =>
            Vector3.SignedAngle(Vector3.ProjectOnPlane(forearm, transform.right),
                Vector3.ProjectOnPlane(paw, transform.right), transform.right);

        private void PlaceOfferedPaw(Foot foot, Vector3 goal, float lift)
        {
            Transform wrist = foot.Chain[0];
            Transform elbow = foot.Chain[1];
            Transform shoulder = foot.Chain[2];
            float upperLength = Vector3.Distance(shoulder.position, elbow.position);
            float forearmLength = Vector3.Distance(elbow.position, wrist.position);
            float pawLength = Vector3.Distance(wrist.position, foot.Tip.position);
            Vector3 reach = goal - shoulder.position;
            Vector3 direction = reach.normalized;
            Vector3 pitchAxis = Vector3.ProjectOnPlane(transform.right, direction).normalized;
            if (reach.sqrMagnitude < .000001f || pitchAxis.sqrMagnitude < .5f ||
                upperLength < .001f || forearmLength < .001f)
            {
                PlaceFoot(foot, goal);
                return;
            }

            // Hold the carpus at a controlled angle and solve the elbow and shoulder around it.
            // A positive pitch folds the paw back toward the body.
            float wristAngle = Mathf.Lerp(foot.WristPitch, 31f, EaseBetween(0f, .76f, lift));
            float radians = wristAngle * Mathf.Deg2Rad;
            float alongForearm = forearmLength + pawLength * Mathf.Cos(radians);
            float behindForearm = pawLength * Mathf.Sin(radians);
            float lowerLength = new Vector2(alongForearm, behindForearm).magnitude;
            float forearmOffset = Mathf.Atan2(behindForearm, alongForearm) * Mathf.Rad2Deg;

            // The elbow stays on the rear side of the reach plane and stops short of fully locking.
            float minReach = Mathf.Sqrt(upperLength * upperLength + lowerLength * lowerLength -
                upperLength * lowerLength);
            float distance = Mathf.Clamp(reach.magnitude, minReach, upperLength + lowerLength - .001f);
            float elbowAlong = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                (2f * distance);
            float elbowBack = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - elbowAlong * elbowAlong));
            Vector3 upper = direction * elbowAlong + Vector3.Cross(pitchAxis, direction) * elbowBack;
            Vector3 lower = direction * distance - upper;
            Vector3 forearm = Quaternion.AngleAxis(-forearmOffset, pitchAxis) * lower;
            Vector3 paw = Quaternion.AngleAxis(wristAngle, pitchAxis) * forearm;

            AimLimbSegment(shoulder, elbow, upper);
            AimLimbSegment(elbow, wrist, forearm);
            AimLimbSegment(wrist, foot.Tip, paw);
        }

        private static void AimLimbSegment(Transform joint, Transform child, Vector3 direction)
        {
            joint.rotation = Quaternion.FromToRotation(child.position - joint.position, direction) * joint.rotation;
        }
    }
}
