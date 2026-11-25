using System.Collections;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        internal readonly struct ExplorationPoint
        {
            public readonly Vector3 Position;
            public readonly string Name;

            public ExplorationPoint(string name, Vector3 position)
            {
                Name = name;
                Position = position;
            }
        }

        private ExplorationPoint[] explorationPoints;
        private Vector3 explorationTarget;
        private int explorationIndex = -1;
        private float exploreAfter, inspectAt, travelUntil, nextOwnerGlance;
        private bool walkingToLandmark, inspectionPending;
        private float glanceAt = -100f, glanceYaw, glancePitch;
        internal string ExplorationPurpose { get; private set; } = "";

        internal void SetExplorationPoints(params ExplorationPoint[] points)
        {
            explorationPoints = points;
            ResetExploration();
        }

        private void ResetExploration()
        {
            exploreAfter = Time.time + 4.8f;
            walkingToLandmark = inspectionPending = false;
            explorationIndex = -1;
            glanceAt = -100f;
            nextOwnerGlance = 0f;
            ExplorationPurpose = "在这里歇一会儿";
        }

        private bool TickExploration(float deltaTime)
        {
            if (explorationPoints == null || explorationPoints.Length == 0) return false;
            ObserveNearbyOwner();

            if (walkingToLandmark)
            {
                if (FlatDistance(transform.position, explorationTarget) < .12f)
                {
                    walkingToLandmark = false;
                    inspectionPending = true;
                    inspectAt = Time.time + .48f;
                    exploreAfter = Time.time + 7f;
                    SetExplorationPurpose("停下来看看" + explorationPoints[explorationIndex].Name);
                }
                else if (Time.time < travelUntil)
                {
                    WalkTowards(explorationTarget, Mathf.Clamp(walkSpeed, .28f, .7f), deltaTime);
                }
                else
                {
                    walkingToLandmark = false;
                    exploreAfter = Time.time + 4f;
                    SetExplorationPurpose("先停在这儿闻一闻");
                    inspectionPending = true;
                    inspectAt = Time.time + .48f;
                }
                return true;
            }

            if (inspectionPending && Time.time >= inspectAt)
            {
                if (TryPlayMicroAction(AirSniff, true)) inspectionPending = false;
                else if (Time.time > inspectAt + 1.7f) inspectionPending = false;
            }
            if (Time.time < exploreAfter || IsPlayingMicroAction) return true;

            for (int count = 1; count <= explorationPoints.Length; count++)
            {
                int index = (explorationIndex + count) % explorationPoints.Length;
                if (!TryApproachPoint(explorationPoints[index].Position, out explorationTarget)) continue;
                explorationIndex = index;
                walkingToLandmark = true;
                travelUntil = Time.time + 12f;
                SetExplorationPurpose("去看看" + explorationPoints[index].Name);
                return true;
            }
            exploreAfter = Time.time + 5f;
            SetExplorationPurpose("留在原地观察");
            return true;
        }

        private void SetExplorationPurpose(string purpose)
        {
            StateLabel = purpose;
            ExplorationPurpose = purpose;
        }

        private bool TryApproachPoint(Vector3 interest, out Vector3 destination)
        {
            destination = default;
            Vector3 origin = transform.position;
            Vector3 towardDog = Vector3.ProjectOnPlane(origin - interest, Vector3.up);
            if (towardDog.sqrMagnitude < .12f) return false;
            Vector3 nearSide = towardDog.normalized;
            foreach (float angle in ApproachAngles)
            {
                Vector3 goal = interest + Quaternion.AngleAxis(angle, Vector3.up) * nearSide * .64f;
                goal.y = origin.y;
                goal = Limit(goal);
                float distance = FlatDistance(origin, goal);
                if (distance < .45f || distance > 5f || FlatDistance(goal, interest) > .9f) continue;
                bool clear = true;
                int segments = Mathf.CeilToInt(distance / .25f);
                for (int i = 1; i <= segments && clear; i++)
                {
                    Vector3 point = Vector3.Lerp(origin, goal, i / (float)segments);
                    clear = HasPlayClearance == null || HasPlayClearance(point, .20f);
                }
                if (!clear) continue;
                destination = goal;
                return true;
            }
            return false;
        }

        private static readonly float[] ApproachAngles = { 0f, 32f, -32f, 65f, -65f, 100f, -100f };

        private void ObserveNearbyOwner()
        {
            if (Time.time < nextOwnerGlance || !TryLookAngles(user, 3.6f, 112f, out _)) return;
            Vector3 toward = user.position - head.position;
            if (Physics.Raycast(head.position, toward.normalized, toward.magnitude - .18f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            glanceAt = Time.time;
            nextOwnerGlance = Time.time + 7.5f;
        }

        private void ApplyExplorationGlance(float dt, bool walking)
        {
            float weight = 0f;
            Vector2 aim = default;
            if (!IsPerforming && Time.time - glanceAt < 2f && TryLookAngles(user, 4f, 114f, out aim))
                weight = Pulse(Time.time - glanceAt, 0f, .31f, .98f, 2f);
            glanceYaw = Blend(glanceYaw, Mathf.Clamp(aim.x, -50f, 50f) * weight, dt, 6f);
            glancePitch = Blend(glancePitch, aim.y * weight * .8f, dt, 5f);
            if (Mathf.Abs(glanceYaw) + Mathf.Abs(glancePitch) < .01f || (!walking && IsPlayingMicroAction)) return;
            Yaw("DEF-neck.01", glanceYaw * .37f);
            Yaw("DEF-neck.02", glanceYaw * .29f);
            Yaw("DEF-head", glanceYaw * .34f);
            AddPlayPitch("DEF-head", glancePitch);
        }

        internal IEnumerator PreviewTravel(string mode)
        {
            LastActivitySucceeded = false;
            bool corner = mode == "corner_sample";
            float distance = mode == "trot_sample" ? 2.5f : 1.4f;
            float speed = mode == "trot_sample" ? 1.46f : .65f;
            Vector3 origin = transform.position;
            foreach (float bearing in ApproachAngles)
            {
                Vector3 forward = Quaternion.AngleAxis(bearing, Vector3.up) * transform.forward;
                Vector3 first = origin + forward * distance;
                Vector3 second = first + Quaternion.AngleAxis(54f, Vector3.up) * forward * 1.25f;
                if (!IsPreviewPathClear(origin, first) || (corner && !IsPreviewPathClear(first, second))) continue;
                yield return WalkPreviewLeg(first, speed);
                if (corner) yield return WalkPreviewLeg(second, speed);
                LastActivitySucceeded = FlatDistance(transform.position, corner ? second : first) < .14f;
                yield break;
            }
        }

        private bool IsPreviewPathClear(Vector3 start, Vector3 end)
        {
            for (int i = 1; i <= 8; i++)
            {
                Vector3 point = Vector3.Lerp(start, end, i / 8f);
                if ((Limit(point) - point).sqrMagnitude > .01f ||
                    (HasPlayClearance != null && !HasPlayClearance(point, .24f))) return false;
            }
            return true;
        }

        private IEnumerator WalkPreviewLeg(Vector3 destination, float speed)
        {
            float deadline = Time.time + 6f;
            while (FlatDistance(transform.position, destination) >= .09f && Time.time < deadline)
            {
                WalkTowards(destination, speed, Time.deltaTime);
                yield return null;
            }
        }
    }
}
