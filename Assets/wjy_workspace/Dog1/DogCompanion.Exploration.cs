using System.Collections;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        internal readonly struct ExplorationPoint
        {
            private readonly Vector3 position;
            private readonly Transform subject;
            private readonly DogToyBody toy;
            public readonly string Name;
            public Vector3 Position => subject != null ? subject.position : position;

            public ExplorationPoint(string name, Vector3 position) : this(name, position, null) { }

            public ExplorationPoint(string name, Vector3 position, Transform subject)
            {
                Name = name;
                this.position = position;
                this.subject = subject;
                toy = subject != null ? subject.GetComponent<DogToyBody>() : null;
            }

            public bool IsAvailable => subject == null || (subject.gameObject.activeInHierarchy && subject.position.y < .8f &&
                (toy == null || toy.Holder == DogToyHolder.Free));
        }

        private enum ArrivalFocus { None, Owner, Object }

        private const float OwnerFocusDuration = 3.05f;
        private const float ObjectFocusDuration = 2.35f;
        private ExplorationPoint[] explorationPoints;
        private Vector3 explorationTarget, explorationSubject;
        private int explorationIndex = -1, arrivalPoint = -1;
        private float exploreAfter, travelUntil, nextOwnerGlance, nextOwnerVisit;
        private bool walkingToLandmark, walkingToOwner;
        private float glanceAt = -100f, glanceYaw, glancePitch;
        private ArrivalFocus arrivalFocus;
        private float arrivalQueuedAt, arrivalStartedAt = -1f, arrivalInvitationUntil;
        private Vector2 lastArrivalAim;
        private bool arrivalMayInvite;
        internal string ExplorationPurpose { get; private set; } = "";
        private bool HasCommittedArrivalFocus => arrivalFocus != ArrivalFocus.None;
        internal bool IsFinishingIdleAction => IsPlayingMicroAction || HasCommittedArrivalFocus ||
            (cueHasStarted && Time.time < attentionUntil);

        internal void SetExplorationPoints(params ExplorationPoint[] points)
        {
            explorationPoints = points;
            ResetExploration();
        }

        private void ResetExploration()
        {
            exploreAfter = Time.time + 4.8f;
            walkingToLandmark = walkingToOwner = false;
            explorationIndex = -1;
            glanceAt = -100f;
            glanceYaw = glancePitch = 0f;
            nextOwnerGlance = 0f;
            nextOwnerVisit = Time.time + 10.5f;
            arrivalFocus = ArrivalFocus.None;
            arrivalStartedAt = -1f;
            arrivalInvitationUntil = 0f;
            ExplorationPurpose = "在这里歇一会儿";
        }

        // Explicit full-body actions leave the old destination behind.
        private void DeferExploration(float seconds)
        {
            walkingToLandmark = walkingToOwner = false;
            exploreAfter = Mathf.Max(exploreAfter, Time.time + seconds);
            arrivalInvitationUntil = 0f;
        }

        private bool TickExploration(float deltaTime)
        {
            if (explorationPoints == null || explorationPoints.Length == 0) return false;
            if (IsFinishingIdleAction || Time.time < pauseUntil || HoldIdleGestures) return true;

            if (walkingToOwner)
            {
                if (IsNearOwner() || (user != null && FlatDistance(transform.position, explorationTarget) < .12f))
                    QueueOwnerArrivalLook();
                else if (user == null || Time.time >= travelUntil ||
                    (FlatDistance(explorationSubject, user.position) > .75f && !FindOwnerApproach()))
                {
                    DeferExploration(1.5f);
                    SetExplorationPurpose("停下来看看周围");
                }
                else WalkTowards(explorationTarget, Mathf.Clamp(walkSpeed, .28f, .72f), deltaTime);
                return true;
            }

            if (walkingToLandmark)
            {
                var point = explorationPoints[explorationIndex];
                bool pointMoved = FlatDistance(explorationSubject, point.Position) > .58f;
                if (!point.IsAvailable || (pointMoved && !FindLandmarkApproach(point)))
                {
                    DeferExploration(1.5f);
                    SetExplorationPurpose("停下来看看周围");
                    return true;
                }
                if (FlatDistance(transform.position, explorationTarget) < .12f)
                {
                    DeferExploration(1.65f);
                    SetExplorationPurpose("停下来看看" + point.Name);
                    if (UnityEngine.Random.value < .72f) ScheduleArrivalFocus(ArrivalFocus.Object, explorationIndex, false);
                }
                else if (Time.time < travelUntil)
                {
                    ObserveNearbyOwner();
                    WalkTowards(explorationTarget, Mathf.Clamp(walkSpeed, .28f, .7f), deltaTime);
                }
                else
                {
                    DeferExploration(3f);
                    SetExplorationPurpose("停下来看看周围");
                }
                return true;
            }

            if (Time.time >= nextOwnerVisit)
            {
                nextOwnerVisit = Time.time + UnityEngine.Random.Range(17f, 26f);
                if (TryLookAngles(user, 4.6f, 105f, out _) && OwnerVisible())
                {
                    if (FlatDistance(transform.position, user.position) <= OwnerStandOff + .30f)
                    {
                        QueueOwnerArrivalLook();
                        return true;
                    }
                    if (FindOwnerApproach())
                    {
                        walkingToOwner = true;
                        travelUntil = Time.time + 10f;
                        SetExplorationPurpose("走过来看看你");
                        return true;
                    }
                }
            }
            if (Time.time < exploreAfter) return true;

            for (int count = 1; count <= explorationPoints.Length; count++)
            {
                int index = (explorationIndex + count) % explorationPoints.Length;
                var point = explorationPoints[index];
                if (!point.IsAvailable || !FindLandmarkApproach(point)) continue;
                explorationIndex = index;
                walkingToLandmark = true;
                travelUntil = Time.time + 12f;
                SetExplorationPurpose("去看看" + point.Name);
                return true;
            }
            exploreAfter = Time.time + 5f;
            SetExplorationPurpose("留在原地观察");
            return true;
        }

        private bool FindOwnerApproach()
        {
            explorationSubject = user.position;
            return TryApproachPoint(explorationSubject, OwnerStandOff, out explorationTarget);
        }

        private bool FindLandmarkApproach(ExplorationPoint point)
        {
            explorationSubject = point.Position;
            return TryApproachPoint(explorationSubject, .64f, out explorationTarget);
        }

        private void QueueOwnerArrivalLook()
        {
            DeferExploration(.8f);
            if (user == null || head == null || HoldIdleGestures || followingWalk) return;
            nextOwnerVisit = Time.time + UnityEngine.Random.Range(17f, 26f);
            nextOwnerGlance = Mathf.Max(nextOwnerGlance, Time.time + 5f);
            PlayTailWag();
            SetExplorationPurpose("抬头看你，等你回应");
            ScheduleArrivalFocus(ArrivalFocus.Owner, -1, UnityEngine.Random.value < .38f);
        }

        private void ScheduleArrivalFocus(ArrivalFocus focus, int point, bool mayInvite)
        {
            arrivalFocus = focus;
            arrivalPoint = point;
            arrivalQueuedAt = Time.time;
            arrivalStartedAt = -1f;
            arrivalMayInvite = mayInvite;
            lastArrivalAim = Vector2.zero;
            glanceAt = -100f;
        }

        // Settle the last step, and turn only enough for the neck to comfortably reach the owner.
        private bool HoldPositionForArrival(float dt)
        {
            if (!HasCommittedArrivalFocus) return false;
            if (arrivalFocus == ArrivalFocus.Owner && arrivalStartedAt < 0f && user != null && !IsPlayingMicroAction &&
                Mathf.Abs(HeadingChange(transform.forward, user.position - transform.position)) > 31f)
                TurnTowards(user.position, dt, 28f);
            return true;
        }

        private bool TrySampleArrivalAttention(bool walking, out UpperBodyPose pose)
        {
            pose = default;
            if (!HasCommittedArrivalFocus) return false;
            bool owner = arrivalFocus == ArrivalFocus.Owner;
            if (arrivalStartedAt < 0f)
            {
                if (Time.time - arrivalQueuedAt > 1.65f)
                {
                    FinishArrivalAttention(false);
                    return false;
                }
                if (walking || IsPlayingMicroAction || !CanPlayMicroPose() ||
                    !TryArrivalAim(owner, out lastArrivalAim) || (owner && !OwnerVisible())) return false;
                arrivalStartedAt = Time.time;
            }

            float age = Time.time - arrivalStartedAt;
            float duration = owner ? OwnerFocusDuration : ObjectFocusDuration;
            if (age >= duration)
            {
                FinishArrivalAttention(owner && !HoldIdleGestures && !followingWalk && IsNearOwner() && OwnerVisible());
                return false;
            }

            // If a prop is picked up or the player steps behind us, ease out with the last valid aim.
            if (TryArrivalAim(owner, out Vector2 targetAim)) lastArrivalAim = targetAim;
            float weight = Pulse(age, .02f, .51f, duration - .83f, duration);
            pose.Aim = lastArrivalAim * weight;
            pose.ChestYaw = pose.Aim.x * .065f;
            if (owner)
            {
                pose.Tilt = -12.5f * Pulse(age, .28f, .78f, 1.94f, 2.84f);
                pose.LeftEar.y = pose.RightEar.y = 4f * Pulse(age, .08f, .24f, .73f, 1.2f);
            }
            else
            {
                pose.Aim.y += 3f * Pulse(age, .7f, .92f, 1.22f, 1.48f);
                pose.Tilt = Mathf.Sign(lastArrivalAim.x) * 3f * weight;
            }
            return true;
        }

        private bool TryArrivalAim(bool owner, out Vector2 aim)
        {
            aim = default;
            if (head == null) return false;
            if (owner)
            {
                if (user == null || !TryLookAngles(user, 3.65f, 92f, out aim)) return false;
                aim.x = Mathf.Clamp(aim.x, -44f, 44f);
                return true;
            }

            if (explorationPoints == null || arrivalPoint < 0 || arrivalPoint >= explorationPoints.Length) return false;
            var point = explorationPoints[arrivalPoint];
            if (!point.IsAvailable) return false;
            Vector3 target = point.Position + Vector3.up * .08f;
            Vector3 offset = target - head.position;
            float distance = new Vector2(offset.x, offset.z).magnitude;
            float yaw = HeadingChange(transform.forward, offset);
            if (distance < .09f || distance > 1.35f || Mathf.Abs(yaw) >= 92f) return false;
            float pitch = -Mathf.Atan2(offset.y, distance) * Mathf.Rad2Deg;
            aim = new Vector2(Mathf.Clamp(yaw, -43f, 43f), Mathf.Clamp(pitch, -18f, 30f));
            return true;
        }

        private void FinishArrivalAttention(bool ownerStillNear)
        {
            if (ownerStillNear && arrivalMayInvite) arrivalInvitationUntil = Time.time + 1.3f;
            arrivalFocus = ArrivalFocus.None;
            arrivalStartedAt = -1f;
            nextQuietLook = Mathf.Max(nextQuietLook, Time.time + 1.15f);
            exploreAfter = Mathf.Max(exploreAfter, Time.time + .28f);
        }

        internal bool TryConsumeArrivalInvitation()
        {
            if (Time.time >= arrivalInvitationUntil) return false;
            arrivalInvitationUntil = 0f;
            return IsNearOwner() && OwnerVisible();
        }

        private void SetExplorationPurpose(string purpose)
        {
            StateLabel = purpose;
            ExplorationPurpose = purpose;
        }

        private bool TryApproachPoint(Vector3 interest, float standOff, out Vector3 destination)
        {
            destination = default;
            Vector3 origin = transform.position;
            Vector3 towardDog = Vector3.ProjectOnPlane(origin - interest, Vector3.up);
            if (towardDog.sqrMagnitude < .12f) return false;
            Vector3 nearSide = towardDog.normalized;
            foreach (float angle in ApproachAngles)
            {
                Vector3 goal = interest + Quaternion.AngleAxis(angle, Vector3.up) * nearSide * standOff;
                goal.y = origin.y;
                goal = Limit(goal);
                float distance = FlatDistance(origin, goal);
                if (distance < .23f || distance > 5f || FlatDistance(goal, interest) > standOff + .25f) continue;
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
            if (Time.time < nextOwnerGlance || !TryLookAngles(user, 3.6f, 112f, out _) || !OwnerVisible()) return;
            glanceAt = Time.time;
            nextOwnerGlance = Time.time + 7.5f;
        }

        private bool OwnerVisible()
        {
            if (user == null || head == null) return false;
            Vector3 toward = user.position - head.position;
            return !Physics.Raycast(head.position, toward.normalized, Mathf.Max(0f, toward.magnitude - .18f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void ApplyExplorationGlance(float dt, bool walking)
        {
            float weight = 0f;
            Vector2 aim = default;
            if (!IsPerforming && !HasCommittedArrivalFocus && Time.time - glanceAt < 2f && TryLookAngles(user, 4f, 114f, out aim))
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
            while (Ready && !IsPerforming && IsFinishingIdleAction) yield return null;
            if (!Ready || IsPerforming) yield break;
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
