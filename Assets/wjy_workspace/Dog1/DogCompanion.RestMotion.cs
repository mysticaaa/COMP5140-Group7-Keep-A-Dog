using System.Collections;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private IEnumerator RestInsideHouse(Transform spot, int version)
        {
            Vector3 outside = transform.position;
            Vector3 inside = spot.TransformPoint(new Vector3(0f, .122f, -.18f * bodyScale));
            Vector3 travel = inside - outside;
            yield return FaceRestDirection(travel, version);
            if (version != actionVersion) yield break;

            StateLabel = "走进狗窝";
            yield return WalkHouseThreshold(outside, inside, .27f, version);
            if (version != actionVersion) yield break;
            // Let the final stride land before pivoting on the inner pad.
            yield return new WaitForSeconds(.22f);
            if (version != actionVersion) yield break;

            StateLabel = "在狗窝里转身";
            yield return FaceRestDirection(-travel, version);
            if (version != actionVersion) yield break;
            yield return new WaitForSeconds(.18f);
            if (version != actionVersion) yield break;

            StateLabel = "在狗窝里趴着休息";
            restSpotCrouch = 1f;
            yield return new WaitForSeconds(5f);
            if (version != actionVersion) yield break;

            StateLabel = "起身回到客厅";
            restSpotCrouch = .22f;
            yield return new WaitForSeconds(.65f);
            if (version != actionVersion) yield break;
            yield return WalkHouseThreshold(inside, outside, 0f, version);
        }

        private IEnumerator FaceRestDirection(Vector3 facing, int version)
        {
            facing = Vector3.ProjectOnPlane(facing, Vector3.up);
            for (float elapsed = 0f; elapsed < 3f && version == actionVersion; elapsed += Time.deltaTime)
            {
                if (Mathf.Abs(TurnTowards(transform.position + facing, Time.deltaTime)) < 2f) yield break;
                yield return null;
            }
        }

        private IEnumerator WalkHouseThreshold(Vector3 from, Vector3 to, float finalCrouch, int version)
        {
            // The scripted doorway segment crosses the normal wandering boundary. Root motion still
            // drives the existing planted-foot gait, so neither leg pose nor facing snaps at the sill.
            float startingCrouch = restSpotCrouch;
            float duration = Mathf.Max(.45f, FlatDistance(from, to) / .53f);
            bool ascending = to.y >= from.y;
            for (float elapsed = 0f; elapsed < duration && version == actionVersion; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                Vector3 point = Vector3.Lerp(from, to, t);
                float step = ascending ? EaseBetween(.22f, .64f, t) : EaseBetween(.36f, .78f, t);
                point.y = Mathf.Lerp(from.y, to.y, step);
                transform.position = point;
                restSpotCrouch = Mathf.Lerp(startingCrouch, finalCrouch, t);
                yield return null;
            }
            if (version != actionVersion) yield break;
            transform.position = to;
            restSpotCrouch = finalCrouch;
        }
    }
}
