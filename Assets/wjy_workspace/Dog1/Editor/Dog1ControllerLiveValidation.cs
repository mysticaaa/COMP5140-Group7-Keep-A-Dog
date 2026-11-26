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
public static class Dog1ControllerLiveValidation
{
    private const string Armed = "CityDogControllerLiveValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static double deadline;

    [Serializable]
    private sealed class ControllerAction
    {
        public string id;
        public string target;
    }

    [Serializable]
    private sealed class ControllerReply
    {
        public string decision;
        public string reply;
        public ControllerAction[] actions;
    }

    static Dog1ControllerLiveValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KEEP_A_DOG_API_KEY")))
        {
            Finish(new InvalidOperationException("The live validation requires a session credential."));
            return;
        }
        string saves = Path.Combine(Path.GetTempPath(), "CityDogControllerLive-" + Guid.NewGuid().ToString("N"));
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
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 150;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Live controller validation exceeded 150 seconds."));
            return;
        }
        if (running) return;
        var training = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (training == null || training.dog == null || !training.dog.Ready) return;
        running = true;
        training.StartCoroutine(Guard(Check(training)));
    }

    private static IEnumerator Check(Dog1Training training)
    {
        Call(training, "BeginPlay");
        Set(training, "useModel", true);
        Set(training, "sessionKey", Environment.GetEnvironmentVariable("KEEP_A_DOG_API_KEY"));
        Set(training, "apiKey", "");
        Set(training, "nextAutonomy", float.MaxValue);
        Set(training, "nextBowlCheck", float.MaxValue);
        training.endpoint = "https://api.deepseek.com/chat/completions";
        training.model = "deepseek-flash";
        var dog = training.dog;
        dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Private).SetValue(dog, true);
        Need(training, "satiety", 80);
        Need(training, "hydration", 80);
        Need(training, "energy", 80);
        Need(training, "mood", 80);
        Need(training, "cleanliness", 80);
        yield return new WaitForSeconds(4f);

        Dog2SpeechInput speech = training.GetComponent<Dog2SpeechInput>();
        Require(speech != null, "Dog1 did not create the speech component.");
        byte[] wav = File.ReadAllBytes(Path.Combine(Application.dataPath, "wjy_workspace/Dog2/SpeechService/Tests~/sit.wav"));
        int deliveries = 0;
        speech.TranscriptReady += _ => deliveries++;
        typeof(Dog2SpeechInput).GetMethod("BeginTranscriptAttempt", Private).Invoke(speech, null);
        typeof(Dog2SpeechInput).GetProperty("IsFinalizing").GetSetMethod(true).Invoke(speech, new object[] { true });
        speech.WarmUp();
        int session = (int)typeof(Dog2SpeechInput).GetField("session", Private).GetValue(speech);
        yield return (IEnumerator)typeof(Dog2SpeechInput).GetMethod("Transcribe", Private).Invoke(speech, new object[] { wav, session });
        yield return WaitForCommand(training);
        Require(deliveries == 1 && speech.LiveText.Contains("坐下"), "Chinese WAV did not deliver one real transcript.");
        Require(ModelReply(training).actions?.Length == 1, "Real transcript did not reach the controller.");
        string[] journals = Directory.GetFiles(Path.Combine(Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES"),
            "VoiceTranscripts"), "*.jsonl");
        Require(journals.Length == 1 && File.ReadAllText(journals[0]).Contains("坐下"),
            "The real transcript was not saved before processing.");
        Debug.Log("DOG_CONTROLLER_LIVE: real Chinese WAV reached Whisper, journal and DeepSeek controller.");

        yield return Voice(training, "休息会儿吧，就在原地趴下来歇一会儿。");
        ControllerReply rest = ModelReply(training);
        Require(rest.decision == "act" && HasAction(rest, "rest") && LatestTrace(training).Contains("rest · 完成"),
            "The genuine fuzzy rest response did not execute a rest motion.");
        Require(!string.IsNullOrWhiteSpace(rest.reply), "The rest plan did not carry a natural response.");
        Debug.Log("DOG_CONTROLLER_LIVE: fuzzy rest returned a genuine plan, natural reply and completed rest.");

        yield return Voice(training, "把鼻子抬起来，闻闻空气里有什么气味。");
        ControllerReply sniff = ModelReply(training);
        Require(sniff.decision == "act" && HasAction(sniff, "air_sniff") &&
            LatestTrace(training).Contains("air_sniff · 完成"), "The genuine fuzzy sniff did not complete the available micro action.");
        Debug.Log("DOG_CONTROLLER_LIVE: fuzzy language selected and completed an available micro action.");

        yield return Voice(training, "先坐下，再抬起右前爪，最后伏低前身邀请玩耍。");
        ControllerReply sequence = ModelReply(training);
        Require(sequence.decision == "act" && sequence.actions?.Length == 3 &&
            sequence.actions[0].id == "sit" && sequence.actions[1].id == "paw" && sequence.actions[2].id == "bow",
            "The genuine provider did not return the requested three actions in order.");
        string trace = LatestTrace(training);
        int sit = trace.IndexOf("sit · 开始", StringComparison.Ordinal);
        int paw = trace.IndexOf("paw · 开始", StringComparison.Ordinal);
        int bow = trace.IndexOf("bow · 开始", StringComparison.Ordinal);
        Require(sit >= 0 && paw > sit && bow > paw && trace.Contains("bow · 完成"),
            "The genuine three-action response was not executed in order.");
        Debug.Log("DOG_CONTROLLER_LIVE: genuine three-step JSON plan completed in order.");

        yield return Voice(training, "把地上的碗拿过来。");
        ControllerReply bowl = ModelReply(training);
        Require((bowl.decision == "decline" || bowl.decision == "reply") && bowl.actions?.Length == 0 &&
            !LatestTrace(training).Contains("come · 开始"), "Unsupported bowl retrieval became a keyword approach action.");
        Debug.Log("DOG_CONTROLLER_LIVE: unsupported bowl retrieval produced no keyword-based approach.");

        Need(training, "satiety", 0);
        yield return Voice(training, "再跑过去把玩具球叼回来吧。");
        ControllerReply hungry = ModelReply(training);
        Require(hungry.decision == "decline" && hungry.actions?.Length == 0 &&
            !string.IsNullOrWhiteSpace(hungry.reply) && !LatestTrace(training).Contains("fetch_toy · 开始"),
            "The genuine provider did not decline an unavailable effortful command with a natural response.");
        Debug.Log("DOG_CONTROLLER_LIVE: hungry state produced a genuine natural decline without movement.");
    }

    private static IEnumerator Voice(Dog1Training training, string words)
    {
        Call(training, "SubmitVoiceTranscript", "麦粒，" + words);
        yield return WaitForCommand(training);
        Set(training, "nextAutonomy", float.MaxValue);
    }

    private static IEnumerator WaitForCommand(Dog1Training training)
    {
        float until = Time.realtimeSinceStartup + 32f;
        do { yield return null; }
        while ((Get<bool>(training, "busy") || Get<object>(training, "pendingVoice") != null) &&
            Time.realtimeSinceStartup < until);
        Require(!Get<bool>(training, "busy") && Get<object>(training, "pendingVoice") == null,
            "The live controller did not release the pending voice.");
        yield return null;
    }

    private static ControllerReply ModelReply(Dog1Training training)
    {
        var traces = (IList)typeof(Dog1Training).GetField("commandTraces", Private).GetValue(training);
        Require(traces.Count > 0, "No command trace was recorded.");
        object latest = traces[0];
        string source = (string)latest.GetType().GetField("Source").GetValue(latest);
        string raw = (string)latest.GetType().GetField("Json").GetValue(latest);
        Require(source.StartsWith("DeepSeek", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(raw),
            "The runtime selected a local result instead of the genuine provider response.");
        return JsonUtility.FromJson<ControllerReply>(raw);
    }

    private static string LatestTrace(Dog1Training training)
    {
        string trace = (string)typeof(Dog1Training).GetProperty("DeepSeekTraceText", Private).GetValue(training);
        int separator = trace.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
        return separator < 0 ? trace : trace.Substring(0, separator);
    }

    private static bool HasAction(ControllerReply reply, string id)
    {
        if (reply.actions == null) return false;
        foreach (var action in reply.actions)
            if (action.id == id) return true;
        return false;
    }

    private static void Need(Dog1Training training, string field, float value)
    {
        object life = Get<DogLifeProgress>(training, "life");
        object data = life.GetType().GetField("data", Private).GetValue(life);
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
        if (failure == null) Debug.Log("DOG_CONTROLLER_LIVE_OK");
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
