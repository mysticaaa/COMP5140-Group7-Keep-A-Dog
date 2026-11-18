#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogMotionValidation
{
    private const string SessionKey = "CityDogMotionValidation";
    private static readonly string[] Paws =
    {
        "DEF-hind_paw.L", "DEF-front_toes.L", "DEF-hind_paw.R", "DEF-front_toes.R"
    };
    private static readonly MethodInfo Contact = typeof(DogCompanion).GetMethod(
        "TryGetFootContact", BindingFlags.NonPublic | BindingFlags.Instance);
    private static bool started;
    private static double deadline;
    private static string output;

    static DogMotionValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("CITY_DOG_MOTION_CAPTURES");
        if (!string.IsNullOrEmpty(output)) Directory.CreateDirectory(output);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 70;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Motion validation timed out.")); return; }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        started = true;
        trainer.StartCoroutine(Validate(trainer.dog));
    }

    private static IEnumerator Validate(DogCompanion dog)
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(PlayThrough(dog));
        while (pending.Count > 0)
        {
            bool more = false;
            object item = null;
            Exception error = null;
            var run = pending.Peek();
            try { more = run.MoveNext(); if (more) item = run.Current; }
            catch (Exception caught) { error = caught; }
            if (error != null) { Finish(error); yield break; }
            if (!more) pending.Pop();
            else if (item is IEnumerator child) pending.Push(child);
            else yield return item;
        }
        Finish(null);
    }

    private static IEnumerator PlayThrough(DogCompanion dog)
    {
        output = Environment.GetEnvironmentVariable("CITY_DOG_MOTION_CAPTURES");
        dog.SetWanderingEnabled(false);
        dog.SetPracticeArea(new Vector3(0.8f, 0, -2.9f), new Vector3(2.6f, .11f, -0.1f), new Vector3(1.5f, 0, .7f));
        // The straight-walk contact order is measured with the dog already facing the path.
        dog.transform.rotation = Quaternion.identity;
        dog.ResetCompanion();
        var skeleton = new Dictionary<string, Transform>();
        foreach (var joint in dog.VisualRoot.GetComponentsInChildren<Transform>())
            if (joint.name.StartsWith("DEF-", StringComparison.Ordinal)) skeleton.Add(joint.name, joint);
        var paw = Array.ConvertAll(Paws, key => skeleton[key]);
        var boneSizes = new Dictionary<Transform, Vector3>();
        foreach (Transform joint in skeleton.Values) boneSizes.Add(joint, joint.localScale);
        var upperLimbs = new[]
        {
            skeleton["DEF-front_forearm.L"], skeleton["DEF-front_paw.L"], skeleton["DEF-hind_shin.L"],
            skeleton["DEF-hind_hock.L"], skeleton["DEF-front_forearm.R"], skeleton["DEF-front_paw.R"],
            skeleton["DEF-hind_shin.R"], skeleton["DEF-hind_hock.R"], skeleton["DEF-spine.01"],
            skeleton["DEF-spine.02"], skeleton["DEF-chest"]
        };
        float[] limbLengths = Array.ConvertAll(upperLimbs, tip => Vector3.Distance(tip.parent.position, tip.position));
        var contacts = new bool[4];
        var anchors = new Vector3[4];
        var sequences = new List<int>();
        float maxSlip = 0, maxLift = 0, meters = 0, nextCaptureDistance = 0.8f;
        float maxSlipAt = 0;
        string maxSlipPaw = "";
        Vector3 previous = dog.transform.position;
        int samples = 0, sampleFrame = 0;
        dog.StartCoroutine(dog.VisitStation(new Vector3(.8f, 0, 1.6f), "验证步态", false));
        while (dog.IsPerforming)
        {
            yield return null;
            float distance = Vector3.Distance(previous, dog.transform.position);
            previous = dog.transform.position;
            meters += distance;
            for (int i = 0; i < paw.Length; i++)
            {
                bool onGround = IsPlanted(dog, Paws[i]);
                Vector3 now = paw[i].position;
                if (onGround && !contacts[i])
                {
                    anchors[i] = now;
                    if (meters > .45f && distance > .00001f) sequences.Add(i);
                }
                if (distance > .00001f && meters > .45f && meters < 4f && onGround)
                {
                    Vector3 shift = now - anchors[i]; shift.y = 0;
                    if (shift.magnitude > maxSlip)
                    {
                        maxSlip = shift.magnitude;
                        maxSlipAt = meters;
                        maxSlipPaw = Paws[i];
                    }
                    samples++;
                }
                if (meters > .45f && distance > .00001f && !onGround)
                    maxLift = Mathf.Max(maxLift, now.y);
                contacts[i] = onGround;
            }
            if (sampleFrame < 16 && meters > nextCaptureDistance)
            {
                sampleFrame++;
                Capture(dog, "walk-" + sampleFrame.ToString("D2"));
                nextCaptureDistance += .055f;
            }
        }
        Require(dog.LastActivitySucceeded && meters > 3f, "Dog did not cover the test path.");
        Require(samples > 8 && sequences.Count >= 8 && maxLift > .04f, "Did not observe raised paws and independent support intervals.");
        int correctTransitions = 0;
        for (int i = 1; i < sequences.Count; i++)
            if (sequences[i] == (sequences[i - 1] + 1) % 4) correctTransitions++;
        Require(correctTransitions >= (sequences.Count - 1) * .72f, "Footfalls did not follow the lateral sequence: " + string.Join(",", sequences));
        Debug.Log("DOG_MOTION_GAIT " + meters.ToString("F2") + "m, contacts=" + string.Join(",", sequences) +
            ", maxSlip=" + (maxSlip * 100).ToString("F1") + "cm at " + maxSlipAt.ToString("F2") + "m (" + maxSlipPaw +
            "), liftY=" + (maxLift * 100).ToString("F1") + "cm");
        Require(maxSlip < .13f, "Foot drift exceeded 13 cm on the sustained straight path: " + maxSlip);
        Camera.main.transform.position = dog.transform.position + new Vector3(0, 1.65f, 2.65f);
        yield return Pose(dog, "sit", () =>
        {
            CheckLimbLengths(upperLimbs, limbLengths);
            Require(paw[1].position.y < .085f && paw[3].position.y < .085f, "Sit lost forepaw support.");
            Require(skeleton["DEF-pelvis"].position.y < .32f, "Sit did not lower the hindquarters.");
        });
        yield return Pose(dog, "paw", () =>
        {
            CheckLimbLengths(upperLimbs, limbLengths);
            Require(paw[3].position.y - paw[1].position.y > .085f, "Right paw did not lift relative to left.");
            Require(IsPlanted(dog, Paws[0]) && IsPlanted(dog, Paws[1]) && IsPlanted(dog, Paws[2]),
                "Paw gesture lost its three-point support.");
            Vector3 wrist = skeleton["DEF-front_paw.R"].position;
            Vector3 forearm = wrist - skeleton["DEF-front_forearm.R"].position;
            Vector3 toes = paw[3].position - wrist;
            Vector3 pitchAxis = dog.transform.right;
            float flex = Vector3.SignedAngle(Vector3.ProjectOnPlane(forearm, pitchAxis),
                Vector3.ProjectOnPlane(toes, pitchAxis), pitchAxis);
            Require(flex > 12f && flex < 60f, $"Raised wrist bent in the wrong direction: {flex:F1} degrees.");
        });
        yield return Pose(dog, "bow", () =>
        {
            CheckLimbLengths(upperLimbs, limbLengths);
            float chest = skeleton["DEF-chest"].position.y, pelvis = skeleton["DEF-pelvis"].position.y;
            Require(chest + .035f < pelvis, $"Bow did not lower front quarters: chest={chest:F3}, pelvis={pelvis:F3}.");
        });
        yield return Pose(dog, "crouch", () =>
        {
            CheckLimbLengths(upperLimbs, limbLengths);
            Require(skeleton["DEF-pelvis"].position.y < .32f && skeleton["DEF-chest"].position.y < .32f, "Lie down did not lower the torso.");
        });
        dog.SetPracticeArea(new Vector3(1.4f, 0, -1.2f), new Vector3(3.15f, .11f, -.45f), new Vector3(2.5f, 0, 1.25f));
        var owner = new GameObject("Motion validation owner").transform;
        owner.position = new Vector3(1.4f, 1.65f, -2.6f);
        dog.user = owner;
        dog.StartCoroutine(dog.PerformAction("fetch_lollipop", .45f));
        Transform candy = GameObject.Find("Lollipop").transform;
        var muzzle = (Transform)typeof(DogCompanion).GetProperty("MuzzleAnchor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dog);
        Require(muzzle != null && muzzle.parent == skeleton["DEF-head"], "Muzzle was not attached to the head bone.");
        bool carried = false;
        while (dog.IsPerforming)
        {
            yield return null;
            if (!carried && Vector3.Distance(candy.position, muzzle.position) < .035f && candy.position.y > .25f)
            {
                carried = true;
                Capture(dog, "fetch");
            }
        }
        Require(carried && dog.LastFetchSucceeded,
            $"Pickup and return did not complete: carried={carried}, returned={dog.LastFetchSucceeded}, {dog.StateLabel}.");
        CheckLimbLengths(upperLimbs, limbLengths);
        foreach (var size in boneSizes) Require(size.Key.localScale == size.Value, "Bone scale changed: " + size.Key.name);
        dog.ShowKeypoints = true;
        Require(dog.ShowKeypoints, "Keypoint overlay did not initialize.");
    }

    private static void CheckLimbLengths(Transform[] bones, float[] baseline)
    {
        for (int i = 0; i < bones.Length; i++)
            Require(Mathf.Abs(Vector3.Distance(bones[i].position, bones[i].parent.position) - baseline[i]) < .001f,
                "Limb segment length changed: " + bones[i].name);
    }

    private static IEnumerator Pose(DogCompanion dog, string action, Action check)
    {
        dog.StartCoroutine(dog.PerformAction(action, .5f));
        yield return new WaitForSeconds(.84f);
        Capture(dog, action);
        check();
        while (dog.IsPerforming) yield return null;
    }

    private static bool IsPlanted(DogCompanion dog, string key)
    {
        object[] args = { key, false };
        Require(Contact != null && (bool)Contact.Invoke(dog, args), "Missing keypoint " + key);
        return (bool)args[1];
    }

    private static void Capture(DogCompanion dog, string name)
    {
        if (string.IsNullOrEmpty(output)) return;
        GameObject view = new GameObject("Motion preview camera");
        var camera = view.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.transform.position = dog.transform.position + dog.transform.right * 1.9f +
            dog.transform.forward * .58f + Vector3.up * .79f;
        camera.transform.LookAt(dog.transform.position + Vector3.up * .37f);
        camera.fieldOfView = 34;
        var target = new RenderTexture(900, 560, 24);
        var frame = new Texture2D(900, 560, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            frame.ReadPixels(new Rect(0, 0, 900, 560), 0, 0);
            frame.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), frame.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(frame);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(view);
        }
    }

    private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }

    private static void Finish(Exception error)
    {
        SessionState.SetBool(SessionKey, false);
        if (error == null) Debug.Log("DOG_MOTION_V2_OK: footfall sequence, planted drift, sit, right paw, bow, lie down, muzzle carry, bone scale and limb lengths.");
        else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
