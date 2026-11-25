using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed class DogMicroAction
    {
        public string Id { get; }
        public string Label { get; }
        public float Duration { get; }
        public IReadOnlyList<string> Tags { get; }

        internal DogMicroAction(string id, string label, float duration, params string[] tags)
        {
            Id = id;
            Label = label;
            Duration = duration;
            Tags = Array.AsReadOnly(tags);
        }
    }

    public sealed partial class DogCompanion
    {
        private static readonly DogMicroAction HeadTurn =
            new DogMicroAction("head_turn", "转头注视", 2.2f, "attention", "yaw", "orient", "stationary");
        private static readonly DogMicroAction HeadTilt =
            new DogMicroAction("head_tilt", "歪头倾听", 2.4f, "attention", "roll", "listen", "stationary");
        private static readonly DogMicroAction EarPerk =
            new DogMicroAction("ear_perk", "竖耳倾听", 1.4f, "attention", "ear", "listen", "stationary");
        private static readonly DogMicroAction EarFlick =
            new DogMicroAction("ear_flick", "单耳轻转", 1.05f, "ear", "orient", "stationary");
        private static readonly DogMicroAction ChestLean =
            new DogMicroAction("chest_lean", "侧身探看", 2.25f, "attention", "chest", "neck", "stationary");
        private static readonly DogMicroAction AirSniff =
            new DogMicroAction("air_sniff", "抬鼻轻嗅", 1.7f, "explore", "nose", "neck", "stationary");
        private static readonly DogMicroAction GazeExchange =
            new DogMicroAction("gaze_exchange", "看你再看玩具", 2.8f, "attention", "toy", "gaze", "stationary");
        private static readonly DogMicroAction SoftShake =
            new DogMicroAction("soft_shake", "轻摇头整理耳朵", 1.5f, "ear", "shake", "self_care", "stationary");
        private static readonly DogMicroAction GroomPaw =
            new DogMicroAction("groom_paw", "伏身舔前爪", 4.0f, "self_care", "groom", "paw", "stationary");
        private static readonly DogMicroAction CheckBehind =
            new DogMicroAction("check_behind", "侧头回望", 2.35f, "attention", "orient", "neck", "stationary");
        private static readonly DogMicroAction NoseToFloor =
            new DogMicroAction("nose_to_floor", "贴近地面闻嗅", 2.6f, "explore", "nose", "floor", "stationary");

        public static IReadOnlyList<DogMicroAction> MicroActions { get; } =
            Array.AsReadOnly(new[] { HeadTurn, HeadTilt, EarPerk, EarFlick, ChestLean, AirSniff, GazeExchange,
                SoftShake, GroomPaw, CheckBehind, NoseToFloor });

        private const float AttentionTiltDuration = 2.5f;
        private float attentionUntil, attentionAt = -100f, tiltAt = -100f, nextAttentionTilt;
        private bool cueHasStarted, tiltRequested;
        private float socialYaw, socialPitch, socialTilt, socialLean, socialChestYaw;
        private float leftEarYaw, rightEarYaw, leftEarPitch, rightEarPitch;
        private DogMicroAction microAction;
        private bool automaticMicroAction, previewMicroAction;
        private float microActionAt, nextQuietLook = -1f, microActionSide = 1f, microActionYaw;
        private int quietLookIndex;
        private float nextGroom;
        internal bool HoldIdleGestures { get; set; }
        internal string CurrentMicroActionLabel => IsPlayingMicroAction ? microAction.Label : "";

        public bool IsPlayingMicroAction => microAction != null &&
            Time.time - microActionAt < microAction.Duration;

        // Observational cues use the same head, neck and ear channels as the tagged micro-actions.
        public void AttendToUser(float seconds = 3f, bool allowTilt = true)
        {
            if (!Ready || user == null) return;
            if (Time.time >= attentionUntil)
            {
                cueHasStarted = false;
                tiltRequested = false;
                if (automaticMicroAction) microAction = null;
            }
            attentionUntil = Mathf.Max(attentionUntil, Time.time + Mathf.Clamp(seconds, 0f, 12f));
            tiltRequested |= allowTilt && Time.time >= nextAttentionTilt;
        }

        public bool TryPlayMicroAction(string id)
        {
            foreach (DogMicroAction action in MicroActions)
                if (action.Id == id) return TryPlayMicroAction(action, false);
            return false;
        }

        internal bool TryPlayPreviewMicroAction(string id)
        {
            foreach (DogMicroAction action in MicroActions)
                if (action.Id == id) return TryPlayMicroAction(action, false, preview: true);
            return false;
        }

        private bool TryPlayMicroAction(DogMicroAction action, bool automatic, bool preview = false)
        {
            if (!Ready || !CanPlayMicroPose() ||
                (IsPlayingMicroAction && (automatic || !automaticMicroAction))) return false;
            if ((action == EarPerk || action == EarFlick) &&
                (Bone("DEF-ear.01.L") == null || Bone("DEF-ear.01.R") == null)) return false;
            bool ownerInView = TryLookAngles(user, 6f, 88f, out Vector2 ownerAngles);
            if (!preview && action == GazeExchange && (!ownerInView || !TryLookAngles(ball, 4f, 72f, out _))) return false;

            microAction = action;
            automaticMicroAction = automatic;
            previewMicroAction = preview;
            microActionAt = Time.time;
            // Leave a pause after an explicit gesture before resuming the idle repertoire.
            if (!automatic) nextQuietLook = Mathf.Max(nextQuietLook, Time.time + action.Duration + 1f);
            microActionSide = ownerInView && Mathf.Abs(ownerAngles.x) > 8f
                ? Mathf.Sign(ownerAngles.x) : (quietLookIndex % 2 == 0 ? 1f : -1f);
            bool aimAtOwner = ownerInView && (automatic || Mathf.Abs(ownerAngles.x) > 10f);
            microActionYaw = aimAtOwner ? Mathf.Clamp(ownerAngles.x, -44f, 44f) : microActionSide * 27f;
            return true;
        }

        private bool CanPlayMicroPose() =>
            !IsPerforming && locomotionBlend < .12f && !carrying &&
            Mathf.Max(Mathf.Max(crouchBlend, bowBlend), Mathf.Max(pawBlend, pickupBlend)) < .1f;

        private static readonly DogMicroAction[] QuietActions = { HeadTurn, EarPerk, HeadTilt,
            GazeExchange, SoftShake, CheckBehind, GroomPaw, AirSniff, NoseToFloor, EarFlick };

        private float GroomLowering => microAction == GroomPaw && IsPlayingMicroAction
            ? Pulse(Time.time - microActionAt, .06f, 1f, 2.9f, 3.9f) : 0f;
        private float GroomReach => microAction == GroomPaw && IsPlayingMicroAction
            ? Pulse(Time.time - microActionAt, .70f, 1.37f, 2.77f, 3.55f) : 0f;

        private void ResetSocialAttention()
        {
            attentionUntil = nextAttentionTilt = 0f;
            attentionAt = tiltAt = -100f;
            cueHasStarted = tiltRequested = false;
            socialYaw = socialPitch = socialTilt = socialLean = socialChestYaw = 0f;
            leftEarYaw = rightEarYaw = leftEarPitch = rightEarPitch = 0f;
            microAction = null;
            automaticMicroAction = false;
            previewMicroAction = false;
            nextQuietLook = -1f;
            quietLookIndex = 0;
            nextGroom = 0f;
        }

        private void ApplySocialAttention(float dt, bool walking)
        {
            bool grooming = microAction == GroomPaw && IsPlayingMicroAction && !IsPerforming;
            bool compatible = !walking && (CanPlayMicroPose() || grooming ||
                CurrentAction == "sit" || CurrentAction == "pet" || CurrentAction == "refuse");
            Vector2 ownerAngles = default;
            bool ownerInView = compatible && TryLookAngles(user, 6f, 88f, out ownerAngles);
            bool liveCue = compatible && ownerInView && Time.time < attentionUntil;
            if (!IsPlayingMicroAction || !compatible || IsPerforming) microAction = null;
            if (microAction == null && compatible && CanPlayMicroPose() && !liveCue && !HoldIdleGestures)
                UpdateQuietGesture();

            UpperBodyPose pose = default;
            if (CurrentAction == "refuse")
            {
                float puzzled = Pulse(actionAge, 0f, .42f, 1.38f, 2.1f);
                pose.Aim = new Vector2(Mathf.Clamp(ownerAngles.x, -34f, 34f), ownerAngles.y) * puzzled;
                pose.Tilt = -21f * puzzled;
                pose.LeftEar.y = 4f * puzzled;
                pose.RightEar.y = -3f * puzzled;
            }
            else if (microAction != null)
            {
                pose = SampleMicroPose(ownerInView, ownerAngles);
            }
            else if (liveCue)
            {
                // Keep the cue until a stable pose can respond, even if it arrived mid-step.
                if (!cueHasStarted)
                {
                    attentionAt = Time.time;
                    cueHasStarted = true;
                }
                if (tiltRequested)
                {
                    tiltAt = Time.time;
                    nextAttentionTilt = Time.time + 8f;
                    tiltRequested = false;
                }
                // The ears orient first. The head acquires the target, then the chest follows.
                float cueAge = Time.time - attentionAt;
                float neckWeight = EaseBetween(.08f, .44f, cueAge) *
                    (1f - EaseBetween(-.45f, 0f, Time.time - attentionUntil));
                float petWeight = 1f - .65f * petBlend;
                pose.Aim = new Vector2(Mathf.Clamp(ownerAngles.x, -35f, 35f), ownerAngles.y) * neckWeight * petWeight;
                pose.Tilt = -15.5f * Pulse(Time.time - tiltAt, .14f, .52f, 1.60f, AttentionTiltDuration) * petWeight;
                float chestWeight = EaseBetween(.22f, .82f, cueAge) * neckWeight;
                pose.ChestYaw = pose.Aim.x * .065f * chestWeight;
                pose.Lean = Mathf.Clamp(ownerAngles.x / 36f, -1f, 1f) * 1.6f * chestWeight;
                float perk = 6f * Pulse(cueAge, 0f, .18f, .56f, 1.22f);
                float swivel = Mathf.Clamp(ownerAngles.x, -35f, 35f) * .15f * neckWeight;
                pose.LeftEar = new Vector2(swivel, perk);
                pose.RightEar = new Vector2(swivel * .72f, perk * .72f);
            }
            ApplyUpperBodyPose(pose, dt);
        }

        private void UpdateQuietGesture()
        {
            if (nextQuietLook < 0f)
            {
                nextQuietLook = Time.time + 1.4f;
                return;
            }
            if (Time.time < nextQuietLook) return;
            DogMicroAction gesture = QuietActions[quietLookIndex++ % QuietActions.Length];
            bool canSeeOwner = TryLookAngles(user, 4.5f, 82f, out _);
            if ((gesture == HeadTurn || gesture == HeadTilt) && !canSeeOwner) gesture = EarPerk;
            if (gesture == GroomPaw)
            {
                if (Time.time < nextGroom) gesture = AirSniff;
                else nextGroom = Time.time + 36f;
            }
            if (!TryPlayMicroAction(gesture, true) && gesture != AirSniff) TryPlayMicroAction(AirSniff, true);
            nextQuietLook = Time.time + gesture.Duration + 1.6f;
        }

        private UpperBodyPose SampleMicroPose(bool ownerInView, Vector2 ownerAngles)
        {
            float age = Time.time - microActionAt;
            float side = microActionSide;
            float settle = Pulse(age, .06f, .48f, microAction.Duration - .7f, microAction.Duration);
            var pose = new UpperBodyPose();

            if (microAction == HeadTurn)
            {
                bool lookingAtToy = ReadPresentedToyAngles(out Vector2 toyAngles);
                float turn = lookingAtToy ? Mathf.Clamp(toyAngles.x, -48f, 48f) : microActionYaw;
                float tilt = lookingAtToy ? toyAngles.y : ownerInView ? ownerAngles.y * .65f : 0f;
                pose.Aim = new Vector2(turn, tilt) * settle;
                pose.LeftEar.x = pose.RightEar.x = turn * .17f * Pulse(age, 0f, .18f, 1.15f, 1.9f);
            }
            else if (microAction == HeadTilt)
            {
                pose.Aim = new Vector2(Mathf.Clamp(ownerAngles.x, -28f, 28f), ownerAngles.y * .65f) * settle;
                pose.Tilt = -17f * Pulse(age, .22f, .64f, 1.64f, microAction.Duration);
                pose.LeftEar.y = 5f * Pulse(age, 0f, .2f, .58f, 1.35f);
                pose.RightEar.y = pose.LeftEar.y * .66f;
            }
            else if (microAction == EarPerk)
            {
                pose.LeftEar.y = 8f * Pulse(age, 0f, .19f, .66f, 1.2f);
                pose.RightEar.y = 8f * Pulse(age, .09f, .27f, .76f, microAction.Duration);
            }
            else if (microAction == EarFlick)
            {
                float flick = Pulse(age, .04f, .19f, .33f, .85f);
                Vector2 ear = new Vector2(side * 13f, -5f) * flick;
                if (side > 0f) pose.RightEar = ear;
                else pose.LeftEar = ear;
            }
            else if (microAction == ChestLean)
            {
                float turn = Mathf.Clamp(microActionYaw, -38f, 38f);
                float chest = Pulse(age, .29f, .82f, 1.47f, microAction.Duration);
                pose.Aim = new Vector2(turn, ownerInView ? ownerAngles.y * .65f : -5f) * settle;
                pose.ChestYaw = side * 3f * chest;
                pose.Lean = side * 3.2f * chest;
            }
            else if (microAction == AirSniff)
            {
                float first = Pulse(age, .08f, .38f, .48f, .73f);
                float second = Pulse(age, .66f, .94f, 1.06f, 1.45f);
                pose.Aim = new Vector2(side * 6f * settle, -7f * first - 5f * second);
                pose.LeftEar.y = pose.RightEar.y = 2f * settle;
            }
            else if (microAction == GazeExchange)
            {
                if (!ownerInView && !previewMicroAction) return pose;
                if (!TryLookAngles(ball, 4f, 72f, out Vector2 toyAngles))
                {
                    if (!previewMicroAction) return pose;
                    toyAngles = new Vector2(-side * 28f, 11f);
                }
                float toyWeight = Pulse(age, .72f, 1.18f, 1.52f, 2.05f);
                pose.Aim = Vector2.Lerp(ownerAngles, toyAngles, toyWeight) * settle;
                pose.Aim.x = Mathf.Clamp(pose.Aim.x, -44f, 44f);
            }
            else if (microAction == SoftShake)
            {
                float shake = Mathf.Sin(age * 14f) * Pulse(age, .10f, .28f, .78f, 1.25f);
                pose.Aim.x = 13f * shake;
                pose.Tilt = 7f * shake;
                pose.LeftEar.x = -8f * shake;
                pose.RightEar.x = -8f * shake;
            }
            else if (microAction == GroomPaw)
            {
                float reach = GroomReach;
                float lick = Mathf.Sin(age * 12f) * Pulse(age, 1.39f, 1.65f, 2.66f, 2.85f);
                pose.Aim = new Vector2(side * 14f * reach, 45f * reach + 3.7f * lick);
                pose.Tilt = side * 7f * reach;
                pose.Lean = -side * 1.7f * reach;
            }
            else if (microAction == CheckBehind)
            {
                pose.Aim = new Vector2(side * 51f, ownerInView ? ownerAngles.y * .65f : -3f) * settle;
                pose.ChestYaw = side * 5f * settle;
                pose.Lean = side * 2f * settle;
                pose.LeftEar.x = pose.RightEar.x = side * 9f * settle;
            }
            else if (microAction == NoseToFloor)
            {
                float low = Pulse(age, .12f, .65f, 1.8f, 2.48f);
                pose.Aim = new Vector2(side * 7f * low + Mathf.Sin(age * 5f) * 5f * low, 33f * low);
                pose.ChestYaw = side * 1.2f * low;
            }
            return pose;
        }

        private bool TryLookAngles(Transform target, float range, float maxBearing, out Vector2 angles)
        {
            angles = Vector2.zero;
            if (target == null || head == null) return false;
            Vector3 toward = target.position - head.position;
            float distance = new Vector2(toward.x, toward.z).magnitude;
            if (distance < .25f || distance > range) return false;
            float yaw = HeadingChange(transform.forward, toward);
            if (Mathf.Abs(yaw) >= maxBearing) return false;
            float pitch = -Mathf.Clamp(Mathf.Atan2(toward.y, distance) * Mathf.Rad2Deg, -15f, 20f);
            angles = new Vector2(yaw, pitch);
            return true;
        }

        private void ApplyUpperBodyPose(UpperBodyPose pose, float dt)
        {
            socialYaw = Blend(socialYaw, pose.Aim.x, dt, 6f);
            socialPitch = Blend(socialPitch, pose.Aim.y, dt, 5f);
            socialTilt = Blend(socialTilt, pose.Tilt, dt, 7f);
            socialLean = Blend(socialLean, pose.Lean, dt, 5f);
            socialChestYaw = Blend(socialChestYaw, pose.ChestYaw, dt, 5f);
            leftEarYaw = Blend(leftEarYaw, pose.LeftEar.x, dt, 14f);
            rightEarYaw = Blend(rightEarYaw, pose.RightEar.x, dt, 14f);
            leftEarPitch = Blend(leftEarPitch, pose.LeftEar.y, dt, 14f);
            rightEarPitch = Blend(rightEarPitch, pose.RightEar.y, dt, 14f);

            // Add only rotations before foot IK. Front supports keep their existing targets.
            if (Mathf.Abs(socialYaw) + Mathf.Abs(socialChestYaw) > .01f)
            {
                Yaw("DEF-chest", socialChestYaw);
                Yaw("DEF-neck.01", socialYaw * .40f - socialChestYaw);
                Yaw("DEF-neck.02", socialYaw * .26f);
                Yaw("DEF-head", socialYaw * .34f);
            }
            if (Mathf.Abs(socialPitch) > .01f)
            {
                AddPlayPitch("DEF-neck.01", socialPitch * .29f);
                AddPlayPitch("DEF-neck.02", socialPitch * .27f);
                AddPlayPitch("DEF-head", socialPitch * .44f);
            }
            if (Mathf.Abs(socialLean) + Mathf.Abs(socialTilt) > .01f)
            {
                ApplyGaitRoll(Bone("DEF-chest"), socialLean);
                ApplyGaitRoll(Bone("DEF-neck.02"), socialTilt * .18f - socialLean * .5f);
                ApplyGaitRoll(head, socialTilt * .82f);
            }
            ApplyEar("DEF-ear.01.L", "DEF-ear.02.L", leftEarYaw, leftEarPitch);
            ApplyEar("DEF-ear.01.R", "DEF-ear.02.R", rightEarYaw, rightEarPitch);
        }

        private void ApplyEar(string earBase, string earTip, float yaw, float pitch)
        {
            if (Mathf.Abs(yaw) + Mathf.Abs(pitch) < .01f) return;
            Yaw(earBase, yaw);
            AddPlayPitch(earBase, pitch);
            Yaw(earTip, yaw * .22f);
            AddPlayPitch(earTip, pitch * .28f);
        }

        private static float Pulse(float age, float start, float rise, float hold, float end) =>
            EaseBetween(start, rise, age) * (1f - EaseBetween(hold, end, age));

        private struct UpperBodyPose
        {
            public Vector2 Aim, LeftEar, RightEar;
            public float Tilt, Lean, ChestYaw;
        }
    }
}
