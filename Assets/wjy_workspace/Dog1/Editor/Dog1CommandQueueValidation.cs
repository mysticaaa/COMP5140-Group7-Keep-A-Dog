#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KeepADog.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog1CommandQueueValidation
{
    private const string Armed = "CityDogCommandQueueValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool started;
    private static double deadline;

    static Dog1CommandQueueValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string saves = Path.Combine(Path.GetTempPath(), "CityDogQueue-" + Guid.NewGuid().ToString("N"));
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
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 60;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Queue probe timed out")); return; }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        started = true;
        trainer.StartCoroutine(Guard(Check(trainer)));
    }

    private static IEnumerator Check(Dog1Training trainer)
    {
        Call(trainer, "BeginPlay");
        trainer.enabled = false;
        Set(trainer, "useModel", false);
        trainer.dog.SetWanderingEnabled(false);
        Set(trainer, "busy", true);
        IList queue = Get<IList>(trainer, "pendingCommands");
        object Trace(string input) => Call(trainer, "AddCommandTrace", input, null, false);
        object first = Trace("最早的一句");
        object second = Trace("第二句");
        object third = Trace("第三句");
        object fourth = Trace("最新的一句");
        foreach (object trace in new[] { first, second, third, fourth }) Call(trainer, "EnqueueCommand", trace);
        Require(queue.Count == 3, "Queue did not enforce three waiting slots");
        Require((string)first.GetType().GetField("Result").GetValue(first) != "", "Evicted command had no result");
        object newest = queue[queue.Count - 1];
        float expires = (float)newest.GetType().GetField("ExpiresAt").GetValue(newest);
        Require(expires > Time.realtimeSinceStartup + 4.8f && expires <= Time.realtimeSinceStartup + 5.1f,
            "Waiting command does not expire after five realtime seconds");
        Require(!(bool)Call(trainer, "TryRunPendingCommand") && queue.Count == 3,
            "Queued intent interrupted an already active action");

        Set(trainer, "busy", false);
        foreach (string expected in new[] { "最新的一句", "第三句", "第二句" })
        {
            Require((bool)Call(trainer, "TryRunPendingCommand"), "Available queue item did not start");
            Require(Get<string>(trainer, "caption") == "你 · " + expected, "Queue did not use last-in-first-out order");
            float until = Time.realtimeSinceStartup + 2;
            while (Get<bool>(trainer, "busy") && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
        }
        Require(queue.Count == 0, "Dequeued item was retained for a duplicate execution");

        var rest = new DogAgentAction { id = "rest", target = "none" };
        Set(trainer, "busy", true);
        var currentPlan = new[] { rest, rest, rest };
        Set(trainer, "queuedPlan", currentPlan);
        Set(trainer, "nextQueuedAction", 1);
        Set(trainer, "queuedPlanExpiresAt", Time.realtimeSinceStartup + 5f);
        object displaced = Trace("共享槽里较早的口令");
        object retained = Trace("共享槽里最新的口令");
        Call(trainer, "EnqueueCommand", displaced);
        Call(trainer, "EnqueueCommand", retained);
        Require(queue.Count == 1 && ReferenceEquals(queue[0].GetType().GetField("Trace").GetValue(queue[0]), retained),
            "Two remaining plan actions did not reserve two of the global three waiting slots");
        Require(((string)displaced.GetType().GetField("Result").GetValue(displaced)).Contains("取消") &&
            ReferenceEquals(Get<DogAgentAction[]>(trainer, "queuedPlan"), currentPlan) && Get<bool>(trainer, "busy"),
            "Shared-slot admission changed the active plan or failed to mark the evicted command");
        Call(trainer, "ClearPendingCommands");
        Set(trainer, "queuedPlan", null);
        Set(trainer, "nextQueuedAction", 0);
        Set(trainer, "busy", false);

        Call(trainer, "EnqueueCommand", Trace("应该过期的一句"));
        foreach (object pending in queue)
            pending.GetType().GetField("ExpiresAt").SetValue(pending, Time.realtimeSinceStartup - 0.01f);
        Call(trainer, "ExpirePendingCommands");
        Require(queue.Count == 0 && !(bool)Call(trainer, "TryRunPendingCommand"), "Expired intent was executed");

        object expired = Trace("过期后不能重新续期的口令");
        expired.GetType().GetField("ExpiresAt").SetValue(expired, Time.realtimeSinceStartup - .01f);
        Call(trainer, "EnqueueCommand", expired);
        Require(queue.Count == 0 && ((string)expired.GetType().GetField("Result").GetValue(expired)).Contains("取消"),
            "An expired command was given a new five-second deadline on re-enqueue");

        Set(trainer, "busy", true);
        Call(trainer, "EnqueueCommand", Trace("要取消的一句"));
        Call(trainer, "ClearPendingCommands");
        Require(queue.Count == 0 && Get<bool>(trainer, "busy"), "Clearing waiting commands altered the active action gate");
        Require((bool)Call(trainer, "SubmitPracticeCue", "坐下") && queue.Count == 1 &&
            Get<bool>(trainer, "busy"), "An explicit practice cue was lost or interrupted the active action");
        Call(trainer, "ClearPendingCommands");
        Set(trainer, "busy", false);
        var decision = new DogAgent(Path.Combine(Path.GetTempPath(), "QueuePlan-" + Guid.NewGuid().ToString("N") + ".json")).Decide("hello");
        var action = new DogAgentAction { id = "rest", target = "none" };
        typeof(DogAgentDecision).GetProperty("Actions").SetValue(decision, new[] { action, action, action, action });
        object guardTrace = Trace("超出三个动作");
        Set(trainer, "activeCommandTrace", guardTrace);
        yield return (IEnumerator)Call(trainer, "ExecuteControllerPlan", decision);
        var actionLines = (List<string>)guardTrace.GetType().GetField("Actions").GetValue(guardTrace);
        Require(actionLines.Exists(line => line.Contains("最多暂存三个动作")) &&
            !actionLines.Exists(line => line.Contains(" · 开始")), "Direct overlong plan bypassed receiver cap");
        typeof(DogAgentDecision).GetProperty("Actions").SetValue(decision, new[] { action });
        guardTrace = Trace("已经过期的动作");
        Set(trainer, "activeCommandTrace", guardTrace);
        Set(trainer, "queuedPlanExpiresAt", Time.realtimeSinceStartup);
        yield return (IEnumerator)Call(trainer, "RunControllerPlan", decision);
        actionLines = (List<string>)guardTrace.GetType().GetField("Actions").GetValue(guardTrace);
        Require(actionLines.Exists(line => line.Contains("取消剩余动作")) &&
            !actionLines.Exists(line => line.Contains(" · 开始")), "Expired plan still started a physical action");
        Set(trainer, "activeCommandTrace", null);

        string origin = Environment.GetEnvironmentVariable("CITYDOG_AGENT_FIXTURE_ORIGIN");
        Require(Uri.TryCreate(origin, UriKind.Absolute, out var fixture) && fixture.Scheme == "http" &&
            fixture.Host == "127.0.0.1" && string.IsNullOrEmpty(fixture.UserInfo),
            "Queue regression requires a credential-free loopback fixture");
        Set(trainer, "useModel", true);
        Set(trainer, "sessionKey", "");
        Set(trainer, "apiKey", "");
        trainer.model = "citydog-controller-fixture";
        trainer.endpoint = new Uri(fixture, "controller-queue-delayed").AbsoluteUri;
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Private).SetValue(trainer.dog, true);
        float settle = Time.realtimeSinceStartup + 8f;
        while (!trainer.dog.CanOfferMicroAction && Time.realtimeSinceStartup < settle) yield return null;
        Require(trainer.dog.CanOfferMicroAction, "Dog did not settle before delayed queue deadline probe");
        object nearExpiry = Trace("马上到期的休息请求");
        Call(trainer, "EnqueueCommand", nearExpiry);
        object delayedPending = queue[0];
        float originalDeadline = Time.realtimeSinceStartup + .3f;
        delayedPending.GetType().GetField("ExpiresAt").SetValue(delayedPending, originalDeadline);
        Require((bool)Call(trainer, "TryRunPendingCommand"), "Near-expiry queue command was not admitted before deadline");
        float untilResponse = Time.realtimeSinceStartup + 8f;
        while (Get<bool>(trainer, "busy") && Time.realtimeSinceStartup < untilResponse) yield return null;
        Require(!Get<bool>(trainer, "busy"), "Delayed model queue request did not finish");
        while (nearExpiry.GetType().GetField("Json").GetValue(nearExpiry) == null &&
            Time.realtimeSinceStartup < untilResponse) yield return null;
        actionLines = (List<string>)nearExpiry.GetType().GetField("Actions").GetValue(nearExpiry);
        Require((bool)nearExpiry.GetType().GetField("ModelRequested").GetValue(nearExpiry) &&
            ((string)nearExpiry.GetType().GetField("Json").GetValue(nearExpiry) ?? "").Contains("收到延迟"),
            "Delayed queue probe did not receive its model response");
        Require(!actionLines.Exists(line => line.Contains(" · 开始")) &&
            ((string)nearExpiry.GetType().GetField("Result").GetValue(nearExpiry)).Contains("取消"),
            "A model response renewed the original queued command deadline and started an expired action");
        Require((float)nearExpiry.GetType().GetField("ExpiresAt").GetValue(nearExpiry) <= originalDeadline,
            "Queued command's original deadline was extended across the model request");

        Require(trainer.dog.TryPlayMicroAction("air_sniff"), "Could not prepare a pose transition for queue expiry probe");
        object poseExpiry = Trace("等待姿势过渡时到期的请求");
        Call(trainer, "EnqueueCommand", poseExpiry);
        object posePending = queue[0];
        float poseDeadline = Time.realtimeSinceStartup + .1f;
        posePending.GetType().GetField("ExpiresAt").SetValue(posePending, poseDeadline);
        Require((bool)Call(trainer, "TryRunPendingCommand"), "Pose-transition request did not enter the controller");
        untilResponse = Time.realtimeSinceStartup + 3f;
        while (Get<bool>(trainer, "busy") && Time.realtimeSinceStartup < untilResponse) yield return null;
        actionLines = (List<string>)poseExpiry.GetType().GetField("Actions").GetValue(poseExpiry);
        Require(!Get<bool>(trainer, "busy") && !actionLines.Exists(line => line.Contains(" · 开始")) &&
            !(bool)poseExpiry.GetType().GetField("ModelRequested").GetValue(poseExpiry) &&
            ((string)poseExpiry.GetType().GetField("Result").GetValue(poseExpiry)).Contains("取消"),
            "Pose settling renewed an expired queue deadline or sent an expired request to the model");
        Debug.Log("DOG_COMMAND_QUEUE_OK: shared three slots, original five-second deadline across model, LIFO, active action preserved, expiry and cancellation");
    }

    private static IEnumerator Guard(IEnumerator test)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(test);
        while (stack.Count > 0)
        {
            IEnumerator step = stack.Peek();
            bool next = false;
            object current = null;
            Exception failure = null;
            try { next = step.MoveNext(); if (next) current = step.Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) { Finish(failure); yield break; }
            if (!next) { stack.Pop(); continue; }
            if (current is IEnumerator child) stack.Push(child);
            else yield return current;
        }
        Finish(null);
    }

    private static object Call(Dog1Training trainer, string name, params object[] args) =>
        typeof(Dog1Training).GetMethod(name, Private).Invoke(trainer, args);
    private static T Get<T>(Dog1Training trainer, string name) =>
        (T)typeof(Dog1Training).GetField(name, Private).GetValue(trainer);
    private static void Set(Dog1Training trainer, string name, object value) =>
        typeof(Dog1Training).GetField(name, Private).SetValue(trainer, value);
    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
    private static void Finish(Exception failure)
    {
        SessionState.SetBool(Armed, false);
        if (failure != null) Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
