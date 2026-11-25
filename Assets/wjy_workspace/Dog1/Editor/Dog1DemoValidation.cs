#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using KeepADog.Demo;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog1DemoValidation
{
    private const string Scene = "Assets/Scenes/Dog1.unity";
    private const string SessionKey = "KeepADogDemoValidation";
    private static bool started;
    private static double deadline;

    static Dog1DemoValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        SessionState.SetBool(SessionKey, true);
        EditorSceneManager.OpenScene(Scene);
        EditorApplication.isPlaying = true;
    }

    // Keep existing build commands working while delivering exactly one player.
    public static void BuildWindows() => BuildDog1PcVr();
    public static void BuildPcVr() => BuildDog1PcVr();

    [MenuItem("WJY/Build Dog1 (VR and Desktop Preview)")]
    public static void BuildDog1PcVr()
    {
        Dog2XrSetup.Configure();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
        BuildPlayer();
    }

    private static void BuildPlayer()
    {
        const string directory = "Builds/CityDog1PCVR";
        string target = Path.Combine(directory, "CityDog1PCVR.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        var fullscreenMode = PlayerSettings.fullScreenMode;
        int width = PlayerSettings.defaultScreenWidth;
        int height = PlayerSettings.defaultScreenHeight;
        bool resizable = PlayerSettings.resizableWindow;
        BuildReport report;
        try
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.resizableWindow = true;
            report = BuildPipeline.BuildPlayer(new[] { Scene }, target, BuildTarget.StandaloneWindows64, BuildOptions.None);
        }
        finally
        {
            PlayerSettings.fullScreenMode = fullscreenMode;
            PlayerSettings.defaultScreenWidth = width;
            PlayerSettings.defaultScreenHeight = height;
            PlayerSettings.resizableWindow = resizable;
        }
        if (report.summary.result == BuildResult.Succeeded)
        {
            File.Copy("Assets/wjy_workspace/Dog1/ThirdParty/ATTRIBUTION.md",
                Path.Combine(Path.GetDirectoryName(target), "ATTRIBUTION.md"), true);
            string speech = Path.Combine(directory, "SpeechService");
            Directory.CreateDirectory(speech);
            foreach (var file in new[] { "server.py", "requirements.txt", "Setup.ps1", "Start.ps1" })
                File.Copy(Path.Combine("Assets/wjy_workspace/Dog2/SpeechService", file), Path.Combine(speech, file), true);
        }
        Debug.Log("KEEP_A_DOG_BUILD: " + report.summary.result + " " + report.summary.totalSize);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    public static void OpenDog1()
    {
        EditorSceneManager.OpenScene(Scene);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying) return;

        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 110;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Demo scene or action timed out."));
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
        string folder = Path.Combine(Path.GetTempPath(), "KeepADogValidation-" + Guid.NewGuid().ToString("N"));
        var run = PlayThrough(dog, Path.Combine(folder, "memory.json"));
        Exception failure = null;
        try
        {
            while (true)
            {
                object current = null;
                try
                {
                    if (!run.MoveNext()) break;
                    current = run.Current;
                }
                catch (Exception error)
                {
                    failure = error;
                    break;
                }
                yield return current;
            }
        }
        finally
        {
            (run as IDisposable)?.Dispose();
            File.Delete(Path.Combine(folder, "memory.json"));
            File.Delete(Path.Combine(folder, "memory.json.bak"));
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }

        Finish(failure);
    }

    private static IEnumerator PlayThrough(DogCompanion dog, string path)
    {
        var agent = new DogAgent(path);
        Require(agent.Decide("坐下").IsRefusal, "A fresh dog should hesitate on the first cue.");
        Require(!agent.CanReward, "Refusal cannot be rewarded.");
        yield return dog.PerformAction("refuse");
        var sit = agent.Decide("坐下");
        Require(sit.Intent == DogIntent.Sit && !agent.CanReward, "Repeat must initiate a pending sit trial.");

        dog.ShowKeypoints = true;
        Require(dog.ShowKeypoints && dog.GetComponent<DogKeypointOverlay>() != null, "Missing rig overlay.");
        Transform head = FindBone(dog, "DEF-head");
        Transform tail = FindBone(dog, "DEF-tail.01");
        Vector3 headSize = head.localScale;
        Vector3 tailSize = tail.localScale;
        yield return dog.PerformAction("sit");
        agent.ReportExecution(sit, true);
        Require(agent.CanReward, "An executed sit should be rewardable.");
        agent.Feedback(true);

        var fresh = new DogAgent(path);
        Require(fresh.Decide("坐下").Intent == DogIntent.Sit, "Sit proficiency must load in a new session.");
        Require(fresh.Decide("我心情不好").Intent == DogIntent.Comfort, "No fetch before teaching.");
        var teach = fresh.Decide(DogAgent.TeachExample);
        Require(teach.Intent == DogIntent.Fetch && teach.Target == DogAgent.Lollipop && !fresh.CanReward,
            "Teaching should begin a lollipop rehearsal.");
        fresh.ReportExecution(teach, false);
        fresh.Feedback(true);
        Require(new DogAgent(path).Decide("我心情不好").Intent == DogIntent.Comfort, "Failed fetch must remain unsaved.");
        teach = fresh.Decide(DogAgent.TeachExample);
        yield return dog.PerformAction("fetch_lollipop");
        Require(dog.LastFetchSucceeded, "Lollipop rehearsal failed: " + dog.StateLabel);
        fresh.ReportExecution(teach, dog.LastFetchSucceeded);
        fresh.Feedback(true);
        Require(string.IsNullOrEmpty(fresh.SaveError), "Memory save error.");

        var reopened = new DogAgent(path);
        Require(!reopened.ShortTermSummary.Contains(DogAgent.TeachExample), "Short-term history leaked across sessions.");
        var recalled = reopened.Decide("我今天心情不好");
        Require(recalled.IsRecall && recalled.Intent == DogIntent.Fetch && recalled.Target == DogAgent.Lollipop,
            "Condition rule did not survive reloading.");
        yield return dog.PerformAction("fetch_lollipop");
        Require(dog.LastFetchSucceeded, "Remembered lollipop action failed: " + dog.StateLabel);
        Require(head.localScale == headSize && tail.localScale == tailSize, "Bone scale changed while acting.");
        reopened.Reset();
        Require(new DogAgent(path).Decide("我心情不好").Intent == DogIntent.Comfort, "Reset did not clear persisted condition.");

        dog.ShowKeypoints = false;
        yield return dog.PerformAction("come");
        Require(dog.LastComeSucceeded, "Come action failed: " + dog.StateLabel);
    }

    private static Transform FindBone(DogCompanion dog, string name)
    {
        foreach (Transform joint in dog.VisualRoot.GetComponentsInChildren<Transform>())
            if (joint.name == name) return joint;
        throw new InvalidOperationException("Missing rig joint " + name);
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    private static void Finish(Exception error)
    {
        SessionState.SetBool(SessionKey, false);
        if (error == null) Debug.Log("KEEP_A_DOG_DEMO_OK: refusal, training feedback, persisted recall, two physical fetches, keypoints and bone scale, come, reset.");
        else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
