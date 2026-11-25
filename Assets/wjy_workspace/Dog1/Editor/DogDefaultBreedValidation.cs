#if UNITY_EDITOR
using System;
using System.Collections;
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
        while (true)
        {
            object current;
            try
            {
                if (!test.MoveNext()) break;
                current = test.Current;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(1);
                yield break;
            }
            yield return current;
        }
        Debug.Log("DOG_DEFAULT_BREED_OK: original Border Collie mesh, curved walking, head tilt, fetch and pet");
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

        var routines = new[] { "pet", "paw", "bow", "fetch_toy", "fetch_lollipop" };
        foreach (string action in routines)
        {
            dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            dog.ResetCompanion();
            yield return new WaitForSeconds(.3f);
            dog.StartCoroutine(dog.PerformAction(action, .15f));
            float deadline = Time.time + 25f;
            while (dog.IsPerforming && Time.time < deadline) yield return null;
            Require(!dog.IsPerforming, "action finishes: " + action);
            if (action.StartsWith("fetch", StringComparison.Ordinal))
                Require(dog.LastFetchSucceeded, "short muzzle reaches and returns " + action);
        }
    }

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
