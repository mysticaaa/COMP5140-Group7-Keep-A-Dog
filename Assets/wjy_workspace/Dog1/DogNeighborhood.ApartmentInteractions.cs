using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogNeighborhood
    {
        private const float PlayerRadius = .21f;
        private const float DoorThickness = .065f;
        private readonly List<ApartmentDoor> apartmentDoors = new List<ApartmentDoor>(2);
        private ParticleSystem showerStreams, showerSplashes;
        private bool showerRunning;
        private Vector3 showerControl;
        private string interactionHint;

        internal string InteractionHint => interactionHint;

        private void AddApartmentDoor(string name, string label, Vector3 hinge, Vector3 target, float width, float closedYaw, float openAngle)
        {
            Transform pivot = new GameObject(name + " hinge").transform;
            pivot.SetParent(root, true);
            pivot.position = hinge;
            pivot.rotation = Quaternion.Euler(0f, closedYaw, 0f);

            GameObject leaf = Box(name + " door", pivot.TransformPoint(At(width * .5f, 1.09f, 0f)),
                At(width, 2.14f, DoorThickness), ColorOf("F7E9D4"), pivot.eulerAngles, solid: true);
            leaf.transform.SetParent(pivot, true);
            GameObject panel = Box(name + " raised panel", pivot.TransformPoint(At(width * .5f, 1.26f, DoorThickness * .5f)),
                At(width - .22f, 1.49f, .018f), ColorOf("F2DFC2"), pivot.eulerAngles);
            panel.transform.SetParent(pivot, true);
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject lever = Box(name + " brass lever",
                    pivot.TransformPoint(At(width - .18f, 1.06f, side * .059f)),
                    At(.15f, .038f, .081f), ColorOf("B08C5E"), pivot.eulerAngles);
                lever.transform.SetParent(pivot, true);
            }

            // Both leaves swing toward the inside of their rooms.
            apartmentDoors.Add(new ApartmentDoor(pivot, target, width, closedYaw, openAngle, label));
        }

        internal DogCareAction? UpdateApartmentInteractions(Transform camera, bool allowInput, bool pressed, float deltaTime, bool trackedPassenger = false)
        {
            interactionHint = null;
            if (lift != null)
            {
                interactionHint = lift.Tick(camera, allowInput, pressed, deltaTime, trackedPassenger);
                if (interactionHint != null) return null;
            }
            if (lift != null && lift.IsGroundLevel(camera.position.y)) return null;

            foreach (ApartmentDoor door in apartmentDoors)
                door.Advance(camera.position, deltaTime);

            if (!allowInput) return null;
            Physics.SyncTransforms();

            float foodAim = BowlAim(camera, FoodBowl);
            float waterAim = BowlAim(camera, WaterBowl);
            if (Mathf.Max(foodAim, waterAim) > 0f)
            {
                DogCareAction action = foodAim >= waterAim ? DogCareAction.Feed : DogCareAction.Water;
                bool full = HasServing(action);
                interactionHint = action == DogCareAction.Feed
                    ? (full ? "食盆里有狗粮" : "E  添加狗粮")
                    : (full ? "水盆里有水" : "E  添加饮水");
                if (pressed && !full) return action;
                return null;
            }

            float best = 2f;
            ApartmentDoor selected = null;
            foreach (ApartmentDoor door in apartmentDoors)
            {
                if (!CanReach(camera, door.Target, 1.8f, door.Pivot, out float distance) || distance >= best) continue;
                selected = door;
                best = distance;
            }

            if (showerStreams != null && showerSplashes != null &&
                CanReach(camera, showerControl, 1.55f, null, out float showerDistance) && showerDistance < best)
            {
                if (pressed)
                {
                    showerRunning = !showerRunning;
                    if (!showerRunning)
                    {
                        showerStreams.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        showerSplashes.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    else
                    {
                        showerStreams.Play();
                        showerSplashes.Play();
                    }
                }
                interactionHint = showerRunning ? "E  关闭花洒" : "E  打开花洒";
            }
            else if (selected != null)
            {
                if (pressed) selected.Open = !selected.Open;
                interactionHint = "E  " + (selected.Open ? "关闭" : "打开") + selected.Label;
            }
            return null;
        }

        private static float BowlAim(Transform camera, DogToyBody bowl)
        {
            if (bowl == null || !bowl.IsSettled) return 0f;
            Vector3 surface = bowl.transform.position + Vector3.up * .16f;
            float aim = Vector3.Dot(camera.forward, (surface - camera.position).normalized);
            return aim > .95f && CanReach(camera, surface, 2.25f, bowl.transform, out _) ? aim : 0f;
        }

        private static bool CanReach(Transform camera, Vector3 target, float reach, Transform owner, out float distance)
        {
            Vector3 offset = target - camera.position;
            distance = offset.magnitude;
            if (distance > reach || Vector3.Dot(camera.forward, offset) < distance * .43f) return false;
            return !Physics.Raycast(camera.position, offset.normalized, out RaycastHit hit, distance + .04f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || (owner != null && hit.transform.IsChildOf(owner));
        }

        private void BuildShowerWater(Vector3 nozzle, Vector3 tray, Vector3 control)
        {
            showerRunning = false;
            showerControl = control;
            const string key = "Shower droplets";
            var water = new Material(Shader.Find("Sprites/Default")) { name = key };
            water.mainTexture = Resources.Load<Texture2D>(ApartmentResources + "ShowerDroplet");
            materials.Add(key, water);

            // A tight falling spray ends above the shower tray, where a second emitter scatters droplets.
            showerStreams = ShowerEmitter("Shower water", nozzle, Quaternion.Euler(90f, 0f, 0f), water,
                .045f, 7f, 220f, 3.7f, new ParticleSystem.MinMaxCurve(.395f, .410f),
                new ParticleSystem.MinMaxCurve(.017f, .028f));
            ParticleSystemRenderer streamRenderer = showerStreams.GetComponent<ParticleSystemRenderer>();
            streamRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            streamRenderer.velocityScale = .032f;
            streamRenderer.lengthScale = 1.7f;
            streamRenderer.sortingFudge = -.5f;

            showerSplashes = ShowerEmitter("Shower tray splashes", tray, Quaternion.Euler(-90f, 0f, 0f), water,
                .20f, 68f, 125f, .68f, new ParticleSystem.MinMaxCurve(.105f, .17f),
                new ParticleSystem.MinMaxCurve(.020f, .040f));
            var splashMain = showerSplashes.main;
            splashMain.startDelay = .39f;
            splashMain.gravityModifier = .65f;
            showerSplashes.GetComponent<ParticleSystemRenderer>().sortingFudge = -.5f;
        }

        private ParticleSystem ShowerEmitter(string name, Vector3 origin, Quaternion direction, Material material,
            float radius, float angle, float flow, float speed, ParticleSystem.MinMaxCurve lifetime,
            ParticleSystem.MinMaxCurve size)
        {
            var emitter = new GameObject(name);
            emitter.SetActive(false);
            emitter.transform.SetParent(root, true);
            emitter.transform.SetPositionAndRotation(origin, direction);
            var particles = emitter.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = new Color(.72f, .91f, 1f, .86f);
            main.maxParticles = 250;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = flow;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = radius;
            shape.angle = angle;

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.95f, .72f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = fade;

            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            emitter.SetActive(true);
            return particles;
        }

        private sealed class ApartmentDoor
        {
            private readonly float width, closedYaw, openAngle;
            public readonly Transform Pivot;
            public readonly Vector3 Target;
            public readonly string Label;
            public bool Open { get; set; }
            public float Angle { get; private set; }

            public ApartmentDoor(Transform pivot, Vector3 target, float width, float closedYaw, float openAngle, string label)
            {
                Pivot = pivot;
                Target = target;
                this.width = width;
                this.closedYaw = closedYaw;
                this.openAngle = openAngle;
                Label = label;
            }

            public bool Overlaps(Vector3 point, float angle)
            {
                Vector3 local = Quaternion.Euler(0f, -closedYaw - angle, 0f) * (point - Pivot.position);
                float dx = local.x - Mathf.Clamp(local.x, 0f, width);
                float dz = Mathf.Max(0f, Mathf.Abs(local.z) - DoorThickness * .5f);
                return dx * dx + dz * dz < PlayerRadius * PlayerRadius;
            }

            public void Reset()
            {
                Open = false;
                Angle = 0f;
                Pivot.rotation = Quaternion.Euler(0f, closedYaw, 0f);
            }

            public void Advance(Vector3 player, float deltaTime)
            {
                float goal = Open ? openAngle : 0f;
                float next = Mathf.MoveTowards(Angle, goal, 255f * deltaTime);
                if (next == Angle) return;

                // Check the swept arc so a leaf cannot rotate through the player on a slow frame.
                int steps = Mathf.CeilToInt(Mathf.Abs(next - Angle) / 3f);
                float initial = Angle;
                for (int step = 1; step <= steps; step++)
                {
                    float candidate = Mathf.Lerp(initial, next, step / (float)steps);
                    if (Overlaps(player, candidate)) break;
                    Angle = candidate;
                }
                Pivot.rotation = Quaternion.Euler(0f, closedYaw + Angle, 0f);
            }
        }
    }
}
