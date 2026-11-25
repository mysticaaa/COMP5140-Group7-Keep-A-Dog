#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Wjy.DogTraining;

public static class DogPerceptionValidation
{
    private const string Flag = "CityDogPerceptionValidation";
    private static double deadline;

    [InitializeOnLoadMethod]
    private static void Watch() => EditorApplication.update += Tick;

    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 60;
        var dog = UnityEngine.Object.FindObjectOfType<DogCompanion>();
        if (dog == null || !dog.Ready)
        {
            if (EditorApplication.timeSinceStartup < deadline) return;
            SessionState.SetBool(Flag, false);
            Debug.LogError("DOG_PERCEPTION_FAILED: dog was not ready");
            EditorApplication.Exit(1);
            return;
        }
        SessionState.SetBool(Flag, false);
        try
        {
            Validate(dog);
            Debug.Log("DOG_PERCEPTION_OK: registered objects, FOV, collider occlusion, hand proximity, local response, model choice validation and fresh-state gate");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    public static void Validate(DogCompanion dog)
    {
        Transform savedUser = dog.user;
        var sensorHost = new GameObject("Perception validation fixture");
        var fakeOwner = new GameObject("Perception validation owner");
        GameObject blocker = null;
        try
        {
            var sensor = sensorHost.AddComponent<DogScenePerception>();
            sensor.Dog = dog;
            sensor.OccluderLayers = 1 << 31;
            dog.user = fakeOwner.transform;
            Vector3 origin = dog.PerceptionHead != null ? dog.PerceptionHead.position + Vector3.up * .025f :
                dog.transform.position + Vector3.up * .45f;
            fakeOwner.transform.position = origin + dog.transform.forward * 2f;
            Physics.SyncTransforms();
            var ahead = sensor.Capture(origin + dog.transform.right * .18f);
            var owner = ahead.Find("owner");
            Require(owner != null && owner.visible && Mathf.Abs(owner.bearingDegrees) < 1f &&
                Mathf.Abs(owner.distanceMeters - 2f) < .02f, "Registered owner distance/FOV is incorrect.");
            Require(ahead.Find("left_hand")?.nearHead == true && ahead.Find("right_hand") == null,
                "Only provided tracked-hand poses should report proximity.");

            fakeOwner.transform.position = origin - dog.transform.forward * 2f;
            var behind = sensor.Capture();
            Require(!behind.Find("owner").visible && !behind.Find("owner").inViewCone,
                "Owner behind dog should be outside body view cone.");
            fakeOwner.transform.position = origin + dog.transform.forward * 2f;
            blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Perception occlusion fixture";
            blocker.layer = 31;
            blocker.transform.position = origin + dog.transform.forward;
            blocker.transform.localScale = Vector3.one * .45f;
            Physics.SyncTransforms();
            var hidden = sensor.Capture();
            Require(hidden.Find("owner").inViewCone && !hidden.Find("owner").lineOfSight && !hidden.Find("owner").visible,
                "Collider between dog and owner should obstruct visibility.");
            blocker.GetComponent<Collider>().enabled = false;
            Physics.SyncTransforms();
            Require(sensor.Capture().Find("owner").visible, "Visibility should recover after occluder is disabled.");
            CheckSelection();
        }
        finally
        {
            dog.user = savedUser;
            if (blocker != null) UnityEngine.Object.DestroyImmediate(blocker);
            UnityEngine.Object.DestroyImmediate(fakeOwner);
            UnityEngine.Object.DestroyImmediate(sensorHost);
        }
    }

    private static void CheckSelection()
    {
        var observation = new DogSceneObservation
        {
            capturedAt = Time.unscaledTime, available = true,
            objects = new[] { new DogObservedObject { id = "owner", distanceMeters = 2f, visible = true } },
            candidates = new[]
            {
                new DogPerceptionCandidate { id = "head_tilt", targetId = "owner" },
                new DogPerceptionCandidate { id = "air_sniff", targetId = "none" }
            }
        };
        Require(DogPerceptionSelection.SelectLocal(observation).ActionId == "head_tilt", "Local attention selection failed.");
        MethodInfo parse = typeof(DogPerceptionSelection).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
        Require(parse != null, "Missing model selection validator.");
        var selected = parse.Invoke(null, new object[] { "{\"actionId\":\"head_tilt\",\"targetId\":\"owner\",\"reply\":\"汪？我在听。\"}", observation }) as DogPerceptionChoice;
        Require(selected != null && selected.UsedModel && DogPerceptionSelection.CanApply(selected, observation),
            "Valid model choice should remain applicable.");
        foreach (string content in new[]
        {
            "{\"actionId\":\"head_tilt\",\"reply\":\"汪\"}",
            "{\"actionId\":\"teleport\",\"targetId\":\"owner\",\"reply\":\"汪\"}",
            "{\"actionId\":\"head_tilt\",\"targetId\":\"ball\",\"reply\":\"汪\"}",
            "{\"actionId\":\"gaze_exchange\",\"targetId\":\"ball\",\"reply\":\"汪\"}"
        }) Require(parse.Invoke(null, new object[] { content, observation }) == null,
            "Unavailable/malformed model choice should be rejected.");
        observation.occupied = true;
        Require(!DogPerceptionSelection.CanApply(selected, observation), "A busy dog must reject the old model choice.");
        observation.occupied = false;
        observation.candidates = Array.Empty<DogPerceptionCandidate>();
        Require(!DogPerceptionSelection.CanApply(selected, observation), "Changed scene candidates must invalidate the old choice.");
    }

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
#endif
