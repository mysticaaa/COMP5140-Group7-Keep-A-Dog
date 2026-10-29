using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    // Skeletal animation authored for the CC BY German Shepherd rig (not a recolored primitive).
    public sealed class DogCompanion : MonoBehaviour
    {
        public Transform user;
        public float wanderRadius = 4f;
        public float walkSpeed = 0.75f;
        public float approachInterval = 12f;
        public float resumeDelay = 3f;
        public string StateLabel { get; private set; } = "自由散步";
        public bool IsPerforming { get; private set; }
        public string CurrentAction { get; private set; } = "wander";
        public bool Ready { get; private set; }
        private Transform visual, head, pelvis;
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly Dictionary<Transform, Quaternion> bindRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> bindPositions = new Dictionary<Transform, Vector3>();
        private Vector3 target;
        private float nextApproach, pauseUntil, gait, actionAge, moveAmount;
        private float crouchBlend, sitBlend, angerBlend;
        private Quaternion startSpin;
        private ParticleSystem smoke;
        private Material coat, smokeMaterial;
        private Texture2D smokeTexture;
        private bool approaching;
        private bool chooseAfterPause;
        private readonly Dictionary<string, Vector3> restingFeet = new Dictionary<string, Vector3>();
        public Transform VisualRoot => visual;

        private void Awake()
        {
            if (user == null && Camera.main != null) user = Camera.main.transform;
            var model = Resources.Load<GameObject>("Dog1/GermanShepherd");
            if (model == null) { StateLabel = "德牧模型尚未导入，请等待 Unity 完成导入"; return; }
            visual = Instantiate(model, transform).transform;
            visual.name = "German Shepherd - rigged";
            foreach (Animator animator in visual.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (Animation animation in visual.GetComponentsInChildren<Animation>()) animation.enabled = false;
            foreach (Transform bone in visual.GetComponentsInChildren<Transform>())
            {
                bones[bone.name] = bone;
                bindRotations[bone] = bone.localRotation;
                bindPositions[bone] = bone.localPosition;
            }
            head = Bone("DEF-head"); pelvis = Bone("DEF-pelvis");
            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds(); bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            if (!hasBounds) { StateLabel = "模型没有可渲染网格"; return; }
            float scale = 0.95f / Mathf.Max(bounds.size.y, 0.001f);
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
            visual.position += new Vector3(transform.position.x - bounds.center.x,
                transform.position.y - bounds.min.y, transform.position.z - bounds.center.z);
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
            }
            foreach (Transform bone in visual.GetComponentsInChildren<Transform>())
            {
                bindRotations[bone] = bone.localRotation;
                bindPositions[bone] = bone.localPosition;
            }
            float footHeight = float.PositiveInfinity;
            foreach (string name in new[] { "DEF-front_toes.L", "DEF-front_toes.R", "DEF-hind_paw.L", "DEF-hind_paw.R" })
                if (Bone(name) != null) footHeight = Mathf.Min(footHeight, Bone(name).position.y);
            if (!float.IsInfinity(footHeight)) visual.position += Vector3.up * (0.025f - footHeight);
            bindPositions[visual] = visual.localPosition;
            foreach (string name in new[] { "DEF-front_toes.L", "DEF-front_toes.R", "DEF-hind_paw.L", "DEF-hind_paw.R" })
                if (Bone(name) != null) restingFeet[name] = transform.InverseTransformPoint(Bone(name).position);
            BuildSmoke();
            nextApproach = Time.time + 6f;
            ChooseDestination(false);
            Ready = true;
        }

        private Transform Bone(string name) { bones.TryGetValue(name, out Transform bone); return bone; }
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
            bone.localRotation = Quaternion.AngleAxis(degrees, localAxis) * bindRotations[bone];
        }

        private void Update()
        {
            if (!Ready) return;
            float dt = Time.deltaTime;
            if (IsPerforming)
            {
                actionAge += dt;
                if (CurrentAction != "spin") FaceUser(dt);
                moveAmount = Mathf.MoveTowards(moveAmount, 0f, dt * 5);
                if (CurrentAction == "spin" && actionAge <= 2.2f)
                    transform.rotation = startSpin * Quaternion.Euler(0, 360f * Mathf.Clamp01(actionAge / 2.2f), 0);
                return;
            }
            if (Time.time >= nextApproach)
            {
                ChooseDestination(true);
                nextApproach = Time.time + UnityEngine.Random.Range(approachInterval, approachInterval + 6f);
                pauseUntil = 0;
            }
            if (Time.time < pauseUntil)
            {
                moveAmount = Mathf.MoveTowards(moveAmount, 0f, dt * 5);
                if (approaching) FaceUser(dt);
                return;
            }
            if (chooseAfterPause) { ChooseDestination(false); chooseAfterPause = false; }
            Vector3 delta = target - transform.position; delta.y = 0;
            if (delta.magnitude < 0.15f)
            {
                pauseUntil = Time.time + UnityEngine.Random.Range(1.5f, 3f);
                StateLabel = approaching ? "靠近你，摇尾巴" : "停下来闻一闻";
                chooseAfterPause = true;
                return;
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(delta), dt * 150f);
            float speed = walkSpeed * Mathf.Clamp01(Vector3.Dot(transform.forward, delta.normalized) + 0.1f);
            transform.position += transform.forward * Mathf.Min(speed * dt, delta.magnitude);
            moveAmount = Mathf.MoveTowards(moveAmount, speed / walkSpeed, dt * 4);
            gait += dt * speed * 9f;
        }

        private void ChooseDestination(bool approach, bool updateLabel = true)
        {
            Vector3 center = user != null ? user.position : Vector3.zero; center.y = 0;
            approaching = approach; chooseAfterPause = false;
            Vector2 offset = UnityEngine.Random.insideUnitCircle;
            if (offset.sqrMagnitude < 0.01f) offset = Vector2.right;
            offset.Normalize();
            float radius = approach ? 1.5f : UnityEngine.Random.Range(2.1f, wanderRadius);
            target = center + new Vector3(offset.x, 0, offset.y) * radius;
            if (updateLabel) StateLabel = approach ? "主动向你靠近" : "自由散步";
        }
        private void FaceUser(float dt)
        {
            if (user == null) return;
            Vector3 delta = user.position - transform.position; delta.y = 0;
            if (delta.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(delta), 170f * dt);
        }

        private void LateUpdate()
        {
            if (!Ready) return;
            foreach (var entry in bindRotations) entry.Key.localRotation = entry.Value;
            foreach (var entry in bindPositions) entry.Key.localPosition = entry.Value;
            float sitTarget = IsPerforming && CurrentAction == "sit" ? 1f : 0f;
            float crouchTarget = IsPerforming && CurrentAction == "crouch" ? 1f : 0f;
            float angerTarget = IsPerforming && CurrentAction == "refuse" && actionAge < 1.7f ? 1f : 0f;
            sitBlend = Mathf.MoveTowards(sitBlend, sitTarget, Time.deltaTime * 2.5f);
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouchTarget, Time.deltaTime * 2.5f);
            angerBlend = Mathf.MoveTowards(angerBlend, angerTarget, Time.deltaTime * 5f);
            float walk = moveAmount * (1f - Mathf.Max(sitBlend, crouchBlend));
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? ".L" : ".R";
                float phase = gait + side * Mathf.PI;
                float swing = Mathf.Sin(phase) * 22f * walk;
                float hindSwing = Mathf.Sin(phase + Mathf.PI) * 23f * walk;
                Bend("DEF-front_upper" + suffix, swing + crouchBlend * 32f - sitBlend * 9f);
                Bend("DEF-front_forearm" + suffix, -Mathf.Max(0, -Mathf.Sin(phase)) * 28f * walk - crouchBlend * 70f);
                Bend("DEF-front_paw" + suffix, crouchBlend * 38f + sitBlend * 9f);
                Bend("DEF-hind_thigh" + suffix, hindSwing - sitBlend * 68f - crouchBlend * 44f);
                Bend("DEF-hind_shin" + suffix, Mathf.Max(0, Mathf.Sin(phase)) * 35f * walk + sitBlend * 112f + crouchBlend * 82f);
                Bend("DEF-hind_hock" + suffix, -sitBlend * 44f - crouchBlend * 38f);
            }
            Bend("DEF-pelvis", -sitBlend * 20f);
            Bend("DEF-spine.01", sitBlend * 13f);
            Bend("DEF-chest", sitBlend * 7f);
            Bend("DEF-neck.01", -sitBlend * 5f - crouchBlend * 8f);
            Yaw("DEF-head", Mathf.Sin(Time.time * 18f) * 12f * angerBlend);
            for (int i = 1; i <= 4; i++)
                Yaw("DEF-tail.0" + i, Mathf.Sin(Time.time * 5.5f + i * 0.6f) * (6f + (approaching ? 7f : 0f)));
            if (pelvis != null)
                pelvis.position += Vector3.down * (sitBlend * 0.23f + crouchBlend * 0.22f)
                    + Vector3.up * Mathf.Sin(gait * 2f) * 0.012f * walk;
            // Keep the front of the torso upright while the haunches lower in a sit.
            Transform chest = Bone("DEF-chest");
            if (chest != null) chest.position += Vector3.up * sitBlend * 0.20f;
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? ".L" : ".R";
                float phase = gait + side * Mathf.PI;
                PlaceFoot("DEF-front_toes" + suffix, new[] { "DEF-front_forearm" + suffix, "DEF-front_upper" + suffix },
                    phase, walk, crouchBlend * 0.06f);
                PlaceFoot("DEF-hind_paw" + suffix, new[] { "DEF-hind_hock" + suffix, "DEF-hind_shin" + suffix, "DEF-hind_thigh" + suffix },
                    phase + Mathf.PI, walk, sitBlend * 0.08f - crouchBlend * 0.04f);
            }
            if (smoke != null)
                smoke.transform.position = (head != null ? head.position : transform.position + Vector3.up * 0.8f) + Vector3.up * 0.19f;
        }

        private void PlaceFoot(string footName, string[] chain, float phase, float walking, float forwardOffset)
        {
            Transform foot = Bone(footName);
            if (foot == null || !restingFeet.TryGetValue(footName, out Vector3 local)) return;
            local.z += forwardOffset + Mathf.Cos(phase) * 0.09f * walking;
            Vector3 goal = transform.TransformPoint(local);
            goal.y = transform.position.y + 0.025f + Mathf.Max(0, Mathf.Sin(phase)) * 0.065f * walking;
            for (int iteration = 0; iteration < 7; iteration++)
            {
                foreach (string jointName in chain)
                {
                    Transform joint = Bone(jointName);
                    if (joint == null) continue;
                    Vector3 from = foot.position - joint.position, to = goal - joint.position;
                    if (from.sqrMagnitude > 0.000001f && to.sqrMagnitude > 0.000001f)
                        joint.rotation = Quaternion.FromToRotation(from, to) * joint.rotation;
                }
            }
        }
        public IEnumerator PerformAction(string action)
        {
            if (action != "sit" && action != "crouch" && action != "spin" && action != "refuse")
                throw new ArgumentException("Unsupported dog action.");
            IsPerforming = true; CurrentAction = action; actionAge = 0; startSpin = transform.rotation;
            StateLabel = action == "sit" ? "坐下" : action == "crouch" ? "蹲低身体" : action == "spin" ? "原地转圈" : "不想执行，生气冒烟";
            if (action == "refuse") { smoke.Play(); smoke.Emit(12); }
            float duration = action == "spin" ? 2.2f : action == "refuse" ? 1.7f : 1.1f;
            yield return new WaitForSeconds(duration);
            if (action == "refuse") smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            StateLabel = "动作完成，3 秒后恢复散步";
            yield return new WaitForSeconds(resumeDelay);
            IsPerforming = false; CurrentAction = "wander";
            pauseUntil = Time.time + 0.4f; nextApproach = Time.time + UnityEngine.Random.Range(7f, 12f);
            ChooseDestination(false);
        }
        public void ResetCompanion()
        {
            IsPerforming = false; CurrentAction = "wander"; sitBlend = crouchBlend = angerBlend = 0;
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ChooseDestination(false); pauseUntil = 0;
        }

        private void BuildSmoke()
        {
            GameObject effect = new GameObject("Angry steam puffs"); effect.transform.SetParent(transform);
            smoke = effect.AddComponent<ParticleSystem>(); smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main; main.playOnAwake = false; main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.23f);
            main.startColor = new Color(0.75f, 0.77f, 0.8f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 60;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = smoke.emission; emission.rateOverTime = 20;
            var shape = smoke.shape; shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 17; shape.radius = 0.07f; shape.rotation = new Vector3(-90, 0, 0);
            var size = smoke.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.9f));
            var color = smoke.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.gray, 1) },
                new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0f, 1) });
            color.color = gradient;
            smokeTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f;
                smokeTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0, 1, 1 - Mathf.Clamp01(d))));
            }
            smokeTexture.Apply();
            smokeMaterial = new Material(Shader.Find("Sprites/Default")); smokeMaterial.mainTexture = smokeTexture;
            smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMaterial;
        }
        private void OnDestroy()
        {
            if (coat != null) Destroy(coat);
            if (smokeMaterial != null) Destroy(smokeMaterial);
            if (smokeTexture != null) Destroy(smokeTexture);
        }
    }
}