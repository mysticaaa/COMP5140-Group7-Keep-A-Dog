using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private sealed class DogDemoAction
        {
            public readonly string Id, Label;
            public readonly IReadOnlyList<string> Tags;
            public readonly bool IsMicro;

            public DogDemoAction(string id, string label, bool isMicro, params string[] tags)
            {
                Id = id;
                Label = label;
                IsMicro = isMicro;
                Tags = tags;
            }
        }

        private static IReadOnlyList<DogDemoAction> DeveloperActions { get; } = BuildDeveloperActions();
        private bool DeveloperSequencePlaying { get; set; }
        private bool CanPreviewDeveloperAction => Ready && !VoiceListening && !VoiceFinalizing;

        private static IReadOnlyList<DogDemoAction> BuildDeveloperActions()
        {
            var actions = new List<DogDemoAction>();
            foreach (var micro in DogCompanion.MicroActions)
            {
                var tags = new List<string>(micro.Tags);
                actions.Add(new DogDemoAction(micro.Id, micro.Label, true, tags.ToArray()));
            }
            actions.AddRange(new[]
            {
                new DogDemoAction("walk_sample", "慢走", false, "locomotion", "walk"),
                new DogDemoAction("corner_sample", "走路转弯", false, "locomotion", "corner"),
                new DogDemoAction("trot_sample", "对角小跑", false, "locomotion", "trot"),
                new DogDemoAction("sit", "坐下", false, "training", "posture"),
                new DogDemoAction("crouch", "趴下", false, "training", "posture"),
                new DogDemoAction("paw", "抬右爪", false, "training", "gesture"),
                new DogDemoAction("spin", "口令转圈", false, "training", "spin"),
                new DogDemoAction("bow", "伏身邀玩", false, "play", "gesture"),
                new DogDemoAction("invite", "邀玩后等待", false, "play", "wait"),
                new DogDemoAction("pounce", "轻扑", false, "play", "clear_space"),
                new DogDemoAction("burst", "兴奋地跑", false, "play", "clear_space"),
                new DogDemoAction("sniff", "沿气味闻嗅", false, "explore", "sniff"),
                new DogDemoAction("rest", "伏地休息", false, "care", "rest"),
                new DogDemoAction("pet", "回应抚摸", false, "touch", "head"),
                new DogDemoAction("come", "靠近玩家", false, "attention", "approach"),
                new DogDemoAction("fetch_toy", "叼玩具", false, "toy", "fetch"),
                new DogDemoAction("fetch_lollipop", "叼棒棒糖毛绒玩具", false, "prop", "fetch"),
                new DogDemoAction("refuse", "疑惑歪头", false, "training", "confused")
            });
            return actions.AsReadOnly();
        }

        private void PreviewDeveloperAction(int index)
        {
            if (!CanPreviewDeveloperAction || index < 0 || index >= DeveloperActions.Count) return;
            StartCoroutine(RunDeveloperPreview(index, false));
        }

        private void PreviewDeveloperSequence()
        {
            if (DeveloperSequencePlaying)
            {
                DeveloperSequencePlaying = false;
                return;
            }
            if (CanPreviewDeveloperAction) StartCoroutine(RunDeveloperPreview(0, true));
        }

        private IEnumerator RunDeveloperPreview(int start, bool sequence)
        {
            DogCompanion companion = dog;
            bool wander = companion.WanderingEnabled;
            bool heldIdleGestures = companion.HoldIdleGestures;
            busy = true;
            DeveloperSequencePlaying = sequence;
            companion.HoldIdleGestures = true;
            companion.SetWanderingEnabled(false);
            try
            {
                int end = sequence ? DeveloperActions.Count : start + 1;
                for (int index = start; index < end; index++)
                {
                    if (sequence && !DeveloperSequencePlaying) break;
                    yield return RunDeveloperAction(companion, DeveloperActions[index]);
                    if (sequence) yield return new WaitForSeconds(.45f);
                }
            }
            finally
            {
                if (companion != null)
                {
                    companion.HoldIdleGestures = heldIdleGestures;
                    companion.SetWanderingEnabled(wander);
                }
                DeveloperSequencePlaying = false;
                busy = false;
                nextAutonomy = Time.unscaledTime + 18f;
            }
        }

        private IEnumerator RunDeveloperAction(DogCompanion companion, DogDemoAction action)
        {
            reply = "动作预览 · " + action.Label;
            yield return ExecuteDeveloperAction(companion, action, succeeded =>
            {
                if (!succeeded && action.IsMicro)
                    reply = "当前犬种或姿势无法演示「" + action.Label + "」。";
                else if (!succeeded && action.Id.EndsWith("_sample", StringComparison.Ordinal))
                    reply = "这里空间不够，请在公园的空地演示。";
            });
        }

        private IEnumerator ExecuteDeveloperAction(DogCompanion companion, DogDemoAction action, Action<bool> completed)
        {
            if (action.IsMicro)
            {
                float deadline = Time.time + 5.25f;
                while (!companion.TryPlayPreviewMicroAction(action.Id))
                {
                    if (Time.time >= deadline)
                    {
                        completed(false);
                        yield break;
                    }
                    yield return null;
                }
                float finish = Time.time + 4.3f;
                while (companion.IsPlayingMicroAction && Time.time < finish) yield return null;
                completed(!companion.IsPlayingMicroAction);
            }
            else if (action.Id.EndsWith("_sample", StringComparison.Ordinal))
            {
                yield return companion.PreviewTravel(action.Id);
                completed(companion.LastActivitySucceeded);
            }
            else
            {
                yield return companion.PerformAction(action.Id, .22f);
                completed(action.Id == "refuse" || DogActionSelection.Succeeded(companion, action.Id));
            }
        }

        private void DrawMotionPreview()
        {
            GUILayout.Label("动作开发者模式", heading);
            GUILayout.Label("逐项查看姿态、头颈动作和走路转弯。", small);
            if (view != null && GUILayout.Button("看向麦粒", button))
                view.LookAt(dog.PerceptionHead != null ? dog.PerceptionHead.position : dog.transform.position + Vector3.up * .45f);
            bool previous = GUI.enabled;
            GUI.enabled = previous && (CanPreviewDeveloperAction || DeveloperSequencePlaying);
            if (GUILayout.Button(DeveloperSequencePlaying ? "停止连播" : "连播全部动作", primary))
                PreviewDeveloperSequence();
            GUI.enabled = previous && CanPreviewDeveloperAction;
            for (int i = 0; i < DeveloperActions.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(DeveloperActions[i].Label, button)) PreviewDeveloperAction(i);
                if (i + 1 < DeveloperActions.Count && GUILayout.Button(DeveloperActions[i + 1].Label, button))
                    PreviewDeveloperAction(i + 1);
                GUILayout.EndHorizontal();
            }
            GUI.enabled = previous;
        }
    }
}
