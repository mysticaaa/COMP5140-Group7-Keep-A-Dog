#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogTailValidation
{
    private const string SessionKey = "CityDogTailValidation";
    private static readonly string[] PawNames =
    {
        "DEF-front_toes.L", "DEF-front_toes.R", "DEF-hind_paw.L", "DEF-hind_paw.R"
    };
    private static double deadline;
    private static bool started;

    static DogTailValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 65;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Tail validation timed out."));
            return;
        }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        started = true;
        trainer.StartCoroutine(Validate(trainer.dog));
    }

    private static IEnumerator Validate(DogCompanion dog)
    {
        IEnumerator test = CheckDog(dog);
        while (true)
        {
            bool more;
            try { more = test.MoveNext(); }
            catch (Exception error)
            {
                Finish(error);
                yield break;
            }
            if (!more) break;
            yield return test.Current;
        }
        Finish(null);
    }

    private static IEnumerator CheckDog(DogCompanion dog)
    {
        dog.SetWanderingEnabled(false);
        yield return new WaitForSeconds(.3f);

        var bones = dog.VisualRoot.GetComponentsInChildren<Transform>();
        var tail = Enumerable.Range(1, 4).Select(i => bones.Single(bone => bone.name == "DEF-tail.0" + i)).ToArray();
        var paws = PawNames.Select(name => bones.Single(bone => bone.name == name)).ToArray();
        var fur = dog.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>()
            .Single(renderer => renderer.name == "Tail coat silhouette");
        var body = fur.transform.parent.GetComponent<SkinnedMeshRenderer>();
        Need(body != null && fur.bones.SequenceEqual(body.bones) && tail.All(fur.bones.Contains),
            "Tail fur does not share the body deformation skeleton.");
        Need(fur.sharedMesh.vertexCount > 0 && fur.enabled, "Tail fur is missing or disabled.");
        var bindScale = tail.Select(joint => joint.localScale).ToArray();
        var bindPosition = tail.Select(joint => joint.localPosition).ToArray();
        Vector3 dogAtRest = dog.transform.position;
        var pawRestPositions = paws.Select(paw => paw.position).ToArray();
        float restTip = TipX(dog, tail);
        var mesh = new Mesh();
        fur.BakeMesh(mesh);
        Vector3[] verticesAtRest = mesh.vertices;
        float maxSkinShift = 0f, maxPawShift = 0f;
        float minimum = 0f, maximum = 0f;
        int crossings = 0, lastSide = 0, frames = 0;
        string output = Environment.GetEnvironmentVariable("CITY_DOG_TAIL_CAPTURES");
        if (!string.IsNullOrEmpty(output)) Directory.CreateDirectory(output);
        Capture(dog, output, "idle");
        dog.StartCoroutine(dog.PerformAction("pet", .15f));
        float time = Time.time, nextFrame = 0f;
        while (Time.time - time < 2.7f)
        {
            yield return null;
            float age = Time.time - time;
            float x = TipX(dog, tail) - restTip;
            minimum = Mathf.Min(minimum, x);
            maximum = Mathf.Max(maximum, x);
            int side = Mathf.Abs(x) < .013f ? 0 : (x > 0f ? 1 : -1);
            if (side != 0)
            {
                if (lastSide != 0 && lastSide != side) crossings++;
                lastSide = side;
            }
            for (int i = 0; i < tail.Length; i++)
            {
                Need(tail[i].localScale == bindScale[i] && tail[i].localPosition == bindPosition[i],
                    "Tail changed segment scale or bind offset.");
                maxPawShift = Mathf.Max(maxPawShift, Vector3.Distance(paws[i].position, pawRestPositions[i]));
            }
            if (age > .45f && age < .85f)
            {
                fur.BakeMesh(mesh);
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                    maxSkinShift = Mathf.Max(maxSkinShift, Vector3.Distance(
                        fur.transform.TransformPoint(verticesAtRest[i]), fur.transform.TransformPoint(vertices[i])));
            }
            if (frames < 36 && age >= nextFrame)
            {
                nextFrame += .075f;
                Capture(dog, output, "wag-" + (++frames).ToString("D2"));
            }
        }
        UnityEngine.Object.Destroy(mesh);
        float settled = Mathf.Abs(TipX(dog, tail) - restTip);
        Debug.Log($"DOG_TAIL_METRICS range={(maximum - minimum) * 100:F1}cm crossings={crossings}, " +
            $"settled={settled * 100:F1}cm furSkinDelta={maxSkinShift * 100:F1}cm petPawShift={maxPawShift * 100:F1}cm");
        Need(minimum < -.02f && maximum > .02f && crossings >= 4,
            "Player interaction did not drive a short lateral wag to both sides.");
        Need(settled < .014f && maxPawShift < .012f && Vector3.Distance(dogAtRest, dog.transform.position) < .012f,
            "Pet shifted the stance or the tail failed to settle.");
        Need(maxSkinShift > .02f, "The tail overlay did not deform with the tail.");

        dog.StartCoroutine(dog.PerformAction("bow", .55f));
        float bowStart = Time.time;
        float bowMin = float.PositiveInfinity, bowMax = float.NegativeInfinity;
        float nextBowFrame = 0f;
        int bowFrames = 0;
        bool comparedCoat = false;
        while (Time.time - bowStart < 2.5f)
        {
            yield return null;
            float age = Time.time - bowStart;
            float tip = TipX(dog, tail);
            bowMin = Mathf.Min(tip, bowMin);
            bowMax = Mathf.Max(tip, bowMax);
            if (bowFrames < 34 && age >= nextBowFrame)
            {
                nextBowFrame += .075f;
                Capture(dog, output, "play-wag-" + (++bowFrames).ToString("D2"));
            }
            if (!comparedCoat && age >= .85f)
            {
                comparedCoat = true;
                Capture(dog, output, "play-coat");
                fur.enabled = false;
                Capture(dog, output, "play-base");
                fur.enabled = true;
            }
        }
        Debug.Log($"DOG_TAIL_PLAY_METRICS bowRange={(bowMax - bowMin) * 100:F1}cm");
        Need(bowMax - bowMin > .09f && !dog.IsPerforming,
            "An accepted play bow did not wag or did not finish.");
        dog.PlayTailWag();
        yield return new WaitForSeconds(.33f);
        dog.StartCoroutine(dog.PerformAction("rest", .75f));
        yield return new WaitForSeconds(.67f);
        Need(Mathf.Abs(TipX(dog, tail) - restTip) < .025f, "Rest did not damp out the play wag.");
    }

    private static float TipX(DogCompanion dog, Transform[] tail) =>
        Vector3.Dot(tail[3].position - tail[0].position, dog.transform.right);

    private static void Capture(DogCompanion dog, string directory, string filename)
    {
        if (string.IsNullOrEmpty(directory) || Camera.main == null) return;
        var view = new GameObject("Tail preview camera");
        var camera = view.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.transform.position = dog.transform.position + dog.transform.right * 1.25f -
            dog.transform.forward * 1.90f + Vector3.up * .90f;
        Vector3 center = dog.transform.position - dog.transform.forward * .20f + Vector3.up * .42f;
        camera.transform.LookAt(center);
        camera.fieldOfView = 30;
        var target = new RenderTexture(800, 530, 24);
        var pixels = new Texture2D(800, 530, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 800, 530), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(directory, filename + ".png"), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(view);
        }
    }

    private static void Need(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(Exception error)
    {
        SessionState.EraseBool(SessionKey);
        if (error == null) Debug.Log("DOG_TAIL_VALIDATION_OK");
        else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif