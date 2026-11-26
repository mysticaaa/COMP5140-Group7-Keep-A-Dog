using System.Collections;
using System.Text;
using KeepADog.Demo;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private DogAgentAction[] queuedPlan;
        private int nextQueuedAction;
        private float queuedPlanExpiresAt;
        private const float ActionQueueWindowSeconds = 5f;

        private string ActionPlanQueueText
        {
            get
            {
                if (queuedPlan == null || nextQueuedAction >= queuedPlan.Length) return "";
                var text = new StringBuilder();
                for (int index = nextQueuedAction; index < queuedPlan.Length; index++)
                {
                    if (text.Length > 0) text.AppendLine();
                    text.Append(queuedPlan[index].id).Append(" → ").Append(queuedPlan[index].target);
                }
                return text.Append("\n剩余等待 ").Append(Mathf.Max(0, queuedPlanExpiresAt - Time.realtimeSinceStartup)
                    .ToString("0.0")).Append(" 秒").ToString();
            }
        }

        private bool CancelExpiredCommand()
        {
            if (activeCommandTrace == null || Time.realtimeSinceStartup < activeCommandTrace.ExpiresAt) return false;
            agent.CancelPendingDecision();
            reply = "等待超过 5 秒，已取消这次口令。";
            activeCommandTrace.Actions.Add("等待超过 5 秒，取消待执行动作。");
            return true;
        }

        private IEnumerator WaitForControllerPose(float expiresAt = float.PositiveInfinity)
        {
            float settleUntil = Mathf.Min(Time.realtimeSinceStartup + 5.1f, expiresAt);
            while (dog.Ready && !dog.CanOfferMicroAction && Time.realtimeSinceStartup < settleUntil) yield return null;
        }

        private IEnumerator ExecuteControllerPlan(DogAgentDecision decision)
        {
            if (decision.Actions.Length == 0) yield break;
            if (decision.Actions.Length > 3)
            {
                activeCommandTrace?.Actions.Add("计划校验未通过 · 最多暂存三个动作");
                reply = "这组动作超过三个，未执行。请分开告诉麦粒。";
                yield break;
            }
            if (CancelExpiredCommand()) yield break;
            if (decision.Actions.Length > AvailableControllerPlanSlots)
            {
                activeCommandTrace?.Actions.Add("等待窗口已满，未执行这组动作；最多暂存三个动作。");
                reply = "等待窗口已满，这组动作没有执行。等当前动作做完再告诉麦粒吧。";
                agent.ReportExecution(decision, false);
                yield break;
            }
            queuedPlan = decision.Actions;
            nextQueuedAction = 0;
            queuedPlanExpiresAt = Mathf.Min(Time.realtimeSinceStartup + ActionQueueWindowSeconds,
                activeCommandTrace?.ExpiresAt ?? float.PositiveInfinity);
            try { yield return RunControllerPlan(decision); }
            finally { queuedPlan = null; nextQueuedAction = 0; }
        }

        private IEnumerator RunControllerPlan(DogAgentDecision decision)
        {
            if (!string.IsNullOrEmpty(decision.ModelReply)) reply = decision.ModelReply;
            var routine = new string[decision.Actions.Length];
            bool practice = IsTrainingPractice(decision);
            if (practice) pendingTrainingSkill = null;

            for (int index = 0; index < decision.Actions.Length; index++)
            {
                DogAgentAction action = decision.Actions[index];
                yield return WaitForControllerPose(queuedPlanExpiresAt);
                if (Time.realtimeSinceStartup >= queuedPlanExpiresAt)
                {
                    activeCommandTrace?.Actions.Add("等待超过五秒，取消剩余动作。");
                    reply = index > 0 ? "刚才的动作完成了，后续动作等待超过 5 秒，已取消。需要时请再告诉麦粒。" :
                        "这组动作等待超过 5 秒，已取消。请再告诉麦粒。";
                    agent.ReportExecution(decision, false);
                    yield break;
                }
                var current = CaptureCommandState();
                if (!DogCommandCatalog.CanExecute(current.actionCatalog, action.id, action.target, out string reason))
                {
                    activeCommandTrace?.Actions.Add(action.id + " · 场景校验阻塞 · " + reason);
                    reply = reason;
                    agent.ReportExecution(decision, false);
                    if (practice) yield return TrainingAlternative(decision, reason);
                    yield break;
                }
                DogCommandOption option = DogCommandCatalog.Find(current.actionCatalog, action.id, action.target);
                nextQueuedAction = index + 1;

                if (practice && !DrawTraining(action.id))
                {
                    yield return TrainingAlternative(decision, "");
                    yield break;
                }

                activeCommandTrace?.Actions.Add(action.id + " · 开始 · 目标 " + action.target);
                Debug.Log("[Dog1 Agent] 模型返回动作：" + action.id);
                bool succeeded;
                if (action.id == "eat" || action.id == "drink")
                {
                    yield return ConsumeFromBowl(action.id == "eat" ? DogCareAction.Feed : DogCareAction.Water);
                    succeeded = lastBowlCareSucceeded;
                }
                else if (DogCommandCatalog.IsMicroAction(action.id))
                {
                    succeeded = dog.TryPlayMicroAction(action.id);
                    if (succeeded)
                        while (dog.IsPlayingMicroAction) yield return null;
                }
                else if (action.id == "rest" && action.target != "none")
                {
                    yield return RestWithFurniture(action.target == "house");
                    succeeded = dog.LastRestSucceeded;
                }
                else
                {
                    yield return dog.PerformAction(action.id, .55f);
                    succeeded = action.id == "rest" || action.id == "crouch" ? dog.LastRestSucceeded :
                        action.id == "refuse" || (action.id == "burst" || action.id == "invite"
                            ? dog.LastCompletedAction == action.id && dog.LastPlaySucceeded : DogActionSelection.Succeeded(dog, action.id));
                }

                activeCommandTrace?.Actions.Add(action.id + (succeeded ? " · 完成" : " · 未完成"));
                if (!succeeded)
                {
                    reply = "麦粒没能完成「" + option.label + "」，先停一会儿。";
                    agent.ReportExecution(decision, false);
                    if (practice) yield return TrainingAlternative(decision, "");
                    yield break;
                }

                routine[index] = action.id;
                if (action.id == "crouch" || (action.id == "rest" && action.target == "none"))
                    RecordRestCompletion();
                if (action.id == "fetch_toy") FinishCare(DogCareAction.Play, true);
                if (action.id == "bow" || action.id == "invite") OpenPlayInvitation(preserveReply: true);
                if (practice) CompleteTraining(action.id, decision);
            }

            agent.ReportExecution(decision, true);
            if (DogRoutines.Valid(routine))
            {
                OpenTrickReward();
                pendingName = DogRoutines.CueName(activeCommandTrace?.Input);
                pendingRoutine = routine;
            }
            if (practice) reply = TrainingResultText;
            else if (!string.IsNullOrEmpty(decision.ModelReply)) reply = decision.ModelReply;
        }
    }
}
