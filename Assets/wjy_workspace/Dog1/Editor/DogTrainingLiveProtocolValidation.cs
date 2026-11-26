#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KeepADog.Demo;
using UnityEditor;
using UnityEngine;

/// <summary>Live-provider protocol probe only. Completion context is synthetic, not scene motion.</summary>
[InitializeOnLoad]
public static class DogTrainingLiveProtocolValidation
{
    private const string Endpoint = "https://api.deepseek.com/chat/completions";
    private const string Model = "deepseek-flash";
    private static Task run;
    private static double deadline;
    private static bool finished;
    private static int total, failed;

    [Serializable]
    private sealed class Skill
    {
        public string id;
        public int successRate = 40;
    }

    [Serializable]
    private sealed class TrainingState
    {
        public Skill[] skills;
        public bool practiceRequested, praiseAvailable;
        public string requestedSkill = "", lastSuccessfulSkill = "";
    }

    [Serializable]
    private sealed class Option
    {
        public string id, label;
        public string target = "none";
        public bool available = true;
        public string reason = "";
        public string[] tags = Array.Empty<string>();
    }

    [Serializable]
    private sealed class State
    {
        public string location = "Apartment";
        public float satiety = 80, hydration = 80, energy = 80, mood = 80, cleanliness = 80;
        public bool dogReady = true, ownerAvailable = true, ballAvailable = true, lollipopAvailable = true;
        public bool houseAvailable = true, cushionAvailable = true, pounceClear = true, burstClear = true;
        public string learnedRoutines = "";
        public Option[] actionCatalog;
        public TrainingState training;
    }

    [Serializable]
    private sealed class Outcome
    {
        public string test, decision, mode;
        public string[] ids;
        public bool usedModel, parsed, providerError, sincere, passed;
        public string error;
    }

    static DogTrainingLiveProtocolValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string saveRoot = Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES");
        string key = Environment.GetEnvironmentVariable("KEEP_A_DOG_API_KEY");
        if (string.IsNullOrWhiteSpace(saveRoot) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("An explicit isolated test-save directory and temporary environment credential are required.");
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Run the live protocol probe outside Play mode; it does not exercise scene motion.");
        string directory = Path.Combine(Path.GetFullPath(saveRoot), "training-live-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        total = failed = 0;
        finished = false;
        deadline = EditorApplication.timeSinceStartup + 90;
        run = Check(directory, key);
    }

    private static async Task Check(string directory, string key)
    {
        var state = Snapshot();
        state.training.practiceRequested = true;
        state.training.requestedSkill = "sit";
        await Ask(directory, key, "explicit-sit-practice", "我们来练坐下，请你按训练规则尝试一次坐下。", state, false, decision =>
        {
            RequireAction(decision, "sit", "none");
            Require(decision.Training?.mode == "practice" && !decision.IsRefusal,
                "Explicit practice did not produce practice metadata or retained the old first-command refusal.");
        });

        state = Snapshot();
        await Ask(directory, key, "ordinary-single-skill-practice", "麦粒，坐下。", state, false, decision =>
        {
            RequireAction(decision, "sit", "none");
            Require(decision.Training?.mode == "practice" && !decision.IsRefusal,
                "A direct single-skill request did not use the practice controller.");
        });

        state = Snapshot();
        state.training.praiseAvailable = true;
        state.training.lastSuccessfulSkill = "sit";
        await Ask(directory, key, "sincere-praise", "刚才坐下做得真棒，好狗狗！", state, true, decision =>
        {
            Require(decision.Actions.Length == 0 && decision.ControllerDecision == "reply" &&
                decision.Training?.mode == "praise" && decision.Training.sincere,
                "Sincere praise of the seeded completed trial was not recognized as typed praise.");
        });

        await Ask(directory, key, "quoted-praise-is-not-reward", "我只是在举例说真棒，并非表扬。", state, true, decision =>
        {
            Require(decision.Actions.Length == 0 && decision.Training?.mode != "practice" &&
                decision.Training?.sincere != true,
                "A quoted or explicitly negated praise sentence would reinforce training.");
        });

        state = Snapshot();
        await Ask(directory, key, "rest-house-stays-care", "回你的小狗屋里歇一会儿吧。", state, false, decision =>
        {
            RequireAction(decision, "rest", "house");
            Require(decision.Training == null || decision.Training.mode == "none",
                "Everyday rest was incorrectly turned into chance-based training.");
        });

        await Ask(directory, key, "held-bowl-is-unavailable", "去食盆那儿吃点狗粮吧。", state, false, decision =>
        {
            Require(decision.Actions.Length == 0 && decision.ControllerDecision == "decline" &&
                (decision.Training == null || decision.Training.mode == "none"),
                "The provider did not decline eating from an unavailable held bowl.");
        });

        Debug.Log("DOG_TRAINING_LIVE_SUMMARY total=" + total + " failed=" + failed + " scope=protocol-only");
        if (failed != 0) throw new InvalidOperationException("One or more live training protocol cases failed.");
    }

    private static async Task Ask(string directory, string key, string name, string input, State state,
        bool seedSuccessfulPractice, Action<DogAgentDecision> verify)
    {
        total++;
        var outcome = new Outcome { test = name, ids = Array.Empty<string>(), mode = "none", error = "" };
        try
        {
            var agent = new DogAgent(Path.Combine(directory, name + "-memory.json"));
            if (seedSuccessfulPractice)
            {
                // This supplies a synthetic protocol precondition. No actual dog animation is claimed.
                DogAgentDecision seed = agent.Decide("坐下");
                if (seed.IsRefusal) seed = agent.Decide("坐下");
                Require(seed.Intent == DogIntent.Sit && !seed.IsRefusal, "Cannot seed an isolated completed-sit trial.");
                agent.ReportExecution(seed, true);
                Require(agent.CanReward, "The isolated completion context was not available.");
            }

            DogAgentDecision decision = await agent.DecideAsync(input, Endpoint, Model, key,
                stateJson: JsonUtility.ToJson(state), captureModelError: _ => outcome.providerError = true);
            outcome.usedModel = decision?.UsedModel == true;
            outcome.parsed = decision != null && decision.Actions != null && string.IsNullOrEmpty(decision.ValidationError);
            outcome.decision = decision?.ControllerDecision ?? "none";
            outcome.mode = decision?.Training?.mode ?? "none";
            outcome.sincere = decision?.Training?.sincere == true;
            if (decision?.Actions != null)
            {
                var ids = new List<string>();
                foreach (DogAgentAction action in decision.Actions) ids.Add(action.id + "/" + action.target);
                outcome.ids = ids.ToArray();
            }
            Require(outcome.usedModel && outcome.parsed && !outcome.providerError,
                "The provider did not return a validated canonical response.");
            verify(decision);
            if (seedSuccessfulPractice)
                Require(agent.CanReward && agent.LongTermSummary.Contains("还没有"),
                    "Protocol parsing prematurely consumed feedback or granted persistent learning.");
            outcome.passed = true;
        }
        catch (Exception error)
        {
            failed++;
            // Only fixed test messages are emitted; no input, reply, raw response, request, header or credential.
            outcome.error = error is InvalidOperationException ? error.Message : error.GetType().Name;
        }
        if (!string.IsNullOrEmpty(key)) outcome.error = outcome.error.Replace(key, "[redacted]");
        Debug.Log("DOG_TRAINING_LIVE_CASE " + JsonUtility.ToJson(outcome));
    }

    private static State Snapshot()
    {
        var skills = new List<Skill>();
        var options = new List<Option>();
        foreach (string id in Wjy.DogTraining.Dog1TrainingProgress.SkillIds)
        {
            skills.Add(new Skill { id = id });
            options.Add(new Option { id = id, label = id, tags = new[] { "training", "practice" } });
        }
        options.Add(new Option { id = "rest", label = "就地趴下休息", tags = new[] { "rest", "care" } });
        options.Add(new Option { id = "rest", target = "house", label = "回狗屋休息", tags = new[] { "rest", "house", "care" } });
        options.Add(new Option { id = "rest", target = "cushion", label = "去软垫休息", tags = new[] { "rest", "cushion", "care" } });
        options.Add(new Option { id = "refuse", label = "疑惑地歪头看看主人", tags = new[] { "expression", "confused", "stationary" } });
        options.Add(new Option { id = "head_tilt", target = "owner", label = "歪头倾听", tags = new[] { "attention", "stationary" } });
        options.Add(new Option { id = "eat", label = "走向当前位置的食盆吃饭", available = false, reason = "碗在手里" });
        options.Add(new Option { id = "drink", label = "走向当前位置的水盆喝水", available = false, reason = "碗在手里" });
        return new State { actionCatalog = options.ToArray(), training = new TrainingState { skills = skills.ToArray() } };
    }

    private static void RequireAction(DogAgentDecision decision, string id, string target) =>
        Require(decision.Actions.Length == 1 && decision.Actions[0].id == id && decision.Actions[0].target == target,
            "The provider chose a different action or target from the explicit request.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Tick()
    {
        if (run == null || finished) return;
        if (EditorApplication.timeSinceStartup >= deadline) Finish(new TimeoutException());
        else if (run.IsFaulted) Finish(run.Exception.GetBaseException());
        else if (run.IsCanceled) Finish(new OperationCanceledException());
        else if (run.IsCompleted) Finish(null);
    }

    private static void Finish(Exception error)
    {
        if (finished) return;
        finished = true;
        Debug.Log(error == null ? "DOG_TRAINING_LIVE_PROTOCOL_OK cases=" + total + " scope=protocol-only" :
            "DOG_TRAINING_LIVE_PROTOCOL_FAILED error=" + error.GetType().Name + " completed=" + total + " failed=" + failed);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
