#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using KeepADog.Demo;
using UnityEditor;
using UnityEngine;

public static class ControllerParserValidation
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static int assertions;
    private const string State = "{\"actionCatalog\":[" +
        "{\"id\":\"sit\",\"target\":\"none\",\"available\":true}," +
        "{\"id\":\"rest\",\"target\":\"none\",\"available\":true}," +
        "{\"id\":\"rest\",\"target\":\"house\",\"available\":false,\"reason\":\"没有狗屋\"}," +
        "{\"id\":\"head_tilt\",\"target\":\"owner\",\"available\":true}," +
        "{\"id\":\"fetch_toy\",\"target\":\"none\",\"available\":false,\"reason\":\"先喝水\"}," +
        "{\"id\":\"fetch_lollipop\",\"target\":\"none\",\"available\":true}]}";

    public static void Run()
    {
        try
        {
            Check();
            Debug.Log("DOG_CONTROLLER_PARSER_OK assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Check()
    {
        Type model = typeof(DogAgent).Assembly.GetType("KeepADog.Demo.DogLanguageModel", true);
        MethodInfo decode = model.GetMethod("ParseControllerResponse", Hidden);
        Require(decode != null, "Canonical controller parser is missing");
        object Parse(string json) => decode.Invoke(null, new object[] { json, State });

        object rest = Parse(Response("act", "好呀，我在这里歇一下。", Action("rest")));
        Require(rest != null && Actions(rest).Length == 1 && Id(Actions(rest).GetValue(0)) == "rest",
            "A valid rest action did not reach the controller plan");
        Require(Reply(rest) == "好呀，我在这里歇一下。", "Valid action lost its natural response");

        object mixed = Parse(Response("act", "我先看你，再坐下。", Action("head_tilt", "owner"), Action("sit")));
        Array plan = Actions(mixed);
        Require(plan.Length == 2 && Id(plan.GetValue(0)) == "head_tilt" && Id(plan.GetValue(1)) == "sit",
            "Mixed micro/full action plan changed order");

        foreach (string json in new[]
        {
            Response("act", "我先留在这里。", Action("teleport")),
            Response("act", "我先留在这里。", Action("sit", "owner")),
            Response("act", "我先留在这里。", Action("rest", "house")),
            Response("act", "我先留在这里。", Action("fetch_toy")),
            Response("act", "我先留在这里。", Action("sit"), Action("teleport")),
            Response("act", "我先留在这里。", Action("sit"), Action("sit"), Action("sit"), Action("sit")),
            Response("reply", "我先留在这里。", Action("sit")),
            Response("decline", "我先留在这里。", Action("sit"))
        })
        {
            object rejected = Parse(json);
            Require(rejected != null && Reply(rejected) == "我先留在这里。",
                "Rejected action discarded its valid natural response");
            Require(Actions(rejected).Length == 0 && !string.IsNullOrEmpty(Field<string>(rejected, "ValidationError")),
                "Rejected plan retained an action or omitted the validation error");
        }

        object decline = Parse(Response("decline", "我有点渴，喝口水再玩。"));
        Require(Actions(decline).Length == 0 && Reply(decline) == "我有点渴，喝口水再玩。",
            "Decline returned motion or lost its explanation");
        object chat = Parse(Response("reply", "我陪你坐一会儿。"));
        Require(Actions(chat).Length == 0 && Reply(chat) == "我陪你坐一会儿。",
            "Natural reply required an action");

        foreach (string json in new[] { "", "{}", "not JSON", "{\"decision\":\"act\",\"reply\":\"好\"}" })
            Require(Parse(json) == null || Actions(Parse(json)).Length == 0, "Malformed response initiated movement");

        MethodInfo legacy = model.GetMethod("ParseStateResponse", Hidden);
        Require(legacy != null, "Legacy state parser is missing");
        object compatible = legacy.Invoke(null, new object[]
        {
            "{\"status\":\"ready\",\"kind\":\"chat\",\"action\":\"none\",\"target\":\"none\",\"reply\":\"好呀，听到啦。\"}"
        });
        Require(compatible != null && Reply(compatible) == "好呀，听到啦。",
            "Old ready/chat output still discarded its safe natural response");

        foreach (string input in new[] { "你看到地上好像有个玩具都不掉过来", "把碗拿过来", "坐下" })
        {
            var dog = new DogAgent(Path.Combine(Path.GetTempPath(), "ControllerNoKeyword-" + Guid.NewGuid().ToString("N") + ".json"));
            var request = dog.DecideAsync(input, "invalid-endpoint", "controller-fixture", stateJson: State);
            Require(request.IsCompleted, "Invalid model endpoint should fail without waiting");
            DogAgentDecision decision = request.GetAwaiter().GetResult();
            Require(decision.Intent == DogIntent.Wait && !decision.IsRefusal && decision.Actions != null &&
                decision.Actions.Length == 0 && !decision.UsedModel,
                "Unavailable canonical model triggered a keyword action for " + input);
        }
    }

    private static Array Actions(object parsed) => Field<Array>(parsed, "Actions") ?? Array.Empty<object>();
    private static string Reply(object parsed) => Field<string>(parsed, "Reply");
    private static string Id(object action) => (string)action.GetType().GetField("id").GetValue(action);
    private static T Field<T>(object value, string name) => value == null ? default :
        (T)value.GetType().GetField(name).GetValue(value);
    private static string Action(string id, string target = "none") => "{\"id\":\"" + id + "\",\"target\":\"" + target + "\"}";
    private static string Response(string decision, string reply, params string[] actions) =>
        "{\"decision\":\"" + decision + "\",\"reply\":\"" + reply + "\",\"actions\":[" + string.Join(",", actions) + "]}";
    private static void Require(bool passed, string context)
    {
        assertions++;
        if (!passed) throw new InvalidOperationException(context);
    }
}
#endif
