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
public static class Dog1ControllerValidation
{
    private const string Armed = "CityDogControllerValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool started;
    private static double deadline;

    static Dog1ControllerValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string saves = Path.Combine(Path.GetTempPath(), "CityDogController-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES", saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        SessionState.SetBool(Armed, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 130;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Controller regression exceeded 130 seconds"));
            return;
        }
        if (started) return;
        var training = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (training == null || training.dog == null || !training.dog.Ready) return;
        started = true;
        training.StartCoroutine(Guard(Check(training)));
    }

    private static IEnumerator Check(Dog1Training training)
    {
        string origin = Environment.GetEnvironmentVariable("CITYDOG_AGENT_FIXTURE_ORIGIN");
        Require(Uri.TryCreate(origin, UriKind.Absolute, out var fixture) && fixture.Scheme == "http" &&
            fixture.Host == "127.0.0.1" && string.IsNullOrEmpty(fixture.UserInfo),
            "Controller regression requires a credential-free loopback fixture");
        Call(training, "BeginPlay");
        Set(training, "useModel", true);
        Set(training, "sessionKey", "");
        Set(training, "apiKey", "");
        Set(training, "nextAutonomy", float.MaxValue);
        Set(training, "nextBowlCheck", float.MaxValue);
        training.model = "citydog-controller-fixture";
        var dog = training.dog;
        dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Private).SetValue(dog, true);
        dog.HasPlayClearance = (point, radius) => true;
        Need(training, "satiety", 80);
        Need(training, "hydration", 80);
        Need(training, "energy", 80);
        yield return new WaitForSeconds(4f);

        training.endpoint = new Uri(fixture, "controller-rest").AbsoluteUri;
        yield return Voice(training, "休息会儿吧");
        Require(Trace(training).Contains("rest · 完成") && Trace(training).Contains("我就在这里歇一会儿"),
            "Fuzzy rest did not execute rest while retaining the model response");
        Debug.Log("DOG_CONTROLLER: fuzzy rest executed with independent model reply");

        training.endpoint = new Uri(fixture, "controller-fuzzy").AbsoluteUri;
        yield return Voice(training, "这里闻起来有什么不一样吗");
        Require(Trace(training).Contains("air_sniff · 完成") && Trace(training).Contains("我抬起鼻子闻一闻"),
            "Fuzzy expression did not execute an eligible micro action. " + Trace(training));
        Debug.Log("DOG_CONTROLLER: fuzzy expression selected a catalogued micro action");

        training.endpoint = new Uri(fixture, "controller-mixed").AbsoluteUri;
        yield return Voice(training, "先闻闻，再趴下来歇一下");
        string trace = Trace(training);
        int sniff = trace.IndexOf("air_sniff · 开始", StringComparison.Ordinal);
        int rest = trace.IndexOf("rest · 开始", StringComparison.Ordinal);
        Require(sniff >= 0 && rest > sniff && trace.Contains("rest · 完成"),
            "Mixed micro/full plan did not preserve execution order");
        Debug.Log("DOG_CONTROLLER: mixed micro/full actions executed in order");

        training.endpoint = new Uri(fixture, "controller-invalid").AbsoluteUri;
        yield return Voice(training, "用一个新办法看看我");
        trace = Trace(training);
        Require(trace.Contains("我听见你了") && !trace.Contains("执行 · teleport") &&
            !trace.Contains("执行 · sit · 开始"), "Invalid plan lost the reply or partly executed");
        Debug.Log("DOG_CONTROLLER: invalid plan retained reply and executed no partial actions");

        Need(training, "satiety", 0);
        training.endpoint = new Uri(fixture, "controller-hungry").AbsoluteUri;
        yield return Voice(training, "再追一次球吧");
        Require(Trace(training).Contains("我肚子饿了，吃点东西再玩") &&
            !Trace(training).Contains("fetch_toy · 开始"), "Hungry state did not prevent the effortful plan");
        Debug.Log("DOG_CONTROLLER: hunger reached model catalog and effortful motion stayed blocked");

