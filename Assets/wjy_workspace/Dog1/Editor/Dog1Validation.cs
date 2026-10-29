#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog1Validation
{
    private static int stage;
    private static double since;
    private static Vector3 before;
    private static DogCompanion dog;
    private static Dog1Training trainer;
    static Dog1Validation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        SessionState.SetBool("WjyDog1Validation", true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool("WjyDog1Validation", false) || !EditorApplication.isPlaying) return;
        try
        {
            if (dog == null)
            {
                dog = UnityEngine.Object.FindObjectOfType<DogCompanion>();
                trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
                if (dog == null || trainer == null || !dog.Ready) return;
                before = dog.transform.position; since = EditorApplication.timeSinceStartup; stage = 0;
                foreach (Transform bone in dog.VisualRoot.GetComponentsInChildren<Transform>())
                    if (bone.name.StartsWith("DEF-")) Debug.Log("DOG_BONE " + bone.name + " world=" + bone.position);
            }
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (stage == 0 && elapsed > 2)
            {
                if (Vector3.Distance(before, dog.transform.position) < 0.05f) throw new Exception("Dog did not wander.");
                Capture("01-wander"); StartAction("sit");
            }
            else if (stage == 1 && elapsed > 1.5) { Capture("02-sit"); stage++; }
            else if (stage == 2 && elapsed > 5.2) { CheckResumed(); StartAction("crouch"); }
            else if (stage == 3 && elapsed > 1.5) { Capture("03-crouch"); stage++; }
            else if (stage == 4 && elapsed > 5.2) { CheckResumed(); StartAction("spin"); }
            else if (stage == 5 && elapsed > 1) { Capture("04-spin"); stage++; }
            else if (stage == 6 && elapsed > 6.3) { CheckResumed(); StartAction("refuse"); }
            else if (stage == 7 && elapsed > 0.9)
            {
                if (dog.GetComponentInChildren<ParticleSystem>().particleCount == 0) throw new Exception("No refusal smoke particles.");
                Capture("05-refuse"); stage++;
            }
            else if (stage == 8 && elapsed > 5.8)
            {
                CheckResumed();
                foreach (string action in new[] { "sit", "crouch", "spin" })
                {
                    Dog1Training.ParseDecision("{\"action\":\"" + action + "\"}", action);
                    Dog1Training.ParseDecision("{\"action\":\"refuse\"}", action);
                }
                bool rejected = false;
                try { Dog1Training.ParseDecision("{\"action\":\"spin\"}", "sit"); } catch (FormatException) { rejected = true; }
                if (!rejected) throw new Exception("Wrong action was not rejected.");
                Debug.Log("DOG1_VALIDATION_OK: model, wandering, four actions, smoke, resume delay, response validation.");
                SessionState.SetBool("WjyDog1Validation", false);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e); SessionState.SetBool("WjyDog1Validation", false); EditorApplication.Exit(1);
        }
    }
    private static void StartAction(string action)
    {
        trainer.StartCoroutine(dog.PerformAction(action)); stage++; since = EditorApplication.timeSinceStartup;
    }
    private static void CheckResumed()
    {
        if (dog.IsPerforming || dog.CurrentAction != "wander") throw new Exception("Dog did not resume wandering after delay.");
    }
    private static void Capture(string name)
    {
        string output = Path.Combine(Application.dataPath, "wjy_workspace/Dog1/Validation");
        Directory.CreateDirectory(output);
        var cameraObject = new GameObject("Validation Camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.transform.position = dog.transform.position + dog.transform.right * 2.2f + dog.transform.forward * 2.6f + Vector3.up * 1.5f;
        camera.transform.LookAt(dog.transform.position + Vector3.up * 0.5f);
        camera.fieldOfView = 36;
        var target = new RenderTexture(960, 720, 24); camera.targetTexture = target;
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}
#endif