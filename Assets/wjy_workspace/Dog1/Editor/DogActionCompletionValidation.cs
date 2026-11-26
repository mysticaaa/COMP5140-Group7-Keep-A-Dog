#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogActionCompletionValidation
{
    private const string Armed = "CityDogActionCompletionValidation";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static double deadline;
    private static int assertions;

    static DogActionCompletionValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string saves = Path.Combine(Path.GetTempPath(), "CityDogCompletion-" + Guid.NewGuid().ToString("N"));
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
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 45;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Action completion probe timed out")); return; }
        if (running) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        running = true;
        trainer.StartCoroutine(Guard(Check(trainer)));
    }

    private static IEnumerator Check(Dog1Training trainer)
    {
        Call(trainer, "BeginPlay");
        trainer.enabled = false;
        var dog = trainer.dog;
        dog.SetWanderingEnabled(false);
        dog.enabled = false;
        var profile = (DogLifeProgress)typeof(Dog1Training).GetField("life", Hidden).GetValue(trainer);
        var cases = new Dictionary<string, string>
        {
            {"fetch_toy", "LastFetchSucceeded"}, {"fetch_lollipop", "LastFetchSucceeded"},
            {"come", "LastComeSucceeded"}, {"pounce", "LastPlaySucceeded"},
            {"burst", "LastPlaySucceeded"}, {"invite", "LastPlaySucceeded"}, {"sniff", "LastPlaySucceeded"},
            {"rest", "LastRestSucceeded"}, {"crouch", "LastRestSucceeded"}
        };
        foreach (bool alreadyPerforming in new[] { false, true })
        {
            foreach (var test in cases)
            {
                Set(dog, "Ready", alreadyPerforming);
                Set(dog, "IsPerforming", alreadyPerforming);
                Set(dog, test.Value, true);
                Set(dog, "LastCompletedAction", test.Key);
                IEnumerator action = dog.PerformAction(test.Key, 0f);
                Require(!action.MoveNext(), "An unavailable action unexpectedly started: " + test.Key);
                Require(!(bool)Get(dog, test.Value) && dog.LastCompletedAction == null,
                    "An early exit retained an earlier success: " + test.Key);
                Require(!Succeeded(dog, test.Key), "A skipped action was reported complete: " + test.Key);
            }
        }

        foreach (var test in cases)
        {
            Set(dog, test.Value, true);
            Set(dog, "LastCompletedAction", "different-action");
            Require(!Succeeded(dog, test.Key), "A success flag bypassed the completed action identity: " + test.Key);
            Set(dog, "LastCompletedAction", test.Key);
            Require(Succeeded(dog, test.Key), "A matching successful completion was rejected: " + test.Key);
        }
        Set(dog, "Ready", false);
        Set(dog, "IsPerforming", false);
        Set(dog, "LastFetchSucceeded", true);
        Set(dog, "LastCompletedAction", "fetch_toy");
        float energyBefore = profile.Energy;
        int experienceBefore = profile.TotalExperience;
        int playBefore = profile.GetTaskProgress(DogCareAction.Play);
        yield return (IEnumerator)Call(trainer, "Care", DogCareAction.Play);
        Require(profile.TotalExperience == experienceBefore && profile.GetTaskProgress(DogCareAction.Play) == playBefore &&
            Mathf.Abs(profile.Energy - energyBefore) < .001f, "A skipped care fetch changed play progress or needs");

        Set(dog, "LastComeSucceeded", true);
        Set(dog, "LastCompletedAction", "come");
        int wasteBefore = profile.WasteCount;
        yield return (IEnumerator)Call(trainer, "FinishWalk");
        Require(profile.TotalExperience == experienceBefore && profile.WasteCount == wasteBefore &&
            Mathf.Abs(profile.Energy - energyBefore) < .001f, "A skipped arrival completed the walk and created waste");
        Set(dog, "Ready", true);
        Debug.Log("DOG_ACTION_COMPLETION_OK assertions=" + assertions);
    }

    private static bool Succeeded(DogCompanion dog, string motion) => (bool)typeof(DogCompanion).Assembly
        .GetType("Wjy.DogTraining.DogActionSelection", true).GetMethod("Succeeded").Invoke(null, new object[] { dog, motion });
    private static object Call(Dog1Training trainer, string method, params object[] args) =>
        typeof(Dog1Training).GetMethod(method, Hidden).Invoke(trainer, args);
    private static object Get(DogCompanion dog, string name) => typeof(DogCompanion)
        .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(dog);
    private static void Set(DogCompanion dog, string name, object value) => typeof(DogCompanion)
        .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetSetMethod(true).Invoke(dog, new[] { value });

    private static IEnumerator Guard(IEnumerator test)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(test);
        while (stack.Count > 0)
        {
            IEnumerator stage = stack.Peek();
            bool more = false;
            object current = null;
            Exception failure = null;
            try { more = stage.MoveNext(); if (more) current = stage.Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) { Finish(failure); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (current is IEnumerator child) stack.Push(child);
            else yield return current;
        }
        Finish(null);
    }

    private static void Require(bool passed, string message)
    {
        assertions++;
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
