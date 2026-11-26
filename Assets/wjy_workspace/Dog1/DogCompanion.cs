using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    // Procedural skeletal animation shared by the companion rigs.
    public sealed partial class DogCompanion : MonoBehaviour
    {
        public Transform user;
        public float wanderRadius = 4f;
        public float walkSpeed = 0.62f;
        public float approachInterval = 12f;
        public float resumeDelay = 3f;
        private bool allowWandering = true;
        public bool WanderingEnabled => allowWandering;
        public void SetWanderingEnabled(bool enabled)
        {
            allowWandering = enabled;
            approaching = false; chooseAfterPause = false;
            target = transform.position;
            if (HasCommittedArrivalFocus) return;
            if (!IsPerforming)
            {
                CurrentAction = enabled ? "wander" : "idle";
                StateLabel = enabled ? "自由散步" : "原地等待训练";
            }
            if (enabled) ChooseDestination(false);
        }
        public string StateLabel { get; private set; } = "自由散步";
        public bool IsPerforming { get; private set; }
        public string CurrentAction { get; private set; } = "wander";
        public bool Ready { get; private set; }
        private Transform visual, head, pelvis;
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly Dictionary<Transform, Quaternion> bindRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> bindPositions = new Dictionary<Transform, Vector3>();
        private Vector3 target;
        private float nextApproach, pauseUntil, actionAge;
        private float crouchBlend, sitBlend;
        private readonly Transform[] tailBones = new Transform[4];
        private readonly float[] tailBends = new float[4];
        private static readonly float[] TailSweep = { 22f, 10f, 5f, 2.5f };
        private static readonly float[] TailResponse = { 32f, 26f, 20f, 15f };
        private float tailWagAge = TailWagDuration;
        private float tailWagPhase, tailCarriage, petBlend, petLookYaw;
        private Quaternion startSpin;
        private float turnIntent, torsoTurn, yawSpeed;
        private int lastTurnFrame = -10;
        private const float SpinDuration = 2.2f;
        private const float SpinMotionEnd = 1.92f;
        private const float SpinStepArc = 60f;
        private Material coat;
        private bool approaching;
        private bool chooseAfterPause;
        private bool followingWalk;
        private readonly List<Foot> feet = new List<Foot>(4);
        private Vector3 lastRootPosition, lastRootForward;
        private float gaitCycle, locomotionBlend, groundHeight;
        private float walkVelocity, strideAcceleration, trotBlend, restSpotCrouch;
        private float remainingWalkDistance, remainingWalkAngle;
        private float strideScale = 1f;
        private int lastWalkFrame = -10;
        private bool trotting;
        private readonly Quaternion[] poseJointRotations = new Quaternion[3];
        internal Transform MuzzleAnchor { get; private set; }

        internal bool TryGetFootContact(string boneName, out bool planted)
        {
            foreach (Foot foot in feet)
            {
                if (foot.Tip != Bone(boneName)) continue;
                planted = foot.Planted;
                return true;
            }
            planted = false;
            return false;
        }

        private sealed class Foot
        {
            public Transform Tip;
            public Transform[] Chain;
            public Vector3 Neutral;
            public Quaternion NeutralRotation;
            public float StepOffset;
            public bool Front;
            public bool Right;
            public bool Planted;
            public Vector3 Contact, SwingStart, Landing;
            public float SwingProgress, SwingHeight, SwingFraction, WristPitch;
            public float LandedAt, SupportError;
            public Vector3 SpinStart, PlayStart;
        }
        public Transform VisualRoot => visual;
        public bool LastFetchSucceeded { get; private set; }
        public bool LastComeSucceeded { get; private set; }
        public bool LastActivitySucceeded { get; private set; }
        public Func<Vector3, Vector3> PositionLimit { private get; set; }
        public Vector3 ToyStation { private get; set; }
        private float pawBlend, bowBlend;
        private float sitPhase, crouchPhase, pawPhase;
        private Vector3 lollipopHome = new Vector3(3.15f, 0.11f, -0.45f);
        private Transform fetchMat, carriedObject;
        public bool ShowKeypoints
        {
            get => keypointOverlay != null && keypointOverlay.enabled;
            set
            {
                if (value && keypointOverlay == null && Ready)
                {
                    keypointOverlay = gameObject.AddComponent<DogKeypointOverlay>();
                    keypointOverlay.Bind(bones);
                }
                if (keypointOverlay != null) keypointOverlay.enabled = value;
            }
        }

        private const float ActionYardLimit = 12f;
        private const float OwnerStandOff = 1.8f;
        private const float TravelTimeLimit = 14f;
        private const float PickupReach = 0.32f;
        private const float StanceFraction = 0.69f;
        private const float TrotStanceFraction = 0.51f;
        private const float PawGroundClearance = 0.025f;
        private const float TailWagDuration = 1.8f;
        private const float PetDuration = 0.85f;
        private DogKeypointOverlay keypointOverlay;
        private Transform trainingProps, lollipop;
        private Material matMaterial, candyMaterial, stickMaterial;
        private bool loweringToPickUp, carrying;
        private float pickupBlend, restorePropAt;
        private int actionVersion;
        private DogCoatCorrectives coatCorrectives;

        private void Awake()
        {
            if (user == null && Camera.main != null) user = Camera.main.transform;
            var model = breed.LoadModel();
            if (model == null) { StateLabel = "狗狗模型尚未导入，请等待 Unity 完成导入"; return; }
            visual = Instantiate(model, transform).transform;
            visual.name = breed.IsOriginal ? "German Shepherd - rigged" : breed.Id + " - rigged";
            foreach (Animator animator in visual.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (Animation animation in visual.GetComponentsInChildren<Animation>()) animation.enabled = false;
            foreach (Transform bone in visual.GetComponentsInChildren<Transform>())
            {
                bones[bone.name] = bone;
                bindRotations[bone] = bone.localRotation;
                bindPositions[bone] = bone.localPosition;
            }
            if (!breed.IsOriginal) BindImportedRig();
            head = Bone("DEF-head"); pelvis = Bone("DEF-pelvis");
            for (int index = 0; index < tailBones.Length; index++)
                tailBones[index] = Bone("DEF-tail.0" + (index + 1));
            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds(); bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
                if (renderer is SkinnedMeshRenderer skin)
                {
                    skin.updateWhenOffscreen = true;
                    if (breed.IsOriginal)
                    {
                        DogCoatCorrectives corrective = DogCoatCorrectives.Attach(skin);
                        if (corrective != null) coatCorrectives = corrective;
                    }
                }
            }
            if (!hasBounds) { StateLabel = "模型没有可渲染网格"; return; }
            float height = breed.IsOriginal ? bounds.size.y : ImportedStandingHeight(bounds.size.y);
            float scale = breed.Height / Mathf.Max(height, 0.001f);
            visual.localScale *= scale;
            // Normalize source forward from anatomy, independent of FBX export axes.
            if (head != null && pelvis != null)
            {
                Vector3 forward = head.position - pelvis.position; forward.y = 0;
                if (forward.sqrMagnitude > 0.0001f)
                    visual.rotation = Quaternion.FromToRotation(forward.normalized, transform.forward) * visual.rotation;
            }
            hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Vector3 center = breed.IsOriginal ? bounds.center : ImportedPawCenter(bounds.center);
            visual.position += new Vector3(transform.position.x - center.x,
                breed.IsOriginal ? transform.position.y - bounds.min.y : 0f, transform.position.z - center.z);
            if (breed.IsOriginal)
            {
                ApplyOriginalCoat(renderers);
            }
            foreach (Transform bone in visual.GetComponentsInChildren<Transform>())
            {
                bindRotations[bone] = bone.localRotation;
                bindPositions[bone] = bone.localPosition;
            }
            float footHeight = float.PositiveInfinity;
            foreach (string name in Paws)
            {
                Transform paw = Bone(name);
                if (paw != null) footHeight = Mathf.Min(footHeight, paw.position.y);
            }
            if (!float.IsInfinity(footHeight)) visual.position += Vector3.up * (transform.position.y + PawGroundClearance * bodyScale - footHeight);
            bindPositions[visual] = visual.localPosition;
            // Lateral sequence: left hind, left fore, right hind, right fore.
            AddFoot(false, false, 0f);
            AddFoot(true, false, 0.25f);
            AddFoot(false, true, 0.5f);
            AddFoot(true, true, 0.75f);
            if (!breed.IsOriginal && (feet.Count != 4 || head == null || pelvis == null))
            {
                StateLabel = "狗狗骨骼缺少必需关节";
                Debug.LogError(breed.Id + " is missing required joints for dog motion.", this);
                return;
            }
            ResetFootContacts();
            if (head != null)
            {
                MuzzleAnchor = new GameObject("Muzzle landmark").transform;
                MuzzleAnchor.SetParent(head, false);
                Vector3 offset = breed.IsOriginal ? transform.forward * 0.17f - Vector3.up * 0.055f : ImportedMuzzleOffset();
                MuzzleAnchor.localPosition = head.InverseTransformVector(offset);
            }
            nextApproach = Time.time + 6f;
            ChooseDestination(false);
            Ready = true;
        }

        private void ApplyOriginalCoat(Renderer[] renderers)
        {
            coat = new Material(Shader.Find("Standard"));
            coat.name = "German Shepherd PBR Coat";
            coat.mainTexture = Resources.Load<Texture2D>("Dog1/mat_001_BaseColor");
            coat.SetFloat("_Glossiness", 0.18f);
            var normal = Resources.Load<Texture2D>("Dog1/mat_001_Normal");
            if (normal != null) { coat.SetTexture("_BumpMap", normal); coat.EnableKeyword("_NORMALMAP"); }
            coat.SetTexture("_OcclusionMap", Resources.Load<Texture2D>("Dog1/mat_001_Ambient_Occlusion"));
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = coat;
                renderer.sharedMaterials = materials;
                if (renderer is SkinnedMeshRenderer skin) DogTailFur.Attach(skin, coat);
            }
        }

        private Transform Bone(string name) { bones.TryGetValue(name, out Transform bone); return bone; }

        private void AddFoot(bool front, bool right, float stepOffset)
        {
            string side = right ? ".R" : ".L";
            Transform tip = Bone((front ? "DEF-front_toes" : "DEF-hind_paw") + side);
            if (tip == null) return;
            var chain = front
                ? new[] { Bone("DEF-front_paw" + side), Bone("DEF-front_forearm" + side), Bone("DEF-front_upper" + side) }
                : new[] { Bone("DEF-hind_hock" + side), Bone("DEF-hind_shin" + side), Bone("DEF-hind_thigh" + side) };
            if (Array.Exists(chain, joint => joint == null)) return;
            if (front && right)
            {
                float standingReach = chain[2].position.y - tip.position.y;
                // The v1 German Shepherd was calibrated directly in metres. Re-estimating
                // its stride from foreleg height shortened it and substantially raised cadence.
                strideScale = breed.IsOriginal ? 1f : Mathf.Clamp(standingReach / .48f, .5f * bodyScale, bodyScale);
            }
            feet.Add(new Foot { Tip = tip, Chain = chain, Neutral = transform.InverseTransformPoint(tip.position),
                NeutralRotation = Quaternion.Inverse(transform.rotation) * tip.rotation,
                StepOffset = stepOffset, Front = front, Right = right,
                WristPitch = front ? ForelegPitch(chain[0].position - chain[1].position, tip.position - chain[0].position) : 0f });
        }

        private void ResetFootContacts()
        {
            CancelQuietStance();
            walkVelocity = strideAcceleration = trotBlend = 0f;
            lastWalkFrame = -10;
            lastTurnFrame = -10;
            yawSpeed = 0f;
            lastSharpTurn = float.NegativeInfinity;
            trotting = false;
            lastRootPosition = transform.position;
            lastRootForward = transform.forward;
            groundHeight = transform.position.y;
            gaitCycle = -0.0001f;
            locomotionBlend = 0f;
            foreach (Foot foot in feet)
            {
                foot.Contact = FootGoal(foot);
                foot.Planted = true;
                foot.SwingProgress = 1f;
                foot.Landing = foot.SwingStart = foot.Contact;
                foot.SwingHeight = 0f;
                foot.SwingFraction = 1f - StanceFraction;
                foot.LandedAt = float.NegativeInfinity;
                foot.SupportError = 0f;
            }
        }
        private void Bend(string name, float degrees)
        {
            Transform bone = Bone(name);
            if (bone == null || bone.parent == null) return;
            Vector3 localAxis = bone.parent.InverseTransformDirection(transform.right);
            bone.localRotation = Quaternion.AngleAxis(degrees, localAxis) * bindRotations[bone];
        }
        private void Yaw(string name, float degrees)
        {
            Transform bone = Bone(name);
            if (bone == null || bone.parent == null) return;
            Vector3 localAxis = bone.parent.InverseTransformDirection(Vector3.up);
            bone.localRotation = Quaternion.AngleAxis(degrees, localAxis) * bone.localRotation;
        }

        public void PlayTailWag()
        {
            // A repeated cue keeps the phase continuous while extending the current response.
            tailWagAge = Mathf.Min(tailWagAge, tailWagAge < TailWagDuration ? 0.26f : 0f);
        }

        private void Update()
        {
            if (!Ready) return;
            turnIntent = 0f;
            if (restorePropAt > 0f && Time.time >= restorePropAt && !IsPerforming)
                RestoreLollipop();
            float dt = Time.deltaTime;
            if (IsPerforming)
            {
                if (CurrentAction == "restSpot" || CurrentAction == "fetch_lollipop" || CurrentAction == "fetch_toy" || CurrentAction == "come" || CurrentAction == "activity") return;
                actionAge += dt;
                if (CurrentAction == "burst") UpdatePlayMotion();
                else if (CurrentAction == "spin")
                {
                    transform.rotation = SpinRotation(SpinAngle(actionAge));
                }
                else if (IsPlayBow(CurrentAction))
                {
                    if (actionAge < playBowTurnEnd) FaceUser(dt);
                }
                // Keep the support paws anchored once the dog settles into a standing or seated pose.
                else if (CurrentAction != "pet" && CurrentAction != "pounce" && CurrentAction != "sniff" &&
                    CurrentAction != "refuse" && actionAge < (CurrentAction == "paw" ? .22f : .45f)) FaceUser(dt);
                return;
            }
            if (HoldPositionForArrival(dt) || IsFinishingIdleAction) return;
            if (!allowWandering)
            {
                StateLabel = "原地等待训练"; CurrentAction = "idle";
                return;
            }
            if (followingWalk && user != null)
            {
                Vector3 owner = user.position;
                owner.y = transform.position.y;
                Vector3 offset = owner - transform.position;
                float distance = offset.magnitude;
                if (distance > 1.55f)
                {
                    Vector3 follow = Vector3.MoveTowards(owner, transform.position, 1.32f);
                    WalkTowards(follow, Mathf.Clamp((distance - 1.3f) * 1.25f, .38f, 1.85f), dt);
                }
                return;
            }
            if (TickExploration(dt)) return;
            if (Time.time >= nextApproach)
            {
                ChooseDestination(true);
                nextApproach = Time.time + UnityEngine.Random.Range(approachInterval, approachInterval + 6f);
                pauseUntil = 0;
            }
            if (Time.time < pauseUntil)
            {
                if (approaching) FaceUser(dt);
                return;
            }
            if (chooseAfterPause) { ChooseDestination(false); chooseAfterPause = false; }
            Vector3 delta = target - transform.position; delta.y = 0;
            if (delta.magnitude < 0.15f)
            {
                pauseUntil = Time.time + UnityEngine.Random.Range(1.5f, 3f);
                StateLabel = approaching ? "靠近你，摇尾巴" : "停下来闻一闻";
                if (approaching && IsNearOwner()) PlayTailWag();
                chooseAfterPause = true;
                return;
            }
            WalkTowards(target, Mathf.Clamp(walkSpeed, 0f, .9f), dt);
        }

        private void ChooseDestination(bool approach, bool updateLabel = true)
        {
            Vector3 center = user != null ? user.position : Vector3.zero; center.y = 0;
            approaching = approach; chooseAfterPause = false;
            Vector2 offset = UnityEngine.Random.insideUnitCircle;
            if (offset.sqrMagnitude < 0.01f) offset = Vector2.right;
            offset.Normalize();
            float radius = approach ? 1.5f : UnityEngine.Random.Range(2.1f, wanderRadius);
            target = Limit(center + new Vector3(offset.x, 0, offset.y) * radius);
            if (updateLabel) StateLabel = approach ? "主动向你靠近" : "自由散步";
        }
        private void FaceUser(float dt)
        {
            if (user == null) return;
            Vector3 delta = user.position - transform.position; delta.y = 0;
            // Around the forward cone the neck can follow the player without shuffling all four paws.
            if (delta.sqrMagnitude > 0.01f && Mathf.Abs(HeadingChange(transform.forward, delta)) > 24f)
                TurnTowards(user.position, dt, 24f);
        }

        private void WalkTowards(Vector3 destination, float speed, float dt)
        {
            dt = Mathf.Min(dt, 0.1f);
            Vector3 offset = destination - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.0001f) return;

            if (Time.frameCount - lastWalkFrame > 1) walkVelocity = strideAcceleration = 0f;
            lastWalkFrame = Time.frameCount;
            float angle = Mathf.Abs(TurnTowards(destination, dt));
            float alignment = Vector3.Dot(transform.forward, offset.normalized);
            float corner = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(19f, 92f, angle));
            float stopSpeed = Mathf.Sqrt(2f * 2.1f * Mathf.Max(0f, offset.magnitude - .009f));
            float requested = Mathf.Min(speed * corner, stopSpeed);
            if (breed.IsOriginal)
                walkVelocity = Mathf.MoveTowards(walkVelocity, requested, dt * (walkVelocity < requested ? 2.35f : 4.3f));
            else walkVelocity = Mathf.SmoothDamp(walkVelocity, requested, ref strideAcceleration,
                requested > walkVelocity ? .20f : .13f, Mathf.Infinity, dt);
            float stride = walkVelocity * dt * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.23f, .95f, alignment));
            stride = Mathf.Min(stride, Mathf.Max(0f, Vector3.Dot(offset, transform.forward)));
            transform.position = Limit(transform.position + transform.forward * stride);
            Vector3 remaining = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
            remainingWalkDistance = remaining.magnitude;
            remainingWalkAngle = HeadingChange(transform.forward, remaining);
        }

        private void LateUpdate()
        {
            if (!Ready) return;
            foreach (var entry in bindRotations) entry.Key.localRotation = entry.Value;
            foreach (var entry in bindPositions) entry.Key.localPosition = entry.Value;
            float sitTarget = IsPerforming && CurrentAction == "sit" ? 1f : 0f;
            float crouchTarget = IsPerforming && (CurrentAction == "crouch" || CurrentAction == "rest") ? 1f : 0f;
            float dt = Time.deltaTime;
            if (IsPerforming && CurrentAction == "restSpot") crouchTarget = restSpotCrouch;
            if (!IsPerforming) crouchTarget = Mathf.Max(crouchTarget, GroomLowering);
            sitBlend = EasePose(ref sitPhase, sitTarget, dt, .54f, .48f);
            crouchBlend = EasePose(ref crouchPhase, crouchTarget, dt, .60f, .53f);
            pickupBlend = Blend(pickupBlend, loweringToPickUp ? 1f : 0f, dt, 12f);
            float pawTarget = IsPerforming && CurrentAction == "paw" ? 1f : 0f;
            pawBlend = EasePose(ref pawPhase, pawTarget, dt, .53f, .46f);
            bowBlend = IsPerforming && IsPlayBow(CurrentAction) ? PlayBowWeight() : 0f;
            float petTarget = IsPerforming && CurrentAction == "pet"
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, PetDuration, actionAge))
                : 0f;
            petBlend = Blend(petBlend, petTarget, dt, 9f);
            float pawLift = EaseBetween(.36f, 1f, pawPhase);
            float pawWeight = EaseBetween(.16f, .72f, pawPhase);

            Vector3 travel = Vector3.ProjectOnPlane(transform.position - lastRootPosition, Vector3.up);
            float yaw = HeadingChange(lastRootForward, transform.forward);
            if (travel.sqrMagnitude > 0.25f || Mathf.Abs(yaw) > 70f || Mathf.Abs(transform.position.y - groundHeight) > 0.25f)
            {
                ResetFootContacts();
                travel = Vector3.zero;
                yaw = 0f;
            }
            lastRootPosition = transform.position;
            lastRootForward = transform.forward;
            groundHeight = transform.position.y;
            // Travel and turning share one stride clock. Summing the two counted every curving step twice.
            float turningArc = .22f * yaw * Mathf.Deg2Rad;
            float distance = Mathf.Sqrt(travel.sqrMagnitude + turningArc * turningArc);
            float walkWeight = 1f - Mathf.Max(Mathf.Max(sitBlend, crouchBlend), Mathf.Max(pawBlend, bowBlend));
            bool walking = dt > 0f && distance / dt > 0.015f && (walkWeight > 0.55f || CurrentAction == "restSpot");
            locomotionBlend = Blend(locomotionBlend, walking ? 1f : 0f, dt, 9f);
            float previousTrotBlend = trotBlend;
            bool turningInPlace = CurrentAction == "spin" || !walking;
            float travelSpeed = dt > 0f ? travel.magnitude / dt : 0f;
            float turnRate = dt > 0f ? Mathf.Abs(yaw) / dt : 0f;
            trotting = !turningInPlace && turnRate < 92f && travelSpeed >= (trotting ? .97f : 1.14f);
            trotBlend = Mathf.MoveTowards(trotBlend, trotting ? 1f : 0f, 2.3f * dt);
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? ".L" : ".R";
                Bend("DEF-scapula" + suffix, crouchBlend * 8f + bowBlend * 7f + pickupBlend * 6f -
                    (side == 1 ? 7f * pawLift : 0f));
                Bend("DEF-front_upper" + suffix, crouchBlend * 20f - sitBlend * 9f + bowBlend * 14f + pickupBlend * 9f);
                Bend("DEF-front_forearm" + suffix, -crouchBlend * 52f - bowBlend * 35f - pickupBlend * 21f);
                Bend("DEF-front_paw" + suffix, crouchBlend * 18f + sitBlend * 7f);
                Bend("DEF-hind_thigh" + suffix, -sitBlend * 56f - crouchBlend * 30f - pickupBlend * 12f);
                Bend("DEF-hind_shin" + suffix, sitBlend * 84f + crouchBlend * 58f + pickupBlend * 21f);
                Bend("DEF-hind_hock" + suffix, -sitBlend * 34f - crouchBlend * 28f - pickupBlend * 10f);
            }
            Bend("DEF-pelvis", -sitBlend * 35f + bowBlend * 10f);
            Bend("DEF-spine.01", -sitBlend * 7f + bowBlend * 14f);
            Bend("DEF-spine.02", sitBlend * 7f + bowBlend * 8f + pickupBlend * 7f);
            float chestSettle = EaseBetween(.12f, .94f, sitBlend);
            float neckSettle = EaseBetween(.19f, 1f, crouchBlend);
            Bend("DEF-chest", chestSettle * 21f + bowBlend * 5f + pickupBlend * 8f);
            Bend("DEF-neck.01", -chestSettle * 5f - neckSettle * 8f + pickupBlend * 24f + petBlend * 9f);
            Bend("DEF-neck.02", pickupBlend * 17f - petBlend * 3f);
            ApplyTurnPose(dt);
            ApplyWalkingTorso();
            ApplyGaitRoll(Bone("DEF-chest"), 4f * pawWeight);
            ApplyHeadPose(dt);
            ApplySocialAttention(dt, walking);
            ApplyExplorationGlance(dt, walking);
            float breath = Mathf.Sin(Time.time * 2.35f) * (1f - locomotionBlend) * (1f - pickupBlend);
            AddPlayPitch("DEF-chest", breath * .85f);
            AddPlayPitch("DEF-neck.01", -breath * .65f);
            AddPlayPitch("DEF-head", 4f * pawWeight);
            Yaw("DEF-head", 5f * pawLift);
            AnimateTail(dt);
            float restSettle = CurrentAction == "restSpot" && IsPerforming ? EaseBetween(.64f, 1f, crouchBlend) : 0f;
            AddPlayPitch("DEF-neck.01", 9f * restSettle);
            AddPlayPitch("DEF-neck.02", 12f * restSettle);
            AddPlayPitch("DEF-head", -6f * restSettle);
            float bodyDrop = sitBlend * 0.194f + crouchBlend * 0.212f + bowBlend * 0.028f + pickupBlend * 0.11f;
            bodyDrop += .055f * restSettle;
            bodyDrop += .039f * GroomReach;
            float carriage = breed.IsOriginal ? Mathf.Lerp(.043f, .062f, trotBlend) : Mathf.Lerp(.019f, .033f, trotBlend);
            float suspension = Mathf.Lerp(.0038f, .0115f, trotBlend);
            bodyDrop += (carriage + Mathf.Cos(gaitCycle * 4f * Mathf.PI) * suspension) * locomotionBlend;
            visual.position += (Vector3.down * bodyDrop - transform.right * (.038f * pawWeight)) * bodyScale;
            ApplyPlayPose();
            ApplyCoatShake();
            UpdateFootContacts(walking, travel, yaw, distance, pawLift, dt, previousTrotBlend, travelSpeed, turnRate);
            coatCorrectives?.Apply(sitBlend, crouchBlend, bowBlend, pawBlend, pickupBlend);
            UpdatePuzzleMark();
            UpdatePawGrooming();
            if (carrying && carriedObject != null && MuzzleAnchor != null)
                carriedObject.SetPositionAndRotation(MuzzleAnchor.position, transform.rotation);
        }

        private void ApplyTurnPose(float dt)
        {
            float intent = turnIntent;
            if (IsPerforming && CurrentAction == "spin")
            {
                float leading = Mathf.SmoothStep(0f, 1f, actionAge / 0.26f);
                float settling = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.55f, SpinMotionEnd, actionAge));
                intent = leading * settling;
            }
            float plantWeight = Mathf.Max(Mathf.Max(sitBlend, crouchBlend), Mathf.Max(pawBlend, pickupBlend));
            torsoTurn = Blend(torsoTurn, intent * (1f - plantWeight), dt, 11f);
            // Let the head and shoulders lead. The pelvis follows with a slight counterturn.
            Yaw("DEF-pelvis", -3.3f * torsoTurn);
            Yaw("DEF-spine.01", 2.1f * torsoTurn);
            Yaw("DEF-spine.02", 3.0f * torsoTurn);
            Yaw("DEF-chest", 4.4f * torsoTurn);
            Yaw("DEF-neck.01", 5.5f * torsoTurn);
            Yaw("DEF-neck.02", 4.0f * torsoTurn);
            if (pelvis != null && pelvis.parent != null)
            {
                Vector3 roll = pelvis.parent.InverseTransformDirection(transform.forward);
                pelvis.localRotation = Quaternion.AngleAxis(-4.0f * torsoTurn, roll) * pelvis.localRotation;
            }
        }

        private void ApplyHeadPose(float dt)
        {
            if (head == null || head.parent == null) return;
            float facing = user == null ? 0f : HeadingChange(transform.forward, user.position - head.position);
            petLookYaw = Blend(petLookYaw, Mathf.Clamp(facing, -30f, 30f) * petBlend, dt, 9f);
            Bend("DEF-head", pickupBlend * 10f - bowBlend * 10f - petBlend * 4f);
            Yaw("DEF-head", petLookYaw + torsoTurn * 5f);
            Vector3 rollAxis = head.parent.InverseTransformDirection(transform.forward);
            float tilt = petBlend * (5f + 0.08f * petLookYaw);
            head.localRotation = Quaternion.AngleAxis(tilt, rollAxis) * head.localRotation;
        }

        private void AnimateTail(float dt)
        {
            bool quietPose = IsPerforming && (CurrentAction == "rest" || CurrentAction == "refuse");
            if (quietPose) tailWagAge = TailWagDuration;
            tailWagAge = Mathf.Min(tailWagAge + dt, TailWagDuration);
            // Brief onset, then a longer release. Frequency and angles are rig tuning, not a breed constant.
            float rise = Mathf.SmoothStep(0f, 1f, tailWagAge / 0.24f);
            float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.88f, TailWagDuration, tailWagAge));
            float wag = rise * fall;
            float frequency = Mathf.Lerp(1.25f, 2.25f, fall);
            tailWagPhase = Mathf.Repeat(tailWagPhase + dt * frequency * Mathf.PI * 2f, Mathf.PI * 2f);
            tailCarriage = Blend(tailCarriage, wag * 19f - crouchBlend * 4f, dt, 11f);

            for (int index = 0; index < tailBones.Length; index++)
            {
                Transform joint = tailBones[index];
                if (joint == null || joint.parent == null) continue;
                float balance = Mathf.Sin(gaitCycle * Mathf.PI * 2f - index * 0.20f) *
                    (1f - wag) * locomotionBlend * (3f - index * 0.55f);
                // The base leads, and the lighter distal motion follows with progressively greater damping.
                float sweep = (Mathf.Sin(tailWagPhase) - 0.12f) * TailSweep[index] * wag;
                tailBends[index] = Blend(tailBends[index], quietPose ? 0f : sweep + balance, dt, TailResponse[index]);
                Vector3 vertical = joint.parent.InverseTransformDirection(Vector3.up);
                Vector3 lateral = joint.parent.InverseTransformDirection(transform.right);
                float lift = tailCarriage * (1f - index * 0.25f);
                joint.localRotation = Quaternion.AngleAxis(tailBends[index], vertical) *
                    Quaternion.AngleAxis(lift, lateral) * bindRotations[joint];
            }
        }

        private static float Blend(float current, float target, float dt, float response)
        {
            float result = Mathf.Lerp(current, target, 1f - Mathf.Exp(-response * dt));
            return Mathf.Abs(result - target) < 0.003f ? target : result;
        }

        private static float EasePose(ref float phase, float target, float dt, float enter, float exit)
        {
            phase = Mathf.MoveTowards(phase, target, dt / (target > phase ? enter : exit));
            return Mathf.SmoothStep(0f, 1f, phase);
        }

        private Vector3 FootGoal(Foot foot, float forwardOffset = 0f, float lift = 0f)
        {
            Vector3 goal = transform.TransformPoint(foot.Neutral + Vector3.forward * forwardOffset);
            goal.y = transform.position.y + PawGroundClearance + lift;
            return goal;
        }

        private void UpdateFootContacts(bool walking, Vector3 travel, float yaw, float distance, float pawLift, float dt,
            float previousTrotBlend, float travelSpeed, float turnRate)
        {
            bool spinning = IsPerforming && CurrentAction == "spin";
            float lastCycle = gaitCycle;
            float pace = Mathf.InverseLerp(.35f, .90f, travelSpeed);
            float strideLength = Mathf.Lerp(Mathf.Lerp(.41f, .51f, pace), Mathf.Lerp(.52f, .62f,
                Mathf.InverseLerp(1.02f, 1.85f, travelSpeed)), trotBlend);
            strideLength = Mathf.Lerp(strideLength, .37f, Mathf.InverseLerp(25f, 95f, turnRate));
            // Shorter first steps keep the initial supports close while the dog gathers speed.
            strideLength *= Mathf.Lerp(.72f, 1f, Mathf.SmoothStep(0f, 1f, gaitCycle)) * strideScale;
            float stance = Mathf.Lerp(StanceFraction, TrotStanceFraction, trotBlend);
            // Preserve phase at rest so shoulder, tail and suspension can fade in place.
            if (walking) gaitCycle += Mathf.Min(distance / strideLength, 0.2f);

            bool posing = sitBlend + crouchBlend + pawBlend + bowBlend + pickupBlend > 0.01f;
            bool pouncing = IsPerforming && CurrentAction == "pounce";
            bool bowing = IsPerforming && IsPlayBow(CurrentAction) && PlayBowAge >= 0f;
            if (bowing) CapturePlayBowContacts();
            if (walking || spinning || posing || pouncing || bowing) CancelQuietStance();
            else UpdateQuietStance(dt);
            Foot earlySwing = walking && !spinning ? EarlyTurnStep(travel, yaw, turnRate, dt, lastCycle, previousTrotBlend) : null;
            foreach (Foot foot in feet)
            {
                if (pouncing)
                {
                    PlacePounceFoot(foot);
                    continue;
                }
                Vector3 goal;
                if (spinning)
                {
                    goal = SpinFootGoal(foot);
                    foot.Contact = goal;
                }
                else if (walking)
                {
                    bool replant = foot == earlySwing;
                    if ((replant || IsScheduledGaitStep(foot, lastCycle, previousTrotBlend)) && foot.Planted)
                    {
                        foot.Planted = false;
                        foot.SwingStart = foot.Contact;
                        foot.SwingProgress = 0f;
                        // Put touchdown ahead of neutral, leaving equal room on either end of stance.
                        foot.SwingFraction = replant ? Mathf.Min(1f - stance, .30f) : 1f - stance;
                        foot.SwingHeight = Mathf.Lerp(foot.Front ? .048f : .042f, foot.Front ? .096f : .083f, trotBlend) * bodyScale;
                        foot.Landing = PredictFootLanding(foot, travel, yaw, distance, strideLength, stance);
                    }
                    if (!foot.Planted)
                    {
                        AdvanceSwing(foot, (gaitCycle - lastCycle) / foot.SwingFraction);
                        if (foot.Planted) foot.LandedAt = Time.time;
                    }
                    goal = foot.Contact;
                    if (Mathf.Abs(yaw) > 0.3f || Mathf.Abs(torsoTurn) > 0.06f)
                        goal = KeepFootOnOwnSide(foot, goal);
                }
                else if (bowing)
                {
                    goal = PlayBowFootGoal(foot);
                }
                else if (posing)
                {
                    float raised = foot.Front && foot.Right ? pawLift : 0f;
                    float groomTuck = foot.Front && foot.Right == (microActionSide > 0f) ? GroomReach : 0f;
                    float foreOffset = crouchBlend * .15f + pickupBlend * .035f;
                    float rearOffset = sitBlend * .075f + crouchBlend * .035f;
                    Vector3 poseGoal = FootGoal(foot, (foot.Front ? foreOffset + raised * .056f - groomTuck * .081f : rearOffset) * bodyScale,
                        (raised * .16f + groomTuck * .039f) * bodyScale);
                    if (pawBlend > .01f && raised == 0f && foot.Planted)
                        poseGoal = foot.Contact;
                    foot.Contact = Vector3.MoveTowards(foot.Contact, poseGoal, dt * 1.3f);
                    foot.Planted = foot.Contact.y <= transform.position.y + PawGroundClearance + 0.005f;
                    foot.SwingStart = foot.Contact;
                    foot.Landing = FootGoal(foot);
                    foot.SwingProgress = 1f;
                    goal = foot.Contact;
                }
                else
                {
                    goal = KeepFootOnOwnSide(foot, foot.Contact);
                }
                if ((walking || spinning) && !foot.Planted)
                {
                    // Bias the solver toward folding the knee/elbow instead of rotating the straight leg.
                    float swing = Mathf.Sin(Mathf.PI * foot.SwingProgress) * Mathf.Lerp(.7f, 1.27f, trotBlend);
                    string side = foot.Right ? ".R" : ".L";
                    if (foot.Front)
                        Bend("DEF-front_forearm" + side, -crouchBlend * 52f - bowBlend * 35f - pickupBlend * 21f - swing * 24f);
                    else
                        Bend("DEF-hind_shin" + side, sitBlend * 84f + crouchBlend * 58f + pickupBlend * 21f + swing * 24f);
                }
                Quaternion neutral = transform.rotation * foot.NeutralRotation;
                if (!walking && foot.Front && foot.Right && pawLift > .001f)
                {
                    PlaceOfferedPaw(foot, goal, pawLift);
                    foot.Tip.rotation = Quaternion.Slerp(neutral, foot.Tip.rotation, EaseBetween(0f, .7f, pawLift));
                    continue;
                }
                if (!breed.IsOriginal && walking && foot.Front && !foot.Planted && !spinning)
                {
                    float swing = Mathf.Sin(Mathf.PI * foot.SwingProgress);
                    PlaceFoot(foot, goal);
                    for (int i = 0; i < foot.Chain.Length; i++)
                        poseJointRotations[i] = foot.Chain[i].localRotation;
                    PlaceOfferedPaw(foot, goal, .48f * swing * swing);
                    for (int i = 0; i < foot.Chain.Length; i++)
                        foot.Chain[i].localRotation = Quaternion.Slerp(poseJointRotations[i],
                            foot.Chain[i].localRotation, swing * swing);
                }
                else PlaceFoot(foot, goal);
                // The tip is the IK endpoint, so leveling the digits does not move the contact.
                float orientationLimit = foot.Planted ? 55f : 36f;
                foot.Tip.rotation = Quaternion.RotateTowards(foot.Tip.rotation, neutral, orientationLimit);
                foot.SupportError = foot.Planted ? Vector3.Distance(foot.Tip.position, goal) : 0f;
            }
        }

        private void StartSpin()
        {
            startSpin = transform.rotation;
            foreach (Foot foot in feet) foot.SpinStart = foot.Contact;
        }

        private static float SpinAngle(float elapsed) => 360f * Mathf.SmoothStep(0f, 1f, elapsed / SpinMotionEnd);

        private Quaternion SpinRotation(float degrees) => Quaternion.AngleAxis(degrees, Vector3.up) * startSpin;

        private Vector3 SpinFootGoal(Foot foot)
        {
            Vector3 goal = SpinFootOnArc(foot, SpinAngle(actionAge), out bool planted, out float swing);
            if (actionAge >= SpinMotionEnd)
            {
                // Clear the angular contacts with short staggered replanting steps before handing back to idle.
                float settle = Mathf.Clamp01((actionAge - SpinMotionEnd - foot.StepOffset * 0.10f) / 0.16f);
                goal.y = transform.position.y + PawGroundClearance;
                goal = Vector3.Lerp(goal, FootGoal(foot), Mathf.SmoothStep(0f, 1f, settle));
                swing = settle;
                planted = settle <= 0f || settle >= 1f;
                if (!planted) goal.y += 0.035f * Mathf.Sin(Mathf.PI * settle);
            }
            foot.Planted = planted;
            foot.SwingProgress = swing;
            return KeepFootOnOwnSide(foot, goal);
        }

        private Vector3 SpinFootOnArc(Foot foot, float angle, out bool planted, out float swing)
        {
            float firstPlant = 12f + foot.StepOffset * SpinStepArc;
            Vector3 from, to;
            if (angle < firstPlant)
            {
                float raiseAt = foot.StepOffset * 19f;
                if (angle <= raiseAt)
                {
                    swing = 1f;
                    planted = true;
                    return foot.SpinStart;
                }
                swing = Mathf.InverseLerp(raiseAt, firstPlant, angle);
                from = foot.SpinStart;
                to = SpinFootPlant(foot, firstPlant + 14f);
            }
            else
            {
                const float supportArc = 30f;
                float cycle = Mathf.Floor((angle - firstPlant) / SpinStepArc);
                float touchdown = firstPlant + cycle * SpinStepArc;
                float stepAngle = angle - touchdown;
                from = SpinFootPlant(foot, touchdown + 14f);
                if (stepAngle <= supportArc)
                {
                    swing = 1f;
                    planted = true;
                    return from;
                }
                swing = Mathf.InverseLerp(supportArc, SpinStepArc, stepAngle);
                to = SpinFootPlant(foot, touchdown + SpinStepArc + 14f);
            }
            planted = false;
            Vector3 goal = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, swing));
            goal.y += (foot.Front ? 0.059f : 0.049f) * Mathf.Sin(Mathf.PI * swing);
            return goal;
        }

        private Vector3 SpinFootPlant(Foot foot, float heading)
        {
            Vector3 stance = foot.Neutral;
            stance.x += Mathf.Sign(stance.x) * 0.036f;
            heading = Mathf.Min(heading, 360f);
            Vector3 point = transform.position + SpinRotation(heading) * Vector3.Scale(stance, transform.lossyScale);
            point.y = transform.position.y + PawGroundClearance;
            return point;
        }

        private Vector3 KeepFootOnOwnSide(Foot foot, Vector3 goal)
        {
            Vector3 local = transform.InverseTransformPoint(goal);
            float side = Mathf.Sign(foot.Neutral.x);
            // A very sharp pivot may otherwise leave an old contact under the opposite leg.
            float clearance = Mathf.Abs(foot.Neutral.x) * 0.48f;
            if (local.x * side < clearance) local.x = side * clearance;
            return transform.TransformPoint(local);
        }

        private static void AdvanceSwing(Foot foot, float amount)
        {
            foot.SwingProgress = Mathf.Clamp01(foot.SwingProgress + amount);
            float t = foot.SwingProgress;
            float height = foot.SwingHeight;
            foot.Contact = Vector3.Lerp(foot.SwingStart, foot.Landing, Mathf.SmoothStep(0f, 1f, t));
            // Ease off the floor and back onto it, with the peak slightly before mid-swing.
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .43f, t));
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.43f, 1f, t));
            foot.Contact.y += height * rise * (1f - fall);
            foot.Planted = t >= 1f;
        }

        private void PlaceFoot(Foot foot, Vector3 goal)
        {
            // Rotate the existing chain with bounded corrections. Positions and scale stay in the rig.
            for (int index = 0; index < foot.Chain.Length; index++)
                poseJointRotations[index] = foot.Chain[index].localRotation;
            for (int iteration = 0; iteration < 18; iteration++)
            {
                if ((foot.Tip.position - goal).sqrMagnitude < 0.000025f) break;
                for (int index = 0; index < foot.Chain.Length; index++)
                {
                    Transform joint = foot.Chain[index];
                    Vector3 from = foot.Tip.position - joint.position;
                    Vector3 to = goal - joint.position;
                    if (from.sqrMagnitude <= 0.000001f || to.sqrMagnitude <= 0.000001f) continue;
                    Quaternion correction = Quaternion.FromToRotation(from, to);
                    Quaternion world = Quaternion.RotateTowards(Quaternion.identity, correction, 22f) * joint.rotation;
                    Quaternion local = Quaternion.Inverse(joint.parent.rotation) * world;
                    float limit = foot.Front && index == 0 ? 26f : 70f;
                    joint.localRotation = Quaternion.RotateTowards(poseJointRotations[index], local, limit);
                }
            }
        }
        public IEnumerator PerformAction(string action, float hold = -1f)
        {
            if (action != "sit" && action != "crouch" && action != "spin" && action != "refuse" &&
                action != "fetch_lollipop" && action != "fetch_toy" && action != "come" &&
                action != "paw" && action != "bow" && action != "invite" && action != "rest" && action != "pet" &&
                action != "pounce" && action != "sniff" && action != "burst")
                throw new ArgumentException("Unsupported dog action.");
            while (Ready && !IsPerforming && IsFinishingIdleAction) yield return null;
            if (!Ready || IsPerforming) yield break;
            if ((action == "pounce" || action == "burst" || action == "invite" || action == "sniff") && !BeginPlayMotion(action))
            {
                StateLabel = "这里空间有点小，麦粒停了下来";
                yield break;
            }
            int version = ++actionVersion;
            IsPerforming = true; CurrentAction = action; actionAge = 0;
            DeferExploration(.7f);
            if (action == "spin") StartSpin();
            if (IsPlayBow(action)) BeginPlayBow();
            if (action == "bow" || action == "invite" || action == "fetch_toy" || action == "pet" ||
                action == "pounce" || action == "burst") PlayTailWag();
            if (action == "fetch_lollipop" || action == "fetch_toy")
                yield return FetchObject(version, action == "fetch_toy");
            else if (action == "come")
                yield return ComeToOwner(version);
            else
            {
                StateLabel = action == "sit" ? "坐下" : action == "crouch" ? "趴下" : action == "spin" ? "原地转圈" :
                    action == "paw" ? "抬起右爪" : action == "bow" || action == "invite" ? "前身伏低，邀请玩耍" :
                    action == "pounce" ? "前爪轻轻扑向前方" : action == "sniff" ? "低头嗅闻" :
                    action == "burst" ? "在草地上兴奋地小跑" : action == "rest" ? "趴下休息" :
                    action == "pet" ? "探头回应抚摸" : "歪头，暂时没跟上口令";
                float duration = action == "spin" ? SpinDuration : action == "refuse" ? 2.1f : action == "rest" ? 2f :
                    action == "pet" ? PetDuration : action == "pounce" ? PounceDuration : action == "sniff" ? SniffDuration :
                    action == "burst" ? BurstDuration + .24f : IsPlayBow(action) ? PlayBowDuration : 1.1f;
                yield return new WaitForSeconds(duration);
                if (version != actionVersion) yield break;
                if (action == "pounce" || action == "burst" || action == "invite" || action == "sniff")
                    LastPlaySucceeded = action != "burst" || !burstStopped;
            }
            if (version != actionVersion) yield break;
            yield return new WaitForSeconds(hold < 0f ? resumeDelay : hold);
            if (version != actionVersion) yield break;
            IsPerforming = false; CurrentAction = allowWandering ? "wander" : "idle";
            if (action == "come" && LastComeSucceeded)
            {
                QueueOwnerArrivalLook();
                yield break;
            }
            if (action == "invite" && allowWandering)
            {
                StateLabel = "望着你，等你回应";
                pauseUntil = Time.time + 5.5f;
                nextApproach = pauseUntil + 5f;
                target = transform.position;
                chooseAfterPause = true;
                yield break;
            }
            if (!allowWandering) { StateLabel = "原地等待训练"; yield break; }
            pauseUntil = Time.time + 0.4f; nextApproach = Time.time + UnityEngine.Random.Range(7f, 12f);
            ChooseDestination(false);
        }

        internal IEnumerator RestAt(Transform spot, bool house, Transform cushion)
        {
            while (Ready && !IsPerforming && IsFinishingIdleAction) yield return null;
            if (!Ready || IsPerforming || spot == null) yield break;
            int version = ++actionVersion;
            IsPerforming = true;
            DeferExploration(.7f);
            CurrentAction = "restSpot";
            LastActivitySucceeded = false;
            restSpotCrouch = 0f;
            StateLabel = house ? "走向狗窝" : "走向软垫";
            Vector3 approach = Limit(spot.TransformPoint(Vector3.back * 1.12f));
            if (house && cushion != null)
            {
                Vector3 route = Vector3.ProjectOnPlane(approach - transform.position, Vector3.up);
                Vector3 toPad = Vector3.ProjectOnPlane(cushion.position - transform.position, Vector3.up);
                float along = Vector3.Dot(toPad, route) / Mathf.Max(.001f, route.sqrMagnitude);
                if (along > 0f && along < 1f && (toPad - route * along).magnitude < 1.05f)
                {
                    float side = Vector3.Dot(-toPad, spot.right) >= 0f ? 1f : -1f;
                    Vector3 lane = cushion.position + spot.right * (side * 1.22f);
                    yield return Travel(lane - spot.forward * .80f, false, version);
                    if (version != actionVersion) yield break;
                    yield return Travel(lane + spot.forward * .87f, false, version);
                    if (version != actionVersion) yield break;
                }
            }
            yield return Travel(approach, false, version);
            if (version != actionVersion) yield break;
            if (house && FlatDistance(transform.position, approach) < .18f)
            {
                yield return RestInsideHouse(spot, version);
                if (version != actionVersion) yield break;
                LastActivitySucceeded = true;
            }
            else if (!house && FlatDistance(transform.position, approach) < .18f)
            {
                Vector3 outside = transform.position;
                Vector3 inside = spot.TransformPoint(new Vector3(0f, .095f, 0f));
                yield return FaceRestDirection(inside - outside, version);
                if (version != actionVersion) yield break;
                for (int pass = 0; pass < 2; pass++)
                {
                    float duration = Mathf.Max(1f, FlatDistance(outside, inside) / .58f);
                    for (float age = 0; age < duration && version == actionVersion; age += Time.deltaTime)
                    {
                        float t = Mathf.SmoothStep(0f, 1f, age / duration);
                        if (pass == 1) t = 1f - t;
                        Vector3 point = Vector3.Lerp(outside, inside, t);
                        point.y = Mathf.Lerp(outside.y, inside.y, EaseBetween(.05f, .6f, t));
                        point.y += Mathf.Sin(Mathf.PI * t) * .14f;
                        transform.position = point;
                        yield return null;
                    }
                    if (version != actionVersion) yield break;
                    if (pass == 0)
                    {
                        transform.position = inside;
                        StateLabel = "趴在软垫上休息";
                        restSpotCrouch = 1f;
                        yield return new WaitForSeconds(5f);
                        if (version != actionVersion) yield break;
                        restSpotCrouch = 0f;
                        StateLabel = "起身回到客厅";
                        yield return new WaitForSeconds(.55f);
                        if (version != actionVersion) yield break;
                    }
                }
                transform.position = outside;
                LastActivitySucceeded = true;
            }
            restSpotCrouch = 0f;
            IsPerforming = false;
            SetWanderingEnabled(allowWandering);
        }

        public IEnumerator VisitStation(Vector3 station, string label, bool lowerHead)
        {
            LastActivitySucceeded = false;
            while (Ready && !IsPerforming && IsFinishingIdleAction) yield return null;
            if (!Ready || IsPerforming) yield break;
            int version = ++actionVersion;
            IsPerforming = true;
            DeferExploration(.7f);
            CurrentAction = "activity";
            StateLabel = label;
            Vector3 approach = transform.position - station;
            approach.y = 0;
            if (approach.sqrMagnitude < 0.01f) approach = Vector3.back;
            Vector3 goal = Limit(station + approach.normalized * (lowerHead ? 0.43f : 0.2f));
            yield return Travel(goal, false, version);
            if (version != actionVersion) yield break;
            LastActivitySucceeded = FlatDistance(transform.position, goal) < 0.23f;
            if (LastActivitySucceeded)
            {
                loweringToPickUp = lowerHead;
                for (float elapsed = 0; elapsed < 1.3f && actionVersion == version; elapsed += Time.deltaTime)
                {
                    TurnTowards(station, Time.deltaTime);
                    yield return null;
                }
                loweringToPickUp = false;
            }
            if (version != actionVersion) yield break;
            IsPerforming = false; CurrentAction = allowWandering ? "wander" : "idle";
            pauseUntil = Time.time + 0.45f;
            nextApproach = Time.time + 8f;
            if (allowWandering) ChooseDestination(false);
        }

        public void SetPracticeArea(Vector3 start, Vector3 fetch, Vector3 toy)
        {
            transform.SetPositionAndRotation(start, Quaternion.Euler(0, 205, 0));
            ConfigurePracticeArea(fetch, toy);
        }

        internal void ConfigurePracticeArea(Vector3 fetch, Vector3 toy)
        {
            ToyStation = toy;
            lollipopHome = fetch;
            EnableTrainingProps();
            fetchMat.position = fetch + new Vector3(0.10f, -0.096f, 0f);
            ResetCompanion();
            nextApproach = Time.time + 8f;
        }

        private Vector3 Limit(Vector3 position) => PositionLimit == null ? position : PositionLimit(position);
        public void ResetCompanion()
        {
            actionVersion++;
            followingWalk = false;
            IsPerforming = false; CurrentAction = allowWandering ? "wander" : "idle";
            sitBlend = crouchBlend = pickupBlend = pawBlend = bowBlend = 0f;
            sitPhase = crouchPhase = pawPhase = 0f;
            ResetSocialAttention();
            petBlend = petLookYaw = tailWagPhase = tailCarriage = 0f;
            turnIntent = torsoTurn = 0f;
            tailWagAge = TailWagDuration;
            Array.Clear(tailBends, 0, tailBends.Length);
            ResetFootContacts();
            loweringToPickUp = false;
            LastFetchSucceeded = LastComeSucceeded = false;
            RestoreLollipop();
            ResetExploration();
            UpdatePuzzleMark();
            if (allowWandering) ChooseDestination(false); else SetWanderingEnabled(false); pauseUntil = 0;
        }

        public void SetWalkingTogether(bool active)
        {
            followingWalk = active;
            if (active) StateLabel = "跟着你去散步";
            else if (allowWandering) ChooseDestination(false);
        }

        public void EnableTrainingProps()
        {
            if (trainingProps != null) return;
            trainingProps = new GameObject("Dog training props").transform;
            matMaterial = MakePropMaterial("Fetch mat", new Color(0.16f, 0.34f, 0.37f));
            candyMaterial = MakePropMaterial("Lollipop candy", new Color(1f, 0.19f, 0.43f));
            stickMaterial = MakePropMaterial("Lollipop stick", new Color(0.98f, 0.90f, 0.79f));
            fetchMat = MakeProp(PrimitiveType.Cylinder, trainingProps, "Fetch mat",
                lollipopHome + new Vector3(0.10f, -0.096f, 0f), new Vector3(0.55f, 0.025f, 0.55f), matMaterial);
            lollipop = new GameObject("Lollipop").transform;
            lollipop.SetParent(trainingProps, false);
            Transform stick = MakeProp(PrimitiveType.Cylinder, lollipop, "Stick", new Vector3(0.065f, 0f, 0f),
                new Vector3(0.026f, 0.098f, 0.026f), stickMaterial);
            stick.localRotation = Quaternion.Euler(0f, 0f, 90f);
            MakeProp(PrimitiveType.Sphere, lollipop, "Candy", new Vector3(0.165f, 0f, 0f),
                new Vector3(0.17f, 0.17f, 0.17f), candyMaterial);
            ball = MakeProp(PrimitiveType.Sphere, trainingProps, "Toy ball", Vector3.zero,
                Vector3.one * 0.20f, matMaterial);
            RestoreLollipop();
        }

        private static Material MakePropMaterial(string name, Color color)
        {
            return new Material(Shader.Find("Standard")) { name = name, color = color };
        }

        private static Transform MakeProp(PrimitiveType shape, Transform parent, string name,
            Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            part.GetComponent<Collider>().enabled = false;
            return part.transform;
        }

        private void RestoreLollipop()
        {
            carrying = false;
            carriedObject = null;
            restorePropAt = 0f;
            ResetFetchProp(lollipop, lollipopHome);
            ResetFetchProp(ball, ToyStation + Vector3.up * .11f);
        }

        private IEnumerator ComeToOwner(int version)
        {
            LastComeSucceeded = false;
            StateLabel = "正在向你走来";
            yield return Travel(transform.position, true, version);
            if (version != actionVersion) yield break;
            LastComeSucceeded = IsNearOwner();
            if (LastComeSucceeded) PlayTailWag();
            StateLabel = LastComeSucceeded ? "在你身边摇尾巴" : "这次没能走到你身边";
        }

        private Transform ball;

        private IEnumerator FetchObject(int version, bool toy)
        {
            LastFetchSucceeded = false;
            Transform item = toy ? ball : lollipop;
            if (item == null || MuzzleAnchor == null || user == null)
            {
                StateLabel = "目前无法叼取玩具";
                yield break;
            }

            var physical = item.GetComponent<DogToyBody>();
            if (physical == null) RestoreLollipop();
            string itemName = physical != null ? physical.Label : toy ? "玩具球" : "棒棒糖毛绒玩具";
            if (physical != null && toy)
            {
                float settleUntil = Time.time + 1.4f;
                while (version == actionVersion && physical.Holder == DogToyHolder.Free &&
                    !physical.IsSettled && Time.time < settleUntil)
                    yield return null;
                if (version != actionVersion) yield break;
            }
            if (physical != null && !physical.CanFetch)
            {
                StateLabel = "等" + itemName + "落稳后再叼回来";
                yield break;
            }
            Vector3 home = item.position;
            StateLabel = "去取" + itemName;
            Vector3 approach = transform.position - home;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f) approach = -transform.forward;
            float approachDistance = Mathf.Lerp(.34f, .44f, Mathf.Clamp01(bodyScale));
            Vector3 pickupSpot = Limit(home + approach.normalized * approachDistance);
            yield return Travel(pickupSpot, false, version);
            if (version != actionVersion) yield break;
            if (FlatDistance(transform.position, pickupSpot) > 0.15f)
            {
                StateLabel = "这次没走到" + itemName + "旁边";
                yield break;
            }

            StateLabel = "低头叼起" + itemName;
            loweringToPickUp = true;
            for (float elapsed = 0f; elapsed < 0.65f && version == actionVersion; elapsed += Time.deltaTime)
            {
                TurnTowards(home, Time.deltaTime);
                yield return null;
            }
            if (version != actionVersion) yield break;
            loweringToPickUp = false;
            if (Vector3.Distance(MuzzleAnchor.position, item.position) > PickupReach ||
                (physical != null && !physical.TryHold(DogToyHolder.Dog)))
            {
                StateLabel = "够不到" + itemName + "，停下来等你";
                yield break;
            }

            carrying = true;
            carriedObject = item;
            StateLabel = "叼着" + itemName + "来找你";
            yield return Travel(transform.position, true, version);
            if (version != actionVersion) yield break;
            if (IsNearOwner())
            {
                Vector3 towardOwner = Vector3.ProjectOnPlane(user.position - transform.position, Vector3.up);
                Vector3 dropSpot = transform.position + Vector3.ClampMagnitude(towardOwner * .55f, .47f) + Vector3.up * .09f;
                if (physical != null) physical.ReleaseFromDog(dropSpot, transform.rotation);
                else
                {
                    item.SetPositionAndRotation(dropSpot, transform.rotation);
                    restorePropAt = Time.time + 8f;
                }
                carrying = false;
                carriedObject = null;
                LastFetchSucceeded = true;
                StateLabel = "把" + itemName + "放到了你面前";
            }
            else
            {
                if (physical == null) RestoreLollipop();
                else
                {
                    physical.ReleaseFromDog(transform.position + transform.forward * .35f + Vector3.up * .1f, transform.rotation);
                    carrying = false;
                    carriedObject = null;
                }
                StateLabel = "没能走到你身边，先把" + itemName + "放下了";
            }
        }

        private IEnumerator Travel(Vector3 destination, bool followUser, int version)
        {
            approaching = followUser;
            Vector3 currentTarget = destination;
            for (float elapsed = 0f; elapsed < TravelTimeLimit && version == actionVersion; elapsed += Time.deltaTime)
            {
                if (followUser)
                {
                    if (user == null) break;
                    if (IsNearOwner()) { TurnTowards(user.position, Time.deltaTime); break; }
                    Vector3 owner = user.position;
                    owner.y = transform.position.y;
                    currentTarget = Vector3.MoveTowards(owner, transform.position, OwnerStandOff);
                }

                currentTarget.x = Mathf.Clamp(currentTarget.x, -ActionYardLimit, ActionYardLimit);
                currentTarget.z = Mathf.Clamp(currentTarget.z, -ActionYardLimit, ActionYardLimit);
                currentTarget.y = transform.position.y;
                currentTarget = Limit(currentTarget);
                Vector3 offset = currentTarget - transform.position;
                if (offset.sqrMagnitude < 0.012f) break;
                WalkTowards(currentTarget, Mathf.Clamp(walkSpeed * 1.37f, .4f, 1.03f), Time.deltaTime);
                yield return null;
            }
        }

        private bool IsNearOwner()
        {
            return user != null && FlatDistance(transform.position, user.position) <= OwnerStandOff + 0.12f;
        }

        private static float FlatDistance(Vector3 from, Vector3 to)
        {
            to.y = from.y;
            return Vector3.Distance(from, to);
        }

        private float TurnTowards(Vector3 position, float dt, float relaxedCone = 0f)
        {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= 0.0001f) return 0f;
            float angle = HeadingChange(transform.forward, offset);
            float remaining = Mathf.Max(0f, Mathf.Abs(angle) - relaxedCone);
            if (Time.frameCount - lastTurnFrame > 1) yawSpeed = 0f;
            lastTurnFrame = Time.frameCount;
            const float angularAcceleration = 550f;
            float requested = Mathf.Min(88f, Mathf.Sqrt(2f * angularAcceleration * remaining)) * Mathf.Sign(angle);
            yawSpeed = Mathf.MoveTowards(yawSpeed, requested, angularAcceleration * dt);
            float step = Mathf.Clamp(yawSpeed * dt, -remaining, remaining);
            turnIntent = Mathf.Clamp(angle / 55f, -1f, 1f);
            transform.rotation = Quaternion.AngleAxis(step, Vector3.up) * transform.rotation;
            return angle;
        }

        private static float HeadingChange(Vector3 from, Vector3 to)
        {
            Vector3 flatFrom = Vector3.ProjectOnPlane(from, Vector3.up);
            Vector3 flatTo = Vector3.ProjectOnPlane(to, Vector3.up);
            // SignedAngle's 180 degree case can have an unstable sign when the cross product approaches zero.
            float sin = Vector3.Dot(Vector3.up, Vector3.Cross(flatFrom, flatTo));
            float cos = Vector3.Dot(flatFrom, flatTo);
            return Mathf.Abs(sin) < 0.00001f && cos < 0f ? 180f : Mathf.Atan2(sin, cos) * Mathf.Rad2Deg;
        }

        private void OnDestroy()
        {
            if (trainingProps != null) Destroy(trainingProps.gameObject);
            if (matMaterial != null) Destroy(matMaterial);
            if (candyMaterial != null) Destroy(candyMaterial);
            if (stickMaterial != null) Destroy(stickMaterial);
            if (coat != null) Destroy(coat);
            DisposeExpressions();
        }
    }
}
