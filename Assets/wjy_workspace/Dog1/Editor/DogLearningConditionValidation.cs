#if UNITY_EDITOR
using System;
using System.Reflection;
using KeepADog.Demo;
using UnityEditor;
using UnityEngine;

public static class DogLearningConditionValidation
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static int assertions;
    private static readonly string[] Primitives =
        { "sit", "crouch", "spin", "come", "paw", "bow", "pounce", "sniff", "fetch_toy", "fetch_lollipop" };
    private const string Line = "我们先试一次，做对了再表扬我。";

    public static void Run()
    {
        try
        {
            Check();
            Debug.Log("DOG_LEARNING_CONDITION_OK assertions=" + assertions);
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
        Require(decode != null, "The canonical controller parser is missing");
        string catalog = "";
        foreach (string id in Primitives)
        {
            if (catalog.Length > 0) catalog += ",";
            catalog += "{\"id\":\"" + id + "\",\"target\":\"none\",\"available\":true}";
        }
        foreach (string id in new[] { "rest", "invite", "pet", "burst" })
            catalog += ",{\"id\":\"" + id + "\",\"target\":\"none\",\"available\":true}";
        catalog += ",{\"id\":\"head_tilt\",\"target\":\"owner\",\"available\":true}";
        string state = "{\"actionCatalog\":[" + catalog + "]}";
        object Parse(string json, string context = null) => decode.Invoke(null, new object[] { json, context ?? state });

        foreach (string id in Primitives)
        {
            object lesson = Parse(Response("act", "\"owner_sad\"", Action(id)));
            Require(lesson != null && Field<string>(lesson, "LearningCondition") == "owner_sad" &&
                Actions(lesson).Length == 1 && Id(Actions(lesson).GetValue(0)) == id &&
                Field<string>(lesson, "Reply") == Line && string.IsNullOrEmpty(Field<string>(lesson, "ValidationError")),
                "A permitted conditional primitive did not survive parsing: " + id);
        }

        foreach (string noCondition in new[] { null, "\"none\"", "null" })
        {
            object ordinary = Parse(Response("act", noCondition, Action("rest")));
            if (ordinary == null || Actions(ordinary).Length != 1 || Field<string>(ordinary, "LearningCondition") != "none")
                Debug.Log("DOG_LEARNING_COMPAT_DIAGNOSTIC condition=" + (noCondition ?? "omitted") +
                    " decoded=" + (ordinary == null ? "null" : "actions=" + Actions(ordinary).Length +
                    " learning=" + Field<string>(ordinary, "LearningCondition") + " error=" + Field<string>(ordinary, "ValidationError")));
            Require(ordinary != null && Actions(ordinary).Length == 1 &&
                Field<string>(ordinary, "LearningCondition") == "none", "An ordinary action lost compatibility without a lesson");
            object chat = Parse(Response("reply", noCondition));
            Require(chat != null && Actions(chat).Length == 0 && Field<string>(chat, "LearningCondition") == "none" &&
                Field<string>(chat, "Reply") == Line, "An ordinary reply acquired a learning condition");
        }

        foreach (string invalidCondition in new[] { "\"owner_hungry\"", "\"\"", "\"OWNER_SAD\"", "\"owner_sad \"", "true", "123", "{}", "[]" })
            Rejected(Parse(Response("act", invalidCondition, Action("sit"))), "Invalid learning value: " + invalidCondition);
        Rejected(Parse(Response("reply", "\"owner_sad\"")), "Chat must not create a lesson");
        Rejected(Parse(Response("decline", "\"owner_sad\"")), "A refusal must not create a lesson");
        Rejected(Parse(Response("act", "\"owner_sad\"")), "An empty plan must not create a lesson");
        Rejected(Parse(Response("act", "\"owner_sad\"", "null")), "A null action must not create a lesson");
        Rejected(Parse(Response("act", "\"owner_sad\"", Action("sit"), Action("paw"))),
            "A multi-step plan must not create a single-action lesson");
        foreach (string id in new[] { "rest", "invite", "pet", "burst", "teleport" })
            Rejected(Parse(Response("act", "\"owner_sad\"", Action(id))), "Unsupported conditional action: " + id);
        Rejected(Parse(Response("act", "\"owner_sad\"", Action("head_tilt", "owner"))),
            "A micro action must not create a primitive lesson");
        Rejected(Parse(Response("act", "\"owner_sad\"", Action("sit", "owner"))),
            "A conditional lesson must not bypass target validation");
        string unavailable = state.Replace("\"id\":\"fetch_toy\",\"target\":\"none\",\"available\":true",
            "\"id\":\"fetch_toy\",\"target\":\"none\",\"available\":false,\"reason\":\"玩具尚未落稳\"");
        Rejected(Parse(Response("act", "\"owner_sad\"", Action("fetch_toy")), unavailable),
            "A lesson must not bypass current availability validation");
    }

    private static void Rejected(object result, string context)
    {
        Require(result != null && Field<string>(result, "Reply") == Line,
            context + ": the safe natural reply was lost");
        Require(Actions(result).Length == 0 && Field<string>(result, "LearningCondition") != "owner_sad" &&
            !string.IsNullOrWhiteSpace(Field<string>(result, "ValidationError")),
            context + ": rejected control retained an action/lesson or no diagnostic");
    }

    private static Array Actions(object value) => Field<Array>(value, "Actions") ?? Array.Empty<object>();
    private static string Id(object action) => (string)action.GetType().GetField("id").GetValue(action);
    private static T Field<T>(object value, string name) => value == null ? default :
        (T)value.GetType().GetField(name).GetValue(value);
    private static string Action(string id, string target = "none") =>
        "{\"id\":\"" + id + "\",\"target\":\"" + target + "\"}";
    private static string Response(string decision, string condition, params string[] actions) =>
        "{\"reply\":\"" + Line + "\",\"decision\":\"" + decision + "\"" +
        (condition == null ? "" : ",\"learningCondition\":" + condition) +
        ",\"actions\":[" + string.Join(",", actions) + "]}";
    private static void Require(bool passed, string message)
    {
        assertions++;
        if (!passed) throw new InvalidOperationException(message);
    }
}
#endif
