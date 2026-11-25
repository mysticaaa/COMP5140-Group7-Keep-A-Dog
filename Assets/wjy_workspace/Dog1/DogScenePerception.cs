using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    [Serializable]
    public sealed class DogObservedObject
    {
        public string id;
        public string[] tags;
        public float distanceMeters;
        public float bearingDegrees;
        public bool inViewCone;
        public bool lineOfSight;
        public bool visible;
        public bool nearHead;
        public bool heldByPlayer;
    }

    [Serializable]
    public sealed class DogPerceptionCandidate
    {
        public string id;
        public string targetId;
        public string label;
        public string[] tags;
    }

    [Serializable]
    public sealed class DogSceneObservation
    {
        public float capturedAt;
        public string sensor = "unity_transforms_and_collider_rays";
        public string origin = "estimated_head";
        public bool available;
        public bool occupied;
        public DogObservedObject[] objects = Array.Empty<DogObservedObject>();
        public DogPerceptionCandidate[] candidates = Array.Empty<DogPerceptionCandidate>();

        public DogObservedObject Find(string id)
        {
            foreach (DogObservedObject item in objects)
                if (item != null && item.id == id) return item;
            return null;
        }

        public string Describe()
        {
            if (!available) return "场景感知等待狗狗就绪";
            DogObservedObject owner = Find("owner");
            string ownerState = owner == null ? "尚未定位到你" :
                "你距离 " + owner.distanceMeters.ToString("0.0") + " 米 · " +
                (owner.visible ? "在视野内" : owner.inViewCone ? "视线被遮挡" : "在视野外");
            DogObservedObject left = Find("left_hand"), right = Find("right_hand");
            if ((left != null && left.nearHead) || (right != null && right.nearHead))
                ownerState += " · 手靠近头部";
            return ownerState;
        }
    }

    /// <summary>Samples registered scene transforms. Visibility is a collider/FOV approximation.</summary>
    public sealed class DogScenePerception : MonoBehaviour
    {
        public DogCompanion Dog;
        [Range(1f, 10f)] public float Range = 6f;
        [Range(20f, 90f)] public float HalfViewAngle = 88f;
        [Range(.08f, .6f)] public float HandProximity = .3f;
        public LayerMask OccluderLayers = Physics.DefaultRaycastLayers;

        private readonly RaycastHit[] rayHits = new RaycastHit[32];
        private Transform boundDog, toyBall, candy;
        private bool targetsAssigned;

        public void SetTargets(Transform ball, Transform lollipop)
        {
            toyBall = ball;
            candy = lollipop;
            targetsAssigned = true;
        }

        public void RefreshTargets()
        {
            boundDog = null;
            targetsAssigned = false;
            Bind();
        }

        public DogSceneObservation Capture(Vector3? leftHand = null, Vector3? rightHand = null)
        {
            Bind();
            var result = new DogSceneObservation
            {
                capturedAt = Time.unscaledTime,
                available = Dog != null && Dog.Ready && Dog.gameObject.activeInHierarchy,
                occupied = Dog == null || !Dog.CanOfferMicroAction,
                origin = Dog != null && Dog.PerceptionHead != null ? "head_bone" : "estimated_head"
            };
            if (!result.available) return result;

            Vector3 origin = Dog.PerceptionHead != null ? Dog.PerceptionHead.position + Vector3.up * .025f : Dog.transform.position + Vector3.up * .45f;
            var observed = new List<DogObservedObject>(5);
            if (Dog.user != null && Dog.user.gameObject.activeInHierarchy)
                observed.Add(Observe("owner", new[] { "person", "social" }, Dog.user.position, origin, Dog.user));
            if (toyBall != null && toyBall.gameObject.activeInHierarchy)
                observed.Add(Observe("ball", new[] { "toy", "fetchable" }, toyBall.position, origin, toyBall));
            if (candy != null && candy.gameObject.activeInHierarchy)
                observed.Add(Observe("lollipop", new[] { "toy", "plush", "fetchable" }, candy.position, origin, candy));
            AddHand(observed, "left_hand", leftHand, origin);
            AddHand(observed, "right_hand", rightHand, origin);
            result.objects = observed.ToArray();
            result.candidates = BuildCandidates(result);
            return result;
        }

        private void Bind()
        {
            Transform current = Dog != null ? Dog.transform : null;
            if (current == boundDog) return;
            boundDog = current;
            if (Dog == null) return;
            if (targetsAssigned) return;
            // Locate the demo's registered props once, rather than scanning a hierarchy every sample.
            GameObject props = GameObject.Find("Training props");
            if (props == null) props = GameObject.Find("Dog training props");
            if (props != null)
            {
                toyBall = props.transform.Find("Toy ball");
                candy = props.transform.Find("Lollipop");
            }
        }

        private DogObservedObject Observe(string id, string[] tags, Vector3 point, Vector3 origin, Transform target)
        {
            Vector3 delta = point - origin;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            float distance = delta.magnitude;
            float bearing = horizontal.sqrMagnitude < .0001f ? 0f : Vector3.SignedAngle(Dog.transform.forward, horizontal, Vector3.up);
            bool inRange = distance <= Mathf.Clamp(Range, 1f, 10f);
            bool inCone = inRange && Mathf.Abs(bearing) <= Mathf.Clamp(HalfViewAngle, 20f, 90f);
            bool clear = inRange && ClearRay(origin, point, target);
            var holder = target != null ? target.GetComponent<DogToyBody>()?.Holder : null;
            return new DogObservedObject
            {
                id = id, tags = tags, distanceMeters = Round(distance), bearingDegrees = Round(bearing),
                inViewCone = inCone, lineOfSight = clear, visible = inCone && clear,
                heldByPlayer = holder == DogToyHolder.RightHand || holder == DogToyHolder.LeftHand || holder == DogToyHolder.Desktop
            };
        }

        private void AddHand(List<DogObservedObject> items, string id, Vector3? hand, Vector3 origin)
        {
            if (!hand.HasValue || !Finite(hand.Value)) return;
            DogObservedObject item = Observe(id, new[] { "hand", "tracked_pose" }, hand.Value, origin, null);
            item.nearHead = item.distanceMeters <= Mathf.Clamp(HandProximity, .08f, .6f);
            items.Add(item);
        }

        private bool ClearRay(Vector3 origin, Vector3 targetPoint, Transform target)
        {
            Vector3 delta = targetPoint - origin;
            float distance = delta.magnitude;
            if (distance < .015f) return true;
            int count = Physics.RaycastNonAlloc(origin, delta / distance, rayHits,
                Mathf.Max(0f, distance - .015f), OccluderLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = rayHits[i].transform;
                if (hit == null || hit == Dog.transform || hit.IsChildOf(Dog.transform)) continue;
                if (target != null && (hit == target || hit.IsChildOf(target))) continue;
                return false;
            }
            // A full buffer can omit an occluder. Treat this sample conservatively.
            return count < rayHits.Length;
        }

        private DogPerceptionCandidate[] BuildCandidates(DogSceneObservation observation)
        {
            if (observation.occupied) return Array.Empty<DogPerceptionCandidate>();
            var result = new List<DogPerceptionCandidate>(DogCompanion.MicroActions.Count);
            DogObservedObject owner = observation.Find("owner"), ball = observation.Find("ball");
            foreach (DogMicroAction action in DogCompanion.MicroActions)
            {
                if (!Dog.CanOfferMicroActionId(action.Id)) continue;
                bool ownerPose = action.Id == "head_turn" || action.Id == "head_tilt" || action.Id == "chest_lean";
                if (ownerPose && (owner == null || !owner.visible || owner.distanceMeters < .25f)) continue;
                if (action.Id == "gaze_exchange" && (owner == null || !owner.visible || owner.distanceMeters < .25f ||
                    Mathf.Abs(owner.bearingDegrees) >= 88f || ball == null || !ball.visible || ball.distanceMeters < .25f || ball.distanceMeters > 4f ||
                    Mathf.Abs(ball.bearingDegrees) >= 72f)) continue;
                var tags = new string[action.Tags.Count];
                for (int i = 0; i < tags.Length; i++) tags[i] = action.Tags[i];
                result.Add(new DogPerceptionCandidate
                {
                    id = action.Id, label = action.Label, tags = tags,
                    targetId = action.Id == "gaze_exchange" ? "ball" : ownerPose ? "owner" : "none"
                });
            }
            return result.ToArray();
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static float Round(float value) => Mathf.Round(value * 100f) / 100f;
    }
}
