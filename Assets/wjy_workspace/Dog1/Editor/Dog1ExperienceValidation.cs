#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KeepADog.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog1ExperienceValidation
{
    private const string Armed = "CityDogExperienceValidation";
    private const string ConditionalMode = "CityDogConditionalExperienceValidation";
    private const string InteractionTail = "CityDogExperienceInteractionTail";
    private const string RestFlowMode = "CityDogExperienceRestFlows";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string RestState = "{\"actionCatalog\":[{\"id\":\"rest\",\"target\":\"none\",\"available\":true}]}";
    private static bool started;
    private static int assertions;
    private static double deadline;

    [Serializable]
    private sealed class MemoryView
    {
        public int recentEventCount;
        public string[] recentEvents;
        public int sadnessStrength;
    }

    static Dog1ExperienceValidation() => EditorApplication.update += Tick;

    public static void Run() => Arm(false);

    public static void RunConditional() => Arm(true);

    public static void RunInteractionTail()
    {
        Arm(false);
        SessionState.SetBool(InteractionTail, true);
    }

    public static void RunRestFlows()
    {
        Arm(false);
        SessionState.SetBool(RestFlowMode, true);
    }

    private static void Arm(bool conditional)
    {
        string saves = Path.Combine(Path.GetTempPath(), "CityDogExperience-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES", saves);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        Environment.SetEnvironmentVariable("KEEP_A_DOG_API_KEY", null);
        SessionState.SetBool(Armed, true);
        SessionState.SetBool(ConditionalMode, conditional);
        SessionState.SetBool(InteractionTail, false);
        SessionState.SetBool(RestFlowMode, false);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Armed, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 220;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Experience regression exceeded 220s")); return; }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        started = true;
        trainer.StartCoroutine(Guard(SessionState.GetBool(ConditionalMode, false) ? Conditional(trainer) :
            SessionState.GetBool(RestFlowMode, false) ? RestFlows(trainer) : Check(trainer)));
    }

    private static IEnumerator RestFlows(Dog1Training trainer)
    {
        Call(trainer, "BeginPlay");
        trainer.enabled = false;
        trainer.dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Hidden).SetValue(trainer.dog, true);
        trainer.dog.HasPlayClearance = (position, radius) => true;
        ResetCare(trainer);
        object data = typeof(DogLifeProgress).GetField("data", Hidden).GetValue(Profile(trainer));
        data.GetType().GetField("energy").SetValue(data, 20f);
        Set(trainer, "nextRestInHouse", false);
        yield return Settle(trainer);
        int bones = Profile(trainer).Bones;
        yield return (IEnumerator)Call(trainer, "Autonomy");
        yield return Settle(trainer);
        Require(Profile(trainer).Energy > 50f && RawRestCount(trainer) == 0 && Profile(trainer).Bones == bones,
            "Autonomous rest did not recover needs or issued a player task/reward");
        Require(((IReadOnlyList<string>)typeof(Dog1Training).GetProperty("StoryChoiceLabels", Hidden).GetValue(trainer)).Count == 2,
            "Autonomous rest did not offer the quiet companionship choices");
        Call(trainer, "ChooseStoryOption", 0);
        yield return Settle(trainer);
        Require(Profile(trainer).Energy > 85f && RawRestCount(trainer) == 1,
            "Choosing quiet companionship did not finish and register one real rest");
        Debug.Log("DOG_EXPERIENCE_REST: autonomous rest only recovers; choosing quiet companionship records one completed rest");

        ResetCare(trainer);
        Set(trainer, "pendingInvitation", true);
        Set(trainer, "invitationResponseUntil", Time.unscaledTime + 26f);
        Set(trainer, "nextRestInHouse", false);
        Call(trainer, "ChooseStoryOption", 1);
        yield return Settle(trainer);
        Require(Profile(trainer).Energy > 70f && RawRestCount(trainer) == 1,
            "Accepting an invitation with quiet rest omitted or double-counted the rest");
        Debug.Log("DOG_EXPERIENCE_REST_OK: quiet invitation rests once without duplicate story accounting");
    }

    private static IEnumerator Conditional(Dog1Training trainer)
    {
        string origin = Environment.GetEnvironmentVariable("CITYDOG_AGENT_FIXTURE_ORIGIN");
        Require(Uri.TryCreate(origin, UriKind.Absolute, out var fixture) && fixture.Scheme == "http" &&
            fixture.Host == "127.0.0.1" && string.IsNullOrEmpty(fixture.UserInfo), "Conditional regression needs its loopback fixture");
        Call(trainer, "BeginPlay");
        trainer.enabled = false;
        Set(trainer, "useModel", true);
        Set(trainer, "sessionKey", "");
        Set(trainer, "apiKey", "");
        trainer.model = "experience-fixture";
        trainer.dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Hidden).SetValue(trainer.dog, true);
        trainer.dog.HasPlayClearance = (position, radius) => true;
        ResetCare(trainer);
        yield return Settle(trainer);
        yield return WaitForToy(trainer, 1);
        trainer.endpoint = new Uri(fixture, "teach-free").AbsoluteUri;
        yield return Voice(trainer, "以后我低落的时候，你给我叼来那个软软的棒棒糖玩偶，好不好");
        var agent = Get<DogAgent>(trainer, "agent");
        Require(agent.CanReward && trainer.dog.LastFetchSucceeded,
            "Semantic conditional teaching did not execute an eligible physical fetch");
        string path = Path.Combine(Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES"), "dog-memory.json");
        Require(!new DogAgent(path).LongTermSummary.Contains("心情不好时"),
            "Semantic conditional teaching was saved before successful execution and praise");
        Call(trainer, "ApplyFeedback", true);
        var restored = new DogAgent(path);
        Require(restored.LongTermSummary.Contains("心情不好时") &&
            JsonUtility.FromJson<MemoryView>((string)Call(restored, "ModelContext")).sadnessStrength > 0,
            "Praised semantic conditional teaching did not survive reload");
        Set(trainer, "agent", restored);
        ResetCare(trainer);
        yield return WaitForToy(trainer, 1);
        trainer.endpoint = new Uri(fixture, "sad-recall").AbsoluteUri;
        yield return Voice(trainer, "今天有点低落，陪陪我吧");
        Require(trainer.dog.LastFetchSucceeded && Get<DogAgent>(trainer, "agent").CanReward,
            "Reloaded conditional memory was not available to the model's semantic recall");
        Debug.Log("DOG_EXPERIENCE_CONDITIONAL_OK: semantic teaching, physical success, praise, reload and semantic recall");
    }

    private static IEnumerator Check(Dog1Training trainer)
    {
        string origin = Environment.GetEnvironmentVariable("CITYDOG_AGENT_FIXTURE_ORIGIN");
        Require(Uri.TryCreate(origin, UriKind.Absolute, out var fixture) && fixture.Scheme == "http" &&
            fixture.Host == "127.0.0.1" && string.IsNullOrEmpty(fixture.UserInfo), "Experience regression needs its loopback fixture");
        Call(trainer, "BeginPlay");
        trainer.enabled = false;
        Set(trainer, "useModel", true);
        Set(trainer, "sessionKey", "");
        Set(trainer, "apiKey", "");
        trainer.model = "experience-fixture";
        trainer.dog.SetWanderingEnabled(false);
        typeof(DogCompanion).GetProperty("HoldIdleGestures", Hidden).SetValue(trainer.dog, true);
        trainer.dog.HasPlayClearance = (position, radius) => true;
        Require(Profile(trainer).CurrentLocation == DogLocation.Apartment, "Experience probe did not start at home");
        yield return Settle(trainer);

        if (!SessionState.GetBool(InteractionTail, false))
        {
        yield return Memory(fixture);

        foreach (bool house in new[] { true, false })
        {
            ResetCare(trainer);
            trainer.StartCoroutine((IEnumerator)Call(trainer, "RestWithFurniture", house));
            yield return Settle(trainer);
            RestCounted(trainer, house ? "direct house rest" : "direct cushion rest");
        }
        ResetCare(trainer);
        Call(trainer, "StartCare", DogCareAction.Rest);
        yield return Settle(trainer);
        RestCounted(trainer, "care rest button");

        foreach (string mode in new[] { "rest", "crouch", "house", "cushion" })
        {
            ResetCare(trainer);
            // An already-practised posture keeps this case focused on activity accounting.
            if (mode == "crouch") Get<DogAgent>(trainer, "agent").Decide("趴下");
            trainer.endpoint = new Uri(fixture, mode).AbsoluteUri;
            yield return Voice(trainer, mode == "house" ? "回屋歇一会儿" : mode == "cushion" ? "到软垫上歇会儿" : "趴一会儿吧");
            RestCounted(trainer, "voice " + mode);
        }
        Debug.Log("DOG_EXPERIENCE: direct rest, furniture, care and semantic voice entrances share recovery and task accounting");

        ResetCare(trainer);
        trainer.endpoint = new Uri(fixture, "invite").AbsoluteUri;
        yield return Voice(trainer, "邀请我一起玩吧");
        Require((bool)typeof(Dog1Training).GetProperty("HasPlayInvitation", Hidden).GetValue(trainer),
            "A model-selected invitation did not open a player response");
        var choices = (IReadOnlyList<string>)typeof(Dog1Training).GetProperty("StoryChoiceLabels", Hidden).GetValue(trainer);
        Require(choices.Count == 3, "Invitation did not expose play, quiet and later choices");
        Call(trainer, "ChooseStoryOption", 2);
        yield return Settle(trainer);
        Debug.Log("DOG_EXPERIENCE: model invitation exposes choices and accepts later without forced play");
        }

        ResetCare(trainer);
        yield return WaitForToy(trainer, 0);
        Get<DogAgent>(trainer, "agent").Decide("叼球");
        trainer.endpoint = new Uri(fixture, "fetch").AbsoluteUri;
        yield return Voice(trainer, "陪我玩球，把球叼回来吧");
        if (!trainer.dog.LastFetchSucceeded || Profile(trainer).GetTaskProgress(DogCareAction.Play) != 1)
            Debug.Log("DOG_EXPERIENCE_FETCH_DIAGNOSTIC " + TeachingDiagnostic(trainer));
        Require(trainer.dog.LastFetchSucceeded && Profile(trainer).GetTaskProgress(DogCareAction.Play) == 1,
            "A successful semantic play/fetch did not count as play");
        Debug.Log("DOG_EXPERIENCE: successful voice fetch counts as play");

        ResetCare(trainer);
        yield return WaitForToy(trainer, 1);
        // This is an explicit teaching control and must work with model mode enabled.
        trainer.endpoint = new Uri(fixture, "unexpected-teaching-network-request").AbsoluteUri;
        Require((bool)Call(trainer, "SubmitPracticeCue", DogAgent.TeachExample), "Explicit conditional teaching was not accepted");
        var teachStates = new List<string>();
        yield return Settle(trainer, teachStates);
        if (!Get<DogAgent>(trainer, "agent").CanReward) Debug.Log("DOG_EXPERIENCE_TEACH_DIAGNOSTIC " + TeachingDiagnostic(trainer) +
            " stages=" + string.Join(" | ", teachStates));
        Require(Get<DogAgent>(trainer, "agent").CanReward, "Explicit conditional teaching did not finish an eligible trial");
        Require(FlatOwnerDistance(trainer) <= 1.92f,
            "The boundary-positioned owner was declared reached outside the original delivery radius");
        Call(trainer, "ApplyFeedback", true);
        Require(Get<DogAgent>(trainer, "agent").LongTermSummary.Contains("心情不好时"),
            "The explicit teaching button lost its conditional long-term memory with model enabled");
        Debug.Log("DOG_EXPERIENCE: explicit teaching and praise preserve conditional memory while model mode is enabled");

        ResetCare(trainer);
        object trainingData = typeof(DogLifeProgress).GetField("data", Hidden).GetValue(Profile(trainer));
        trainingData.GetType().GetField("chapterIndex").SetValue(trainingData, 3);
        Get<DogAgent>(trainer, "agent").Decide("坐下");
        trainer.endpoint = new Uri(fixture, "sit").AbsoluteUri;
        yield return Voice(trainer, "坐下");
        Require(Get<DogAgent>(trainer, "agent").CanReward, "First completed training was not eligible for praise");
        Call(trainer, "ApplyFeedback", true);
        int afterFirstReward = Profile(trainer).Bones;
        Require(Profile(trainer).GetTaskProgress(DogCareAction.Train) == 1, "First completed and praised training did not count");
        yield return Voice(trainer, "再坐下一次");
        Require(Get<DogAgent>(trainer, "agent").CanReward, "Second completed training was not eligible for praise");
        Call(trainer, "ApplyFeedback", true);
        Require(Profile(trainer).GetTaskProgress(DogCareAction.Train) == 2 && Profile(trainer).Bones == afterFirstReward,
            "Reward cooldown swallowed a completed training task or paid duplicate bones");
        Debug.Log("DOG_EXPERIENCE: consecutive completed training counts while reward cooldown prevents duplicate bones");

        ResetCare(trainer);
        Call(trainer, "StartCare", DogCareAction.Walk);
        Require(Get<bool>(trainer, "walking"), "Walk did not start");
        Set(trainer, "xrTab", 1);
        Call(trainer, "BuildHeadsetPage");
        Button cancel = trainer.HeadsetCanvas.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(button => button.GetComponentInChildren<Text>()?.text.Contains("取消") == true &&
                button.GetComponentInChildren<Text>().text.Contains("遛弯"));
        Require(cancel != null && cancel.interactable, "VR care page has no usable walk cancellation");
        cancel.onClick.Invoke();
        Require(!Get<bool>(trainer, "walking") && Profile(trainer).GetTaskProgress(DogCareAction.Walk) == 0,
            "VR walk cancellation did not cancel cleanly or awarded an unfinished walk");
        Debug.Log("DOG_EXPERIENCE: VR page can cancel an unfinished walk without task credit");
    }

    private static IEnumerator Memory(Uri fixture)
    {
        string path = Path.Combine(Path.GetTempPath(), "ExperienceMemory-" + Guid.NewGuid().ToString("N") + ".json");
        var agent = new DogAgent(path);
        Task<DogAgentDecision> request = agent.DecideAsync("记住今天的暗号是小云朵", new Uri(fixture, "memory").AbsoluteUri,
            "experience-fixture", stateJson: RestState);
        while (!request.IsCompleted) yield return null;
        Require(request.GetAwaiter().GetResult().UsedModel, "First conversational turn failed");
        request = agent.DecideAsync("刚才的暗号是什么", new Uri(fixture, "memory").AbsoluteUri,
            "experience-fixture", stateJson: RestState);
        while (!request.IsCompleted) yield return null;
        var answer = request.GetAwaiter().GetResult();
        Require(answer.UsedModel && answer.Reply.Contains("小云朵") && answer.Actions?.Length == 0,
            "The next model turn could not see the preceding player and dog conversation");
        for (int index = 0; index < 8; index++)
        {
            request = agent.DecideAsync("新的交流第" + index + "次", new Uri(fixture, "memory").AbsoluteUri,
                "experience-fixture", stateJson: RestState);
            while (!request.IsCompleted) yield return null;
            Require(request.GetAwaiter().GetResult().UsedModel, "Bounded conversation turn failed");
        }
        var memory = JsonUtility.FromJson<MemoryView>((string)Call(agent, "ModelContext"));
        Require(memory.recentEventCount == 8 && memory.recentEvents.Length == 8 && memory.recentEvents.All(text => text.Length <= 100),
            "Short-term conversation exceeded its item or length bound");
        Require(!memory.recentEvents.Any(text => text.Contains("小云朵")), "Old short-term conversation was not evicted");
        var teaching = agent.Decide(DogAgent.TeachExample);
        agent.ReportExecution(teaching, true);
        agent.Feedback(true);
        var restored = new DogAgent(path);
        memory = JsonUtility.FromJson<MemoryView>((string)Call(restored, "ModelContext"));
        Require(memory.sadnessStrength > 0 && memory.recentEventCount == 0,
            "Short-term conversation changes broke saved long-term conditional memory or persisted temporary chat");
        Debug.Log("DOG_EXPERIENCE: bounded two-way short-term conversation reaches the model and long-term training still reloads");
    }

    private static void ResetCare(Dog1Training trainer)
    {
        Call(trainer, "ClearTrickReward");
        object data = typeof(DogLifeProgress).GetField("data", Hidden).GetValue(Profile(trainer));
        foreach (string field in new[] { "satiety", "hydration", "mood", "cleanliness" })
            data.GetType().GetField(field).SetValue(data, 80f);
        data.GetType().GetField("energy").SetValue(data, 40f);
        data.GetType().GetField("chapterIndex").SetValue(data, 2);
        data.GetType().GetField("chapterCounts").SetValue(data, new int[7]);
        data.GetType().GetField("cooldowns").SetValue(data, new float[7]);
        Set(trainer, "nextAutonomy", float.MaxValue);
        Set(trainer, "nextBowlCheck", float.MaxValue);
    }

    private static void RestCounted(Dog1Training trainer, string entrance)
    {
        Require(Profile(trainer).Energy > 70f && Profile(trainer).GetTaskProgress(DogCareAction.Rest) == 1,
            entrance + " did not recover energy and count exactly once as rest");
    }

    private static IEnumerator Voice(Dog1Training trainer, string words)
    {
        Call(trainer, "SubmitVoiceTranscript", "麦粒，" + words);
        yield return Settle(trainer);
    }

    private static IEnumerator Settle(Dog1Training trainer, List<string> sampled = null)
    {
        float until = Time.realtimeSinceStartup + 30f;
        string previous = null;
        do
        {
            Call(trainer, "ExpirePendingCommands");
            Call(trainer, "TryRunPendingCommand");
            if (sampled != null && trainer.dog.StateLabel != previous)
            {
                previous = trainer.dog.StateLabel;
                sampled.Add(previous + " " + PickupGeometry(trainer));
            }
            yield return null;
        }
        while ((Get<bool>(trainer, "busy") || Get<object>(trainer, "activeCommandTrace") != null ||
            (bool)typeof(Dog1Training).GetProperty("HasPendingCommands", Hidden).GetValue(trainer) ||
            trainer.dog.IsPerforming || !trainer.dog.CanOfferMicroAction) && Time.realtimeSinceStartup < until);
        Require(!Get<bool>(trainer, "busy") && !trainer.dog.IsPerforming &&
            !(bool)typeof(Dog1Training).GetProperty("HasPendingCommands", Hidden).GetValue(trainer),
            "Player action did not finish within 30s");
    }

    private static IEnumerator WaitForToy(Dog1Training trainer, int index)
    {
        Array props = Get<Array>(trainer, "physicalProps");
        object toy = props.GetValue(index);
        Require(toy != null, "Experience setup has no required physical toy");
        var canFetch = toy.GetType().GetProperty("CanFetch", Hidden);
        float until = Time.realtimeSinceStartup + 8f;
        float stableSince = Time.realtimeSinceStartup;
        do
        {
            yield return null;
            if (!(bool)canFetch.GetValue(toy)) stableSince = Time.realtimeSinceStartup;
        } while (Time.realtimeSinceStartup - stableSince < .5f && Time.realtimeSinceStartup < until);
        Require((bool)canFetch.GetValue(toy) && Time.realtimeSinceStartup - stableSince >= .5f,
            "Experience setup toy did not settle for an eligible fetch");
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

    private static object Call(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, Hidden).Invoke(target, arguments);
    private static DogLifeProgress Profile(Dog1Training trainer) => Get<DogLifeProgress>(trainer, "life");
    private static int RawRestCount(Dog1Training trainer)
    {
        object data = typeof(DogLifeProgress).GetField("data", Hidden).GetValue(Profile(trainer));
        return ((int[])data.GetType().GetField("chapterCounts").GetValue(data))[(int)DogCareAction.Rest];
    }
    private static string TeachingDiagnostic(Dog1Training trainer)
    {
        object trial = Get<object>(Get<DogAgent>(trainer, "agent"), "trial");
        Array props = Get<Array>(trainer, "physicalProps");
        object plush = props.GetValue(1);
        string canFetch = plush == null ? "missing" : plush.GetType().GetProperty("CanFetch", Hidden).GetValue(plush).ToString();
        string holder = plush == null ? "missing" : plush.GetType().GetProperty("Holder", Hidden).GetValue(plush).ToString();
        string trialState = trial == null ? "none" : trial.GetType().GetField("Intent").GetValue(trial) + "/" +
            trial.GetType().GetField("Outcome").GetValue(trial) + "/conditional=" + trial.GetType().GetField("IsConditional").GetValue(trial);
        string trace = (string)typeof(Dog1Training).GetProperty("DeepSeekTraceText", Hidden).GetValue(trainer);
        int split = trace.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
        if (split >= 0) trace = trace.Substring(0, split);
        return "LastFetchSucceeded=" + trainer.dog.LastFetchSucceeded + " plushCanFetch=" + canFetch + " holder=" + holder +
            " trial=" + trialState + " trace=" + trace;
    }
    private static string PickupGeometry(Dog1Training trainer)
    {
        var prop = Get<Array>(trainer, "physicalProps").GetValue(1) as Component;
        Transform muzzle = trainer.dog.GetType().GetProperty("MuzzleAnchor", Hidden).GetValue(trainer.dog) as Transform;
        if (prop == null || muzzle == null) return "geometry unavailable";
        Collider shape = prop.GetComponent<Collider>();
        return "dog=" + trainer.dog.transform.position.ToString("F3") + " prop=" + prop.transform.position.ToString("F3") +
            " owner=" + (trainer.dog.user == null ? "missing" : trainer.dog.user.position.ToString("F3")) +
            " muzzle=" + muzzle.position.ToString("F3") + " pivotDistance=" + Vector3.Distance(muzzle.position, prop.transform.position).ToString("F3") +
            " contactDistance=" + (shape == null ? "missing" : Vector3.Distance(muzzle.position, shape.ClosestPoint(muzzle.position)).ToString("F3"));
    }
    private static float FlatOwnerDistance(Dog1Training trainer)
    {
        if (trainer.dog.user == null) return float.PositiveInfinity;
        Vector3 owner = trainer.dog.user.position;
        owner.y = trainer.dog.transform.position.y;
        return Vector3.Distance(trainer.dog.transform.position, owner);
    }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Finish(Exception failure)
    {
        SessionState.SetBool(Armed, false);
        if (failure == null) Debug.Log("DOG_EXPERIENCE_OK assertions=" + assertions);
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
