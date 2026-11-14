#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using KeepADog.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogPlayAgentValidation
{
    private const string SessionKey = "CityDogPlayAgentValidation";
    private static readonly MethodInfo MotionFor = typeof(DogAgent).Assembly
        .GetType("Wjy.DogTraining.DogActionSelection", true).GetMethod("MotionFor");

    static DogPlayAgentValidation() => EditorApplication.playModeStateChanged += CheckOnPlay;

    public static void Run()
    {
        // Use an empty scene so the checks never open or alter the player's game saves.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void CheckOnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false);
        string fixture = Path.Combine(Path.GetTempPath(), "CityDogPlayAgent-" + Guid.NewGuid().ToString("N"));
        int exitCode = 0;
        try
        {
            Directory.CreateDirectory(fixture);
            CheckTraining(Path.Combine(fixture, "memory.json"));
            CheckRoutines(Path.Combine(fixture, "routines.json"));
            CheckModelParsing();
            CheckClearance();
            Debug.Log("DOG_PLAY_AGENT_OK: pounce, sniff, reward, save migration, old aliases, routines, negative cues, model action limits and neighborhood clearance.");
        }
        catch (Exception error)
        {
            exitCode = 1;
            Debug.LogException(error);
        }
        finally
        {
            if (Directory.Exists(fixture))
            {
                foreach (string file in Directory.EnumerateFiles(fixture)) File.Delete(file);
                Directory.Delete(fixture);
            }
        }
        EditorApplication.Exit(exitCode);
    }

    private static void CheckTraining(string path)
    {
        // Version 1 save before pounce/sniff, with a learned bow at its original enum index.
        File.WriteAllText(path, "{\"version\":1,\"skills\":[0,0,0,0,0,0,0,0,2,0],\"sadAction\":0,\"sadTarget\":\"\",\"sadStrength\":0}");
        var dog = new DogAgent(path);
        AssertMotion(dog.Decide("拜一拜"), DogIntent.Bow, "bow", "Original trained bow lost on first read");
        AssertMotion(dog.Decide("伏身邀玩"), DogIntent.Bow, "bow", "New bow label changed the learned action");

        Require(dog.Decide("轻扑一下").IsRefusal, "Untrained pounce did not hesitate");
        var pounce = dog.Decide("轻扑一下");
        AssertMotion(pounce, DogIntent.Pounce, "pounce", "Pounce was not accepted after repeating");
        Require(!dog.CanReward, "Pounce was rewardable before execution");
        dog.ReportExecution(pounce, false);
        Require(!dog.CanReward, "Blocked pounce could be rewarded");
        dog.Feedback(true);
        dog = new DogAgent(path);
        Require(dog.Decide("轻扑一下").IsRefusal, "Blocked pounce was persisted as learned");
        Train(dog, "轻扑一下", DogIntent.Pounce, "pounce");

        Require(dog.Decide("闻一闻").IsRefusal, "Untrained sniff did not hesitate");
        Train(dog, "闻一闻", DogIntent.Sniff, "sniff");
        var reopened = new DogAgent(path);
        AssertMotion(reopened.Decide("小扑一下"), DogIntent.Pounce, "pounce", "Pounce did not survive reload");
        AssertMotion(reopened.Decide("嗅一嗅"), DogIntent.Sniff, "sniff", "Sniff did not survive reload");
        AssertMotion(reopened.Decide("拜一拜"), DogIntent.Bow, "bow", "Migrated bow was lost while saving new skills");

        foreach (string negative in new[] { "不要扑向玩具", "别再轻扑一下", "不要去嗅一嗅", "别闻一闻", "do not pounce", "don't sniff" })
        {
            var decision = reopened.Decide(negative);
            Require(decision.Intent == DogIntent.Wait && !decision.IsRefusal && decision.Steps == null &&
                SelectedMotion(decision) == null, "Negative phrase initiated an action: " + negative);
        }
    }

    private static void Train(DogAgent dog, string phrase, DogIntent intent, string expectedMotion)
    {
        var decision = dog.Decide(phrase);
        AssertMotion(decision, intent, expectedMotion, "Repeated cue did not enter a trial");
        dog.ReportExecution(decision, true);
        Require(dog.CanReward, "Succeeded action could not be rewarded");
        dog.Feedback(true);
        Require(!dog.CanReward && string.IsNullOrEmpty(dog.SaveError), "Reward remained open or did not save");
    }

    private static void AssertMotion(DogAgentDecision decision, DogIntent intent, string motion, string context) =>
        Require(!decision.IsRefusal && decision.Intent == intent && SelectedMotion(decision) == motion, context);

    private static string SelectedMotion(DogAgentDecision decision) =>
        (string)MotionFor.Invoke(null, new object[] { decision });

    private static void CheckRoutines(string path)
    {
        var routines = new DogRoutines(path);
        string[] expected = { "bow", "pounce", "sniff" };
        Require(routines.TryParse("游戏时间：先拜一拜，再轻扑一下，然后闻一闻", out string name, out string[] steps) &&
            name == "游戏时间" && steps.SequenceEqual(expected), "Routine with old bow alias and new gestures did not parse");
        Require(routines.Remember(name, steps), "Could not save routine");
        Require(new DogRoutines(path).Recall(name)?.SequenceEqual(expected) == true, "Routine did not survive reload");
        Require(!routines.TryParse("停一下：先坐下、不要扑向玩具", out _, out _), "Routine accepted a negative action");
    }

    private static void CheckModelParsing()
    {
        var model = typeof(DogAgent).Assembly.GetType("KeepADog.Demo.DogLanguageModel", true);
        MethodInfo parse = model.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
        Require(parse != null, "Cannot find model parser");
        object Decode(string json) => parse.Invoke(null, new object[] { json });
        object Command(string action, string target) =>
            Decode("{\"kind\":\"command\",\"action\":\"" + action + "\",\"target\":\"" + target + "\",\"reply\":\"\"}");
        Require(Command("pounce", "none") != null && Command("sniff", "none") != null,
            "Model parser did not allow the new grounded gestures");
        Require(Command("pounce", "owner") == null && Command("burst", "none") == null,
            "Model parser accepted an unsupported action or target");
        Require(Decode("{\"kind\":\"sequence\",\"action\":\"none\",\"target\":\"none\",\"reply\":\"\",\"steps\":[\"bow\",\"pounce\",\"sniff\"]}") != null,
            "Valid model routine did not parse");
        Require(Decode("{\"kind\":\"sequence\",\"action\":\"none\",\"target\":\"none\",\"reply\":\"\",\"steps\":[\"pounce\",\"burst\"]}") == null,
            "Model routine accepted a park-only autonomous action");
    }

    private static void CheckClearance()
    {
        var parent = new GameObject("Clearance fixture");
        using (var scene = new DogNeighborhood(parent.transform))
        {
            Require(!scene.HasDogPlayClearance(Vector3.zero, .45f), "Unloaded neighborhood admitted play");
            scene.Load((int)DogLocation.Apartment);
            Require(scene.HasDogPlayClearance(scene.DogStart, .45f), "Apartment starting area is unexpectedly blocked");
            Require(!scene.HasDogPlayClearance(scene.FoodPosition, .20f), "Apartment bowl footprint did not block play");
            Require(!scene.HasDogPlayClearance(scene.FetchPosition, .20f), "Apartment fetch mat did not block play");
            Require(!scene.HasDogPlayClearance(new Vector3(3.60f, 0f, -2f), .25f),
                "Apartment edge admitted a footprint crossing the boundary");
            scene.Load((int)DogLocation.Park);
            Require(scene.HasDogPlayClearance(new Vector3(4.5f, 0f, -3.5f), .50f), "Open park grass was blocked");
            Require(!scene.HasDogPlayClearance(scene.WaterPosition, .25f), "Park bowl footprint did not block play");
            Require(!scene.HasDogPlayClearance(new Vector3(-3.84f, 0f, 6.45f), .10f), "Park lamp footprint did not block play");
        }
        UnityEngine.Object.Destroy(parent);
    }

    private static void Require(bool passed, string context)
    {
        if (!passed) throw new InvalidOperationException(context);
    }
}
#endif
