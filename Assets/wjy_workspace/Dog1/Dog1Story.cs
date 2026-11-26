using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using KeepADog.Demo;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private static readonly string[] InvitationOptions = { "陪它玩球", "陪它安静一会儿", "等一会儿再玩" };
        private static readonly string[] QuietOptions = { "轻声陪陪它", "给它留个安静角" };
        private static readonly string[] InvitationIds = { "play", "quiet", "later" };
        private const string InvitationInstructions =
            "You are a small virtual dog that just invited its owner to play. The player has replied. " +
            "Choose one exact ID from candidates. Choose play to fetch the ball, quiet to rest together, or later to leave space. " +
            "Honor the player's request and consider energy, water and the available ball. " +
            "Return only JSON {\"activity\":\"candidate_id\",\"reply\":\"brief Chinese line as the dog\"}. " +
            "Keep the reply under 36 characters. Do not claim an activity has already finished or add items or people.";

        private float quietStoryUntil, invitationResponseUntil;
        private float storyResponseUntil;
        private bool HasPlayInvitation => pendingInvitation && (CanRewardTrick || Time.unscaledTime < invitationResponseUntil);
        private bool HasQuietStory => Time.unscaledTime < quietStoryUntil;

        private string StoryTitle => !started ? "接麦粒回家" :
            life.ChapterFinished ? "第一周完成" : "第 " + life.CurrentChapter.Number + " 章 · " + life.CurrentChapter.Title;

        private string StoryPrompt => !started ? "挑一只狗狗，再一起看看新家。" :
            HasPlayInvitation ? "麦粒伏低前身，看了看玩具，又看了看你。你想怎么回应它？" :
            HasQuietStory ? "麦粒自己找了个地方歇下。你可以过去陪它，也可以给它留点空间。" :
            life.ChapterFinished ? "和麦粒想想明天。你想把哪段时间留给它？" : life.CurrentChapter.Description;

        private IReadOnlyList<string> StoryChoiceLabels => !Ready ? Array.Empty<string>() :
            HasPlayInvitation ? InvitationOptions : HasQuietStory ? QuietOptions : Array.Empty<string>();

        private string StoryProgress
        {
            get
            {
                if (life.ChapterFinished) return "小骨头和约定都留在手记里，可以继续自由生活。";
                DogLifeChapter chapter = life.CurrentChapter;
                var progress = new StringBuilder();
                foreach (DogLifeTask task in chapter.Tasks)
                {
                    if (progress.Length > 0) progress.Append("   ");
                    progress.Append(DogLifeProgress.GetActionName(task.Action)).Append(' ')
                        .Append(life.GetTaskProgress(task.Action)).Append('/').Append(task.Required);
                }
                if (chapter.RequiredLocation.HasValue)
                {
                    DogLocation place = chapter.RequiredLocation.Value;
                    progress.Append("\n到访").Append(DogLifeProgress.GetLocationName(place)).Append(life.HasVisited(place) ? " 1/1" : " 0/1");
                }
                return progress.ToString();
            }
        }

        private void ChooseStoryOption(int option)
        {
            if (!Ready || option < 0 || option >= StoryChoiceLabels.Count) return;
            if (HasPlayInvitation)
            {
                StartCoroutine(PerformInvitationChoice(InvitationIds[option], ""));
                return;
            }
            quietStoryUntil = 0f;
            storyResponseUntil = Time.unscaledTime + 6f;
            if (option == 0)
            {
                dog.AttendToUser(2.6f);
                reply = "麦粒听见你的声音，抬头看了看，又接着休息。";
            }
            else
            {
                reply = "麦粒眯着眼。你把这个角落留给了它。";
                nextAutonomy = Time.unscaledTime + 24f;
            }
        }

        private bool IsInvitationReply(string input)
        {
            if (!HasPlayInvitation || input.Contains("叼") || input.Contains("拿") || input.Contains("心情")) return false;
            return input.Contains("玩球") || input.Contains("陪你玩") || input.Contains("一起玩") || input.Contains("来玩") ||
                input.Contains("玩一会儿") || input.Contains("不玩") || input.Contains("再玩") ||
                input.Contains("休息") || input.Contains("安静");
        }

        private IEnumerator ResolveInvitationSpeech(string input)
        {
            bool offersPlay = input.Contains("玩") || input.Contains("球");
            bool offersQuiet = input.Contains("安静") || input.Contains("休息");
            bool asksPreference = (offersPlay && offersQuiet) || input.Contains("还是") || input.Contains("你想");
            string choice = input.Contains("等一会儿") || input.Contains("稍后") || input.Contains("不玩") ? "later" :
                !asksPreference && offersQuiet ? "quiet" : CanPlayNow() ? "play" : "quiet";
            string line = "";
            if (asksPreference && ModelConfigured)
            {
                var scene = CapturePerception();
                var context = new InvitationContext
                {
                    playerSentence = input.Length > 180 ? input.Substring(0, 180) : input,
                    visibleBall = scene.Find("ball")?.visible ?? false,
                    energy = Mathf.RoundToInt(life.Energy),
                    hydration = Mathf.RoundToInt(life.Hydration),
                    candidates = CanPlayNow() ? InvitationIds : new[] { "quiet", "later" }
                };
                reply = "麦粒看了看球，又听了听你。";
                storyResponseUntil = Time.unscaledTime + 14f;
                var request = DogLanguageModel.RequestJsonAsync(InvitationInstructions, JsonUtility.ToJson(context), endpoint, model, ModelKey);
                while (!request.IsCompleted) yield return null;
                var decision = request.Status == TaskStatus.RanToCompletion ? ParseInvitation(request.Result, context.candidates) : null;
                if (decision != null && HasPlayInvitation)
                {
                    choice = decision.activity;
                    line = decision.reply;
                }
            }
            if (choice == "play" && !CanPlayNow()) { choice = "quiet"; line = ""; }
            yield return PerformInvitationChoice(choice, line);
        }

        private bool CanPlayNow() => life.GetBlockReason(DogCareAction.Play).Length == 0 && dog.CanUsePhysicalBall;

        private IEnumerator PerformInvitationChoice(string choice, string modelLine)
        {
            ClearTrickReward();
            quietStoryUntil = 0f;
            storyResponseUntil = Time.unscaledTime + 6f;
            busy = true;
            try
            {
                if (choice == "play" && CanPlayNow())
                {
                    yield return Care(DogCareAction.Play);
                    if (dog.LastFetchSucceeded && modelLine.Length > 0) reply = modelLine + " · " + reply;
                }
                else if (choice == "later")
                {
                    reply = modelLine.Length > 0 ? modelLine : "麦粒收起玩具，先自己待一会儿。";
                    dog.AttendToUser(2f);
                }
                else
                {
                    reply = modelLine.Length > 0 ? modelLine : "麦粒在你旁边找了个位置，安静地趴下。";
                    yield return RestWithFurniture(nextRestInHouse);
                    if (life.GetBlockReason(DogCareAction.Rest).Length == 0) FinishCare(DogCareAction.Rest, dog.LastActivitySucceeded || HomeRestSpot(false) == null);
                }
            }
            finally
            {
                storyResponseUntil = Time.unscaledTime + 6f;
                busy = false;
                nextAutonomy = Time.unscaledTime + 24f;
            }
        }

        private static InvitationReply ParseInvitation(string content, string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Length > 300) return null;
            InvitationReply decision;
            try { decision = JsonUtility.FromJson<InvitationReply>(content); }
            catch (ArgumentException) { return null; }
            if (decision == null || Array.IndexOf(allowed, decision.activity) < 0 || string.IsNullOrWhiteSpace(decision.reply) || decision.reply.Length > 36) return null;
            decision.reply = decision.reply.Replace("<", "").Replace(">", "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return decision.reply.Length > 0 ? decision : null;
        }

        [Serializable]
        private sealed class InvitationContext
        {
            public string playerSentence;
            public bool visibleBall;
            public int energy, hydration;
            public string[] candidates;
        }

        [Serializable]
        private sealed class InvitationReply
        {
            public string activity = "", reply = "";
        }
    }
}
