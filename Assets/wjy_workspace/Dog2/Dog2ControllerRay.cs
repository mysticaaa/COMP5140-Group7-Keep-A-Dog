using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wjy.DogTraining
{
    // A controller ray for this scene's world-space uGUI panel. No mouse emulation.
    public sealed class Dog2ControllerRay : MonoBehaviour
    {
        public Dog2OpenXR Rig;
        public Canvas TargetCanvas;
        public bool RightHand;
        public bool ManualInputForValidation;
        public int ClickCount { get; private set; }
        private LineRenderer line;
        private Transform marker;
        private Material material;
        private PointerEventData pointer;
        private EventSystem eventSystem;
        private GameObject hovered, pressed;
        private bool wasHeld;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private static readonly Color IdleColor = new Color(0.2f, 0.75f, 1f);
        private static readonly Color HoverColor = new Color(0.35f, 1f, 0.35f);

        private void Awake()
        {
            if (EventSystem.current == null) new GameObject("Dog2 UI EventSystem", typeof(EventSystem));
            material = new Material(Shader.Find("Sprites/Default"));
            var visual = new GameObject("Controller UI ray");
            visual.transform.SetParent(transform, false);
            line = visual.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = 2; line.useWorldSpace = true;
            line.startWidth = 0.007f; line.endWidth = 0.003f;
            line.enabled = false;
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Controller ray hit"; dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.018f;
            Destroy(dot.GetComponent<Collider>());
            dot.GetComponent<Renderer>().sharedMaterial = material;
            marker = dot.transform; dot.SetActive(false);
        }
        private void LateUpdate()
        {
            if (ManualInputForValidation) return;
            if (Rig == null || !Rig.TryGetAimRay(RightHand, out Ray ray))
            {
                ProcessRay(new Ray(), false, false); return;
            }
            ProcessRay(ray, Rig.IsTriggerHeld(RightHand), true);
        }
        // This is also exercised by the geometric UI integration test without a headset.
        public void ProcessRay(Ray ray, bool held, bool tracked)
        {
            if (pointer == null || eventSystem != EventSystem.current)
            {
                eventSystem = EventSystem.current;
                pointer = new PointerEventData(eventSystem) { pointerId = RightHand ? -21 : -20, button = PointerEventData.InputButton.Left };
            }
            GameObject target = null;
            Vector3 end = ray.GetPoint(5f);
            if (tracked && TargetCanvas != null && TargetCanvas.worldCamera != null)
            {
                var rect = TargetCanvas.GetComponent<RectTransform>();
                var plane = new Plane(rect.forward, rect.position);
                if (plane.Raycast(ray, out float distance) && distance <= 5f)
                {
                    Vector3 hit = ray.GetPoint(distance);
                    if (rect.rect.Contains((Vector2)rect.InverseTransformPoint(hit)))
                    {
                        end = hit;
                        pointer.position = RectTransformUtility.WorldToScreenPoint(TargetCanvas.worldCamera, hit);
                        hits.Clear();
                        var raycaster = TargetCanvas.GetComponent<GraphicRaycaster>();
                        if (raycaster != null) raycaster.Raycast(pointer, hits);
                        foreach (var result in hits)
                        {
                            var candidate = ExecuteEvents.GetEventHandler<IPointerClickHandler>(result.gameObject);
                            var selectable = candidate != null ? candidate.GetComponent<Selectable>() : null;
                            if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable()) continue;
                            target = candidate;
                            pointer.pointerCurrentRaycast = result;
                            break;
                        }
                    }
                }
            }
            if (hovered != target)
            {
                if (hovered != null) ExecuteEvents.Execute(hovered, pointer, ExecuteEvents.pointerExitHandler);
                hovered = target; pointer.pointerEnter = target;
                if (hovered != null) ExecuteEvents.Execute(hovered, pointer, ExecuteEvents.pointerEnterHandler);
            }
            if (!tracked) held = false;
            if (held && !wasHeld && target != null)
            {
                pressed = target; pointer.pointerPress = target;
                pointer.pressPosition = pointer.position;
                pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
                pointer.eligibleForClick = true;
                ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerDownHandler);
            }
            if (!held && wasHeld)
            {
                if (pressed != null)
                {
                    ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerUpHandler);
                    if (tracked && target == pressed)
                    {
                        ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerClickHandler);
                        ClickCount++;
                    }
                }
                pressed = null; pointer.pointerPress = null; pointer.eligibleForClick = false;
            }
            wasHeld = held;
            line.enabled = tracked;
            marker.gameObject.SetActive(tracked && target != null);
            if (tracked)
            {
                line.SetPosition(0, ray.origin); line.SetPosition(1, end);
                Color color = target != null ? HoverColor : IdleColor;
                line.startColor = line.endColor = color;
                marker.position = end;
            }
        }
        private void OnDisable()
        {
            if (line != null) ProcessRay(new Ray(), false, false);
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}