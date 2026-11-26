using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private const int MaxPendingCommands = 3;
        private const float PendingCommandLifetime = 5f;
        private readonly List<PendingCommand> pendingCommands = new List<PendingCommand>();

        private sealed class PendingCommand
        {
            public CommandTrace Trace;
            public float ExpiresAt;
        }

        private bool HasPendingCommands => pendingCommands.Count > 0;
        private int PendingControllerActionCount =>
            queuedPlan == null ? 0 : Mathf.Max(0, queuedPlan.Length - nextQueuedAction);
        private int PendingActionCount => PendingControllerActionCount + pendingCommands.Count;
        private int AvailablePendingActionSlots => Mathf.Max(0, MaxPendingCommands - PendingActionCount);
        private int AvailableControllerPlanSlots => Mathf.Max(0, MaxPendingCommands - pendingCommands.Count);

        private string PendingCommandsText
        {
            get
            {
                if (!HasPendingCommands) return "";
                var text = new StringBuilder();
                float now = Time.realtimeSinceStartup;
                for (int i = pendingCommands.Count - 1; i >= 0; i--)
                {
                    PendingCommand command = pendingCommands[i];
                    if (text.Length > 0) text.AppendLine();
                    float expiresAt = Mathf.Min(command.ExpiresAt, command.Trace.ExpiresAt);
                    int remaining = Mathf.CeilToInt(Mathf.Max(0f, expiresAt - now));
                    text.Append(command.Trace.Input).Append(" · 剩余 ").Append(remaining).Append(" 秒");
                }
                return text.ToString();
            }
        }

        private void EnqueueCommand(CommandTrace trace)
        {
            if (trace == null || string.IsNullOrWhiteSpace(trace.Input)) return;
            ExpirePendingCommands();
            float now = Time.realtimeSinceStartup;
            trace.ExpiresAt = Mathf.Min(trace.ExpiresAt, now + PendingCommandLifetime);
            if (now >= trace.ExpiresAt)
            {
                trace.Result = "等待超过 5 秒，已取消。";
                return;
            }
            while (AvailablePendingActionSlots == 0 && HasPendingCommands)
            {
                pendingCommands[0].Trace.Result = "等待口令已满，已取消最早的一条。";
                pendingCommands.RemoveAt(0);
            }
            if (AvailablePendingActionSlots == 0)
            {
                trace.Result = "已有三个待执行动作，这条口令未加入等待。";
                return;
            }
            pendingCommands.Add(new PendingCommand
            {
                Trace = trace,
                ExpiresAt = trace.ExpiresAt
            });
            trace.Result = "已排队，等当前动作完成后优先处理最新口令，5 秒后过期。";
        }

        private bool TryRunPendingCommand()
        {
            ExpirePendingCommands();
            if (!Ready || VoiceListening || VoiceFinalizing || !HasPendingCommands) return false;
            int last = pendingCommands.Count - 1;
            PendingCommand command = pendingCommands[last];
            CommandTrace trace = command.Trace;
            trace.ExpiresAt = Mathf.Min(trace.ExpiresAt, command.ExpiresAt);
            if (Time.realtimeSinceStartup >= trace.ExpiresAt)
            {
                trace.Result = "等待超过 5 秒，已取消。";
                pendingCommands.RemoveAt(last);
                return false;
            }
            if (!SubmitCommand(trace.Input, trace)) return false;
            pendingCommands.RemoveAt(last);
            return true;
        }

        private void ExpirePendingCommands()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = pendingCommands.Count - 1; i >= 0; i--)
            {
                PendingCommand command = pendingCommands[i];
                if (now < Mathf.Min(command.ExpiresAt, command.Trace.ExpiresAt)) continue;
                command.Trace.Result = "等待超过 5 秒，已取消。";
                pendingCommands.RemoveAt(i);
            }
        }

        private void ClearPendingCommands()
        {
            foreach (PendingCommand command in pendingCommands)
                command.Trace.Result = "已取消等待中的口令。";
            pendingCommands.Clear();
        }
    }
}
