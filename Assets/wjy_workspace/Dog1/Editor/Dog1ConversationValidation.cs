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
public static class Dog1ConversationValidation
{
    private const string RunKey = "CityDogConversationValidation";
    private const string Teach = "我心情不好的时候，给我叼来棒棒糖毛绒玩具";
    private const string Invite = "你想玩球还是安静一会儿？";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static double deadline;

    static Dog1ConversationValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string saves = Path.Combine(Path.GetTempPath(), "CityDogConversation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES", saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        SessionState.SetBool(RunKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(RunKey, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 160;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Dog1 conversation validation exceeded 160 seconds."));
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
        DogCompanion dog = training.dog;
        Call(training, "BeginPlay");
        Set(training, "useModel", false);
        Set(training, "sessionKey", "");
        Set(training, "apiKey", "");
        Set(training, "nextAutonomy", float.MaxValue);
        dog.SetWanderingEnabled(false);

        string saves = Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES");
        Require(!string.IsNullOrEmpty(saves), "Isolated saves were not configured");
        CheckMemoryFailure(Path.Combine(saves, "failed-fetch.json"));
        var knowledgeCheck = new DogAgent(Path.Combine(saves, "question.json")).Decide("你会坐下吗？");
        Require(knowledgeCheck.Intent == DogIntent.Wait && !knowledgeCheck.IsRefusal, "A question about ability triggered a training action");

        // Reproduce the neutral space used by the motion fixture to isolate voice, fetch and memory.
        var owner = new GameObject("Conversation test listener").transform;
        owner.position = new Vector3(1.1f, 1.65f, 3.1f);
        dog.user = owner;
        dog.PositionLimit = null;
        dog.HasPlayClearance = (point, radius) => true;
        dog.SetPracticeArea(Vector3.zero, new Vector3(1.5f, .11f, 1.4f), new Vector3(-1.5f, 0f, 1.6f));
        dog.transform.rotation = Quaternion.identity;
        dog.ResetCompanion();
        yield return new WaitForSeconds(.7f);

        yield return Practice(training, Teach);
        Require(dog.LastFetchSucceeded, "Recognized plush cue did not complete physical fetch");
        Require(Get<DogAgent>(training, "agent").CanReward, "Successful plush fetch did not open praise");
        yield return Practice(training, "真棒");
        string path = Path.Combine(saves, "dog-memory.json");
        var reopened = new DogAgent(path);
        DogAgentDecision recall = reopened.Decide("我今天心情不好");
        Require(recall.IsRecall && recall.Intent == DogIntent.Fetch && recall.Target == DogAgent.Lollipop,
            "Praised plush agreement did not survive reload");
        Set(training, "agent", new DogAgent(path));
        yield return Practice(training, "我今天心情不好");
        Require(dog.LastFetchSucceeded, "Reloaded plush agreement could not complete a second physical fetch");
        Debug.Log("DOG_CONVERSATION: explicit practice, physical plush retrieval, praise and save reload passed");

        dog.SetWanderingEnabled(false);
        yield return dog.PerformAction("invite", .2f);
        Require(dog.LastPlaySucceeded, "Play bow invitation did not finish");
        Call(training, "OpenTrickReward");
        Set(training, "pendingInvitation", true);
        var responses = new HashSet<string>();
        yield return Practice(training, Invite, () => responses.Add(dog.CurrentAction));
        bool responded = responses.Contains("fetch_toy") || responses.Contains("rest") || responses.Contains("restSpot");
        if (!responded && Environment.GetEnvironmentVariable("CITYDOG_AGENT_REQUIRE_INVITE") == "1")
            throw new InvalidOperationException("An invitation reply never played a grounded play or rest action.");
        Debug.Log(responded ? "DOG_CONVERSATION: invitation reply caused play or rest" :
            "DOG_CONVERSATION_LIMIT: invitation reply currently does not execute a grounded play or rest action");

        var arrivalLesson = Get<DogAgent>(training, "agent");
        arrivalLesson.Decide("过来");
        var arrivalDecision = arrivalLesson.Decide("过来");
        arrivalLesson.ReportExecution(arrivalDecision, true);
        Require(arrivalLesson.CanReward, "Successful come is eligible for praise");
        yield return (IEnumerator)Call(training, "OfferPlay");
        var playInvitation = typeof(Dog1Training).GetProperty("HasPlayInvitation", Private);
        Require((bool)playInvitation.GetValue(training) && arrivalLesson.CanReward,
            "The invitation appears without taking the come lesson's pending reward");
        Call(training, "ApplyFeedback", true);
        Require(!new DogAgent(path).Decide("过来").IsRefusal && (bool)playInvitation.GetValue(training),
            "The reward still trains come while the play invitation remains open");
        Debug.Log("DOG_CONVERSATION: play invitation and come reward coexist");

        string fixtureOrigin = Environment.GetEnvironmentVariable("CITYDOG_AGENT_FIXTURE_ORIGIN");
        if (string.IsNullOrWhiteSpace(fixtureOrigin))
        {
            Debug.Log("DOG_CONVERSATION_SKIPPED: no local model fixture configured");
            yield break;
        }
        Require(Uri.TryCreate(fixtureOrigin, UriKind.Absolute, out var fixture) && fixture.Scheme == "http" &&
            fixture.Host == "127.0.0.1" && string.IsNullOrEmpty(fixture.UserInfo),
            "Conversation QA accepts only a loopback HTTP fixture without credentials");
        Set(training, "useModel", true);
        training.model = "citydog-conversation-fixture";
        foreach (string mode in new[] { "allowed", "disallowed", "unavailable" })
        {
            training.endpoint = new Uri(fixture, mode).AbsoluteUri;
            bool sawResponse = false;
            string prefix = mode == "allowed" ? "场景选择 · " : "麦粒 · ";
            yield return Practice(training, "看看我", () => sawResponse |= dog.IsPlayingMicroAction &&
                Get<string>(training, "reply").StartsWith(prefix, StringComparison.Ordinal));
            Require(sawResponse, "No eligible motion or local fallback for fixture mode " + mode +
                ". Last reply: " + Get<string>(training, "reply"));
            Debug.Log("DOG_CONVERSATION: model scene motion passed for " + mode);
        }
        training.endpoint = new Uri(fixture, "invite").AbsoluteUri;
        Call(training, "OpenTrickReward");
        Set(training, "pendingInvitation", true);
        bool modelQuiet = false, resting = false;
        yield return Practice(training, Invite, () =>
        {
            modelQuiet |= Get<string>(training, "reply").Contains("我想和你安静待一会儿");
            resting |= dog.CurrentAction == "rest" || dog.CurrentAction == "restSpot";
        });
        Require(modelQuiet && resting, "Loopback invitation did not select and play the quiet branch");
        Debug.Log("DOG_CONVERSATION: invitation JSON selected a bounded quiet action and reply");

        yield return CheckCommandReceiver(training, fixture);
    }

    private static IEnumerator CheckCommandReceiver(Dog1Training training, Uri fixture)
    {
        DogCompanion dog = training.dog;
        var actions = new List<string>();
        void SampleAction()
        {
            if (!dog.IsPerforming) return;
            string action = dog.CurrentAction;
            if (actions.Count == 0 || actions[actions.Count - 1] != action)
                actions.Add(action);
        }

        // Learn the first cue locally so the model response is not hidden by the game's first-cue refusal.
        var agent = Get<DogAgent>(training, "agent");
        agent.Decide("坐下");
        bool modelSelectedSit = false;
        void SampleLog(string condition, string stackTrace, LogType type)
        {
            modelSelectedSit |= type == LogType.Log && condition == "[Dog1 Agent] 模型返回动作：sit";
        }
        Application.logMessageReceived += SampleLog;
        try
        {
            training.endpoint = new Uri(fixture, "command-single").AbsoluteUri;
            yield return Voice(training, "坐下", SampleAction);
        }
        finally { Application.logMessageReceived -= SampleLog; }
        Require(modelSelectedSit && string.Join(",", actions) == "sit",
            "The model did not select and execute the locally recognizable sit cue");
        string trace = LatestCommandTrace(training);
        Require(trace.Contains("语音原文 · 麦粒，坐下") && trace.Contains("输入 · 坐下") &&
            trace.Contains("决策来源 · 模型 · citydog-conversation-fixture") &&
            trace.Contains("请求状态 · {") && trace.Contains("模型返回 · {") &&
            trace.Contains("执行 · sit · 开始") && trace.Contains("执行 · sit · 完成"),
            "The command trace did not connect the voice transcript, model state, response and completed action");
        Debug.Log("DOG_CONVERSATION: ordinary cue used model JSON and executed sit");

        actions.Clear();
        training.endpoint = new Uri(fixture, "command-sequence").AbsoluteUri;
        yield return Voice(training, "按我的提示做一组动作", SampleAction);
        Require(string.Join(",", actions) == "sit,paw,bow",
            "Model sequence did not execute in returned order: " + string.Join(",", actions));
        Require(Get<string>(training, "reply") == "好，我按顺序试试。",
            "Completed model sequence discarded the independent model reply");
        trace = LatestCommandTrace(training);
        int sit = trace.IndexOf("执行 · sit · 开始", StringComparison.Ordinal);
        int paw = trace.IndexOf("执行 · paw · 开始", StringComparison.Ordinal);
        int bow = trace.IndexOf("执行 · bow · 开始", StringComparison.Ordinal);
        Require(sit >= 0 && paw > sit && bow > paw && trace.Contains("执行 · bow · 完成"),
            "The command trace did not show the three actions in the order executed");
        Debug.Log("DOG_CONVERSATION: model JSON sequence executed sit, paw, bow in order");

        actions.Clear();
        training.endpoint = new Uri(fixture, "command-invalid").AbsoluteUri;
        yield return Voice(training, "按我的提示做一组新动作", SampleAction);
        Require(actions.Count == 0, "Invalid sequence partly executed: " + string.Join(",", actions));
        trace = LatestCommandTrace(training);
        Require(trace.Contains("决策来源 · 模型 · citydog-conversation-fixture") && trace.Contains("模型返回 · {") &&
            trace.Contains("teleport") && trace.Contains("我听见你了。") &&
            !trace.Contains("sit · 开始") && !trace.Contains("teleport · 开始"),
            "The command trace did not preserve the model's safe reply while rejecting the plan");
        Debug.Log("DOG_CONVERSATION: unknown motion ID rejected before any sequence step while preserving reply");

        actions.Clear();
        dog.user.position = dog.transform.position + new Vector3(3f, 1.65f, 3f);
        bool obstacleAppeared = false;
        dog.HasPlayClearance = (point, radius) => !obstacleAppeared;
        try
        {
            training.endpoint = new Uri(fixture, "command-state-change").AbsoluteUri;
            yield return Voice(training, "换一组动作演示一下", () =>
            {
                SampleAction();
                obstacleAppeared |= dog.IsPerforming && dog.CurrentAction == "sit";
            });
            Require(obstacleAppeared && string.Join(",", actions) == "sit" &&
                Get<string>(training, "reply").Contains("空间") && Get<string>(training, "reply").Contains("轻扑"),
                "Sequence did not stop when clearance changed before pounce: " + string.Join(",", actions) +
                ". " + LatestCommandTrace(training));
            trace = LatestCommandTrace(training);
            Require(trace.Contains("执行 · sit · 完成") && trace.Contains("执行 · pounce · 场景校验阻塞") &&
                !trace.Contains("执行 · pounce · 开始"),
                "The command trace did not identify the step blocked during execution");
            Debug.Log("DOG_CONVERSATION: refreshed state stopped the sequence before the blocked step");

            actions.Clear();
            training.endpoint = new Uri(fixture, "command-blocked").AbsoluteUri;
            yield return Voice(training, "轻扑一下", SampleAction);
            Require(actions.Count == 0 && Get<string>(training, "reply") == "我前面有障碍，先等等。",
                "Blocked model JSON did not leave the dog waiting with its reply");
            trace = LatestCommandTrace(training);
            Require(trace.Contains("决策来源 · 模型 · citydog-conversation-fixture") &&
                trace.Contains("模型返回 · {") && trace.Contains("decline") &&
                trace.Contains("回应 · 我前面有障碍，先等等。") &&
                !trace.Contains("pounce · 开始") && !trace.Contains("refuse · "),
                "The command trace did not show the model's blocked reply without execution");
            Debug.Log("DOG_CONVERSATION: blocked JSON returned a reply without motion");
            Debug.Log("DOG_CONVERSATION: trace verified voice, model state and raw response, fallback, execution order and blocked results");
        }
        finally { dog.HasPlayClearance = (point, radius) => true; }
    }

    private static void CheckMemoryFailure(string path)
    {
        var dog = new DogAgent(path);
        var decision = dog.Decide(Teach);
        Require(decision.Intent == DogIntent.Fetch && decision.Target == DogAgent.Lollipop,
            "Plush cue no longer recognizes the saved toy ID");
        dog.ReportExecution(decision, false);
        dog.Feedback(true);
        Require(!new DogAgent(path).Decide("我今天心情不好").IsRecall,
            "Failed fetch was saved by praise");
    }

    private static IEnumerator Practice(Dog1Training training, string words, Action sample = null)
    {
        Require((bool)Call(training, "SubmitPracticeCue", words), "Explicit practice cue was not accepted");
        Require(Get<string>(training, "caption").Contains(words), "Practice cue did not reach Dog1 training");
        float until = Time.realtimeSinceStartup + 27f;
        while (Get<bool>(training, "busy") && Time.realtimeSinceStartup < until)
        {
            sample?.Invoke();
            yield return null;
        }
        Require(!Get<bool>(training, "busy"), "Practice did not release the training gate");
        Set(training, "nextAutonomy", float.MaxValue);
        yield return null;
    }
    private static IEnumerator Voice(Dog1Training training, string words, Action sample = null)
    {
        Call(training, "SubmitVoiceTranscript", "麦粒，" + words);
        Require(Get<string>(training, "caption").Contains(words), "Voice transcript did not reach Dog1 training");
        float until = Time.realtimeSinceStartup + 27f;
        while (Get<bool>(training, "busy") && Time.realtimeSinceStartup < until)
        {
            sample?.Invoke();
            yield return null;
        }
        Require(!Get<bool>(training, "busy"), "Conversation did not release the training gate");
        Set(training, "nextAutonomy", float.MaxValue);
        yield return null;
    }

    private static string LatestCommandTrace(Dog1Training training)
    {
        string text = (string)typeof(Dog1Training).GetProperty("DeepSeekTraceText", Private).GetValue(training);
        int separator = text.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
        return separator < 0 ? text : text.Substring(0, separator);
    }

    private static IEnumerator Guard(IEnumerator test)
    {
        var stages = new Stack<IEnumerator>();
        stages.Push(test);
        while (stages.Count > 0)
        {
            IEnumerator stage = stages.Peek();
            bool more = false;
            object current = null;
            Exception failure = null;
            try { more = stage.MoveNext(); if (more) current = stage.Current; }
            catch (Exception caught) { failure = caught; }
            if (failure != null) { Finish(failure); yield break; }
            if (!more) { stages.Pop(); continue; }
            if (current is IEnumerator child) stages.Push(child);
            else yield return current;
        }
        Finish(null);
    }

    private static object Call(Dog1Training training, string method, params object[] args) =>
        typeof(Dog1Training).GetMethod(method, Private).Invoke(training, args);

    private static T Get<T>(Dog1Training training, string field) =>
        (T)typeof(Dog1Training).GetField(field, Private).GetValue(training);

    private static void Set(Dog1Training training, string field, object value) =>
        typeof(Dog1Training).GetField(field, Private).SetValue(training, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(Exception failure)
    {
        SessionState.SetBool(RunKey, false);
        if (failure == null) Debug.Log("DOG_CONVERSATION_OK");
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
