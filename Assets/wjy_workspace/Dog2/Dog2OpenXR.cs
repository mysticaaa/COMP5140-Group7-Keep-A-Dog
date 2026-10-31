using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Wjy.DogTraining
{
    // Only Dog2 explicitly starts XR. Other scenes retain their desktop behavior.
    public sealed class Dog2OpenXR : MonoBehaviour
    {
        public Transform Origin { get; private set; }
        public Camera HeadCamera { get; private set; }
        public string Status { get; private set; } = "正在启动 OpenXR……";
        public bool SkipInitializationForValidation;
        public event System.Action ToggleSpeech;
        public event System.Action CancelSpeech;
        private Transform offset;
        private InputAction talk, cancel, headPosition, headRotation, leftStick, rightStick;
        private InputAction leftPosition, leftRotation, rightPosition, rightRotation;
        private Transform leftHand, rightHand;
        private InputAction leftAimPosition, leftAimRotation, rightAimPosition, rightAimRotation, leftTrigger, rightTrigger;
        private bool leftTracked, rightTracked;
        private Material handMaterial;
        private bool ownsLoader, xrStarted, snapReleased = true;
        public bool IsSessionRunning => xrStarted;
        private XRManagerSettings manager;
        public bool IsInitializing { get; private set; }
        public bool DisplayRunning => manager != null && manager.activeLoader != null &&
            manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>() != null &&
            manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>().running;
        public string DisplayStatus => "双眼显示：" + (DisplayRunning ? "运行中" : "未启动") +
            "；头显设备：" + (UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid ? "已检测到" : "未检测到");
        private bool previousTalk, previousCancel;
        private int talkPressCount;
        private string lastVoiceInput = "尚未收到按键";
        public string InputStatus { get; private set; } = "等待右手柄连接";

        private void Awake()
        {
            var originObject = new GameObject("Dog2 XR Origin");
            Origin = originObject.transform; Origin.SetParent(transform);
            Origin.position = new Vector3(0, 0, -5);
            offset = new GameObject("Camera Floor Offset").transform;
            offset.SetParent(Origin, false); offset.localPosition = Vector3.up * 1.65f;
            HeadCamera = Camera.main;
            if (HeadCamera == null) HeadCamera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            HeadCamera.tag = "MainCamera";
            HeadCamera.transform.SetParent(offset, false);
            HeadCamera.transform.localPosition = Vector3.zero; HeadCamera.transform.localRotation = Quaternion.identity;
            HeadCamera.nearClipPlane = 0.05f; HeadCamera.farClipPlane = 200;
            HeadCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            HeadCamera.clearFlags = CameraClearFlags.SolidColor;
            HeadCamera.backgroundColor = new Color(0.52f, 0.67f, 0.78f);
            // Create and enable actions before OpenXR creates its session.
            talk = new InputAction("Voice toggle A", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            cancel = new InputAction("Cancel voice B", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            headPosition = new InputAction("Head position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            headRotation = new InputAction("Head rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            leftStick = new InputAction("Walk", InputActionType.Value, "<XRController>{LeftHand}/thumbstick", expectedControlType: "Vector2");
            rightStick = new InputAction("Snap turn", InputActionType.Value, "<XRController>{RightHand}/thumbstick", expectedControlType: "Vector2");
            leftPosition = new InputAction("Left pose position", InputActionType.Value, "<XRController>{LeftHand}/devicePosition", expectedControlType: "Vector3");
            leftRotation = new InputAction("Left pose rotation", InputActionType.Value, "<XRController>{LeftHand}/deviceRotation", expectedControlType: "Quaternion");
            rightPosition = new InputAction("Right pose position", InputActionType.Value, "<XRController>{RightHand}/devicePosition", expectedControlType: "Vector3");
            rightRotation = new InputAction("Right pose rotation", InputActionType.Value, "<XRController>{RightHand}/deviceRotation", expectedControlType: "Quaternion");
            leftAimPosition = new InputAction("Left aim position", InputActionType.Value, "<XRController>{LeftHand}/pointerPosition", expectedControlType: "Vector3");
            leftAimRotation = new InputAction("Left aim rotation", InputActionType.Value, "<XRController>{LeftHand}/pointerRotation", expectedControlType: "Quaternion");
            rightAimPosition = new InputAction("Right aim position", InputActionType.Value, "<XRController>{RightHand}/pointerPosition", expectedControlType: "Vector3");
            rightAimRotation = new InputAction("Right aim rotation", InputActionType.Value, "<XRController>{RightHand}/pointerRotation", expectedControlType: "Quaternion");
            leftTrigger = new InputAction("Left UI select", InputActionType.Value, "<XRController>{LeftHand}/trigger");
            rightTrigger = new InputAction("Right UI select", InputActionType.Value, "<XRController>{RightHand}/trigger");
            foreach (var action in AllActions()) action.Enable();
            var driver = HeadCamera.gameObject.AddComponent<TrackedPoseDriver>();
            driver.positionInput = new InputActionProperty(headPosition); driver.rotationInput = new InputActionProperty(headRotation);
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            handMaterial = new Material(Shader.Find("Standard")); handMaterial.color = new Color(0.75f, 0.82f, 0.88f);
            leftHand = CreateHand("Left controller"); rightHand = CreateHand("Right controller");
        }
        private Transform CreateHand(string name)
        {
            var hand = GameObject.CreatePrimitive(PrimitiveType.Capsule); hand.name = name;
            hand.transform.SetParent(offset, false); hand.transform.localScale = new Vector3(0.04f, 0.07f, 0.04f);
            Destroy(hand.GetComponent<Collider>()); hand.GetComponent<Renderer>().sharedMaterial = handMaterial;
            hand.SetActive(false); return hand.transform;
        }
        private IEnumerable<InputAction> AllActions()
        {
            return new[] { talk, cancel, headPosition, headRotation, leftStick, rightStick, leftPosition, leftRotation, rightPosition, rightRotation, leftAimPosition, leftAimRotation, rightAimPosition, rightAimRotation, leftTrigger, rightTrigger };
        }
        private IEnumerator Start()
        {
            yield return null;
            if (SkipInitializationForValidation) { Status = "桌面测试模式：未启动头显"; yield break; }
            yield return InitializeXR();
        }
        public void RetryXR()
        {
            if (IsInitializing) { Status = "正在连接 VR，请稍候。"; return; }
            if (SkipInitializationForValidation) { Status = "桌面测试模式未启用 XR。"; return; }
            if (xrStarted && DisplayRunning)
            {
                Status = "双眼显示正在运行。若只看到桌面，请收起 Quest Link 的桌面菜单。";
                return;
            }
            xrStarted = leftTracked = rightTracked = false;
            leftHand.gameObject.SetActive(false); rightHand.gameObject.SetActive(false);
            if (ownsLoader && manager != null)
            {
                manager.StopSubsystems(); manager.DeinitializeLoader(); ownsLoader = false;
            }
            StartCoroutine(InitializeXR());
        }
        private IEnumerator InitializeXR()
        {
            if (IsInitializing) yield break;
            IsInitializing = true;
            Status = "正在连接 OpenXR，请保持佩戴头显并进入 Quest Link……";
            try
            {            // Enable the profiles on the actual settings used by this session before initialization.
            var settings = OpenXRSettings.Instance;
            int controllerProfiles = 0;
            if (settings != null)
                foreach (var feature in settings.GetFeatures())
                    if (feature is OculusTouchControllerProfile || feature is MetaQuestTouchPlusControllerProfile)
                    {
                        feature.enabled = true;
                        controllerProfiles++;
                    }
            Debug.Log("[Dog2 XR] Controller profiles before loader initialization: " + controllerProfiles);
            if (controllerProfiles == 0)
            {
                Status = "OpenXR 缺少 Quest 手柄配置，请退出 Play，执行 WJY / Configure Dog2 PC VR。";
                yield break;
            }
            manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null) { Status = "缺少 OpenXR 配置，请执行 WJY / Configure Dog2 PC VR 后重启 Unity。"; yield break; }
            if (manager.activeLoader == null)
            {
                yield return manager.InitializeLoader();
                ownsLoader = manager.activeLoader != null;
            }
            if (manager.activeLoader == null)
            {
                Status = "VR 启动失败：运行时未提供可用头显。保持佩戴并进入 Quest Link，然后点击重新连接 VR 或按 F9。"; yield break;
            }
            int activeProfiles = 0;
            foreach (var feature in OpenXRSettings.Instance.GetFeatures())
                if (feature.enabled && (feature is OculusTouchControllerProfile || feature is MetaQuestTouchPlusControllerProfile)) activeProfiles++;
            Debug.Log("[Dog2 XR] Enabled Quest profiles after loader initialization: " + activeProfiles);
            if (activeProfiles == 0)
            {
                Status = "OpenXR 启动时手柄配置丢失。请确认 OpenXR 已更新到 1.17.1，重启 Unity 后再运行。";
                Debug.LogError("[Dog2 XR] No enabled controller profiles after loader initialization; controller rays cannot track.");
                yield break;
            }
            manager.StartSubsystems();
            Status = "正在等待双眼显示启动……";
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!DisplayRunning && Time.realtimeSinceStartup < deadline) yield return null;
            if (!DisplayRunning)
            {
                Status = "头显尚未就绪，正在持续等待 Quest Link。连接成功后会自动恢复手柄和射线；也可按 F9 重连。";
                Debug.Log("[Dog2 XR] Display is not ready yet; Update will keep watching for a late session start.");
                yield break;
            }
            SynchronizeSessionState(true);
            }
            finally { IsInitializing = false; }
        }
        // A Link session can become ready long after loader initialization, or resume
        // after the headset sleeps. Never permanently latch an early timeout.
        private void SynchronizeSessionState(bool displayRunning)
        {
            if (xrStarted == displayRunning) return;
            xrStarted = displayRunning;
            if (displayRunning)
            {
                var inputs = new List<XRInputSubsystem>(); SubsystemManager.GetInstances(inputs);
                foreach (var input in inputs)
                    if (input.running && input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                        offset.localPosition = Vector3.zero;
                Status = "OpenXR 双眼显示已就绪：手柄射线自动启用，扳机点击；A 语音，B 取消。";
                Debug.Log("[Dog2 XR] Session display became ready; controller poses and rays are enabled.");
            }
            else
            {
                leftTracked = rightTracked = false;
                leftHand.gameObject.SetActive(false); rightHand.gameObject.SetActive(false);
                Status = "头显显示暂停，等待 Quest Link 恢复；手柄和射线将在会话恢复后自动启用。";
                Debug.Log("[Dog2 XR] Session display paused; waiting for it to resume.");
            }
        }
        private void Update()
        {
            if (!SkipInitializationForValidation) SynchronizeSessionState(DisplayRunning);
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) RetryXR();
            // Some PC VR runtimes expose CommonUsages even when Input System actions
            // have not resolved. Combine both sources before edge detection to avoid doubles.
            var rightDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool a = false, b = false;
            if (rightDevice.isValid)
            {
                rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out a);
                rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out b);
            }
            bool talkHeld = a || talk.IsPressed();
            bool cancelHeld = b || cancel.IsPressed();
            bool keyboardTalk = Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
            bool keyboardCancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool talkPressed = (talkHeld && !previousTalk) || keyboardTalk;
            bool cancelPressed = (cancelHeld && !previousCancel) || keyboardCancel;
            previousTalk = talkHeld;
            previousCancel = cancelHeld;
            var leftDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputStatus = "左手柄：" + (leftDevice.isValid ? "已连接" : "未连接") + "；" + (rightDevice.isValid ? "右手柄已连接：" + rightDevice.name :
                talk.controls.Count > 0 ? "右手柄输入已绑定" : "未检测到右手柄：请进入 Quest Link 并唤醒手柄") +
                "；A：" + (talkHeld ? "按下" : "松开") + "；" + lastVoiceInput;
            if (cancelPressed)
            {
                lastVoiceInput = "已收到取消键";
                CancelSpeech?.Invoke();
            }
            else if (talkPressed)
            {
                talkPressCount++;
                lastVoiceInput = "已收到" + (keyboardTalk ? " F8 " : " A ") + talkPressCount + "次";
                Debug.Log("[Dog2 Input] " + lastVoiceInput);
                ToggleSpeech?.Invoke();
            }
            if (!xrStarted) return;
            Vector2 move = leftStick.ReadValue<Vector2>();
            if (leftDevice.isValid && leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 fallbackMove)) move = fallbackMove;
            Vector3 forward = Vector3.ProjectOnPlane(HeadCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Origin.position += (forward * move.y + right * move.x) * (1.4f * Time.deltaTime);
            Vector3 position = Origin.position; position.x = Mathf.Clamp(position.x, -8, 8); position.z = Mathf.Clamp(position.z, -8, 8); Origin.position = position;
            float turn = rightStick.ReadValue<Vector2>().x;
            if (rightDevice.isValid && rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 fallbackTurn)) turn = fallbackTurn.x;
            if (Mathf.Abs(turn) < 0.3f) snapReleased = true;
            if (snapReleased && Mathf.Abs(turn) > 0.75f)
            {
                Origin.RotateAround(HeadCamera.transform.position, Vector3.up, Mathf.Sign(turn) * 30f); snapReleased = false;
            }
            leftTracked = SetHand(leftHand, leftPosition, leftRotation, leftDevice);
            rightTracked = SetHand(rightHand, rightPosition, rightRotation, rightDevice);
        }
        private static bool SetHand(Transform hand, InputAction position, InputAction rotation, UnityEngine.XR.InputDevice device)
        {
            Vector3 p = Vector3.zero; Quaternion q = Quaternion.identity;
            bool tracked = device.isValid &&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out p) &&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out q);
            if (tracked && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool isTracked)) tracked = isTracked;
            if (!tracked && position.activeControl != null && rotation.activeControl != null)
            {
                var controller = position.activeControl.device as XRController;
                tracked = controller != null && controller.isTracked.isPressed;
                p = position.ReadValue<Vector3>(); q = rotation.ReadValue<Quaternion>();
            }
            hand.gameObject.SetActive(tracked);
            if (tracked) { hand.localPosition = p; hand.localRotation = q; }
            return tracked;
        }
        public bool TryGetAimRay(bool right, out Ray ray)
        {
            var hand = right ? rightHand : leftHand;
            bool tracked = right ? rightTracked : leftTracked;
            ray = new Ray();
            if (!xrStarted || !tracked || hand == null) return false;
            var position = right ? rightAimPosition : leftAimPosition;
            var rotation = right ? rightAimRotation : leftAimRotation;
            if (position.activeControl != null && rotation.activeControl != null)
                ray = new Ray(offset.TransformPoint(position.ReadValue<Vector3>()),
                    offset.rotation * rotation.ReadValue<Quaternion>() * Vector3.forward);
            else ray = new Ray(hand.position, hand.forward);
            return true;
        }
        public bool IsTriggerHeld(bool right)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(right ? XRNode.RightHand : XRNode.LeftHand);
            bool pressed = false;
            float analog = 0f;
            if (device.isValid)
            {
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out pressed);
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out analog);
            }
            return pressed || analog > 0.65f || (right ? rightTrigger : leftTrigger).ReadValue<float>() > 0.65f;
        }
        private void OnDestroy()
        {
            foreach (var action in AllActions()) action?.Dispose();
            if (ownsLoader && manager != null) { manager.StopSubsystems(); manager.DeinitializeLoader(); }
            if (handMaterial != null) Destroy(handMaterial);
        }
    }
}