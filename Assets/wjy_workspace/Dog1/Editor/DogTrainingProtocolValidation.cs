#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using KeepADog.Demo;
using UnityEditor;
using UnityEngine;
using Wjy.DogTraining;

public static class DogTrainingProtocolValidation
{
    private const BindingFlags HiddenStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags HiddenInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Line = "我们试试看，做完后再告诉我。";
    private static int assertions;
    private static MethodInfo decode;
    private static string state;

    public static void Run()
    {
        try
        {
            assertions = 0;
            CheckProtocol();
            CheckAgentAndProgress();
            Debug.Log("DOG_TRAINING_PROTOCOL_OK assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckProtocol()
    {
        Type model = typeof(DogAgent).Assembly.GetType("KeepADog.Demo.DogLanguageModel", true);
        decode = model.GetMethod("ParseControllerResponse", HiddenStatic);
        Require(decode != null, "Canonical controller parser is missing");
        string catalog = "";
        foreach (string skill in Dog1TrainingProgress.SkillIds) catalog += Entry(skill);
        foreach (string id in new[] { "rest", "refuse", "invite", "burst", "fetch_lollipop", "eat", "drink" }) catalog += Entry(id);
        foreach (DogMicroAction micro in DogCompanion.MicroActions)
        {
            string target = micro.Id == "gaze_exchange" ? "ball" :
                micro.Id == "head_turn" || micro.Id == "head_tilt" || micro.Id == "chest_lean" ? "owner" : "none";
            catalog += Entry(micro.Id, target);
        }
        state = "{\"actionCatalog\":[" + catalog.Substring(1) + "],\"training\":{\"praiseAvailable\":true}}";

        foreach (string metadata in new[] { null, "null", "{\"mode\":\"none\"}" })
        {
            object parsed = Parse(Response("act", metadata, Action("rest")));
            Require(Valid(parsed, 1) && (Directive(parsed) == null || Directive(parsed).mode == "none"),
                "Old canonical plans must remain non-probabilistic when training is absent/null/none");
            Require(Valid(Parse(Response("reply", metadata)), 0), "Ordinary conversation lost compatibility");
        }

        foreach (string skill in Dog1TrainingProgress.SkillIds)
        {
            object parsed = Parse(Response("act", Practice(), Action(skill)));
            DogTrainingDirective training = Directive(parsed);
            Require(Valid(parsed, 1) && training?.mode == "practice" && training.alternative.id == "head_tilt" &&
                training.alternative.target == "owner" && training.failureReply == "这次没有跟上，再试一次吧。",
                "A registered single skill did not retain valid practice metadata: " + skill);
        }
        Require(Valid(Parse(Response("act", Practice("refuse", "none"), Action("sit"))), 1),
            "The registered stationary confusion response must be a valid alternative");
        foreach (DogMicroAction micro in DogCompanion.MicroActions)
        {
            string target = micro.Id == "gaze_exchange" ? "ball" :
                micro.Id == "head_turn" || micro.Id == "head_tilt" || micro.Id == "chest_lean" ? "owner" : "none";
            Require(Valid(Parse(Response("act", Practice(micro.Id, target), Action("sit"))), 1),
                "An available stationary micro action was rejected: " + micro.Id);
        }

        foreach (string sincere in new[] { "true", "false" })
        {
            object praise = Parse(Response("reply", "{\"mode\":\"praise\",\"sincere\":" + sincere + "}"));
            Require(Valid(praise, 0) && Directive(praise)?.mode == "praise" && Directive(praise).sincere == (sincere == "true"),
                "A typed praise decision was not preserved");
        }
        foreach (string metadata in new[]
        {
            "true", "false", "123", "\"practice\"", "[]", "{}", "{\"mode\":null}", "{\"mode\":true}",
            "{\"mode\":1}", "{\"mode\":\"PRACTICE\"}", "{\"mode\":\"unknown\"}", "{\"mode\":\"\"}",
            "{\"mode\":\"none\",\"sincere\":true}", "{\"mode\":\"none\",\"unknown\":1}",
            "{\"mode\":\"praise\"}", "{\"mode\":\"praise\",\"sincere\":\"true\"}",
            "{\"mode\":\"praise\",\"sincere\":1}", "{\"mode\":\"praise\",\"sincere\":null}",
            "{\"mode\":\"praise\",\"sincere\":true,\"extra\":0}", "{\"mode\":\"practice\"}",
            Practice().Replace("\"alternative\":{\"id\":\"head_tilt\",\"target\":\"owner\"}", "\"alternative\":null"),
            Practice().Replace("\"alternative\":{", "\"alternative\":[{").Replace("},\"failureReply\"", "}],\"failureReply\""),
            Practice().Replace("\"id\":\"head_tilt\"", "\"id\":null"),
            Practice().Replace("\"target\":\"owner\"", "\"target\":true"),
            Practice().Replace("\"id\":\"head_tilt\"", "\"id\":\"head_tilt\",\"extra\":1"),
            Practice().Replace("\"failureReply\":\"这次没有跟上，再试一次吧。\"", "\"failureReply\":false"),
            Practice().Replace("这次没有跟上，再试一次吧。", ""),
            Practice().Replace("这次没有跟上，再试一次吧。", new string('a', 101)),
            Practice().Replace("\"mode\":\"practice\"", "\"mode\":\"practice\",\"sincere\":true"),
            Practice().Replace("\"mode\":\"practice\"", "\"mode\":\"practice\",\"mode\":\"none\"")
        }) Rejected(Parse(Response("act", metadata, Action("sit"))), "Malformed training metadata: " + metadata);

        foreach (string id in new[] { "rest", "invite", "burst", "eat", "drink", "fetch_lollipop", "head_tilt" })
            Rejected(Parse(Response("act", Practice(), Action(id, id == "head_tilt" ? "owner" : "none"))),
                "Practice must not turn everyday or unsupported actions into six-skill training: " + id);
        foreach (string id in new[] { "rest", "invite", "burst", "come", "sit", "fetch_toy", "fetch_lollipop", "teleport" })
            Rejected(Parse(Response("act", Practice(id, "none"), Action("sit"))), "Unsafe or unregistered alternative: " + id);
        Rejected(Parse(Response("act", Practice("head_tilt", "ball"), Action("sit"))), "Wrong alternative target");
        Rejected(Parse(Response("act", Practice(), Action("sit"), Action("paw"))), "A two-action plan cannot be one practice trial");
        Rejected(Parse(Response("reply", Practice())), "Practice cannot attach to conversation");
        Rejected(Parse(Response("decline", Practice())), "Practice cannot attach to decline");
        Rejected(Parse(Response("act", "{\"mode\":\"praise\",\"sincere\":true}", Action("sit"))), "Praise must not execute an action");
        Rejected(Parse(Response("decline", "{\"mode\":\"praise\",\"sincere\":true}")), "Praise requires a reply decision");
        string blocked = state.Replace("\"id\":\"head_tilt\",\"target\":\"owner\",\"available\":true",
            "\"id\":\"head_tilt\",\"target\":\"owner\",\"available\":false");
        Rejected(Parse(Response("act", Practice(), Action("sit")), blocked), "An unavailable alternative cannot be promised");
        blocked = state.Replace("\"id\":\"sit\",\"target\":\"none\",\"available\":true",
            "\"id\":\"sit\",\"target\":\"none\",\"available\":false");
        Rejected(Parse(Response("act", Practice(), Action("sit")), blocked), "Practice cannot bypass current action availability");
        string duplicate = Response("act", Practice(), Action("sit")).Replace("\"training\":", "\"training\":null,\"training\":");
        Rejected(Parse(duplicate), "Duplicate training member");
        duplicate = Response("act", Practice(), Action("sit")).Replace("\"mode\":\"practice\"", "\"mo\\u0064e\":\"none\",\"mode\":\"practice\"");
        Rejected(Parse(duplicate), "Escaped duplicate field name");
        string conditional = Response("act", Practice(), Action("fetch_toy")).Replace("\"decision\":", "\"learningCondition\":\"owner_sad\",\"decision\":");
        Require(Valid(Parse(conditional), 1) && Field<string>(Parse(conditional), "LearningCondition") == "owner_sad",
            "Practice must retain an explicitly selected conditional lesson");
        foreach (string id in new[] { "eat", "drink" })
            Require(Valid(Parse(Response("act", null, Action(id))), 1), "Dynamic care actions must remain outside probability training");
    }

    private static void CheckAgentAndProgress()
    {
        string saveRoot = Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES");
        Require(!string.IsNullOrWhiteSpace(saveRoot), "Use an explicit isolated test-save directory");
        string directory = Path.Combine(saveRoot, "training-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        MethodInfo process = typeof(DogAgent).GetMethod("Process", HiddenInstance);
        DogAgentDecision Apply(DogAgent agent, object decoded) =>
            (DogAgentDecision)process.Invoke(agent, new[] { (object)"我们练一次坐下", decoded });

        var practiceAgent = new DogAgent(Path.Combine(directory, "practice-memory.json"));
        var decision = Apply(practiceAgent, Parse(Response("act", Practice(), Action("sit"))));
        Require(decision.Intent == DogIntent.Sit && !decision.IsRefusal && decision.Training?.mode == "practice",
            "A valid practice trial must bypass the old first-command automatic refusal");
        Require(!practiceAgent.CanReward, "A practice plan must not count as actual success");
        practiceAgent.ReportExecution(decision, true);
        Require(practiceAgent.CanReward, "Actual success must open the existing memory feedback gate");
        var praise = Apply(practiceAgent, Parse(Response("reply", "{\"mode\":\"praise\",\"sincere\":true}")));
        Require(praise.Training?.sincere == true && practiceAgent.CanReward &&
            practiceAgent.LongTermSummary.Contains("还没有"), "Model praise must not grant or consume feedback before backend window validation");
        var oldAgent = new DogAgent(Path.Combine(directory, "legacy-memory.json"));
        Require(Apply(oldAgent, Parse(Response("act", null, Action("sit")))).IsRefusal,
            "Omitted training metadata unexpectedly changed old training compatibility");

        string memoryPath = Path.Combine(directory, "conditional-memory.json");
        var conditionalAgent = new DogAgent(memoryPath);
        string lesson = Response("act", Practice(), Action("fetch_toy")).Replace("\"decision\":", "\"learningCondition\":\"owner_sad\",\"decision\":");
        var conditional = Apply(conditionalAgent, Parse(lesson));
        Require(!conditional.IsRefusal && !conditionalAgent.LongTermSummary.Contains("你心情不好时"),
            "Planning a conditional trial must not immediately teach the memory");
        conditionalAgent.ReportExecution(conditional, true);
        conditionalAgent.Feedback(true);
        Require(new DogAgent(memoryPath).LongTermSummary.Contains("你心情不好时"),
            "Actual conditional practice success plus confirmed feedback must persist the existing memory");

        string progressPath = Path.Combine(directory, "skills.json");
        var progress = new Dog1TrainingProgress(progressPath);
        foreach (string skill in Dog1TrainingProgress.SkillIds)
            Require(progress.GetSuccessRate(skill) == 40, "New skills must start at 40 percent");
        Require(progress.IncreaseRate("sit") == 50 && progress.IncreaseRate("sit") == 60,
            "Confirmed success and separate confirmed praise should each add ten points");
        Require(new Dog1TrainingProgress(progressPath).GetSuccessRate("sit") == 60, "Practice rates did not persist");
        for (int count = 0; count < 8; count++) progress.IncreaseRate("sit");
        Require(progress.GetSuccessRate("sit") == 90 && new Dog1TrainingProgress(progressPath).GetSuccessRate("sit") == 90,
            "Practice probability must stop at 90 percent");
        Require(progress.GetSuccessRate("paw") == 40, "One skill's success must not change another skill");
        File.WriteAllText(progressPath, "corrupt");
        Require(new Dog1TrainingProgress(progressPath).GetSuccessRate("sit") == 80,
            "Corrupt current progress must recover the valid backup");
    }

    private static object Parse(string json, string snapshot = null) => decode.Invoke(null, new object[] { json, snapshot ?? state });
    private static string Entry(string id, string target = "none") => ",{" + "\"id\":\"" + id + "\",\"target\":\"" + target + "\",\"available\":true}";
    private static string Action(string id, string target = "none") => "{\"id\":\"" + id + "\",\"target\":\"" + target + "\"}";
    private static string Practice(string id = "head_tilt", string target = "owner") =>
        "{\"mode\":\"practice\",\"alternative\":" + Action(id, target) + ",\"failureReply\":\"这次没有跟上，再试一次吧。\"}";
    private static string Response(string decision, string training, params string[] actions) =>
        "{\"reply\":\"" + Line + "\",\"decision\":\"" + decision + "\",\"actions\":[" + string.Join(",", actions) + "]" +
        (training == null ? "" : ",\"training\":" + training) + "}";
    private static DogTrainingDirective Directive(object parsed) => Field<DogTrainingDirective>(parsed, "Training");
    private static T Field<T>(object value, string name) => value == null ? default : (T)value.GetType().GetField(name).GetValue(value);
    private static bool Valid(object result, int actions) => result != null && Field<Array>(result, "Actions")?.Length == actions &&
        string.IsNullOrEmpty(Field<string>(result, "ValidationError")) && Field<string>(result, "Reply") == Line;
    private static void Rejected(object result, string context)
    {
        Require(result != null && Field<string>(result, "Reply") == Line, context + ": safe natural reply was lost");
        Require(Field<Array>(result, "Actions")?.Length == 0 && Directive(result) == null &&
            !string.IsNullOrEmpty(Field<string>(result, "ValidationError")), context + ": an invalid plan retained actions or training");
    }
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
