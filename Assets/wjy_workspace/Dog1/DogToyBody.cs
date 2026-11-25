using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    internal enum DogToyHolder { Free, LeftHand, RightHand, Desktop, Dog }

    [DisallowMultipleComponent]
    internal sealed class DogToyBody : MonoBehaviour
    {
        private readonly Dictionary<Color32, Material> materials = new Dictionary<Color32, Material>();
        private Collider toyCollider;
        private Rigidbody body;
        private Vector3 home, targetPosition;
        private Quaternion homeRotation, targetRotation;
        private float recoveryAt;
        private float inaccessibleSince;
        private System.Func<Vector3, Vector3> dogFloorLimit;

        internal string Label { get; private set; }
        internal DogToyHolder Holder { get; private set; }
        internal bool CanFetch => Holder == DogToyHolder.Free && body.velocity.sqrMagnitude < .15f &&
            Mathf.Abs(body.position.y - home.y) < .32f;

        internal void Configure(string label, Collider shape, float mass)
        {
            Label = label;
            toyCollider = shape;
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.drag = .65f;
            body.angularDrag = 1.2f;
            body.maxAngularVelocity = 10f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            SetHome(transform.position, transform.rotation);
        }

        internal void SetHome(Vector3 position, Quaternion rotation)
        {
            home = position;
            homeRotation = rotation;
            ReturnHome();
        }

        internal void SetPlayArea(System.Func<Vector3, Vector3> constrain) => dogFloorLimit = constrain;

        private void ReturnHome()
        {
            body.isKinematic = false;
            body.position = home;
            body.rotation = homeRotation;
            transform.SetPositionAndRotation(home, homeRotation);
            Drop(Vector3.zero);
        }

        internal Vector3 ClosestPoint(Vector3 point) => toyCollider.ClosestPoint(point);

        internal bool TryHold(DogToyHolder holder)
        {
            if (Holder != DogToyHolder.Free || holder == DogToyHolder.Free ||
                (holder == DogToyHolder.Dog && !CanFetch)) return false;
            Holder = holder;
            body.velocity = body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.interpolation = holder == DogToyHolder.Dog ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            toyCollider.enabled = holder != DogToyHolder.Dog;
            targetPosition = body.position;
            targetRotation = body.rotation;
            return true;
        }

        internal void SetGripPose(Vector3 position, Quaternion rotation)
        {
            targetPosition = position;
            targetRotation = rotation;
        }

        internal void Drop(Vector3 velocity)
        {
            Holder = DogToyHolder.Free;
            toyCollider.enabled = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.velocity = Vector3.ClampMagnitude(velocity, 4.2f);
            body.angularVelocity = Vector3.zero;
            recoveryAt = Time.time + 1f;
            inaccessibleSince = 0f;
        }

        internal void ReleaseFromDog(Vector3 position, Quaternion rotation)
        {
            if (Holder != DogToyHolder.Dog) return;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Drop(Vector3.zero);
        }

        private void FixedUpdate()
        {
            if (Holder == DogToyHolder.Dog) return;
            if (Holder == DogToyHolder.Free)
            {
                Vector3 difference = body.position - home;
                if (Time.time >= recoveryAt) RecoverLostToy(difference);
                return;
            }

            Vector3 step = Vector3.ClampMagnitude(targetPosition - body.position, 7f * Time.fixedDeltaTime);
            float distance = step.magnitude;
            if (distance > .001f && body.SweepTest(step / distance, out RaycastHit hit, distance + .008f,
                    QueryTriggerInteraction.Ignore))
                step *= Mathf.Clamp01((hit.distance - .008f) / distance);
            body.MovePosition(body.position + step);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, targetRotation, 720f * Time.fixedDeltaTime));
        }

        private void RecoverLostToy(Vector3 difference)
        {
            if (difference.y < -4f || difference.sqrMagnitude > 1225f)
            {
                ReturnHome();
                return;
            }
            bool outOfReach = dogFloorLimit != null &&
                (dogFloorLimit(body.position) - body.position).sqrMagnitude > .18f;
            if (!outOfReach || body.velocity.sqrMagnitude > .15f) inaccessibleSince = 0f;
            else if (inaccessibleSince == 0f) inaccessibleSince = Time.time;
            else if (Time.time - inaccessibleSince > 3.5f) ReturnHome();
        }

        internal Material ColorMaterial(Color32 color)
        {
            if (!materials.TryGetValue(color, out Material material))
            {
                material = new Material(Shader.Find("Standard")) { color = color };
                material.SetFloat("_Glossiness", .16f);
                materials.Add(color, material);
            }
            return material;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials.Values) Destroy(material);
        }
    }
}
