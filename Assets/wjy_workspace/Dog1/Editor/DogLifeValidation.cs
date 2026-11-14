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
public static class DogLifeValidation
{
    private const string Key = "DogLifeValidation";
    private static bool running;
    private static double deadline;

    static DogLifeValidation()
    {
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 180;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("City dog validation timed out."));
            return;
        }
        if (running) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        running = true;
        trainer.StartCoroutine(Validate(trainer));
    }

    private static IEnumerator Validate(Dog1Training trainer)
    {
        var test = PlayThrough(trainer);
        while (true)
        {
            bool more = false;
            object current = null;
            Exception error = null;
            try
            {
                more = test.MoveNext();
                current = more ? test.Current : null;
            }
            catch (Exception caught) { error = caught; }
            if (!more)
            {
                Finish(error);
                yield break;
            }
            yield return current;
        }
    }

    private static IEnumerator PlayThrough(Dog1Training trainer)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Call(string method, params object[] args) => typeof(Dog1Training).GetMethod(method, flags).Invoke(trainer, args);
        object Property(string property) => typeof(Dog1Training).GetProperty(property, flags).GetValue(trainer);
        var profile = (DogLifeProgress)Property("Profile");
        var area = (DogNeighborhood)Property("Neighborhood");
        var dog = trainer.dog;
        var camera = Camera.main;

        Require(profile.CurrentLocation == DogLocation.Apartment && profile.Bones >= 3, "Fresh profile and daily gift");
        Require(!profile.ClaimDailyBonus().Succeeded, "Daily bonus was claimed twice");
        Call("BeginPlay");
        yield return new WaitForSeconds(0.3f);
        Capture("01-apartment", camera);
        foreach (DogCareAction action in new[] { DogCareAction.Feed, DogCareAction.Water, DogCareAction.Play })
        {
            int before = profile.TotalExperience;
            Call("StartCare", action);
            while (!(bool)Property("WaitingForPlayer")) yield return null;
            Require(profile.TotalExperience > before, "No reward after " + action + ". Dog " + dog.StateLabel);
        }
        Require(profile.CurrentChapter.Number == 2, "First chapter failed");
        Call("Buy", DogLocation.Park);
        Require(profile.CurrentLocation == DogLocation.Park && profile.IsUnlocked(DogLocation.Park), "Park purchase/navigation");
        yield return new WaitForSeconds(0.4f);
        Capture("02-park", camera);
        Call("StartCare", DogCareAction.Walk);
        Vector3 waypoint = area.WalkDestination;
        camera.transform.position = waypoint + Vector3.up * 1.65f;
        yield return null;
        while (!(bool)Property("WaitingForPlayer")) yield return null;
        Require(profile.WasteCount == 1, "Walk should create one item to clean");
        Call("StartCare", DogCareAction.Clean);
        while (!(bool)Property("WaitingForPlayer")) yield return null;
        Require(profile.WasteCount == 0, "Cleanup failed");

        camera.GetComponent<Dog1PlayerView>().Place(area.PlayerStart);
        Require((bool)Call("Submit", "晚安：坐下、抬右爪、拜一拜"), "Sequence input rejected");
        while (!(bool)Property("WaitingForPlayer")) yield return null;
        Require((bool)Property("CanRewardTrick"), "Sequence did not complete");
        Call("ApplyFeedback", true);
        var folder = Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES");
        var reloaded = new DogRoutines(Path.Combine(folder, "dog-routine.json"));
        Require(reloaded.Recall("晚安")?.Length == 3, "Named sequence did not persist");
        Require((bool)Call("Submit", "晚安"), "Saved cue was not accepted");
        while (!(bool)Property("WaitingForPlayer")) yield return null;
        Require((bool)Property("CanRewardTrick"), "Recalling the named cue did not finish all three movements");
        Call("ApplyFeedback", false);
        Require(profile.CurrentChapter.Number == 3, "Chapter 2 training wasn't credited");
        var loaded = new DogLifeProgress(Path.Combine(folder, "dog-life.json"));
        Require(loaded.CurrentLocation == DogLocation.Park && loaded.CurrentChapter.Number == 3, "Progress data did not reload");

        // Progress the press build to the final location without waiting out real-time cooldowns.
        for (int round = 0; round < 15 && (profile.Level < 4 || profile.Bones < profile.GetUnlockCost(DogLocation.Terrace)); round++)
        {
            for (int second = 0; second < 33; second++) profile.Tick(2);
            foreach (DogCareAction action in Enum.GetValues(typeof(DogCareAction))) profile.Complete(action);
        }
        Call("Buy", DogLocation.Terrace);
        Require(profile.CurrentLocation == DogLocation.Terrace && profile.IsUnlocked(DogLocation.Terrace), "Terrace purchase/navigation");
        yield return new WaitForSeconds(0.4f);
        Capture("03-terrace", camera);
        dog.ShowKeypoints = true;
        Require(dog.ShowKeypoints, "Skeletal overlay");
    }

    private static void Capture(string name, Camera camera)
    {
        string output = Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_CAPTURES");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var target = new RenderTexture(1280, 800, 24);
        var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void Require(bool condition, string context)
    {
        if (!condition) throw new InvalidOperationException(context);
    }

    private static void Finish(Exception error)
    {
        SessionState.SetBool(Key, false);
        if (error == null)
            Debug.Log("CITY_DOG_OK: daily gift, feed/water/play, chapter, park, walk, cleaning, sequence, save reload, terrace and keypoints.");
        else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
