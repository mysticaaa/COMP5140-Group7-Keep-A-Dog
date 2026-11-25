using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed class Dog1PlayerView : MonoBehaviour
    {
        public DogNeighborhood Neighborhood { get; set; }
        public bool InputSuspended { get; set; }
        private float yaw, pitch;

        public void Place(Vector3 position)
        {
            transform.position = position;
            yaw = 0f;
            pitch = 19f;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        }

        public void LookAt(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            if (direction.sqrMagnitude < .01f) return;
            Vector3 angles = Quaternion.LookRotation(direction, Vector3.up).eulerAngles;
            yaw = angles.y;
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -65f, 65f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        }

        private void Update()
        {
            if (InputSuspended || !Input.GetMouseButton(1)) return;
            yaw += Input.GetAxis("Mouse X") * 2f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2f, -65, 65);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 direction = transform.right * Input.GetAxisRaw("Horizontal") +
                Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * Input.GetAxisRaw("Vertical");
            Vector3 proposed = transform.position + direction.normalized * Time.deltaTime * 2.5f;
            if (Neighborhood != null) proposed = Neighborhood.ConstrainPlayerMotion(transform.position, proposed);
            transform.position = proposed;
        }
    }
}
