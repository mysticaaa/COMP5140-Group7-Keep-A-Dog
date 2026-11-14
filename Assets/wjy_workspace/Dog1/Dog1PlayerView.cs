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