        Need(training, "satiety", 80);
        Need(training, "energy", 0);
        training.endpoint = new Uri(fixture, "controller-tired").AbsoluteUri;
        yield return Voice(training, "今天还能跑一会儿吗");
        Require(Trace(training).Contains("我累了，想先歇一下") &&
            !Trace(training).Contains("burst · 开始"), "Fatigued state did not block running");
        Debug.Log("DOG_CONTROLLER: tired state reached model while gentle rest stayed available");

        Need(training, "energy", 80);
        training.endpoint = new Uri(fixture, "controller-unsupported").AbsoluteUri;
        yield return Voice(training, "把碗拿过来");
        Require(!Trace(training).Contains("come · 开始") && Trace(training).Contains("碗"),
            "Unsupported bowl instruction incorrectly became come");
        training.endpoint = new Uri(fixture, "unavailable").AbsoluteUri;
        yield return Voice(training, "你看到地上好像有个玩具都不掉过来");
        Require(!Trace(training).Contains("come · 开始") && !Trace(training).Contains("fetch_toy · 开始"),
            "Model failure fell back to matching a keyword in free speech");
        Debug.Log("DOG_CONTROLLER: unsupported objects and unavailable model caused no keyword action");

        bool obstacle = false;
        dog.HasPlayClearance = (point, radius) => !obstacle;
        training.endpoint = new Uri(fixture, "controller-stale").AbsoluteUri;
        yield return Voice(training, "闻闻以后轻扑一下", () =>
        {
            if (dog.IsPlayingMicroAction) obstacle = true;
        });
        trace = Trace(training);
        Require(obstacle && trace.Contains("air_sniff · 完成") &&
            trace.Contains("pounce · 场景校验阻塞") && !trace.Contains("pounce · 开始"),
            "Changed scene state did not stop the later action");
        Debug.Log("DOG_CONTROLLER: latest scene state prevented a later stale plan step");
        dog.HasPlayClearance = (point, radius) => true;
    }

    private static IEnumerator Voice(Dog1Training training, string words, Action sample = null)
    {
        Call(training, "SubmitVoiceTranscript", "麦粒，" + words);
        float until = Time.realtimeSinceStartup + 25f;
        do
        {
            sample?.Invoke();
            yield return null;
        } while ((Get<bool>(training, "busy") || (bool)typeof(Dog1Training).GetProperty("HasPendingCommands", Private).GetValue(training)) &&
            Time.realtimeSinceStartup < until);
        Require(!Get<bool>(training, "busy"), "Controller did not release after " + words);
        Set(training, "nextAutonomy", float.MaxValue);
        yield return null;
    }

    private static string Trace(Dog1Training training)
    {
        string trace = (string)typeof(Dog1Training).GetProperty("DeepSeekTraceText", Private).GetValue(training);
        int separator = trace.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
        return separator < 0 ? trace : trace.Substring(0, separator);
    }

    private static void Need(Dog1Training training, string field, float value)
    {
        var life = Get<DogLifeProgress>(training, "life");
        object data = typeof(DogLifeProgress).GetField("data", Private).GetValue(life);
        data.GetType().GetField(field).SetValue(data, value);
    }

    private static IEnumerator Guard(IEnumerator test)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(test);
        while (stack.Count > 0)
        {
            IEnumerator stage = stack.Peek();
            bool next = false;
            object current = null;
            Exception failure = null;
            try { next = stage.MoveNext(); if (next) current = stage.Current; }
            catch (Exception caught) { failure = caught; }
            if (failure != null) { Finish(failure); yield break; }
            if (!next) { stack.Pop(); continue; }
            if (current is IEnumerator child) stack.Push(child);
            else yield return current;
        }
        Finish(null);
    }

    private static object Call(Dog1Training training, string name, params object[] arguments) =>
        typeof(Dog1Training).GetMethod(name, Private).Invoke(training, arguments);
    private static T Get<T>(Dog1Training training, string field) =>
        (T)typeof(Dog1Training).GetField(field, Private).GetValue(training);
    private static void Set(Dog1Training training, string field, object value) =>
        typeof(Dog1Training).GetField(field, Private).SetValue(training, value);
    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
    private static void Finish(Exception failure)
    {
        SessionState.SetBool(Armed, false);
        if (failure == null) Debug.Log("DOG_CONTROLLER_OK");
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
