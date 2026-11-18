using UnityEngine;

namespace Wjy.DogTraining
{
    [DisallowMultipleComponent]
    public sealed class DogApartmentLift : MonoBehaviour
    {
        [SerializeField] private Transform cabin, cabinPanel, upperCallButton, lowerCallButton;
        [SerializeField] private Transform upperStop, lowerStop;
        [SerializeField] private Transform[] upperDoors, lowerDoors, cabinDoors;
        [SerializeField] private Rect upperHall, shaft;
        [SerializeField] private float travelSeconds = 4.2f;

        private Vector3[] upperClosed, lowerClosed, cabinClosed;
        private float progress;
        private bool upper = true, targetUpper, doorsOpen, travelling, passenger;
        private float passengerOffset;

        internal Rect UpperHall => upperHall;
        internal bool CarryingPlayer => travelling && passenger;
        internal bool IsGroundLevel(float eyeHeight) => eyeHeight < (upperStop.position.y + lowerStop.position.y) * .5f + 1.65f;

        internal void ResetLift()
        {
            if (upperClosed == null)
            {
                upperClosed = ClosedPositions(upperDoors);
                lowerClosed = ClosedPositions(lowerDoors);
                cabinClosed = ClosedPositions(cabinDoors);
            }
            upper = true;
            travelling = passenger = doorsOpen = false;
            cabin.position = upperStop.position;
            ShowDoors();
        }

        internal bool Blocks(Vector3 position, float radius)
        {
            if (position.x + radius < shaft.xMin || position.x - radius > shaft.xMax ||
                Mathf.Abs(position.z - shaft.yMax) > radius + .07f)
                return false;
            return travelling || !doorsOpen || IsGroundLevel(position.y) == upper;
        }

        internal string Tick(Transform camera, bool allowInput, bool pressed, float deltaTime)
        {
            if (travelling)
            {
                progress = Mathf.Min(1f, progress + deltaTime / Mathf.Max(.1f, travelSeconds));
                Vector3 start = upper ? upperStop.position : lowerStop.position;
                Vector3 end = targetUpper ? upperStop.position : lowerStop.position;
                cabin.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, progress));
                if (passenger)
                {
                    Vector3 position = camera.position;
                    position.y = cabin.position.y + passengerOffset;
                    camera.position = position;
                }
                if (progress < 1f) return passenger ? (targetUpper ? "电梯上行…" : "电梯下行…") : null;
                upper = targetUpper;
                passenger = travelling = false;
                doorsOpen = true;
                ShowDoors();
            }

            if (!allowInput) return null;
            bool playerUpper = !IsGroundLevel(camera.position.y);
            Vector3 location = camera.position;
            if (shaft.Contains(new Vector2(location.x, location.z)) && location.z < shaft.yMax - .34f)
            {
                if (!LookingAt(camera, cabinPanel.position, 2.15f)) return null;
                if (pressed)
                {
                    passengerOffset = location.y - cabin.position.y;
                    StartRide(!playerUpper, true);
                    return playerUpper ? "电梯下行…" : "电梯上行…";
                }
                return playerUpper ? "E  去地面层" : "E  返回公寓层";
            }

            Transform call = playerUpper ? upperCallButton : lowerCallButton;
            if ((doorsOpen && upper == playerUpper) || !LookingAt(camera, call.position, 1.9f)) return null;
            if (pressed)
            {
                if (upper == playerUpper)
                {
                    doorsOpen = true;
                    ShowDoors();
                    return null;
                }
                StartRide(playerUpper, false);
                return "电梯正在到达…";
            }
            return "E  呼叫电梯";
        }

        private void StartRide(bool toUpper, bool carry)
        {
            targetUpper = toUpper;
            passenger = carry;
            travelling = true;
            doorsOpen = false;
            progress = 0f;
            ShowDoors();
        }

        private void ShowDoors()
        {
            PositionDoors(upperDoors, upperClosed, doorsOpen && upper);
            PositionDoors(lowerDoors, lowerClosed, doorsOpen && !upper);
            PositionDoors(cabinDoors, cabinClosed, doorsOpen);
        }

        private static Vector3[] ClosedPositions(Transform[] doors)
        {
            if (doors == null) return System.Array.Empty<Vector3>();
            var points = new Vector3[doors.Length];
            for (int i = 0; i < doors.Length; i++) points[i] = doors[i].localPosition;
            return points;
        }

        private static void PositionDoors(Transform[] doors, Vector3[] closed, bool open)
        {
            for (int i = 0; i < closed.Length; i++)
                doors[i].localPosition = closed[i] + (open ? Vector3.right * (i == 0 ? -.70f : .70f) : Vector3.zero);
        }

        private static bool LookingAt(Transform camera, Vector3 target, float range)
        {
            Vector3 offset = target - camera.position;
            float distance = offset.magnitude;
            return distance <= range && Vector3.Dot(camera.forward, offset) >= distance * .49f;
        }
    }
}
