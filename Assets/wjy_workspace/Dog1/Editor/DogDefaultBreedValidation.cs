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
public static class DogDefaultBreedValidation
{
    private const string Flag = "DogDefaultBreedValidation.Play";
    private static bool running;
    private static string Output => Environment.GetEnvironmentVariable("CITYDOG_BREED_OUTPUT") ?? "Logs/DefaultBreedValidation";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    static DogDefaultBreedValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Flag, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || running) return;
        running = true;
        Time.captureFramerate = 60;
        var go = new GameObject("Default breed validation");
        go.SetActive(false);
        var dog = go.AddComponent<DogCompanion>();
        var breedClass = typeof(DogCompanion).Assembly.GetType("Wjy.DogTraining.DogBreed");
        var breeds = (Array)breedClass.GetField("Options", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        typeof(DogCompanion).GetMethod("SetBreedBeforeActivation", Instance).Invoke(dog, new[] { breeds.GetValue(0) });
        go.SetActive(true);
        dog.StartCoroutine(Guard(Check(dog)));
    }

    private static IEnumerator Guard(IEnumerator test)
    {
        var steps = new Stack<IEnumerator>();
        steps.Push(test);
        while (steps.Count > 0)
        {
            object current;
            try
            {
                IEnumerator step = steps.Peek();
                if (!step.MoveNext())
                {
                    steps.Pop();
                    continue;
                }
                current = step.Current;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(1);
                yield break;
            }
            if (current is IEnumerator nested) steps.Push(nested);
            else yield return current;
        }
        Debug.Log("DOG_DEFAULT_BREED_OK: original Border Collie, grounded invitation, unbroken target attention, curved walking and fetch");
        Finish(0);
    }

    private static IEnumerator Check(DogCompanion dog)
    {
        Require(dog.Ready && dog.VisualRoot.name == "border-collie - rigged", "default breed is the existing Border Collie model");
        var skin = dog.GetComponentInChildren<SkinnedMeshRenderer>();
        Require(skin != null && skin.sharedMesh != null, "existing weighted mesh is visible");
        Transform head = Joint(dog, "DEF-head");
        Transform fl = Joint(dog, "DEF-front_toes.L"), fr = Joint(dog, "DEF-front_toes.R");
        Transform hl = Joint(dog, "DEF-hind_paw.L"), hr = Joint(dog, "DEF-hind_paw.R");
        Require(head.position.y > .41f && head.position.y < .68f, "head height is calibrated for standing interactions");
        var owner = new GameObject("Owner gaze target").transform;
        owner.position = new Vector3(.72f, 1.43f, 1.75f);
        dog.user = owner;
        dog.HasPlayClearance = (point, radius) => true;
        dog.SetWanderingEnabled(false);
        var camera = BuildCamera();
        dog.SetPracticeArea(Vector3.zero, new Vector3(.56f, .11f, 1.2f), new Vector3(-.6f, .11f, 1.2f));
        yield return new WaitForSeconds(.55f);
        Capture(camera, dog, "border-collie-standing.png");

        var walk = typeof(DogCompanion).GetMethod("WalkTowards", Instance);
        Vector3 origin = dog.transform.position;
        float width = float.PositiveInfinity;
        for (int i = 0; i < 235; i++)
        {
            float angle = i / 234f * 1.26f;
            var destination = new Vector3(.95f * (1f - Mathf.Cos(angle)), .0f, .10f + .95f * Mathf.Sin(angle));
            walk.Invoke(dog, new object[] { destination, .64f, Time.deltaTime });
            yield return null;
            width = Mathf.Min(width, Vector3.Dot(fr.position - fl.position, dog.transform.right),
                Vector3.Dot(hr.position - hl.position, dog.transform.right));
        }
        Require((dog.transform.position - origin).magnitude > .80f && width > .035f, "curved route advances with uncrossed paws");
        yield return new WaitForSeconds(.9f);
        Require(FilteredGround(fl) && FilteredGround(fr) && FilteredGround(hl) && FilteredGround(hr), "four paws planted after turning");

        dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        dog.ResetCompanion();
        yield return new WaitForSeconds(.3f);
        Quaternion restHead = head.rotation;
        Require(dog.TryPlayMicroAction("head_tilt"), "head tilt can be selected");
        yield return new WaitForSeconds(.95f);
        Require(Quaternion.Angle(restHead, head.rotation) >= 12f, "head tilt reaches visible articulation");
        Capture(camera, dog, "border-collie-head-tilt.png");
        yield return new WaitForSeconds(1.65f);

        var routines = new[] { "pet", "paw", "bow", "invite", "fetch_toy", "fetch_lollipop" };
        foreach (string action in routines)
        {
            dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            dog.ResetCompanion();
            yield return new WaitForSeconds(.3f);
            if (action == "invite")
            {
                Vector3 oldOwner = owner.position;
                owner.position = new Vector3(.08f, 1.43f, 1.82f);
                Transform chest = Joint(dog, "DEF-chest"), hips = Joint(dog, "DEF-pelvis");
                Transform muzzle = head.Find("Muzzle landmark");
                Vector3 hipStart = hips.position;
                Vector3 plantedLeft = default, plantedRight = default;
                float phaseStart = Time.time, steadySeconds = 0f, peakCrest = 0f, peakSlide = 0f;
                float rootSlip = 0f, lowestGap = float.PositiveInfinity, minimumLook = float.PositiveInfinity;
                bool captured = false;
                dog.StartCoroutine(dog.PerformAction(action, .15f));
                while (dog.IsPerforming && Time.time - phaseStart < 5f)
                {
                    yield return null;
                    float age = Time.time - phaseStart;
                    rootSlip = Mathf.Max(rootSlip, dog.transform.position.magnitude);
                    if (age < .64f) peakCrest = Mathf.Max(peakCrest, Mathf.Max(fl.position.y, fr.position.y));
                    if (age < .92f || age > 1.47f) continue;
                    steadySeconds += Time.deltaTime;
                    lowestGap = Mathf.Min(lowestGap, hips.position.y - chest.position.y);
                    if (muzzle != null)
                    {
                        Vector3 nose = (muzzle.position - head.position).normalized;
                        minimumLook = Mathf.Min(minimumLook, Vector3.Dot(nose, (owner.position - head.position).normalized));
                    }
                    if (!captured)
                    {
                        plantedLeft = fl.position;
                        plantedRight = fr.position;
                        Capture(camera, dog, "border-collie-invite.png");
                        captured = true;
                    }
                    else peakSlide = Mathf.Max(peakSlide, Mathf.Max(FlatMotion(plantedLeft, fl.position), FlatMotion(plantedRight, fr.position)));
                }
                Debug.Log($"DEFAULT_BOW hold={steadySeconds:F2}s torso gap={lowestGap:F3}m paw crest={peakCrest:F3}m hold drift={peakSlide:F3}m look={minimumLook:F2}");
                Require(captured && steadySeconds > .43f && lowestGap > .035f,
                    "play invitation holds with forequarters below the hips");
                Require(peakCrest > .032f && peakSlide < .036f && rootSlip < .003f,
                    "play invitation lifts the front paws then keeps the stance");
                Require(muzzle != null && minimumLook > .65f && hips.position.y > hipStart.y - .06f,
                    "play invitation looks at the owner and returns to standing");
                owner.position = oldOwner;
            }
            else
            {
                dog.StartCoroutine(dog.PerformAction(action, .15f));
                float deadline = Time.time + 25f;
                while (dog.IsPerforming && Time.time < deadline) yield return null;
            }
            Require(!dog.IsPerforming, "action finishes: " + action);
            if (action.StartsWith("fetch", StringComparison.Ordinal))
                Require(dog.LastFetchSucceeded, "short muzzle reaches and returns " + action);
        }
        yield return CheckArrival(dog, owner, camera);
    }

    private static IEnumerator CheckArrival(DogCompanion dog, Transform owner, Camera camera)
    {
        dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        dog.ResetCompanion();
        owner.position = new Vector3(.24f, 1.45f, 3.3f);
        yield return dog.PerformAction("come", .1f);
        Require(dog.LastComeSucceeded, "arrival fixture reaches the player");
        var startedAt = typeof(DogCompanion).GetField("arrivalStartedAt", Instance);
        var focus = typeof(DogCompanion).GetProperty("HasCommittedArrivalFocus", Instance);
        var tilt = typeof(DogCompanion).GetField("socialTilt", Instance);
        var pitch = typeof(DogCompanion).GetField("socialPitch", Instance);
        var yaw = typeof(DogCompanion).GetField("socialYaw", Instance);
        var consumeInvite = typeof(DogCompanion).GetMethod("TryConsumeArrivalInvitation", Instance);
        typeof(DogCompanion).GetField("arrivalMayInvite", Instance).SetValue(dog, true);
        float wait = Time.time + 2f;
        while ((float)startedAt.GetValue(dog) < 0f && Time.time < wait) yield return null;
        float ageOrigin = (float)startedAt.GetValue(dog);
        Require(ageOrigin >= 0f, "arrival settles into a timed look");

        Vector3 planted = dog.transform.position;
        yield return new WaitForSeconds(1.1f);
        float firstTilt = (float)tilt.GetValue(dog);
        Require(firstTilt < -7f && (float)pitch.GetValue(dog) < -8f, "arrival lifts its muzzle and tilts its head");
        Require(!dog.TryPlayMicroAction("soft_shake") && !(bool)consumeInvite.Invoke(dog, null), "arrival pose owns the attention until it finishes");
        dog.AttendToUser(3f);
        yield return new WaitForSeconds(.38f);
        Require((float)startedAt.GetValue(dog) == ageOrigin && (float)tilt.GetValue(dog) < -7f &&
            Vector3.Distance(planted, dog.transform.position) < .002f, "repeated attention keeps the same look and planted stance");
        Require(dog.transform.Find("Puzzled question mark")?.gameObject.activeSelf != true, "friendly anticipation has no question mark");
        Capture(camera, dog, "border-collie-owner-arrival.png");
        while ((bool)focus.GetValue(dog) && Time.time < ageOrigin + 3.3f) yield return null;
        Require(!(bool)focus.GetValue(dog) && (bool)consumeInvite.Invoke(dog, null) && !(bool)consumeInvite.Invoke(dog, null),
            "owner look completes before offering play once");

        var prop = new GameObject("Moved toy target").transform;
        prop.position = dog.transform.TransformPoint(.41f, .03f, .68f);
        var pointType = typeof(DogCompanion).GetNestedType("ExplorationPoint", BindingFlags.NonPublic);
        var points = Array.CreateInstance(pointType, 1);
        points.SetValue(Activator.CreateInstance(pointType, new object[] { "玩具", Vector3.zero, prop }), 0);
        typeof(DogCompanion).GetMethod("SetExplorationPoints", Instance).Invoke(dog, new object[] { points });
        var focusType = typeof(DogCompanion).GetNestedType("ArrivalFocus", BindingFlags.NonPublic);
        typeof(DogCompanion).GetMethod("ScheduleArrivalFocus", Instance).Invoke(dog, new[] { Enum.Parse(focusType, "Object"), (object)0, false });
        yield return new WaitForSeconds(.95f);
        Require((float)yaw.GetValue(dog) > 10f && (float)pitch.GetValue(dog) > 13f,
            "inspection looks down and toward the toy's actual moved position");
        prop.position = dog.transform.TransformPoint(0f, 0f, -2f);
        dog.StartCoroutine(dog.PerformAction("paw", .1f));
        yield return new WaitForSeconds(.32f);
        Require((bool)focus.GetValue(dog) && !dog.IsPerforming && (float)yaw.GetValue(dog) > 8f,
            "a moved toy and a queued action do not cut an inspection short");
        wait = Time.time + 3f;
        while (!dog.IsPerforming && Time.time < wait) yield return null;
        Require(dog.CurrentAction == "paw" && !(bool)focus.GetValue(dog), "queued action starts after the inspection releases");
        while (dog.IsPerforming && Time.time < wait) yield return null;
    }

    private static float FlatMotion(Vector3 from, Vector3 to) =>
        Vector3.ProjectOnPlane(to - from, Vector3.up).magnitude;

    private static Transform Joint(DogCompanion dog, string name)
    {
        var joint = (Transform)typeof(DogCompanion).GetMethod("Bone", Instance).Invoke(dog, new object[] { name });
        if (joint == null) throw new InvalidOperationException("Missing joint " + name);
        return joint;
    }

    private static bool FilteredGround(Transform paw) => paw.position.y > -.02f && paw.position.y < .055f;

    private static Camera BuildCamera()
    {
        var camera = new GameObject("Default breed portrait camera", typeof(Camera)).GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.55f, .65f, .60f);
        camera.orthographic = true;
        camera.orthographicSize = .50f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.62f, .64f, .63f);
        var key = new GameObject("Window light", typeof(Light)).GetComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.25f;
        key.shadows = LightShadows.Soft;
        key.transform.rotation = Quaternion.Euler(46f, 211f, 0f);
        var fill = new GameObject("Warm bounce", typeof(Light)).GetComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = .35f;
        fill.color = new Color(1f, .85f, .65f);
        fill.transform.rotation = Quaternion.Euler(28f, 40f, 0f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(.51f, .59f, .53f) };
        return camera;
    }

    private static void Capture(Camera camera, DogCompanion dog, string name)
    {
        camera.transform.position = dog.transform.TransformPoint(1.14f, .91f, 1.26f);
        camera.transform.LookAt(dog.transform.position + Vector3.up * .34f);
        var surface = new RenderTexture(960, 720, 24);
        var prior = RenderTexture.active;
        camera.targetTexture = surface;
        camera.Render();
        RenderTexture.active = surface;
        var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = prior;
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(surface);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Debug.Log("DEFAULT_BREED_CHECK: " + message);
    }

    private static void Finish(int result)
    {
        SessionState.SetBool(Flag, false);
        Time.captureFramerate = 0;
        EditorApplication.Exit(result);
    }
}
#endif
