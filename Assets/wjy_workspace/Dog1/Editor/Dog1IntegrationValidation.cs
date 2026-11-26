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
public static class Dog1IntegrationValidation
{
    private const string Armed = "CityDogMainIntegrationValidation";
    private const string Mode = "CityDogMainIntegrationValidationMode";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool running;
    private static int assertions;
    private static double deadline;
    private static Uri fixture;

    static Dog1IntegrationValidation() => EditorApplication.update += Tick;

    public static void Run() => Arm("all");
    public static void RunController() => Arm("controller");
    public static void RunTraining() => Arm("training");
    public static void RunTrainingGuards() => Arm("training-guards");
    public static void RunDelayedPraise() => Arm("delayed-praise");
    public static void RunBowls() => Arm("bowls");

    private static void Arm(string mode)
    {
        string saves = Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES");
        if (string.IsNullOrWhiteSpace(saves))
            saves = Path.Combine(Path.GetTempPath(), "CityDogIntegration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES", saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        Environment.SetEnvironmentVariable("KEEP_A_DOG_API_KEY", null);
        SessionState.SetBool(Armed, true);
        SessionState.SetString(Mode, mode);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 240;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Integration exceeded 240 seconds")); return; }
        if (running) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        running = true;
        trainer.StartCoroutine(Guard(Check(trainer)));
    }

    private static IEnumerator Check(Dog1Training trainer)
    {
        string origin = Environment.GetEnvironmentVariable("CITYDOG_AGENT_FIXTURE_ORIGIN");
        Require(Uri.TryCreate(origin, UriKind.Absolute, out fixture) && fixture.Scheme == "http" &&
            fixture.Host == "127.0.0.1" && string.IsNullOrEmpty(fixture.UserInfo),
            "Integration requires its credential-free loopback fixture");
        Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KEEP_A_DOG_API_KEY")),
            "Personal credentials must not be present in integration");
        Call(trainer, "BeginPlay");
        trainer.enabled = false;
        Set(trainer, "useModel", true);
        Set(trainer, "apiKey", "");
        Set(trainer, "sessionKey", "");
        trainer.model = "integration-fixture";
        trainer.dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Hidden).SetValue(trainer.dog, true);
        ResetNeeds(trainer);
        yield return Settle(trainer);
        string mode = SessionState.GetString(Mode, "all");
        if (mode == "all" || mode == "controller") yield return Controller(trainer);
        if (mode == "all" || mode == "training") yield return Training(trainer);
        if (mode == "all" || mode == "training-guards") yield return TrainingGuards(trainer);
        if (mode == "all" || mode == "delayed-praise") yield return DelayedPraise(trainer);
        if (mode == "all" || mode == "bowls") yield return Bowls(trainer);
    }

    private static IEnumerator Controller(Dog1Training trainer)
    {
        object progress = Get(trainer, "trainingProgress");
        int before = Rate(progress, "sit");
        Set(trainer, "trainingDrawOverride", new Func<int>(() => throw new InvalidOperationException(
            "A legacy plan without training metadata unexpectedly drew a training probability")));
        // Prime only the old command familiarity. New probability progress remains untouched.
        ((DogAgent)Get(trainer, "agent")).Decide("坐下");
        trainer.endpoint = Route("action/sit/none");
        Call(trainer, "SubmitVoiceTranscript", "麦粒，请坐下来陪我一会儿。");
        yield return Settle(trainer);
        Require(trainer.dog.LastCompletedAction == "sit" && Trace(trainer).Contains("sit · 完成"),
            "The canonical legacy action did not complete through voice/model/controller");
        Require(Rate(progress, "sit") == before, "A normal non-training action changed probability progress");
        Require(!Trace(trainer).Contains("模型请求失败") && !Trace(trainer).Contains("计划校验未通过"),
            "The successful legacy plan contains a model or harness failure");

        trainer.endpoint = Route("plan/sit:none,paw:none");
        Call(trainer, "SubmitVoiceTranscript", "先坐下，然后抬起右爪。");
        yield return Settle(trainer);
        string chain = Trace(trainer);
        Require(chain.Contains("sit · 完成") && chain.Contains("paw · 完成") &&
            chain.IndexOf("sit · 开始", StringComparison.Ordinal) < chain.IndexOf("paw · 开始", StringComparison.Ordinal),
            "The legacy two-action plan did not preserve order");
        Require(Rate(progress, "sit") == before && Rate(progress, "paw") == 40,
            "A multi-action plan changed practice rates");

        trainer.endpoint = Route("invalid");
        Call(trainer, "SubmitVoiceTranscript", "这次检查不在目录里的动作。");
        yield return Settle(trainer);
        Require(Trace(trainer).Contains("计划校验未通过") && !Trace(trainer).Contains("not_registered · 开始"),
            "The integrated controller accepted an unregistered action");
        var conditionalAgent = new DogAgent(Path.Combine(Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES"),
            "integration-conditional.json"));
        string conditionState = "{\"actionCatalog\":[{\"id\":\"fetch_lollipop\",\"target\":\"none\",\"available\":true}]}";
        var conditional = conditionalAgent.DecideAsync("以后我难过时，把毛绒玩具叼给我。", Route("teach-plush"),
            "integration-fixture", stateJson: conditionState);
        float conditionalUntil = Time.realtimeSinceStartup + 8f;
        while (!conditional.IsCompleted && Time.realtimeSinceStartup < conditionalUntil) yield return null;
        Require(conditional.Status == System.Threading.Tasks.TaskStatus.RanToCompletion &&
            conditional.Result.UsedModel && conditional.Result.Actions?.Length == 1 &&
            conditional.Result.Actions[0].id == "fetch_lollipop" && !conditional.Result.IsRefusal,
            "Conditional teaching without probability metadata stopped mapping to its original action");
        Require(!conditionalAgent.CanReward && !conditionalAgent.LongTermSummary.Contains("心情不好时"),
            "A conditional plan alone granted rewards or stored an unperformed lesson");
        Set(trainer, "trainingDrawOverride", null);
        Debug.Log("DOG_INTEGRATION_CONTROLLER_OK: no-training legacy plan, ordered sequence and invalid plan coexist");
    }

    private static IEnumerator Training(Dog1Training trainer)
    {
        object progress = Get(trainer, "trainingProgress");
        Require(progress != null, "Integrated training progress was not initialized");
        foreach (string skill in new[] { "sit", "crouch", "paw", "spin", "come", "fetch_toy" })
            Require(Rate(progress, skill) == 40, "A fresh skill did not start at 40 percent: " + skill);

        trainer.endpoint = Route("practice/sit");
        Set(trainer, "trainingDrawOverride", new Func<int>(() => 100));
        Require((bool)Call(trainer, "SubmitTrainingCue", "sit"), "The integrated practice button was rejected");
        yield return Settle(trainer);
        Require(trainer.dog.LastCompletedAction == "refuse" && Trace(trainer).Contains("训练备选 · refuse · 完成"),
            "A failed training draw did not play its approved alternative");
        Require(Rate(progress, "sit") == 40 && Get(trainer, "pendingTrainingSkill") == null,
            "A failed practice incorrectly increased the rate or opened praise");

        Set(trainer, "trainingDrawOverride", new Func<int>(() => 1));
        Call(trainer, "SubmitVoiceTranscript", "我想教你坐下，陪我练一次。");
        yield return Settle(trainer);
        Require(trainer.dog.LastCompletedAction == "sit" && Rate(progress, "sit") == 50,
            "A successful draw and real completion did not increase practice by 10");
        Require((string)Get(trainer, "pendingTrainingSkill") == "sit" &&
            (float)Get(trainer, "trainingPraiseUntil") > Time.realtimeSinceStartup,
            "Completed practice did not open a bounded praise window");

        trainer.endpoint = Route("insincere");
        Call(trainer, "SubmitVoiceTranscript", "我只是在说话，这句不是表扬。");
        yield return Settle(trainer);
        Require(Rate(progress, "sit") == 50, "Unconfirmed praise increased practice");
        trainer.endpoint = Route("praise");
        Call(trainer, "SubmitVoiceTranscript", "刚才做得很好，我很喜欢你这样坐下来。");
        yield return Settle(trainer);
        Require(Rate(progress, "sit") == 60 && Get(trainer, "pendingTrainingSkill") == null,
            "Genuine model-confirmed praise did not reinforce exactly once");
        Call(trainer, "SubmitVoiceTranscript", "我再夸你一句，还是刚才那次。");
        yield return Settle(trainer);
        Require(Rate(progress, "sit") == 60, "Repeated praise reinforced one completion twice");

        // Trusted local practice is still usable with model disabled and uses the same result bookkeeping.
        Set(trainer, "useModel", false);
        Require((bool)Call(trainer, "SubmitTrainingCue", "paw"), "Offline explicit practice was rejected");
        yield return Settle(trainer);
        Require(trainer.dog.LastCompletedAction == "paw" && Rate(progress, "paw") == 50,
            "Offline explicit practice did not use the unified execution result");
        Set(trainer, "useModel", true);
        Set(trainer, "trainingPraiseUntil", Time.realtimeSinceStartup - 1f);
        Call(trainer, "SubmitVoiceTranscript", "刚才抬爪真棒。");
        yield return Settle(trainer);
        Require(Rate(progress, "paw") == 50, "Expired praise reinforced an old completion");

        // An old completed conditional trial must not receive the praise for a new trusted practice.
        var liveAgent = (DogAgent)Get(trainer, "agent");
        DogAgentDecision oldLesson = liveAgent.Decide(DogAgent.TeachExample);
        liveAgent.ReportExecution(oldLesson, true);
        Require(!liveAgent.LongTermSummary.Contains("心情不好时"), "The stale-trial setup unexpectedly saved its lesson");
        Set(trainer, "useModel", false);
        Require((bool)Call(trainer, "SubmitTrainingCue", "paw"), "The second offline practice was rejected");
        yield return Settle(trainer);
        Require(Rate(progress, "paw") == 60 && trainer.dog.LastCompletedAction == "paw",
            "The second explicit practice did not complete its own action");
        Call(trainer, "ApplyFeedback", true);
        Require(Rate(progress, "paw") == 70 && !liveAgent.LongTermSummary.Contains("心情不好时"),
            "Praise for offline practice reinforced a stale conditional trial instead of its current skill");
        Set(trainer, "useModel", true);

        string save = (string)Get(progress, "savePath");
        Require(Path.GetFullPath(save).StartsWith(Path.GetFullPath(Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES")),
            StringComparison.OrdinalIgnoreCase), "Training progress escaped the isolated save root");
        object reloaded = Activator.CreateInstance(progress.GetType(), new object[] { save });
        Require(Rate(reloaded, "sit") == 60 && Rate(reloaded, "paw") == 70,
            "Practice and genuine praise did not survive save/reload");
        Set(trainer, "trainingProgress", reloaded);
        Set(trainer, "trainingDrawOverride", null);
        Debug.Log("DOG_INTEGRATION_TRAINING_OK: deterministic failure/success, genuine single praise, expiry, offline practice and reload");
    }

    private static IEnumerator Bowls(Dog1Training trainer)
    {
        object world = Get(trainer, "world");
        object life = Get(trainer, "life");
        object data = Get(life, "data");
        ResetNeeds(trainer);
        Set(data, "chapterIndex", 0);
        Set(data, "chapterCounts", new int[7]);
        Set(data, "cooldowns", new float[7]);
        Set(data, "satiety", 20f);
        Set(data, "hydration", 20f);
        object food = Call(world, "CareBowl", DogCareAction.Feed);
        object water = Call(world, "CareBowl", DogCareAction.Water);
        Require(food is Component && water is Component, "The integrated scene lacks movable care bowls");
        Call(world, "FillBowl", DogCareAction.Feed);
        Call(world, "FillBowl", DogCareAction.Water);
        yield return WaitForBowl(food);
        yield return WaitForBowl(water);

        Type holderType = food.GetType().GetProperty("Holder", Hidden).PropertyType;
        object held = Enum.Parse(holderType, "RightHand");
        Require((bool)Call(food, "TryHold", held), "The care bowl could not be held");
        Require(!string.IsNullOrEmpty((string)Call(trainer, "BowlCommandIssue", DogCareAction.Feed)),
            "A held bowl remained available to the controller");
        Require(!CatalogueAvailable(trainer, "eat"), "Held food remained advertised as executable");
        yield return (IEnumerator)Call(trainer, "ConsumeFromBowl", DogCareAction.Feed);
        Require(!(bool)Get(trainer, "lastBowlCareSucceeded") && Mathf.Abs((float)Get(data, "satiety") - 20f) < .001f &&
            RawCount(data, DogCareAction.Feed) == 0, "A held bowl granted a meal or task completion");
        Call(food, "Drop", Vector3.zero);
        yield return WaitForBowl(food);

        bool cancelledConsumeEnded = false;
        trainer.StartCoroutine(ConsumeWithCompletion(trainer, DogCareAction.Feed, () => cancelledConsumeEnded = true));
        float lowerUntil = Time.realtimeSinceStartup + 12f;
        while (!(bool)Get(trainer.dog, "loweringToPickUp") && Time.realtimeSinceStartup < lowerUntil) yield return null;
        Require((bool)Get(trainer.dog, "loweringToPickUp"), "The cancel test never reached its real eating hold");
        Set(trainer.dog, "actionVersion", (int)Get(trainer.dog, "actionVersion") + 1);
        float cancelUntil = Time.realtimeSinceStartup + 3f;
        while (!cancelledConsumeEnded && Time.realtimeSinceStartup < cancelUntil) yield return null;
        Require(cancelledConsumeEnded && !trainer.dog.LastActivitySucceeded &&
            !(bool)Get(trainer, "lastBowlCareSucceeded") && Mathf.Abs((float)Get(data, "satiety") - 20f) < .001f &&
            RawCount(data, DogCareAction.Feed) == 0,
            "Cancelling during the eating hold retained an early success or recorded a meal");
        // Restore only the fixture's motion state after the deliberately invalidated version.
        trainer.dog.GetType().GetProperty("IsPerforming", Hidden).GetSetMethod(true).Invoke(trainer.dog, new object[] { false });
        trainer.dog.GetType().GetProperty("CurrentAction", Hidden).GetSetMethod(true).Invoke(trainer.dog, new object[] { "idle" });
        Set(trainer.dog, "loweringToPickUp", false);
        yield return Settle(trainer);

        // Move the bowl after the real approach starts. Reaching its old point must not count as eating.
        trainer.StartCoroutine((IEnumerator)Call(trainer, "ConsumeFromBowl", DogCareAction.Feed));
        float approachUntil = Time.realtimeSinceStartup + 3f;
        while (!trainer.dog.IsPerforming && Time.realtimeSinceStartup < approachUntil) yield return null;
        Require(trainer.dog.IsPerforming, "The eat command never began its station approach");
        Require((bool)Call(food, "TryHold", held), "The moving-bowl interruption setup could not take the bowl");
        yield return Settle(trainer);
        Require(!(bool)Get(trainer, "lastBowlCareSucceeded") && Mathf.Abs((float)Get(data, "satiety") - 20f) < .001f &&
            RawCount(data, DogCareAction.Feed) == 0, "Taking a bowl during approach counted a meal at its old position");
        Call(food, "Drop", Vector3.zero);

        Vector3 oldFood = ((Component)food).transform.position;
        Vector3 movedFood = FindBowlDestination(trainer, world, oldFood);
        // This establishes a deterministic moved scene fixture. Tracked grab/release physics has its own suite.
        Call(food, "SetHome", movedFood, Quaternion.identity);
        yield return WaitForBowl(food);
        Require(Vector3.Distance(oldFood, ((Component)food).transform.position) > .8f,
            "The relocated bowl was not meaningfully different from the original point");
        Require(string.IsNullOrEmpty((string)Call(trainer, "BowlCommandIssue", DogCareAction.Feed)) &&
            CatalogueAvailable(trainer, "eat"), "A filled reachable moved bowl was not available to the model");
        trainer.endpoint = Route("action/eat/none");
        Call(trainer, "SubmitVoiceTranscript", "我把饭碗放在这里了，去吃一点吧。");
        yield return Settle(trainer);
        Require((bool)Get(trainer, "lastBowlCareSucceeded") && (float)Get(data, "satiety") > 40f &&
            RawCount(data, DogCareAction.Feed) == 1, "Eating from the moved bowl did not restore need and record one meal");
        Require(Vector3.ProjectOnPlane(trainer.dog.transform.position - ((Component)food).transform.position,
            Vector3.up).magnitude <= .7f && !(bool)Call(world, "HasServing", DogCareAction.Feed),
            "The dog was not at the real moved bowl, or the consumed serving remained");

        Vector3 movedWater = FindBowlDestination(trainer, world, ((Component)water).transform.position);
        Call(water, "SetHome", movedWater, Quaternion.identity);
        yield return WaitForBowl(water);
        trainer.endpoint = Route("action/drink/none");
        Call(trainer, "SubmitVoiceTranscript", "清水换到这个位置了，过来喝一点。");
        yield return Settle(trainer);
        Require((bool)Get(trainer, "lastBowlCareSucceeded") && (float)Get(data, "hydration") > 40f &&
            RawCount(data, DogCareAction.Water) == 1 && !(bool)Call(world, "HasServing", DogCareAction.Water),
            "Drinking at the moved bowl did not restore need and record one drink");
        Require(Vector3.ProjectOnPlane(trainer.dog.transform.position - ((Component)water).transform.position,
            Vector3.up).magnitude <= .7f, "The drinking action used the original station instead of the moved bowl");
        Debug.Log("DOG_INTEGRATION_BOWLS_OK: held and interrupted meals rejected; actual moved food/water commands restore needs exactly once");
    }

    private static IEnumerator TrainingGuards(Dog1Training trainer)
    {
        object progress = Get(trainer, "trainingProgress");
        int sitBefore = Rate(progress, "sit"), spinBefore = Rate(progress, "spin");
        string completedBefore = trainer.dog.LastCompletedAction;
        Set(trainer, "trainingDrawOverride", new Func<int>(() => throw new InvalidOperationException(
            "A mismatched practice response reached the probability draw")));
        foreach (string route in new[] { "practice/spin", "action/sit/none" })
        {
            trainer.endpoint = Route(route);
            Require((bool)Call(trainer, "SubmitTrainingCue", "sit"), "The practice guard setup was rejected");
            yield return Settle(trainer);
            Require(Trace(trainer).Contains("练习校验阻塞") &&
                trainer.dog.LastCompletedAction == completedBefore && Rate(progress, "sit") == sitBefore &&
                Rate(progress, "spin") == spinBefore,
                "An explicit sit practice accepted the wrong skill or missing practice metadata: " + route);
        }
        Set(trainer, "trainingDrawOverride", null);
        Debug.Log("DOG_INTEGRATION_PRACTICE_GUARDS_OK: explicit requested skill and training metadata must agree");
    }

    private static IEnumerator DelayedPraise(Dog1Training trainer)
    {
        object progress = Get(trainer, "trainingProgress");
        Set(trainer, "useModel", false);
        Set(trainer, "trainingDrawOverride", new Func<int>(() => 1));
        Require((bool)Call(trainer, "SubmitTrainingCue", "crouch"), "Delayed-praise setup practice was rejected");
        yield return Settle(trainer);
        int completedRate = Rate(progress, "crouch");
        float now = Time.realtimeSinceStartup;
        Set(trainer, "lastTrainingSuccessAt", now - 15f);
        Set(trainer, "trainingPraiseUntil", now - 3f);
        Set(trainer, "voiceStartedAt", now - 4f);
        Set(trainer, "useModel", true);
        trainer.endpoint = Route("praise");
        Call(trainer, "SubmitVoiceTranscript", "刚才趴下来做得很好，我是在表扬你。");
        yield return Settle(trainer);
        Require(Rate(progress, "crouch") == Math.Min(90, completedRate + 10),
            "Speech begun inside the praise window lost credit only because transcription arrived later");
        Set(trainer, "trainingDrawOverride", null);
        Debug.Log("DOG_INTEGRATION_DELAYED_PRAISE_OK: speech start time controls the twelve-second window");
    }

    private static int RawCount(object data, DogCareAction action) => ((int[])Get(data, "chapterCounts"))[(int)action];
    private static IEnumerator ConsumeWithCompletion(Dog1Training trainer, DogCareAction action, Action completed)
    {
        yield return (IEnumerator)Call(trainer, "ConsumeFromBowl", action);
        completed();
    }
    private static bool CatalogueAvailable(Dog1Training trainer, string id)
    {
        object state = Call(trainer, "CaptureCommandState");
        foreach (DogCommandOption entry in (DogCommandOption[])Get(state, "actionCatalog"))
            if (entry.id == id && entry.target == "none") return entry.available;
        throw new InvalidOperationException("The integrated catalogue omitted " + id);
    }
    private static Vector3 FindBowlDestination(Dog1Training trainer, object world, Vector3 old)
    {
        Vector3[] offsets = { new Vector3(-1.2f, 0, .45f), new Vector3(1.2f, 0, .45f),
            new Vector3(0, 0, 1.2f), new Vector3(-.8f, 0, -.8f), new Vector3(.8f, 0, -.8f) };
        foreach (Vector3 offset in offsets)
        {
            Vector3 candidate = (Vector3)Call(world, "ConstrainDogPosition", trainer.dog.transform.position + offset);
            candidate.y = old.y;
            if (Vector3.ProjectOnPlane(candidate - old, Vector3.up).magnitude > .8f &&
                (bool)Call(world, "HasDogPlayClearance", candidate, .4f)) return candidate;
        }
        throw new InvalidOperationException("The scene has no clear reachable relocated bowl test point");
    }
    private static IEnumerator WaitForBowl(object bowl)
    {
        float until = Time.realtimeSinceStartup + 5f;
        do { yield return new WaitForFixedUpdate(); }
        while (!(bool)bowl.GetType().GetProperty("IsSettled", Hidden).GetValue(bowl) && Time.realtimeSinceStartup < until);
        Require((bool)bowl.GetType().GetProperty("IsSettled", Hidden).GetValue(bowl), "A test bowl did not settle");
    }

    private static string Route(string path) => new Uri(fixture, path).AbsoluteUri;
    private static int Rate(object progress, string skill) => (int)Call(progress, "GetSuccessRate", skill);
    private static string Trace(Dog1Training trainer) => (string)trainer.GetType().GetProperty("DeepSeekTraceText", Hidden).GetValue(trainer);
    private static void ResetNeeds(Dog1Training trainer)
    {
        object life = Get(trainer, "life");
        object data = Get(life, "data");
        foreach (string name in new[] { "satiety", "hydration", "energy", "mood", "cleanliness" }) Set(data, name, 80f);
        Set(trainer, "nextAutonomy", float.MaxValue);
        Set(trainer, "nextBowlCheck", float.MaxValue);
        Call(trainer, "ClearTrickReward");
    }

    private static IEnumerator Settle(Dog1Training trainer)
    {
        float until = Time.realtimeSinceStartup + 30f;
        do
        {
            Call(trainer, "ExpirePendingCommands");
            Call(trainer, "TryRunPendingCommand");
            yield return null;
        }
        while (((bool)Get(trainer, "busy") || Get(trainer, "activeCommandTrace") != null ||
            (bool)trainer.GetType().GetProperty("HasPendingCommands", Hidden).GetValue(trainer) ||
            trainer.dog.IsPerforming || !trainer.dog.CanOfferMicroAction) && Time.realtimeSinceStartup < until);
        Require(!(bool)Get(trainer, "busy") && Get(trainer, "activeCommandTrace") == null && !trainer.dog.IsPerforming,
            "An integrated command did not finish within 30 seconds; " + Trace(trainer));
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    private static object Call(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Hidden).Invoke(target, arguments);
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
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
            catch (Exception error) { failure = error is TargetInvocationException ? error.InnerException : error; }
            if (failure != null) { Finish(failure); yield break; }
            if (!next) { stack.Pop(); continue; }
            if (current is IEnumerator child) stack.Push(child);
            else yield return current;
        }
        Finish(null);
    }
    private static void Finish(Exception failure)
    {
        SessionState.SetBool(Armed, false);
        if (failure == null) Debug.Log("DOG_INTEGRATION_OK assertions=" + assertions);
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
