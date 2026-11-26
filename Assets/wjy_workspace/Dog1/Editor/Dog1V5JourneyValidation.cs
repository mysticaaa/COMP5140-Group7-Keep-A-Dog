#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using KeepADog.Demo;
using Wjy.DogTraining;

/// <summary>VR interaction simulation with real scene, input actions, raycasts and coroutines; no headset hardware claim.</summary>
[InitializeOnLoad]
public static class Dog1V5JourneyValidation
{
    private const string Armed = "CityDogV5JourneyValidation";
    private const string FacilitiesMode = "CityDogV5JourneyFacilities";
    private const string InputProbeMode = "CityDogV5JourneyInputProbe";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool running;
    private static int assertions;
    private static double deadline;
    private static InputDevice leftDevice, rightDevice;
    private static bool inputFocusSaved;
    private static InputSettings.BackgroundBehavior savedBackground;
    private static InputSettings.EditorInputBehaviorInPlayMode savedEditorFocus;

    static Dog1V5JourneyValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        SessionState.SetBool(FacilitiesMode, false);
        SessionState.SetBool(InputProbeMode, false);
        Arm();
    }

    public static void RunFacilities()
    {
        SessionState.SetBool(FacilitiesMode, true);
        SessionState.SetBool(InputProbeMode, false);
        Arm();
    }

    public static void RunInputProbe()
    {
        SessionState.SetBool(FacilitiesMode, false);
        SessionState.SetBool(InputProbeMode, true);
        Arm();
    }

    private static void Arm()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES")))
            throw new InvalidOperationException("An isolated test-save directory is required.");
        Environment.SetEnvironmentVariable("KEEP_A_DOG_API_KEY", null);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        SessionState.SetBool(Armed, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 240;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("V5 journey exceeded 240 seconds")); return; }
        if (running) return;
        Dog1Training trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer?.dog == null || !trainer.dog.Ready) return;
        running = true;
        trainer.StartCoroutine(Guard(Journey(trainer)));
    }

    private static IEnumerator Journey(Dog1Training trainer)
    {
        Call(trainer, "BeginPlay");
        trainer.enabled = false; // Drive actual input functions deterministically without desktop clearing simulated XR grips.
        Set(trainer, "useModel", false);
        Set(trainer, "nextAutonomy", float.MaxValue);
        trainer.dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Hidden).SetValue(trainer.dog, true);
        object life = Get(trainer, "life");
        object data = Get(life, "data");
        foreach (string need in new[] { "satiety", "hydration", "energy", "mood", "cleanliness" }) Set(data, need, 80f);
        Set(data, "cooldowns", new float[7]);
        var rig = trainer.HeadsetRig;
        rig.enabled = false;
        Set(rig, "xrStarted", true);
        rig.HeadCamera.enabled = true;
        Camera desktop = Camera.main;
        if (desktop != null && desktop != rig.HeadCamera) desktop.enabled = false;
        trainer.dog.user = rig.HeadCamera.transform;
        Set(trainer, "viewCamera", rig.HeadCamera);
        Call(trainer, "ConfigureOwnerReachability");
        trainer.HeadsetCanvas.gameObject.SetActive(true);
        Call(trainer, "BringHeadsetMenu");
        foreach (Dog2ControllerRay ray in trainer.GetComponentsInChildren<Dog2ControllerRay>()) ray.ManualInputForValidation = true;
        Dog2ControllerRay pointer = trainer.GetComponentsInChildren<Dog2ControllerRay>().First(ray => ray.RightHand);
        yield return null;
        yield return Settle(trainer);

        if (SessionState.GetBool(FacilitiesMode, false))
        {
            yield return ApartmentFacilities(trainer, rig);
            Debug.Log("DOG_V5_FACILITIES_OK assertions=" + assertions + " scope=scene-physics-gaze-and-grip-simulation");
            yield break;
        }

        ConfigureVirtualInput(rig);
        if (SessionState.GetBool(InputProbeMode, false))
        {
            yield return InputProbe(rig);
            Debug.Log("DOG_V5_INPUT_PROBE_OK assertions=" + assertions);
            yield break;
        }
        yield return Locomotion(trainer, rig);
        yield return GrabPetAndLostTracking(trainer, rig, pointer);
        yield return MenuPauseAndWalk(trainer, rig, pointer);
        yield return BowlFacilities(trainer);
        yield return CrossFloorAndScene(trainer, rig);
        yield return PraiseAfterAnotherActivity(trainer);
        Debug.Log("DOG_V5_JOURNEY_OK assertions=" + assertions + " scope=scene,input-action,ray,coroutine-simulation");
    }

    private static void ConfigureVirtualInput(Dog2OpenXR rig)
    {
        // Hidden batch GameViews otherwise route public Update() to Editor-only buffers, whose
        // InputActionState intentionally ignores action monitors. Restore both settings in Finish.
        savedBackground = InputSystem.settings.backgroundBehavior;
        savedEditorFocus = InputSystem.settings.editorInputBehaviorInPlayMode;
        inputFocusSaved = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.RegisterLayout("{\"name\":\"CityDogV5Controller\",\"extend\":\"XRController\",\"runInBackground\":\"enabled\",\"controls\":[" +
            "{\"name\":\"trigger\",\"layout\":\"Axis\"},{\"name\":\"thumbstick\",\"layout\":\"Vector2\"}]}");
        rightDevice = InputSystem.AddDevice("CityDogV5Controller");
        leftDevice = InputSystem.AddDevice("CityDogV5Controller");
        InputSystem.SetDeviceUsage(rightDevice, UnityEngine.InputSystem.CommonUsages.RightHand);
        InputSystem.SetDeviceUsage(leftDevice, UnityEngine.InputSystem.CommonUsages.LeftHand);
        foreach (InputAction action in (IEnumerable<InputAction>)Call(rig, "AllActions"))
        {
            action.Disable();
            action.Enable();
            int resolved = action.controls.Count;
        }
        InputSystem.Update();
    }

    private static IEnumerator InputProbe(Dog2OpenXR rig)
    {
        Require(rightDevice.canRunInBackground && leftDevice.canRunInBackground,
            "The virtual XR controller layout must keep input alive in a hidden batch test");
        Stick(leftDevice, new Vector2(.2f, .8f));
        Stick(rightDevice, new Vector2(1, 0));
        Trigger(true);
        InputAction left = (InputAction)Get(rig, "leftStick");
        InputAction right = (InputAction)Get(rig, "rightStick");
        Debug.Log("DOG_V5_INPUT_PROBE_STATE left=" + left.ReadValue<Vector2>() + " right=" + right.ReadValue<Vector2>() +
            " directRight=" + ((Vector2Control)rightDevice["thumbstick"]).ReadValue() +
            " usages=" + string.Join(",", rightDevice.usages.Select(usage => usage.ToString())) +
            " controls=" + string.Join(",", right.controls.Select(control => control.path)) +
            " background=" + InputSystem.settings.backgroundBehavior + " update=" + InputState.currentUpdateType +
            " enabled=" + rightDevice.enabled + " backgroundCapable=" + rightDevice.canRunInBackground);
        Require((left.ReadValue<Vector2>() - new Vector2(.2f, .8f)).magnitude < .001f &&
            (right.ReadValue<Vector2>() - Vector2.right).magnitude < .001f,
            "The virtual input device state did not reach both real movement actions");
        Require(rig.IsTriggerHeld(true), "The virtual trigger did not reach the real XR input action");
        Stick(leftDevice, Vector2.zero); Stick(rightDevice, Vector2.zero); Trigger(false);
        yield return null;
    }

    private static IEnumerator Locomotion(Dog1Training trainer, Dog2OpenXR rig)
    {
        rig.LocomotionSuspended = false;
        Vector3 origin = rig.Origin.position;
        Vector3 eye = rig.HeadCamera.transform.position;
        Vector3 localEye = rig.HeadCamera.transform.localPosition;
        Stick(leftDevice, new Vector2(0, 1));
        for (int i = 0; i < 5; i++) { Stick(leftDevice, Vector2.up); Call(rig, "Update"); yield return null; }
        Require(Vector3.ProjectOnPlane(rig.Origin.position - origin, Vector3.up).magnitude > .001f,
            "Left stick did not move the actual XR origin");
        Require(rig.HeadCamera.transform.localPosition == localEye, "Locomotion rewrote the tracked local eye pose");
        Stick(leftDevice, Vector2.zero);
        Stick(rightDevice, Vector2.zero);
        Call(rig, "Update");
        yield return null;
        float yaw = rig.Origin.eulerAngles.y;
        Stick(rightDevice, new Vector2(1, 0));
        InputAction turnAction = (InputAction)Get(rig, "rightStick");
        Vector2 requestedTurn = turnAction.ReadValue<Vector2>();
        bool releasedBefore = (bool)Get(rig, "snapReleased");
        Call(rig, "Update");
        Debug.Log("DOG_V5_SNAP_DIAGNOSTIC requested=" + requestedTurn + " releasedBefore=" + releasedBefore +
            " yawBefore=" + yaw + " yawAfter=" + rig.Origin.eulerAngles.y + " nativeRightValid=" +
            UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand).isValid);
        Debug.Log("DOG_V5_INPUT_DIAGNOSTIC direct=" + ((Vector2Control)rightDevice["thumbstick"]).ReadValue() +
            " usages=" + string.Join(",", rightDevice.usages.Select(usage => usage.ToString())) +
            " actionEnabled=" + turnAction.enabled + " controls=" + string.Join(",", turnAction.controls.Select(control => control.path)));
        Require(Mathf.Abs(Mathf.DeltaAngle(yaw, rig.Origin.eulerAngles.y) - 30f) < .01f,
            "Right stick did not produce one 30 degree snap turn");
        float heldYaw = rig.Origin.eulerAngles.y;
        for (int i = 0; i < 3; i++) { Stick(rightDevice, Vector2.right); Call(rig, "Update"); yield return null; }
        Require(Mathf.Abs(Mathf.DeltaAngle(heldYaw, rig.Origin.eulerAngles.y)) < .01f,
            "Holding the snap stick repeated turns without release");
        Stick(rightDevice, Vector2.zero); Call(rig, "Update");
        rig.LocomotionSuspended = true;
        Vector3 pausedOrigin = rig.Origin.position;
        Stick(leftDevice, Vector2.up); Stick(rightDevice, Vector2.right);
        Call(rig, "Update");
        Require(rig.Origin.position == pausedOrigin && Mathf.Abs(Mathf.DeltaAngle(heldYaw, rig.Origin.eulerAngles.y)) < .01f,
            "Suspended locomotion still moved or snap-turned the player");
        Stick(leftDevice, Vector2.zero); Stick(rightDevice, Vector2.zero);
        rig.Origin.SetPositionAndRotation(origin, Quaternion.identity);
        Call(trainer, "BringHeadsetMenu");
        Debug.Log("DOG_V5_JOURNEY_LOCOMOTION_OK");
    }

    private static IEnumerator GrabPetAndLostTracking(Dog1Training trainer, Dog2OpenXR rig, Dog2ControllerRay pointer)
    {
        object grip = Get(trainer, "rightToyGrip");
        Component toy = (Component)((Array)Get(trainer, "physicalProps")).GetValue(0);
        Vector3 home = toy.transform.position;
        Transform hand = (Transform)Get(rig, "rightHand");
        Set(rig, "rightTracked", true); Set(rig, "leftTracked", false);
        hand.position = home;
        Trigger(true);
        Require(rig.IsTriggerHeld(true), "Virtual trigger did not resolve through the real input action");
        Call(trainer, "UpdateGrabButtons", true, grip, home, Quaternion.identity, false, true);
        Require(ReferenceEquals(Get(grip, "Toy"), toy) && (bool)Call(trainer, "ToyBlocksPetting", true),
            "Held ball did not block petting with the same hand");
        int page = (int)Get(trainer, "xrTab");
        int clicks = pointer.ClickCount;
        AimClick(trainer, pointer, "设置", false);
        Require(pointer.ClickCount == clicks && (int)Get(trainer, "xrTab") == page,
            "A trigger holding a toy also selected a menu page");
        Set(trainer, "petDuration", .34f);
        Call(trainer, "CheckHeadsetPetting");
        Require((float)Get(trainer, "petDuration") == 0 && !(bool)Get(trainer, "busy"),
            "Held-toy contact initiated petting");

        // Real loss entry (UpdateHandGrip), multiple lost frames, then recovery with trigger still down.
        Set(rig, "rightTracked", false);
        Call(trainer, "UpdateHandGrip", true, grip);
        yield return null;
        Trigger(true);
        Call(trainer, "UpdateHandGrip", true, grip);
        Require(Get(grip, "Toy") == null && (bool)Call(trainer, "PropBlocksUi", true),
            "Repeated tracking loss forgot the trigger ownership before actual release");
        Set(rig, "rightTracked", true);
        Call(trainer, "UpdateHandGrip", true, grip);
        AimClick(trainer, pointer, "设置", false);
        Require(pointer.ClickCount == clicks, "Recovered held trigger clicked the menu");
        Trigger(false);
        Call(trainer, "UpdateHandGrip", true, grip);
        Require((bool)Call(trainer, "PropBlocksUi", true), "Tracking-loss release frame leaked a menu click");
        Call(trainer, "UpdateHandGrip", true, grip);
        Require(!(bool)Call(trainer, "PropBlocksUi", true), "New independent presses remained blocked after release");
        AimClick(trainer, pointer, "设置", true);
        Require((int)Get(trainer, "xrTab") == 4, "Fresh post-release press did not reach settings");
        Call(toy, "SetHome", home, Quaternion.identity);

        // No device pose is claimed: a bounded tracked hand path drives the actual pet proximity/speed gate.
        Set(rig, "rightTracked", true); Set(rig, "leftTracked", false);
        Set(trainer, "lastPet", -100f);
        Set(trainer, "rightPetTracked", false);
        float until = Time.realtimeSinceStartup + 2f;
        float phase = 0f;
        float maxPetDuration = 0f, maxHandSpeed = 0f;
        bool started = false;
        while (!started && Time.realtimeSinceStartup < until)
        {
            // Match the pose sampler's minimum time resolution in very high FPS batch scenes.
            phase += Mathf.Max(Time.unscaledDeltaTime, .001f) * 4f;
            Vector3 center = trainer.dog.PerceptionHead.position;
            Vector3 previousHand = hand.position;
            hand.position = center + new Vector3(Mathf.Cos(phase) * .025f, 0, Mathf.Sin(phase) * .025f);
            maxHandSpeed = Mathf.Max(maxHandSpeed, Vector3.Distance(previousHand, hand.position) /
                Mathf.Max(Time.unscaledDeltaTime, .001f));
            Call(trainer, "CheckHeadsetPetting");
            maxPetDuration = Mathf.Max(maxPetDuration, (float)Get(trainer, "petDuration"));
            started = trainer.dog.CurrentAction == "pet";
            yield return null;
        }
        Debug.Log("DOG_V5_PET_CONTACT_DIAGNOSTIC started=" + started + " ready=" +
            trainer.GetType().GetProperty("Ready", Hidden).GetValue(trainer) +
            " blocked=" + Call(trainer, "ToyBlocksPetting", true) + " tracked=" + Get(rig, "rightTracked") +
            " session=" + Get(rig, "xrStarted") + " durationPeak=" + maxPetDuration +
            " speedPeak=" + maxHandSpeed + " action=" + trainer.dog.CurrentAction +
            " triggerPressed=" + Get(grip, "TriggerPressed") + " gripPressed=" + Get(grip, "Pressed"));
        Require(started, "Slow empty-hand head contact did not start the actual pet response");
        yield return Settle(trainer);
        Require(trainer.dog.LastCompletedAction == "pet", "Pet response never completed its body action");
        Coroutine sitting = trainer.StartCoroutine(trainer.dog.PerformAction("sit", .55f));
        yield return null;
        Set(trainer, "petDuration", .34f);
        Call(trainer, "CheckHeadsetPetting");
        Require(trainer.dog.CurrentAction == "sit" && (float)Get(trainer, "petDuration") == 0,
            "Pet contact interrupted a performing action");
        yield return sitting;
        yield return Settle(trainer);
        Debug.Log("DOG_V5_JOURNEY_PET_GRAB_TRACKING_OK");
    }

    private static IEnumerator MenuPauseAndWalk(Dog1Training trainer, Dog2OpenXR rig, Dog2ControllerRay pointer)
    {
        AimClick(trainer, pointer, "收起 · X", true);
        Require(!trainer.HeadsetCanvas.gameObject.activeSelf, "Hide button did not hide the world-space menu");
        int clicks = pointer.ClickCount;
        pointer.ProcessRay(new Ray(rig.HeadCamera.transform.position, rig.HeadCamera.transform.forward), true, true);
        pointer.ProcessRay(new Ray(rig.HeadCamera.transform.position, rig.HeadCamera.transform.forward), false, true);
        Require(pointer.ClickCount == clicks, "Hidden menu accepted a controller ray click");
        ((Action)Get(rig, "RecenterPanel")).Invoke();
        yield return null;
        Require(trainer.HeadsetCanvas.gameObject.activeSelf &&
            Vector3.Dot(rig.HeadCamera.transform.forward, trainer.HeadsetCanvas.transform.position - rig.HeadCamera.transform.position) > 1f,
            "Menu recall event did not return the journal in front of the user");
        AimClick(trainer, pointer, "设置", true);
        yield return null; // A player sees the rebuilt page before pointing at its next control.
        Vector3 origin = rig.Origin.position;
        AimClick(trainer, pointer, "重新连接 VR", true);
        Require(rig.Status.Contains("未启用 XR") && rig.Origin.position == origin,
            "No-headset reconnect did not explain its fallback or changed the player pose");

        AimClick(trainer, pointer, "故事", true);
        yield return null;
        object grip = Get(trainer, "rightToyGrip");
        Component toy = (Component)((Array)Get(trainer, "physicalProps")).GetValue(0);
        Vector3 home = toy.transform.position;
        Call(trainer, "UpdateGrabButtons", true, grip, home, Quaternion.identity, false, true);
        Require(Get(grip, "Toy") != null, "Pause test could not take the ball");
        Dog2ControllerRay left = trainer.GetComponentsInChildren<Dog2ControllerRay>().First(ray => !ray.RightHand);
        AimClick(trainer, left, "暂停", true);
        Require(Time.timeScale == 0 && (bool)Get(trainer, "pause"), "Headset pause did not suspend game time");
        yield return null;
        Call(trainer, "UpdatePhysicalProps");
        Require(Get(grip, "Toy") == null && !toy.GetComponent<Rigidbody>().isKinematic &&
            Vector3.ProjectOnPlane(toy.GetComponent<Rigidbody>().velocity, Vector3.up).sqrMagnitude < .001f,
            "Pause did not safely release the held toy without throw velocity");
        AimClick(trainer, left, "继续陪伴", true);
        Require(Time.timeScale == 1 && !(bool)Get(trainer, "pause"), "Headset resume did not restore game time");
        Call(trainer, "ResetGrip", grip, false);
        Call(trainer, "UpdateGrabButtons", true, grip, home, Quaternion.identity, false, false);
        Call(trainer, "UpdateGrabButtons", true, grip, home, Quaternion.identity, false, false);
        AimClick(trainer, pointer, "照料", true);
        yield return null;
        int bones = Profile(trainer).Bones;
        int walkCount = Profile(trainer).GetTaskProgress(DogCareAction.Walk);
        AimClick(trainer, pointer, DogLifeProgress.GetActionName(DogCareAction.Walk), true);
        Require((bool)Get(trainer, "walking") && Get(trainer, "walkMarker") != null, "VR care page did not start a walk");
        Call(trainer, "RefreshHeadsetText");
        AimClick(trainer, pointer, "取消这次遛弯", true);
        yield return null;
        Require(!(bool)Get(trainer, "walking") && (Transform)Get(trainer, "walkMarker") == null &&
            Profile(trainer).Bones == bones && Profile(trainer).GetTaskProgress(DogCareAction.Walk) == walkCount,
            "Cancelled VR walk kept a route marker or awarded completion");
        Debug.Log("DOG_V5_JOURNEY_MENU_PAUSE_WALK_OK");
    }

    private static IEnumerator BowlFacilities(Dog1Training trainer)
    {
        object world = Get(trainer, "world");
        Transform gaze = new GameObject("V5 simulated fixture gaze").transform;
        try
        {
            foreach (DogCareAction action in new[] { DogCareAction.Feed, DogCareAction.Water })
            {
                Component bowl = (Component)Call(world, "CareBowl", action);
                Call(world, "EmptyBowl", action);
                yield return WaitForBowl(bowl);
                Require(FindGaze(world, gaze, bowl.transform.position + Vector3.up * .16f,
                    action == DogCareAction.Feed ? "狗粮" : "饮水"), "No reachable view could operate a care bowl");
                object holder = Enum.Parse(bowl.GetType().GetProperty("Holder", Hidden).PropertyType, "RightHand");
                Require((bool)Call(bowl, "TryHold", holder), "Facility test could not take the bowl");
                object heldRequest = Call(world, "UpdateApartmentInteractions", gaze, true, true, .02f, true);
                Require(!(heldRequest is DogCareAction request && request == action) && !(bool)Call(world, "HasServing", action),
                    "A held bowl was filled by the aimed fixture interaction");
                Call(bowl, "Drop", Vector3.zero);
                yield return WaitForBowl(bowl);
                object offer = Call(world, "UpdateApartmentInteractions", gaze, true, true, .02f, true);
                Require(offer is DogCareAction && (DogCareAction)offer == action, "A released bowl did not offer its corresponding refill");
                Call(world, "FillBowl", action);
                object duplicate = Call(world, "UpdateApartmentInteractions", gaze, true, true, .02f, true);
                Require((bool)Call(world, "HasServing", action) && duplicate == null, "Full bowl interaction repeatedly offered refills");
            }
        }
        finally { UnityEngine.Object.Destroy(gaze.gameObject); }
        Debug.Log("DOG_V5_JOURNEY_BOWL_FIXTURES_OK");
    }

    private static IEnumerator CrossFloorAndScene(Dog1Training trainer, Dog2OpenXR rig)
    {
        object world = Get(trainer, "world");
        Vector3 origin = rig.Origin.position;
        rig.Origin.position += trainer.dog.transform.position - Vector3.ProjectOnPlane(rig.HeadCamera.transform.position, Vector3.up);
        trainer.dog.user = rig.HeadCamera.transform;
        Require(Available(trainer, "come"), "Normal headset eye height incorrectly made the owner unreachable");
        rig.Origin.position -= Vector3.up * 3.6f;
        Require(!Available(trainer, "come") && !Available(trainer, "fetch_toy") &&
            !(bool)Call(trainer.dog, "IsNearOwner"), "A different-floor owner could receive a fake come/fetch completion");
        rig.Origin.position = origin;
        object data = Get(Get(trainer, "life"), "data");
        Set(data, "totalExperience", 285); Set(data, "bones", 100);
        Component held = (Component)Call(world, "CareBowl", DogCareAction.Feed);
        object grip = Get(trainer, "rightToyGrip");
        Call(trainer, "UpdateGrabButtons", true, grip, held.transform.position, Quaternion.identity, false, true);
        Require(Get(grip, "Toy") != null, "Scene-switch fixture could not take the food bowl");
        Call(trainer, "Buy", DogLocation.Park);
        yield return null;
        Require(Profile(trainer).CurrentLocation == DogLocation.Park && Get(grip, "Toy") == null,
            "Scene transition retained a held old-scene bowl");
        Require(Call(world, "CareBowl", DogCareAction.Feed) is Component &&
            !ReferenceEquals(held, Call(world, "CareBowl", DogCareAction.Feed)), "New scene did not replace its care bowls");
        Call(trainer, "Buy", DogLocation.Terrace);
        yield return null;
        Require(Profile(trainer).CurrentLocation == DogLocation.Terrace && ((Array)Get(trainer, "physicalProps")).Length == 3,
            "Terrace transition lost the shared and scene-specific toys");
        Require((bool)Call(trainer, "Visit", DogLocation.Apartment), "Returning to the apartment failed");
        yield return Settle(trainer);
        Debug.Log("DOG_V5_JOURNEY_FLOOR_AND_SCENE_OK");
    }

    private static IEnumerator PraiseAfterAnotherActivity(Dog1Training trainer)
    {
        Set(trainer, "trainingDrawOverride", new Func<int>(() => 1));
        Require((bool)Call(trainer, "SubmitTrainingCue", "sit"), "New practice could not start");
        yield return Settle(trainer);
        object progress = Get(trainer, "trainingProgress");
        int rate = (int)Call(progress, "GetSuccessRate", "sit");
        Require(Get(trainer, "pendingTrainingSkill") != null, "Successful practice did not open feedback");
        Call(trainer, "StartCare", DogCareAction.Rest);
        yield return Settle(trainer);
        object lifeData = Get(Get(trainer, "life"), "data");
        int trainCount = ((int[])Get(lifeData, "chapterCounts"))[(int)DogCareAction.Train];
        int experience = Profile(trainer).TotalExperience;
        Call(trainer, "ApplyFeedback", true);
        Require((int)Call(progress, "GetSuccessRate", "sit") == rate &&
            ((int[])Get(lifeData, "chapterCounts"))[(int)DogCareAction.Train] == trainCount &&
            Profile(trainer).TotalExperience == experience,
            "Praise after a different completed activity reinforced or rewarded the old training trial");
        var agent = (DogAgent)Get(trainer, "agent");
        int sadness = (int)Get(Get(agent, "memory"), "sadStrength");
        Require((bool)Call(trainer, "SubmitPracticeCue", DogAgent.TeachExample), "Conditional toy rehearsal could not start");
        yield return Settle(trainer);
        Require(trainer.dog.LastFetchSucceeded && agent.CanReward,
            "The conditional cross-activity fixture did not actually return the plush first");
        Call(trainer, "StartCare", DogCareAction.Rest);
        yield return Settle(trainer);
        Require(!agent.CanReward, "A new rest activity kept the earlier completed conditional trial rewardable");
        Call(trainer, "ApplyFeedback", true);
        Require((int)Get(Get(agent, "memory"), "sadStrength") == sadness,
            "Praise after resting incorrectly saved the earlier unconfirmed conditional toy lesson");
        Debug.Log("DOG_V5_JOURNEY_FEEDBACK_BOUNDARY_OK");
    }

    private static DogLifeProgress Profile(Dog1Training trainer) => (DogLifeProgress)Get(trainer, "life");

    private static bool FindGaze(object world, Transform gaze, Vector3 target, string hint)
    {
        foreach (Vector3 direction in new[] { Vector3.back, Vector3.forward, Vector3.right, Vector3.left })
        {
            gaze.position = target + direction * .85f + Vector3.up * .55f;
            gaze.LookAt(target);
            Call(world, "UpdateApartmentInteractions", gaze, true, false, .02f, true);
            if (((string)world.GetType().GetProperty("InteractionHint", Hidden).GetValue(world) ?? "").Contains(hint)) return true;
        }
        return false;
    }

    private static IEnumerator ApartmentFacilities(Dog1Training trainer, Dog2OpenXR rig)
    {
        object world = Get(trainer, "world");
        Transform head = rig.HeadCamera.transform;
        Vector3 savedPosition = head.position;
        Quaternion savedRotation = head.rotation;
        var doors = (IList)Get(world, "apartmentDoors");
        Require(doors.Count > 0, "The apartment has no configured door interaction");
        try
        {
            foreach (object door in doors)
            {
                Vector3 target = (Vector3)Get(door, "Target");
                string label = (string)Get(door, "Label");
                Require(FindFixtureGaze(trainer, head, target, label), "No reachable geometric door view: " + label);
                bool wasOpen = (bool)door.GetType().GetProperty("Open").GetValue(door);
                object grip = Get(trainer, "rightToyGrip");
                Component ball = (Component)((Array)Get(trainer, "physicalProps")).GetValue(0);
                Vector3 ballHome = ball.transform.position;
                Call(trainer, "UseGripPress", true, grip, ball, ballHome, Quaternion.identity);
                Set(trainer, "xrInteract", false);
                Call(trainer, "UpdateGrabButtons", true, grip, head.position, head.rotation, true, false);
                Call(trainer, "UpdateHeadsetApartment");
                Require(Get(grip, "Toy") != null && (bool)door.GetType().GetProperty("Open").GetValue(door) == wasOpen,
                    "A grip holding a ball also toggled the aimed door: " + label);
                Call(trainer, "DropGrip", grip, false);
                Call(ball, "SetHome", ballHome, Quaternion.identity);
                FixturePress(trainer, head);
                Require((bool)door.GetType().GetProperty("Open").GetValue(door) != wasOpen,
                    "Empty-hand grip failed to toggle the aimed door: " + label);
                for (int frame = 0; frame < 20; frame++)
                {
                    Call(trainer, "UpdateHeadsetApartment");
                    yield return null;
                }
                float angle = (float)door.GetType().GetProperty("Angle").GetValue(door);
                Require(Mathf.Abs(angle) > .1f, "The door changed its flag without moving its real leaf: " + label);
                Require(FindFixtureGaze(trainer, head, target, label) &&
                    ((string)world.GetType().GetProperty("InteractionHint", Hidden).GetValue(world) ?? "").Contains("关闭"),
                    "Open door did not offer closing feedback: " + label);
                FixturePress(trainer, head);
                Require((bool)door.GetType().GetProperty("Open").GetValue(door) == wasOpen,
                    "The aimed door could not be toggled back: " + label);
                Debug.Log("DOG_V5_FACILITY_DOOR_OK " + label);
            }

            var streams = (ParticleSystem)Get(world, "showerStreams");
            var splashes = (ParticleSystem)Get(world, "showerSplashes");
            Require(streams != null && splashes != null, "The apartment shower has no configured visual water systems");
            Vector3 control = (Vector3)Get(world, "showerControl");
            Require(FindFixtureGaze(trainer, head, control, "花洒"), "The actual shower control has no reachable geometric view");
            bool wasEmitting = streams.isEmitting;
            FixturePress(trainer, head);
            yield return null;
            Require(streams.isEmitting != wasEmitting && splashes.isEmitting == streams.isEmitting,
                "Aimed shower grip did not toggle both real water emitters");
            Require(((string)world.GetType().GetProperty("InteractionHint", Hidden).GetValue(world) ?? "").Contains(
                streams.isEmitting ? "关闭花洒" : "打开花洒"), "Shower feedback does not match the actual emitter state");
            Require((bool)Get(world, "showerRunning") == streams.isEmitting,
                "The shower logical switch does not match its initially toggled emitters");
            if (!wasEmitting)
            {
                // Simulate effect culling pausing visuals, without changing the game switch.
                streams.Pause(false); splashes.Pause(false);
                Require((bool)Get(world, "showerRunning"), "Pausing visual effects changed the logical shower switch");
            }
            Require(FindFixtureGaze(trainer, head, control, "花洒"),
                "The shower control became unreachable after toggling it");
            Debug.Log("DOG_V5_SHOWER_BEFORE_SECOND original=" + wasEmitting + " streams=" + streams.isEmitting +
                "/playing=" + streams.isPlaying + " splashes=" + splashes.isEmitting + "/playing=" + splashes.isPlaying +
                " pendingGrip=" + Get(trainer, "xrInteract") + " hint=" +
                (string)world.GetType().GetProperty("InteractionHint", Hidden).GetValue(world));
            FixturePress(trainer, head);
            Debug.Log("DOG_V5_SHOWER_AFTER_SECOND_IMMEDIATE streams=" + streams.isEmitting + "/playing=" + streams.isPlaying +
                " splashes=" + splashes.isEmitting + "/playing=" + splashes.isPlaying + " pendingGrip=" +
                Get(trainer, "xrInteract") + " hint=" + (string)world.GetType().GetProperty("InteractionHint", Hidden).GetValue(world));
            yield return null;
            Debug.Log("DOG_V5_SHOWER_DIAGNOSTIC original=" + wasEmitting + " streams=" + streams.isEmitting +
                " splashes=" + splashes.isEmitting + " hint=" +
                (string)world.GetType().GetProperty("InteractionHint", Hidden).GetValue(world));
            Require(streams.isEmitting == wasEmitting && splashes.isEmitting == streams.isEmitting,
                "The aimed shower control did not return both emitters to their original state");
            Require((bool)Get(world, "showerRunning") == wasEmitting,
                "The second grip toggled particle effects without restoring the logical switch");
            Debug.Log("DOG_V5_FACILITY_SHOWER_OK");
        }
        finally { head.SetPositionAndRotation(savedPosition, savedRotation); }
    }

    private static bool FindFixtureGaze(Dog1Training trainer, Transform head, Vector3 target, string hint)
    {
        object world = Get(trainer, "world");
        foreach (float distance in new[] { .85f, 1.25f, .55f })
            for (int angle = 0; angle < 8; angle++)
            {
                float radians = angle * Mathf.PI / 4;
                head.position = target + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * distance + Vector3.up * .25f;
                head.LookAt(target);
                Set(trainer, "xrInteract", false);
                Call(trainer, "UpdateHeadsetApartment");
                if (((string)world.GetType().GetProperty("InteractionHint", Hidden).GetValue(world) ?? "").Contains(hint)) return true;
            }
        return false;
    }

    private static void FixturePress(Dog1Training trainer, Transform head)
    {
        object grip = Get(trainer, "rightToyGrip");
        Set(trainer, "xrInteract", false);
        Call(trainer, "UpdateGrabButtons", true, grip, head.position, head.rotation, false, false);
        Call(trainer, "UpdateGrabButtons", true, grip, head.position, head.rotation, true, false);
        Require((bool)Get(trainer, "xrInteract"), "An empty grip did not dispatch the aimed fixture interaction");
        Call(trainer, "UpdateHeadsetApartment");
        Call(trainer, "UpdateGrabButtons", true, grip, head.position, head.rotation, false, false);
    }

    private static void AimClick(Dog1Training trainer, Dog2ControllerRay ray, string label, bool expected)
    {
        Call(trainer, "RefreshHeadsetText");
        Canvas.ForceUpdateCanvases();
        Button button = trainer.HeadsetCanvas.GetComponentsInChildren<Button>().FirstOrDefault(b =>
            b.gameObject.name == label || b.gameObject.name.StartsWith(label + "\n", StringComparison.Ordinal));
        Require(button != null, "Missing headset button: " + label);
        RectTransform rect = button.GetComponent<RectTransform>();
        Vector3 eye = trainer.HeadsetRig.HeadCamera.transform.position;
        Ray aim = new Ray(eye, (rect.TransformPoint(rect.rect.center) - eye).normalized);
        int before = ray.ClickCount;
        ray.ProcessRay(aim, false, true); ray.ProcessRay(aim, true, true); ray.ProcessRay(aim, false, true);
        if (ray.ClickCount != before + (expected ? 1 : 0))
            Debug.Log("DOG_V5_RAY_FAILURE label=" + label + " active=" + button.gameObject.activeInHierarchy +
                " interactable=" + button.IsInteractable() + " blocked=" + Call(trainer, "PropBlocksUi", ray.RightHand) +
                " hovered=" + Get(ray, "hovered") + " canvas=" + trainer.HeadsetCanvas.gameObject.activeInHierarchy +
                " graphicDepth=" + button.targetGraphic.depth + " xrTab=" + Get(trainer, "xrTab"));
        Require(ray.ClickCount == before + (expected ? 1 : 0), "Unexpected geometric ray click result: " + label);
    }

    private static bool Available(Dog1Training trainer, string id)
    {
        object state = Call(trainer, "CaptureCommandState");
        return ((DogCommandOption[])Get(state, "actionCatalog")).Any(o => o.id == id && o.target == "none" && o.available);
    }

    private static IEnumerator WaitForBowl(Component bowl)
    {
        float until = Time.realtimeSinceStartup + 5;
        do { yield return new WaitForFixedUpdate(); }
        while (!(bool)bowl.GetType().GetProperty("IsSettled", Hidden).GetValue(bowl) && Time.realtimeSinceStartup < until);
        Require((bool)bowl.GetType().GetProperty("IsSettled", Hidden).GetValue(bowl), "Bowl did not settle");
    }

    private static IEnumerator Settle(Dog1Training trainer)
    {
        float until = Time.realtimeSinceStartup + 35;
        do { yield return null; }
        while (((bool)Get(trainer, "busy") || Get(trainer, "activeCommandTrace") != null || trainer.dog.IsPerforming ||
            !trainer.dog.CanOfferMicroAction) && Time.realtimeSinceStartup < until);
        Require(!(bool)Get(trainer, "busy") && Get(trainer, "activeCommandTrace") == null && !trainer.dog.IsPerforming,
            "A player interaction did not finish within 35 seconds");
    }

    private static void Trigger(bool held)
    {
        // Direct virtual device state still notifies real action monitors, without Editor GameView event routing.
        EnsurePlayerInputBuffer();
        InputState.Change((AxisControl)rightDevice["trigger"], held ? 1f : 0f, InputUpdateType.Dynamic);
    }
    private static void Stick(InputDevice device, Vector2 value)
    {
        EnsurePlayerInputBuffer();
        InputState.Change((Vector2Control)device["thumbstick"], value, InputUpdateType.Dynamic);
    }
    private static void EnsurePlayerInputBuffer()
    {
        if (InputState.currentUpdateType == InputUpdateType.Editor) InputSystem.Update();
    }
    private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static IEnumerator Guard(IEnumerator test)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(test);
        while (stack.Count > 0)
        {
            IEnumerator step = stack.Peek(); object value = null; bool moved;
            try { moved = step.MoveNext(); if (moved) value = step.Current; }
            catch (Exception error) { Finish(error); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (value is IEnumerator nested) { stack.Push(nested); continue; }
            yield return value;
        }
        Finish(null);
    }
    private static void Finish(Exception error)
    {
        SessionState.SetBool(Armed, false); Time.timeScale = 1;
        if (rightDevice != null && rightDevice.added) InputSystem.RemoveDevice(rightDevice);
        if (leftDevice != null && leftDevice.added) InputSystem.RemoveDevice(leftDevice);
        if (inputFocusSaved)
        {
            InputSystem.settings.backgroundBehavior = savedBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorFocus;
            inputFocusSaved = false;
        }
        if (error != null) Debug.LogException(error);
        string output = Environment.GetEnvironmentVariable("CITYDOG_QA_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "v5-journey-result.txt"), error == null
                ? "PASS: scene/input-action/ray/coroutine simulation; assertions=" + assertions + "; no physical headset claim."
                : error.ToString());
        }
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
