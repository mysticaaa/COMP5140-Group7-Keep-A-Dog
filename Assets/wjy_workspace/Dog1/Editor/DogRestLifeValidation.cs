#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Wjy.DogTraining;

public static class DogRestLifeValidation
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;

    public static void Run()
    {
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "CityDogRestLife-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Validate(folder);
            Debug.Log("DOG_REST_LIFE_OK assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Validate(string folder)
    {
        var autonomous = New(folder, "autonomous");
        Set(autonomous, "energy", 20f);
        Set(autonomous, "mood", 40f);
        var selfRest = autonomous.CompleteRest(false);
        Require(selfRest.Succeeded && Near(autonomous.Energy, 56f) && Near(autonomous.Mood, 45f),
            "A completed autonomous rest did not restore needs");
        Require(autonomous.TotalExperience == 0 && autonomous.Bones == 0 && RawRestCount(autonomous) == 0,
            "Autonomous rest advanced companionship tasks or issued a reward");
        Require(autonomous.GetCooldownRemaining(DogCareAction.Rest) == 0f,
            "Autonomous rest consumed the player's reward opportunity");

        string playerPath = Path.Combine(folder, "player.json");
        var player = new DogLifeProgress(playerPath);
        Set(player, "energy", 100f);
        Set(player, "mood", 99f);
        Require(player.GetBlockReason(DogCareAction.Rest) == "", "Full energy blocked a quiet rest");
        var first = player.Complete(DogCareAction.Rest);
        Require(first.Succeeded && first.ExperienceEarned == 8 && first.BonesEarned == 1,
            "The ordinary care entry did not use the unified rest reward");
        Require(player.Energy == 100f && player.Mood == 100f, "Rest needs were not capped at full");
        Require(Near(player.GetCooldownRemaining(DogCareAction.Rest), 50f), "Rest reward cooldown changed");

        Set(player, "energy", 20f);
        Set(player, "mood", 40f);
        var repeated = player.CompleteRest();
        Require(repeated.Succeeded && Near(player.Energy, 56f) && Near(player.Mood, 45f),
            "Reward cooldown prevented actual recovery");
        Require(repeated.ExperienceEarned == 0 && repeated.BonesEarned == 0 && player.TotalExperience == 8 && player.Bones == 1,
            "Repeated rest farmed reward during cooldown");
        Require(player.GetBlockReason(DogCareAction.Rest) == "" && RawRestCount(player) == 2,
            "Repeated successful player rest did not register as an interaction");
        for (int second = 0; second < 10; second++) player.Tick(2f);
        Require(Near(player.GetCooldownRemaining(DogCareAction.Rest), 30f), "Cooldown did not count down normally");
        player.CompleteRest(false);
        Require(Near(player.GetCooldownRemaining(DogCareAction.Rest), 30f), "Autonomous rest renewed reward cooldown");
        player.CompleteRest();
        Require(Near(player.GetCooldownRemaining(DogCareAction.Rest), 30f), "Repeated player rest renewed reward cooldown");
        var loaded = new DogLifeProgress(playerPath);
        Require(Near(loaded.Energy, player.Energy) && Near(loaded.Mood, player.Mood) && RawRestCount(loaded) == RawRestCount(player),
            "Rest recovery or task state did not survive reload");
        Require(loaded.CompleteRest().BonesEarned == 0, "Reload bypassed the saved reward cooldown");
        for (int second = 0; second < 16; second++) loaded.Tick(2f);
        var renewed = loaded.CompleteRest();
        Require(renewed.ExperienceEarned == 8 && renewed.BonesEarned == 1,
            "Reward did not become available when its original cooldown expired");

        var chapter = New(folder, "chapter");
        PrepareRestOnlyChapter(chapter, 2);
        Set(chapter, "energy", 100f);
        SetRestCooldown(chapter, 20f);
        Require(chapter.GetTaskProgress(DogCareAction.Rest) == 0 && chapter.CurrentChapter.Number == 3,
            "Rest task fixture was not on chapter three");
        var completion = chapter.CompleteRest();
        Require(completion.Succeeded && chapter.CurrentChapter.Number == 4 && chapter.TotalExperience == 122 && chapter.Bones == 14,
            "Full energy or reward cooldown prevented the rest task/chapter reward");
        int completedXp = chapter.TotalExperience;
        int completedBones = chapter.Bones;
        chapter.CompleteRest();
        Require(chapter.CurrentChapter.Number == 4 && chapter.GetTaskProgress(DogCareAction.Rest) == 1 &&
            chapter.TotalExperience == completedXp && chapter.Bones == completedBones,
            "A repeated rest duplicated the previous chapter's reward");

        var finale = New(folder, "finale");
        PrepareRestOnlyChapter(finale, 4);
        SetRestCooldown(finale, 40f);
        finale.CompleteRest(false);
        Require(!finale.ChapterFinished && finale.GetTaskProgress(DogCareAction.Rest) == 0,
            "Autonomous recovery completed the player's final companionship task");
        finale.CompleteRest();
        Require(finale.ChapterFinished && finale.TotalExperience == 130 && finale.Bones == 16,
            "The final rest could not complete the week while reward was cooling down");
        finale.CompleteRest();
        Require(finale.TotalExperience == 130 && finale.Bones == 16,
            "The final chapter reward was issued twice");
        var restoredFinale = new DogLifeProgress(Path.Combine(folder, "finale.json"));
        Require(restoredFinale.ChapterFinished && restoredFinale.TotalExperience == 130 && restoredFinale.Bones == 16,
            "The final rest result did not persist");

        var otherCare = New(folder, "other-care");
        Set(otherCare, "satiety", 100f);
        Set(otherCare, "hydration", 100f);
        Set(otherCare, "energy", 10f);
        Require(!otherCare.Complete(DogCareAction.Feed).Succeeded && !otherCare.Complete(DogCareAction.Water).Succeeded &&
            !otherCare.Complete(DogCareAction.Play).Succeeded && !otherCare.Complete(DogCareAction.Train).Succeeded,
            "Rest changes altered another care action's readiness rules");

        var training = New(folder, "training");
        var firstTraining = training.Complete(DogCareAction.Train);
        Require(firstTraining.Succeeded && firstTraining.ExperienceEarned == 16 && firstTraining.BonesEarned == 2,
            "Initial training reward changed");
        training.Tick(2f);
        float energyBefore = training.Energy;
        float moodBefore = training.Mood;
        float remaining = training.GetCooldownRemaining(DogCareAction.Train);
        var repeatedTraining = training.Complete(DogCareAction.Train);
        Require(repeatedTraining.Succeeded && Near(training.Energy, energyBefore - 6f) && Near(training.Mood, moodBefore + 7f) &&
            RawCount(training, DogCareAction.Train) == 2, "Reward cooldown swallowed a completed training interaction");
        Require(repeatedTraining.ExperienceEarned == 0 && repeatedTraining.BonesEarned == 0 &&
            Near(training.GetCooldownRemaining(DogCareAction.Train), remaining),
            "Repeated training renewed reward cooldown or farmed a reward");
        Set(training, "energy", 0f);
        Require(!training.Complete(DogCareAction.Train).Succeeded, "Low-energy training start was no longer blocked");
        int beforeCount = RawCount(training, DogCareAction.Train);
        Require(training.RecordCompletedCare(DogCareAction.Train).Succeeded &&
            RawCount(training, DogCareAction.Train) == beforeCount + 1,
            "A physically completed action was rejected by a second need check");
        Require(!training.RecordCompletedCare((DogCareAction)999).Succeeded,
            "The completion recorder accepted an unknown action");

        var trainingChapter = New(folder, "training-chapter");
        Set(trainingChapter, "chapterIndex", 1);
        Set(trainingChapter, "totalExperience", 100);
        Set(trainingChapter, "bones", 10);
        Set(trainingChapter, "visitedLocations", 7);
        Set(trainingChapter, "unlockedLocations", 7);
        var chapterCounts = (int[])Data(trainingChapter).GetType().GetField("chapterCounts").GetValue(Data(trainingChapter));
        chapterCounts[(int)DogCareAction.Walk] = 1;
        chapterCounts[(int)DogCareAction.Clean] = 1;
        ((float[])Data(trainingChapter).GetType().GetField("cooldowns").GetValue(Data(trainingChapter)))[(int)DogCareAction.Train] = 20f;
        trainingChapter.RecordCompletedCare(DogCareAction.Train);
        Require(trainingChapter.CurrentChapter.Number == 3 && trainingChapter.TotalExperience == 116 && trainingChapter.Bones == 13,
            "A completed training task could not finish its chapter during reward cooldown");
    }

    private static DogLifeProgress New(string folder, string name) => new DogLifeProgress(Path.Combine(folder, name + ".json"));
    private static object Data(DogLifeProgress life) => typeof(DogLifeProgress).GetField("data", Hidden).GetValue(life);
    private static void Set(DogLifeProgress life, string field, object value) => Data(life).GetType().GetField(field).SetValue(Data(life), value);
    private static int RawRestCount(DogLifeProgress life) => RawCount(life, DogCareAction.Rest);
    private static int RawCount(DogLifeProgress life, DogCareAction action) =>
        ((int[])Data(life).GetType().GetField("chapterCounts").GetValue(Data(life)))[(int)action];
    private static void SetRestCooldown(DogLifeProgress life, float value) =>
        ((float[])Data(life).GetType().GetField("cooldowns").GetValue(Data(life)))[(int)DogCareAction.Rest] = value;

    private static void PrepareRestOnlyChapter(DogLifeProgress life, int index)
    {
        Set(life, "chapterIndex", index);
        Set(life, "totalExperience", 100);
        Set(life, "bones", 10);
        Set(life, "unlockedLocations", 7);
        Set(life, "visitedLocations", 7);
        var counts = (int[])Data(life).GetType().GetField("chapterCounts").GetValue(Data(life));
        foreach (DogLifeTask task in life.CurrentChapter.Tasks)
            counts[(int)task.Action] = task.Action == DogCareAction.Rest ? 0 : task.Required;
    }

    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .001f;
    private static void Require(bool passed, string message)
    {
        assertions++;
        if (!passed) throw new InvalidOperationException(message);
    }
}
#endif
