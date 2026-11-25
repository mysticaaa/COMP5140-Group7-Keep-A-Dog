using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRUsages = UnityEngine.XR.CommonUsages;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private sealed class ToyGrip
        {
            public DogToyBody Toy;
            public Vector3 PreviousPosition, Offset, Velocity;
            public Quaternion RotationOffset;
            public bool Tracked, Pressed, Shown;
            public float PresentTime;
        }

        private readonly ToyGrip leftToyGrip = new ToyGrip();
        private readonly ToyGrip rightToyGrip = new ToyGrip();
        private readonly ToyGrip desktopToyGrip = new ToyGrip();
        private readonly DogToyBody[] physicalProps = new DogToyBody[3];
        private InputAction leftGripInput, rightGripInput;
        private float lastToyAttention = -10f;
        internal string PhysicalPropHint { get; private set; } = "";

        private void ConfigurePhysicalProps()
        {
            if (dog == null || !dog.Ready) return;
            DropGrip(leftToyGrip, false);
            DropGrip(rightToyGrip, false);
            DropGrip(desktopToyGrip, false);
            if (physicalProps[2] != null)
            {
                physicalProps[2].gameObject.SetActive(false);
                Destroy(physicalProps[2].gameObject);
            }
            dog.ConfigurePhysicalFetchProps(out physicalProps[0], out physicalProps[1]);
            Vector3 offset = life.CurrentLocation == DogLocation.Apartment ? new Vector3(-.36f, .07f, .58f) :
                new Vector3(-.48f, .08f, -.35f);
            Vector3 center = world.ConstrainDogPosition(world.ToyPosition + offset);
            physicalProps[2] = dog.CreateNeighborhoodToy(life.CurrentLocation, center);
            foreach (DogToyBody toy in physicalProps) toy.SetPlayArea(world.ConstrainDogPosition);
            perception.SetTargets(physicalProps[0].transform, physicalProps[1].transform);
            PhysicalPropHint = "";

            if (leftGripInput != null) return;
            leftGripInput = new InputAction("Dog1 left grab", InputActionType.Value, "<XRController>{LeftHand}/grip");
            rightGripInput = new InputAction("Dog1 right grab", InputActionType.Value, "<XRController>{RightHand}/grip");
            leftGripInput.Enable();
            rightGripInput.Enable();
        }

        private void DisposePhysicalProps()
        {
            leftGripInput?.Dispose();
            rightGripInput?.Dispose();
        }

        private void UpdatePhysicalProps()
        {
            xrInteract = false;
            PhysicalPropHint = "";
            bool canUse = started && !pause && !choosingBreed && !world.IsElevatorMoving;
            if (!canUse)
            {
                ResetGrip(leftToyGrip);
                ResetGrip(rightToyGrip);
                DropGrip(desktopToyGrip, false);
                return;
            }
            if (InHeadset)
            {
                DropGrip(desktopToyGrip, false);
                UpdateHandGrip(false, leftToyGrip);
                UpdateHandGrip(true, rightToyGrip);
                return;
            }
            ResetGrip(leftToyGrip);
            ResetGrip(rightToyGrip);
            UpdateDesktopGrip();
        }

        private void UpdateHandGrip(bool right, ToyGrip grip)
        {
            if (!HeadsetRig.TryGetHandPosition(right, out Vector3 position) ||
                !HeadsetRig.TryGetAimRay(right, out Ray aim))
            {
                ResetGrip(grip);
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(aim.direction, HeadsetRig.HeadCamera.transform.up);
            bool pressed = ReadGrip(right, grip.Pressed);
            if (!grip.Tracked)
            {
                grip.Tracked = true;
                grip.PreviousPosition = position;
                grip.Pressed = pressed;
                return;
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, .001f);
            Vector3 delta = position - grip.PreviousPosition;
            grip.Velocity = delta.sqrMagnitude > .045f ? Vector3.zero :
                Vector3.Lerp(grip.Velocity, delta / dt, Mathf.Clamp01(dt * 19f));
            grip.PreviousPosition = position;

            if (grip.Toy != null)
            {
                if (!pressed) DropGrip(grip, true);
                else FollowGrip(grip, position, rotation);
            }
            else
            {
                DogToyBody candidate = NearbyToy(position);
                if (candidate != null) PhysicalPropHint = "握把拿起  ·  " + candidate.Label;
                if (pressed && !grip.Pressed) UseGripPress(right, grip, candidate, position, rotation);
            }
            if (grip.Toy != null)
            {
                PhysicalPropHint = "松开握把放下  ·  伸给麦粒看";
                WatchPresentation(grip, dt);
            }
            grip.Pressed = pressed;
        }

        private void UseGripPress(bool right, ToyGrip grip, DogToyBody candidate, Vector3 position, Quaternion rotation)
        {
            if (candidate != null && TryGrip(grip, candidate, position, rotation,
                    right ? DogToyHolder.RightHand : DogToyHolder.LeftHand))
            {
                var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(right ? XRNode.RightHand : XRNode.LeftHand);
                if (device.isValid) device.SendHapticImpulse(0, .17f, .04f);
            }
            else if (right) xrInteract = true;
        }

        private bool ReadGrip(bool right, bool wasPressed)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(right ? XRNode.RightHand : XRNode.LeftHand);
            float threshold = wasPressed ? .42f : .64f;
            float value = (right ? rightGripInput : leftGripInput)?.ReadValue<float>() ?? 0f;
            if (device.isValid && device.TryGetFeatureValue(XRUsages.grip, out float analog))
                value = Mathf.Max(value, analog);
            if (value > 0f) return value >= threshold;
            return device.isValid && device.TryGetFeatureValue(XRUsages.gripButton, out bool pressed) && pressed;
        }

        private DogToyBody NearbyToy(Vector3 position)
        {
            DogToyBody best = null;
            float bestDistance = .17f;
            foreach (DogToyBody toy in physicalProps)
            {
                if (toy == null || toy.Holder != DogToyHolder.Free) continue;
                Vector3 contact = toy.ClosestPoint(position);
                float distance = Vector3.Distance(position, contact);
                if (distance >= bestDistance || !ClearToyRay(position, contact, toy)) continue;
                best = toy;
                bestDistance = distance;
            }
            return best;
        }

        private static bool ClearToyRay(Vector3 from, Vector3 to, DogToyBody toy)
        {
            if (!Physics.Linecast(from, to, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            return hit.collider.transform == toy.transform || hit.collider.transform.IsChildOf(toy.transform);
        }

        private static bool TryGrip(ToyGrip grip, DogToyBody toy, Vector3 position, Quaternion rotation, DogToyHolder holder)
        {
            if (!toy.TryHold(holder)) return false;
            var relative = Quaternion.Inverse(rotation);
            grip.Toy = toy;
            grip.Offset = relative * (toy.transform.position - position);
            grip.RotationOffset = relative * toy.transform.rotation;
            grip.PresentTime = 0f;
            grip.Shown = false;
            return true;
        }

        private static void FollowGrip(ToyGrip grip, Vector3 position, Quaternion rotation) =>
            grip.Toy.SetGripPose(position + rotation * grip.Offset, rotation * grip.RotationOffset);

        private static void DropGrip(ToyGrip grip, bool allowThrow)
        {
            if (grip.Toy == null) return;
            bool nearHand = Vector3.Distance(grip.Toy.transform.position, grip.PreviousPosition) < .32f;
            grip.Toy.Drop(allowThrow && nearHand ? grip.Velocity : Vector3.zero);
            grip.Toy = null;
            grip.PresentTime = 0f;
            grip.Velocity = Vector3.zero;
        }

        private static void ResetGrip(ToyGrip grip)
        {
            DropGrip(grip, false);
            grip.Tracked = false;
            grip.Pressed = false;
        }

        private bool ToyBlocksPetting(bool right)
        {
            ToyGrip hand = right ? rightToyGrip : leftToyGrip;
            return hand.Toy != null || hand.Pressed;
        }

        private void WatchPresentation(ToyGrip grip, float dt)
        {
            if (grip.Shown || dog?.PerceptionHead == null || !Ready || VoiceListening || VoiceFinalizing ||
                Time.unscaledTime - lastToyAttention < 3f)
            {
                grip.PresentTime = 0f;
                return;
            }
            Vector3 origin = dog.PerceptionHead.position + Vector3.up * .025f;
            Vector3 item = grip.Toy.transform.position;
            float distance = Vector3.Distance(origin, item);
            if (distance < .24f || distance > 1.28f || grip.Velocity.sqrMagnitude > 1.4f ||
                !ClearToyRay(origin, item, grip.Toy))
            {
                grip.PresentTime = 0f;
                return;
            }
            grip.PresentTime += dt;
            if (grip.PresentTime < .4f || !dog.NoticePresentedToy(grip.Toy.transform)) return;
            grip.Shown = true;
            lastToyAttention = Time.unscaledTime;
            reply = "麦粒转头看着你手里的" + grip.Toy.Label + "，尾巴摇了起来。";
        }

        private void UpdateDesktopGrip()
        {
            if (viewCamera == null) return;
            var head = viewCamera.transform;
            Vector3 position = head.position + head.forward * .72f - head.up * .16f;
            bool pressed = !IsTextEntryFocused && !VoiceListening && !VoiceFinalizing && Input.GetKeyDown(KeyCode.G);
            if (desktopToyGrip.Toy != null)
            {
                if (pressed) DropGrip(desktopToyGrip, false);
                else
                {
                    FollowGrip(desktopToyGrip, position, head.rotation);
                    WatchPresentation(desktopToyGrip, Time.unscaledDeltaTime);
                    PhysicalPropHint = "G  放下玩具";
                }
                return;
            }
            if (!Physics.SphereCast(head.position, .09f, head.forward, out RaycastHit hit, 1.75f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            var toy = hit.collider.GetComponentInParent<DogToyBody>();
            if (toy == null || toy.Holder != DogToyHolder.Free) return;
            PhysicalPropHint = "G  拿起" + toy.Label;
            if (!pressed) return;
            if (TryGrip(desktopToyGrip, toy, position, head.rotation, DogToyHolder.Desktop))
                desktopToyGrip.Offset = Vector3.zero;
        }
    }
}
