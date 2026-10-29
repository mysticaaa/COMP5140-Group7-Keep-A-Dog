using UnityEngine;
namespace Wjy.DogTraining
{
    public sealed class Dog1PlayerView : MonoBehaviour
    {
        private float yaw, pitch;
        private void Start() { yaw = transform.eulerAngles.y; pitch = transform.eulerAngles.x; }
        private void Update()
        {
            if (!Input.GetMouseButton(1)) return;
            yaw += Input.GetAxis("Mouse X") * 2f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2f, -65, 65);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 direction = transform.right * Input.GetAxisRaw("Horizontal") +
                Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * Input.GetAxisRaw("Vertical");
            transform.position += direction.normalized * Time.deltaTime * 2.5f;
            Vector3 position = transform.position; position.y = 1.65f;
            position.x = Mathf.Clamp(position.x, -7f, 7f); position.z = Mathf.Clamp(position.z, -7f, 7f);
            transform.position = position;
        }
    }
}