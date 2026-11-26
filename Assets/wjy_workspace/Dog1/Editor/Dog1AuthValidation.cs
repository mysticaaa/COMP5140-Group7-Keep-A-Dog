#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using KeepADog.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog1AuthValidation
{
    private const string Armed = "CityDogAuthValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string State = "{\"actionCatalog\":[{\"id\":\"rest\",\"target\":\"none\",\"available\":true}]}";
    private static bool started;
    private static double deadline;
    private static int assertions;

    static Dog1AuthValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string saves = Path.Combine(Path.GetTempPath(), "CityDogAuth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES", saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        Environment.SetEnvironmentVariable("KEEP_A_DOG_API_KEY", null);
        SessionState.SetBool(Armed, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 75;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Auth regression exceeded 75s")); return; }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        started = true;
        trainer.StartCoroutine(Guard(Check(trainer)));
    }

    private static IEnumerator Check(Dog1Training trainer)
    {
        string origin = Environment.GetEnvironmentVariable("CITYDOG_AGENT_FIXTURE_ORIGIN");
        Require(Uri.TryCreate(origin, UriKind.Absolute, out var fixture) && fixture.Scheme == "http" &&
            fixture.Host == "127.0.0.1" && string.IsNullOrEmpty(fixture.UserInfo),
            "Auth regression requires its credential-free loopback fixture");

        var dog = new DogAgent(Path.Combine(Path.GetTempPath(), "AuthAgent-" + Guid.NewGuid().ToString("N") + ".json"));
        string missing = null;
        var absent = dog.DecideAsync("你在干什么呢", "https://api.deepseek.com/chat/completions", "deepseek-flash",
            stateJson: State, captureModelError: error => missing = error);
        Require(absent.IsCompleted, "Missing official key made a network request instead of failing synchronously");
        var absentDecision = absent.GetAwaiter().GetResult();
        NoMotion(absentDecision, "missing official key");
        Require(!string.IsNullOrWhiteSpace(missing) && absentDecision.Reply.Contains("Key") &&
            !absentDecision.Reply.Contains("没听清"), "Missing key was reported as misunderstood speech");
        Debug.Log("DOG_AUTH: missing official key stopped before HTTP with an explicit configuration message");

        foreach (int status in new[] { 401, 403, 429, 503 })
        {
            string error = null;
            Task<DogAgentDecision> request = dog.DecideAsync("把碗拿过来", new Uri(fixture, "http-" + status).AbsoluteUri,
                "auth-fixture", stateJson: State, captureModelError: value => error = value);
            while (!request.IsCompleted) yield return null;
            DogAgentDecision failed = request.GetAwaiter().GetResult();
            NoMotion(failed, "HTTP " + status);
            Require(!string.IsNullOrWhiteSpace(error) && failed.Reply.Contains(status.ToString()) &&
                failed.ValidationError.Contains(status.ToString()) && !failed.Reply.Contains("没听清") &&
                !failed.Reply.Contains("MUST_NOT_LEAK_RESPONSE_BODY"), "HTTP failure lost its safe specific message: " + status);
            Debug.Log("DOG_AUTH: HTTP " + status + " preserved its explicit failure reason without motion or body disclosure");
        }

        string timeout = null;
        var delayed = dog.DecideAsync("过来", new Uri(fixture, "timeout").AbsoluteUri, "auth-fixture",
            stateJson: State, captureModelError: error => timeout = error);
        while (!delayed.IsCompleted) yield return null;
        DogAgentDecision timedOut = delayed.GetAwaiter().GetResult();
        NoMotion(timedOut, "timeout");
        Require(!string.IsNullOrWhiteSpace(timeout) && timedOut.Reply.Contains("超时") &&
            !timedOut.Reply.Contains("没听清"), "Timeout did not reach the natural reply as a network error");
        Debug.Log("DOG_AUTH: timeout kept the action array empty and reported a request timeout");

        var healthy = dog.DecideAsync("你在干什么呢", new Uri(fixture, "chat").AbsoluteUri, "auth-fixture", stateJson: State);
        while (!healthy.IsCompleted) yield return null;
        var chat = healthy.GetAwaiter().GetResult();
        Require(chat.UsedModel && chat.Actions?.Length == 0 && chat.Reply == "我在这里陪着你。" &&
            string.IsNullOrEmpty(chat.ValidationError), "Successful reply retained a stale authentication error");
        var actionRequest = dog.DecideAsync("休息会儿吧", new Uri(fixture, "act").AbsoluteUri, "auth-fixture", stateJson: State);
        while (!actionRequest.IsCompleted) yield return null;
        var action = actionRequest.GetAwaiter().GetResult();
        Require(action.UsedModel && action.Actions?.Length == 1 && action.Actions[0].id == "rest" &&
            action.ModelReply == "好，我就在这里休息。", "Auth changes broke a valid canonical action reply");
        Debug.Log("DOG_AUTH: healthy chat and canonical action stayed compatible after failures");

        Call(trainer, "BeginPlay");
        Set(trainer, "useModel", true);
        Set(trainer, "sessionKey", "");
        Set(trainer, "apiKey", "");
        Set(trainer, "nextAutonomy", float.MaxValue);
        Set(trainer, "nextBowlCheck", float.MaxValue);
        trainer.dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Private).SetValue(trainer.dog, true);
        trainer.model = "auth-fixture";
        trainer.endpoint = new Uri(fixture, "http-401").AbsoluteUri;
        Call(trainer, "SubmitVoiceTranscript", "你在干什么呢");
        float until = Time.realtimeSinceStartup + 12;
        while (Get<bool>(trainer, "busy") && Time.realtimeSinceStartup < until) yield return null;
        string reply = Get<string>(trainer, "reply");
        string trace = (string)typeof(Dog1Training).GetProperty("DeepSeekTraceText", Private).GetValue(trainer);
        Require(!Get<bool>(trainer, "busy") && reply.Contains("401") && !reply.Contains("没听清") &&
            trace.Contains("401") && !trace.Contains("come · 开始"), "HTTP 401 did not reach actual Dog1 speech UI and trace");
        Debug.Log("DOG_AUTH: actual Dog1 voice input surfaced HTTP 401 in feedback and debug trace");
    }

    private static void NoMotion(DogAgentDecision decision, string test)
    {
        Require(decision != null && !decision.UsedModel && !decision.IsRefusal && decision.Intent == DogIntent.Wait &&
            decision.Actions != null && decision.Actions.Length == 0, test + " initiated a keyword motion or learning refusal");
    }

    private static IEnumerator Guard(IEnumerator test)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(test);
        while (stack.Count > 0)
        {
            IEnumerator next = stack.Peek();
            bool available = false;
            object current = null;
            Exception failure = null;
            try { available = next.MoveNext(); if (available) current = next.Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) { Finish(failure); yield break; }
            if (!available) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
        Finish(null);
    }

    private static object Call(Dog1Training trainer, string name, params object[] args) =>
        typeof(Dog1Training).GetMethod(name, Private).Invoke(trainer, args);
    private static T Get<T>(Dog1Training trainer, string name) => (T)typeof(Dog1Training).GetField(name, Private).GetValue(trainer);
    private static void Set(Dog1Training trainer, string name, object value) => typeof(Dog1Training).GetField(name, Private).SetValue(trainer, value);
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Finish(Exception failure)
    {
        SessionState.SetBool(Armed, false);
        if (failure == null) Debug.Log("DOG_AUTH_OK assertions=" + assertions);
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
