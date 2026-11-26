#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog1TrainingModelValidation
{
    private const string RunKey = "Dog1TrainingModelValidation";
    private const string SaveFolderVariable = "KEEP_A_DOG_TEST_SAVES";
    private const string Example = "把屁股放到地上，前腿保持直立";
    private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static double deadline;
    private static HttpListener listener;

    static Dog1TrainingModelValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "CityDogTrainingModel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable(SaveFolderVariable, folder);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        SessionState.SetBool(RunKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(RunKey, false)) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 145;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Dog1 training model validation timed out."));
            return;
        }
        if (running || !EditorApplication.isPlaying) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        running = true;
        trainer.StartCoroutine(Guard(Check(trainer)));
    }

    private static IEnumerator Check(Dog1Training trainer)
    {
        Call(trainer, "BeginPlay");
        Set(trainer, "tab", 0);
        Set(trainer, "sessionKey", "");
        Set(trainer, "apiKey", "");
        Set(trainer, "nextAutonomy", float.MaxValue);
        trainer.dog.SetWanderingEnabled(false);
        trainer.endpoint = StartFixture();
        Get<Dog2SpeechInput>(trainer, "speech").transcriptionEndpoint = trainer.endpoint + "transcribe";
        trainer.model = "citydog-training-validation";

        string savePath = Path.Combine(Environment.GetEnvironmentVariable(SaveFolderVariable), "dog-training-skills.json");
        Require(File.Exists(savePath), "Dog1 did not initialize its training save.");
        AssertLegacyRatesReset(Path.Combine(Path.GetDirectoryName(savePath), "legacy-training-skills.json"));
        AssertRates(savePath, 40);
        string beginning = File.ReadAllText(savePath);

        yield return Exchange(trainer, Example, 40,
            Decision("sit", "train", "head_tilt", "麦粒坐在地上。", "麦粒把头轻轻歪向一边。", successRate: 40),
            expectedText: "麦粒把头轻轻歪向一边。", expectedRate: 40, expectTilt: true, sampleRoll: 41);
        Require(File.ReadAllText(savePath) == beginning, "A probability failure changed saved proficiency.");

        Set(trainer, "tab", 1);
        yield return Exchange(trainer, Example, 40,
            Decision("sit", "train", "sit", "不应显示的模型结果。", "不应显示的替代说明。", successRate: 40));
        Require(!TrainingResult(trainer).Contains("不应显示"), "The panel displayed an invalid model decision.");
        Require(File.ReadAllText(savePath) == beginning, "Invalid alternative changed saved proficiency.");

        yield return Exchange(trainer, Example, 40,
            Decision("sit", "train", "head_tilt", "本轮使用了错误的成功率。", "麦粒歪头。", successRate: 50),
            expectedText: "模型结果校验失败");
        Require(File.ReadAllText(savePath) == beginning, "A prediction made with the wrong probability changed the save.");

        yield return Exchange(trainer, Example, 40,
            Decision("sit", "train", "head_tilt", "麦粒坐在地上。", "麦粒歪头。", successRate: 40),
            expectedText: "没能取得本机抽样结果", sampleRoll: 101);
        Require(File.ReadAllText(savePath) == beginning, "An invalid draw changed the save.");

        yield return Exchange(trainer, "外面是不是要下雨了", 40,
            Decision("none", "unmatched", "none", "麦粒转头听了听。", ""),
            expectedText: "麦粒转头听了听。");
        Require(File.ReadAllText(savePath) == beginning, "An unmatched utterance changed saved proficiency.");

        yield return Exchange(trainer, Example, 40,
            Decision("sit", "train", "head_tilt", "麦粒收起后腿坐在地上。", "麦粒把头轻轻歪向一边。", successRate: 40),
            expectedText: "麦粒收起后腿坐在地上。", expectedRate: 50, expectSit: true, sampleRoll: 40);
        AssertRates(savePath, 50);
        Require(TrainingResult(trainer).Contains("40% → 50%"), "The training panel did not reflect the immediate increase.");
        string afterSuccess = File.ReadAllText(savePath);
        Call(trainer, "ApplyFeedback", true);
        Require(File.ReadAllText(savePath) == afterSuccess, "Button feedback incorrectly increased a skill rate.");

        const string praise = "刚才那一下做得挺稳的";
        const string praiseReply = "听到玩家夸奖刚完成的坐下。";
        yield return Exchange(trainer, praise, 50,
            Decision("sit", "praise", "none", praiseReply, ""),
            expectedText: praiseReply, expectedRate: 60, expectPraiseContext: true);
        AssertRates(savePath, 60);
        string improved = File.ReadAllText(savePath);

        yield return Exchange(trainer, praise, 60,
            Decision("sit", "praise", "none", praiseReply, ""),
            expectedText: "没有匹配的近期成功动作");
        Require(File.ReadAllText(savePath) == improved, "Duplicate praise changed a saved skill rate.");

        yield return Exchange(trainer, Example, 60,
            Decision("sit", "train", "head_tilt", "麦粒坐在地上。", "麦粒歪头。", successRate: 60),
            expectedRate: 70, expectSit: true, sampleRoll: 60);
        AssertRates(savePath, 70);
        string afterSecondSuccess = File.ReadAllText(savePath);
        object pending = Get<object>(trainer, "pendingTrainingPraise");
        pending.GetType().GetField("expiresAt").SetValue(pending, Time.unscaledTime - 1f);
        yield return Exchange(trainer, praise, 70,
            Decision("sit", "praise", "none", praiseReply, ""),
            expectedText: "没有匹配的近期成功动作");
        Require(File.ReadAllText(savePath) == afterSecondSuccess, "Late praise changed a saved skill rate.");

        yield return Exchange(trainer, Example, 70,
            Decision("sit", "train", "head_tilt", "麦粒坐在地上。", "麦粒歪头望着你。", successRate: 70),
            expectedText: "麦粒歪头望着你。", expectedRate: 70, expectTilt: true, sampleRoll: 71);
        Require(File.ReadAllText(savePath) == afterSecondSuccess, "A later failure changed the improved save.");
        AssertRates(savePath, 70);

        yield return CheckMovedTargets(trainer, savePath);
    }

    private static IEnumerator CheckMovedTargets(Dog1Training trainer, string savePath)
    {
        var world = Get<DogNeighborhood>(trainer, "world");
        var props = Get<Array>(trainer, "physicalProps");
        var ball = (Component)props.GetValue(0);
        Vector3 destination = world.ToyPosition + Vector3.left * .45f;
        trainer.dog.transform.SetPositionAndRotation(destination + Vector3.back, Quaternion.identity);
        var owner = new GameObject("Fixture fetch owner").transform;
        owner.position = destination + Vector3.back * 2.45f + Vector3.up * 1.65f;
        trainer.dog.user = owner;
        MoveProp(ball, destination + Vector3.up * .115f);
        yield return new WaitForSeconds(.4f);
        Vector3 movedBall = ball.transform.position;
        yield return SendPropCommand(trainer, "把玩具拿过来",
            Decision("fetch_toy", "train", "head_tilt", "麦粒把球叼到玩家面前。", "麦粒歪头。", 40, "ball"),
            "ball", movedBall, sampleRoll: 20);
        Require(new Dog1TrainingProgress(savePath).GetSuccessRate("fetch_toy") == 50 &&
            trainer.dog.LastFetchSucceeded &&
            Vector3.ProjectOnPlane(ball.transform.position - movedBall, Vector3.up).magnitude > .7f,
            "The ball must travel from its moved location and increase the skill rate after delivery.");

        object stats = Get<object>(Get<DogLifeProgress>(trainer, "life"), "data");
        foreach (bool food in new[] { true, false })
        {
            string skill = food ? "eat" : "drink";
            string target = food ? "food_bowl" : "water_bowl";
            string line = food ? "麦粒先去吃饭吧" : "麦粒去喝点水吧";
            string bowlName = food ? "FoodBowl" : "WaterBowl";
            var bowl = (Component)world.GetType().GetProperty(bowlName, NonPublicInstance).GetValue(world);
            var need = stats.GetType().GetField(food ? "satiety" : "hydration");
            string decision = Decision(skill, "success", "none", food ? "麦粒低头吃了几口。" : "麦粒低头喝水。", "", target: target);
            Vector3 newBowl = destination + Vector3.left * .4f;
            MoveProp(bowl, newBowl + Vector3.up * .07f);
            yield return new WaitForSeconds(.35f);
            Vector3 movedBowl = bowl.transform.position;
            need.SetValue(stats, 41f);
            Vector3 previous = trainer.dog.transform.position;
            yield return SendPropCommand(trainer, line, decision, target, movedBowl, expectBlockedNeed: true);
            Require(Vector3.Distance(previous, trainer.dog.transform.position) < .1f &&
                (float)need.GetValue(stats) < 42f && TrainingResult(trainer).Contains("暂时没有过去"),
                "Needs above 40 must prevent a bowl command even when the model approves it.");

            trainer.dog.transform.SetPositionAndRotation(movedBowl + Vector3.back * .9f, Quaternion.identity);
            need.SetValue(stats, 40f);
            yield return SendPropCommand(trainer, line, decision, target, movedBowl);
            Require((float)need.GetValue(stats) > 60f && TrainingResult(trainer).Contains("完成"),
                "The dog should use the moved bowl at a need value of 40 or below.");
        }
        UnityEngine.Object.Destroy(owner.gameObject);
    }

    private static void MoveProp(Component prop, Vector3 position)
    {
        var rigid = prop.GetComponent<Rigidbody>();
        rigid.position = position;
        rigid.velocity = Vector3.zero;
        rigid.angularVelocity = Vector3.zero;
        prop.transform.position = position;
        Physics.SyncTransforms();
    }

    private static IEnumerator SendPropCommand(Dog1Training trainer, string utterance, string decision,
        string target, Vector3 position, bool expectBlockedNeed = false, int? sampleRoll = null)
    {
        Task<ReceivedRequest> request = RespondOnce(decision, sampleRoll);
        Call(trainer, "SubmitVoiceTranscript", utterance);
        float until = Time.realtimeSinceStartup + 25f;
        while (Get<bool>(trainer, "busy") || !request.IsCompleted)
        {
            Require(Time.realtimeSinceStartup < until, "Model prop command did not finish.");
            yield return null;
        }
        var body = JsonUtility.FromJson<ModelRequest>(request.GetAwaiter().GetResult().Body);
        var context = JsonUtility.FromJson<TrainingInput>(body.messages.Single(message => message.role == "user").content);
        DogObservedObject seen = context.observations.Single(item => item.id == target);
        Require(Vector3.Distance(seen.position, position) < .09f && seen.settled,
            "Model input did not contain the current settled position of " + target + ".");
        if (target != "ball")
        {
            float need = target == "food_bowl" ? context.needs.satiety : context.needs.hydration;
            Require((need > context.needs.maximumToConsume) == expectBlockedNeed && context.needs.maximumToConsume == 40f,
                "Model input did not contain the current need and its 40-point gate.");
        }
    }

    private static IEnumerator Exchange(Dog1Training trainer, string utterance, int sentRate, string decision,
        string expectedText = null, int? expectedRate = null, bool expectSit = false, bool expectTilt = false,
        bool expectPraiseContext = false, int? sampleRoll = null)
    {
        Task<ReceivedRequest> request = RespondOnce(decision, sampleRoll);
        Call(trainer, "SubmitVoiceTranscript", utterance);
        Require(Get<string>(trainer, "caption").Contains(utterance), "The speech transcript did not reach Dog1.");
        bool sawSit = false, sawTilt = false;
        float until = Time.realtimeSinceStartup + 25f;
        while (Get<bool>(trainer, "busy") || !request.IsCompleted)
        {
            Require(Time.realtimeSinceStartup < until, "Model training round did not finish.");
            sawSit |= trainer.dog.IsPerforming && trainer.dog.CurrentAction == "sit";
            sawTilt |= trainer.dog.IsPlayingMicroAction &&
                ((DogMicroAction)typeof(DogCompanion).GetField("microAction", NonPublicInstance).GetValue(trainer.dog))?.Id == "head_tilt";
            yield return null;
        }
        ReceivedRequest received = request.GetAwaiter().GetResult();
        AssertPrompt(received, utterance, sentRate, expectPraiseContext);
        IList traces = Get<IList>(trainer, "trainingTraces");
        object latest = traces[0];
        Require(latest.GetType().GetField("Request").GetValue(latest).Equals(received.Body) &&
            latest.GetType().GetField("Output").GetValue(latest).Equals(decision),
            "The trace did not retain the actual request and model output.");
        if (expectPraiseContext)
            Require(latest.GetType().GetField("Effect").GetValue(latest).ToString().Contains("50% → 60%"),
                "The trace did not show the effect of praise on Unity's saved rate.");
        if (expectSit || expectTilt)
            Require(latest.GetType().GetField("Sampling").GetValue(latest).ToString().Contains("本机抽样 " + sampleRoll + "/100"),
                "The trace did not show the separate draw used for this action.");
        Require(sawSit == expectSit, "Requested sit did not match the validated outcome.");
        if (expectTilt) Require(sawTilt, "The selected failure gesture did not play.");
        string result = TrainingResult(trainer);
        if (expectedText != null)
            Require(result.Contains(expectedText), "Training panel did not show the model's result: " + result);
        if (expectedRate.HasValue)
            Require(result.Contains(expectedRate.Value + "%"), "Training panel did not show the current success rate: " + result);
    }

    private static void AssertRates(string path, int sit)
    {
        var restored = new Dog1TrainingProgress(path);
        Require(Dog1TrainingProgress.SkillIds.SequenceEqual(new[] { "sit", "crouch", "paw", "spin", "come", "fetch_toy" }),
            "The six expected skills must include approaching the player and exclude bowing.");
        foreach (string skill in Dog1TrainingProgress.SkillIds)
            Require(restored.GetSuccessRate(skill) == (skill == "sit" ? sit : 40), "Unexpected saved rate for " + skill + ".");
    }

    private static void AssertLegacyRatesReset(string path)
    {
        string[] skills = Dog1TrainingProgress.SkillIds.Select(id =>
        {
            int rate = id == "come" ? 35 : id == "spin" ? 95 : 20;
            return "{\"id\":\"" + id + "\",\"successRate\":" + rate + "}";
        }).ToArray();
        File.WriteAllText(path, "{\"version\":1,\"skills\":[" + string.Join(",", skills) + "]}");

        var reset = new Dog1TrainingProgress(path);
        Require(Dog1TrainingProgress.SkillIds.All(id => reset.GetSuccessRate(id) == 40), "Legacy rates did not reset to 40%.");
        for (int i = 0; i < 6; i++) reset.IncreaseRate("spin");
        Require(new Dog1TrainingProgress(path).GetSuccessRate("spin") == 90,
            "Praise did not cap at 90% or reload applied the reset again.");
    }

    private static string StartFixture()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        string address = "http://127.0.0.1:" + port + "/";
        listener = new HttpListener();
        listener.Prefixes.Add(address);
        listener.Start();
        return address;
    }

    private static async Task<ReceivedRequest> RespondOnce(string decision, int? sampleRoll)
    {
        string response = JsonUtility.ToJson(new ModelResponse
        {
            choices = new[] { new Choice { message = new Message { role = "assistant", content = decision } } }
        });
        HttpListenerContext exchange = await listener.GetContextAsync().ConfigureAwait(false);
        ReceivedRequest request;
        using (var reader = new StreamReader(exchange.Request.InputStream, Encoding.UTF8))
        {
            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            request = new ReceivedRequest
            {
                Body = body,
                HasAuthorization = !string.IsNullOrEmpty(exchange.Request.Headers["Authorization"])
            };
        }
        await WriteReply(exchange, response).ConfigureAwait(false);
        if (sampleRoll.HasValue)
        {
            int rate = JsonUtility.FromJson<TrainingDecision>(decision).successRate;
            HttpListenerContext sample = await listener.GetContextAsync().ConfigureAwait(false);
            using (var reader = new StreamReader(sample.Request.InputStream, Encoding.UTF8))
            {
                string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                Require(sample.Request.Url.AbsolutePath == "/training-draw" && body == "{\"successRate\":" + rate + "}",
                    "The local sampler did not receive the matched skill's exact rate.");
            }
            string status = sampleRoll.Value <= rate ? "success" : "failure";
            await WriteReply(sample, "{\"service\":\"wjy-dog1-training-draw-v1\",\"successRate\":" + rate +
                ",\"roll\":" + sampleRoll.Value + ",\"status\":\"" + status + "\"}").ConfigureAwait(false);
        }
        return request;
    }

    private static async Task WriteReply(HttpListenerContext exchange, string response)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(response);
        exchange.Response.ContentType = "application/json; charset=utf-8";
        exchange.Response.ContentLength64 = bytes.Length;
        await exchange.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        exchange.Response.Close();
    }

    private static void AssertPrompt(ReceivedRequest request, string utterance, int rate, bool expectPraiseContext)
    {
        Require(!request.HasAuthorization, "Dog1 attached a credential to the local validation request.");
        var body = JsonUtility.FromJson<ModelRequest>(request.Body);
        string input = body?.messages?.FirstOrDefault(message => message.role == "user")?.content;
        Require(!string.IsNullOrWhiteSpace(input), "Training model request is missing the player input.");
        var prompt = JsonUtility.FromJson<TrainingInput>(input);
        Require(prompt?.utterance == utterance, "Training prompt lost the utterance.");
        Require(!input.Contains("\"roll\":"), "Unity sent a sampled number instead of deferring the decision.");
        Require(prompt.skills != null && prompt.skills.Select(skill => skill.id).OrderBy(id => id)
            .SequenceEqual(Dog1TrainingProgress.SkillIds.OrderBy(id => id)), "Training prompt did not offer exactly six skills.");
        Require(prompt.skills.Single(skill => skill.id == "sit").successRate == rate, "Model did not receive the current saved rate.");
        if (expectPraiseContext)
            Require(prompt.praiseContext?.skill == "sit" && prompt.praiseContext.previousInstruction == Example,
                "The model did not receive the actual previous command and eligible action.");
        else
            Require(string.IsNullOrEmpty(prompt.praiseContext?.skill), "An ineligible action was sent as praise context.");
        Require(prompt.alternatives != null && prompt.alternatives.Select(action => action.id).Distinct().Count() == 31 &&
            prompt.alternatives.Any(action => action.id == "sit"),
            "The model needs 30 distinct executable alternatives after excluding the requested motion.");
    }

    private static string Decision(string skill, string status, string alternative, string description, string alternativeDescription,
        int successRate = 0, string target = null) =>
        JsonUtility.ToJson(new TrainingDecision
        {
            skill = skill, status = status, successRate = successRate, target = target, alternative = alternative, description = description,
            alternativeDescription = alternativeDescription
        });

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

    private static void Call(Dog1Training trainer, string method, params object[] arguments) =>
        typeof(Dog1Training).GetMethod(method, NonPublicInstance).Invoke(trainer, arguments);

    private static void Set(Dog1Training trainer, string field, object value) =>
        typeof(Dog1Training).GetField(field, NonPublicInstance).SetValue(trainer, value);

    private static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, NonPublicInstance).GetValue(target);

    private static string TrainingResult(Dog1Training trainer) =>
        (string)typeof(Dog1Training).GetProperty("TrainingResultText", NonPublicInstance).GetValue(trainer);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(Exception failure)
    {
        SessionState.SetBool(RunKey, false);
        listener?.Close();
        if (failure == null) Debug.Log("DOG1_TRAINING_MODEL_OK");
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }

    private sealed class ReceivedRequest { public string Body; public bool HasAuthorization; }
    [Serializable] private sealed class ModelRequest { public Message[] messages = null; }
    [Serializable] private sealed class ModelResponse { public Choice[] choices; }
    [Serializable] private sealed class Choice { public Message message; }
    [Serializable] private sealed class Message { public string role; public string content; }
    [Serializable] private sealed class TrainingDecision
    {
        public string skill, status, target, alternative, description, alternativeDescription;
        public int successRate;
    }
    [Serializable] private sealed class TrainingInput
    {
        public string utterance = null;
        public ModelAction[] skills = null, alternatives = null;
        public PraiseContext praiseContext = null;
        public DogObservedObject[] observations = null;
        public NeedsContext needs = null;
    }
    [Serializable] private sealed class NeedsContext
    {
        public float satiety, hydration, maximumToConsume;
    }
    [Serializable] private sealed class PraiseContext
    {
        public string skill = null, previousInstruction = null;
    }
    [Serializable] private sealed class ModelAction
    {
        public string id = null;
        public int successRate = 0;
    }
}
#endif
