using System;
using System.Collections;
using KeepADog.Demo;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private Dog1TrainingProgress trainingProgress;
        private string pendingTrainingSkill;
        private DogAgentDecision pendingTrainingDecision;
        private float lastTrainingSuccessAt, trainingPraiseUntil;
        private float voiceStartedAt = -1f;
        // Editor regression can choose a draw without changing global animation randomness.
        private Func<int> trainingDrawOverride;
        private string TrainingResultText { get; set; } = "";
        private bool HasTrainingPraise => !string.IsNullOrEmpty(pendingTrainingSkill);

        [Serializable]
        private sealed class TrainingSkillState
        {
            public string id;
            public int successRate;
        }

        [Serializable]
        private sealed class TrainingContext
        {
            public TrainingSkillState[] skills;
            public bool practiceRequested, praiseAvailable;
            public string requestedSkill, lastSuccessfulSkill;
        }

        private TrainingContext CaptureTrainingContext()
        {
            var skills = new TrainingSkillState[Dog1TrainingProgress.SkillIds.Count];
            for (int i = 0; i < skills.Length; i++)
            {
                string id = Dog1TrainingProgress.SkillIds[i];
                skills[i] = new TrainingSkillState { id = id, successRate = TrainingProbability(id) };
            }
            float began = activeCommandTrace?.InputStartedAt ?? Time.realtimeSinceStartup;
            return new TrainingContext
            {
                skills = skills,
                practiceRequested = activeCommandTrace?.PracticeRequested == true,
                requestedSkill = activeCommandTrace?.PracticeSkill ?? "",
                praiseAvailable = CanRewardTraining(began),
                lastSuccessfulSkill = pendingTrainingSkill ?? ""
            };
        }

        private int TrainingProbability(string id) => trainingProgress?.GetSuccessRate(id) ?? 40;

        private static string TrainingLabel(string id)
        {
            switch (id)
            {
                case "sit": return "坐下";
                case "crouch": return "趴下";
                case "paw": return "抬右爪";
                case "spin": return "转圈";
                case "come": return "过来";
                case "fetch_toy": return "叼球";
                default: return id;
            }
        }

        private bool SubmitTrainingCue(string skillId)
        {
            if (!CanSubmitInput || !Dog1TrainingProgress.IsSkill(skillId)) return false;
            CommandTrace trace = AddCommandTrace(TrainingLabel(skillId), null, localControls: true);
            trace.PracticeRequested = true;
            trace.PracticeSkill = skillId;
            if (HasPendingCommands || !SubmitCommand(trace.Input, trace)) EnqueueCommand(trace);
            return true;
        }

        private DogAgentDecision LocalPracticeDecision(string skill)
        {
            // Only fixed, trusted button labels use this local path; free speech always uses the model.
            DogAgentDecision decision = agent.Decide(TrainingLabel(skill));
            if (decision.IsRefusal) decision = agent.Decide(TrainingLabel(skill));
            decision.Actions = new[] { new DogAgentAction { id = skill, target = "none" } };
            decision.ControllerDecision = "act";
            decision.Training = new DogTrainingDirective
            {
                mode = "practice", alternative = new DogAgentAction { id = "refuse", target = "none" },
                failureReply = "麦粒歪头看着你，这次还没跟上。再陪它练一次吧。"
            };
            return decision;
        }

        private bool IsTrainingPractice(DogAgentDecision decision) => decision.Training?.mode == "practice";

        private bool DrawTraining(string skill)
        {
            InvalidateTrainingFeedback();
            int rate = TrainingProbability(skill);
            int roll = trainingDrawOverride != null ? trainingDrawOverride() : UnityEngine.Random.Range(1, 101);
            bool valid = roll >= 1 && roll <= 100;
            activeCommandTrace?.Actions.Add("训练抽样 · " + skill + " · 成功率 " + rate + "% · 抽样 " +
                (valid ? roll.ToString() : "无效"));
            return valid && roll <= rate;
        }

        private IEnumerator TrainingAlternative(DogAgentDecision decision, string reason)
        {
            if (CancelExpiredCommand())
            {
                agent.ReportExecution(decision, false);
                yield break;
            }
            DogTrainingDirective training = decision.Training;
            DogAgentAction alternative = training?.alternative;
            var current = CaptureCommandState();
            if (alternative != null && DogCommandCatalog.CanExecute(current.actionCatalog,
                    alternative.id, alternative.target, out _))
            {
                bool played = false;
                if (DogCommandCatalog.IsMicroAction(alternative.id))
                {
                    played = dog.TryPlayMicroAction(alternative.id);
                    if (played) while (dog.IsPlayingMicroAction) yield return null;
                }
                else if (alternative.id == "refuse")
                {
                    yield return dog.PerformAction("refuse", .2f);
                    played = dog.LastCompletedAction == "refuse";
                }
                activeCommandTrace?.Actions.Add("训练备选 · " + alternative.id + (played ? " · 完成" : " · 未完成"));
            }
            TrainingResultText = string.IsNullOrWhiteSpace(training?.failureReply)
                ? "麦粒这次没能完成练习，先歇一会儿。" : training.failureReply;
            if (!string.IsNullOrEmpty(reason)) TrainingResultText += " " + reason;
            reply = TrainingResultText;
            agent.ReportExecution(decision, false);
        }

        private void CompleteTraining(string skill, DogAgentDecision decision)
        {
            int rate = trainingProgress.IncreaseRate(skill);
            pendingTrainingSkill = skill;
            pendingTrainingDecision = decision;
            lastTrainingSuccessAt = Time.realtimeSinceStartup;
            trainingPraiseUntil = lastTrainingSuccessAt + 12f;
            TrainingResultText = "麦粒完成了「" + TrainingLabel(skill) + "」。成功率 " + rate +
                "%。现在可以表扬它。";
            activeCommandTrace?.Actions.Add("训练完成 · 成功率 " + rate + "% · 表扬窗口 12 秒");
        }

        private bool CanRewardTraining(float began) => HasTrainingPraise &&
            began >= lastTrainingSuccessAt && began <= trainingPraiseUntil;

        private void InvalidateTrainingFeedback()
        {
            agent?.DiscardFeedback(pendingTrainingDecision);
            agent?.DiscardCompletedFeedback();
            pendingTrainingDecision = null;
            pendingTrainingSkill = null;
            trainingPraiseUntil = 0f;
        }

        private bool RewardTraining(float began)
        {
            if (!CanRewardTraining(began)) return false;
            string skill = pendingTrainingSkill;
            pendingTrainingSkill = null;
            pendingTrainingDecision = null;
            int rate = trainingProgress.IncreaseRate(skill);
            dog.PlayTailWag();
            TrainingResultText = "麦粒听到你的表扬，尾巴摇了起来。「" + TrainingLabel(skill) + "」成功率 " + rate + "% 。";
            activeCommandTrace?.Actions.Add("训练表扬 · 已强化一次 · 成功率 " + rate + "%");
            return true;
        }

        private void ResolveTrainingPraise(DogAgentDecision decision)
        {
            float began = activeCommandTrace?.InputStartedAt ?? Time.realtimeSinceStartup;
            if (decision.Training.sincere && CanRewardTraining(began))
            {
                ApplyFeedback(true);
                if (!string.IsNullOrEmpty(decision.Reply)) reply = decision.Reply + " " + TrainingResultText;
            }
            else
            {
                reply = decision.Reply;
                activeCommandTrace?.Actions.Add(decision.Training.sincere
                    ? "训练表扬 · 已过期或没有本次成功记录，未强化" : "训练表扬 · 未确认表扬，未强化");
            }
        }
    }
}
