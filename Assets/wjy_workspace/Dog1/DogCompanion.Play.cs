using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private const float PounceDuration = 1.14f;
        private const float SniffDuration = 1.36f;
        private const float BurstDuration = 2.25f;
        private const float BurstLength = 2.4f;
        private const float BurstTurn = 105f;
        private Vector3 burstOrigin;
        private Quaternion burstHeading;
        private int burstDirection;
        private bool burstStopped;
        private float playBowTurnEnd;
        private bool playBowContactsCaptured;

        private const float PlayBowBrace = .17f;
        private const float PlayBowDropEnd = .60f;
        private float PlayBowAge => actionAge - playBowTurnEnd - PlayBowBrace;
        private float PlayBowHoldEnd => CurrentAction == "invite" ? 1.37f : .99f;
        private float PlayBowDuration => playBowTurnEnd + PlayBowBrace + PlayBowHoldEnd + .68f;

        private static bool IsPlayBow(string action) => action == "bow" || action == "invite";

        private void BeginPlayBow()
        {
            float bearing = user == null ? 0f : Mathf.Abs(HeadingChange(transform.forward, user.position - transform.position));
            playBowTurnEnd = bearing > 24f ? (bearing - 24f) / 88f + .19f : 0f;
            playBowContactsCaptured = false;
        }

        private float PlayBowWeight()
        {
            float age = PlayBowAge;
            return EaseBetween(.04f, PlayBowDropEnd, age) * (1f - EaseBetween(PlayBowHoldEnd, PlayBowHoldEnd + .51f, age));
        }

        private void CapturePlayBowContacts()
        {
            if (playBowContactsCaptured) return;
            playBowContactsCaptured = true;
            foreach (Foot foot in feet) foot.PlayStart = foot.Contact;
        }

        private Vector3 PlayBowFootGoal(Foot foot)
        {
            // A play bow first reaches and plants each forepaw; the planted targets stay put during the hold.
            // Rear paws keep their existing ground sites, including the placement left by the preparatory turn.
            float age = PlayBowAge;
            float floor = transform.position.y + PawGroundClearance;
            Vector3 contact = foot.PlayStart;
            contact.y = Mathf.Lerp(foot.PlayStart.y, floor, EaseBetween(0f, .15f, age));
            float lift = 0f;
            if (foot.Front)
            {
                float reachStart = foot.Right ? .21f : .02f;
                float recoverStart = PlayBowHoldEnd + (foot.Right ? .43f : .23f);
                float reach = Mathf.InverseLerp(reachStart, reachStart + .19f, age);
                float recover = Mathf.InverseLerp(recoverStart, recoverStart + .19f, age);
                float extended = Mathf.SmoothStep(0f, 1f, reach) * (1f - Mathf.SmoothStep(0f, 1f, recover));
                Vector3 forward = KeepFootOnOwnSide(foot, foot.PlayStart + transform.forward * (.17f * bodyScale));
                contact.x = Mathf.Lerp(contact.x, forward.x, extended);
                contact.z = Mathf.Lerp(contact.z, forward.z, extended);
                float swing = Mathf.Sin(Mathf.PI * reach) + Mathf.Sin(Mathf.PI * recover);
                lift = .048f * bodyScale * swing * swing;
            }
            contact.y += lift;
            foot.Contact = contact;
            foot.Landing = contact;
            foot.SwingStart = contact;
            foot.SwingProgress = 1f;
            foot.Planted = contact.y <= floor + .002f;
            return contact;
        }

        private void ApplyPlayBowUpperBody()
        {
            visual.position += transform.forward * (.035f * bodyScale * bowBlend);
            if (head == null || MuzzleAnchor == null) return;

            // The low shoulders and chest are the signal. Counter their downward pitch in the neck so
            // the dog keeps its eyes on the play partner instead of appearing to sniff the floor.
            Vector3 muzzle = MuzzleAnchor.position - head.position;
            Vector3 toUser = user == null ? transform.forward : user.position - head.position;
            float bearing = HeadingChange(transform.forward, toUser);
            float length = new Vector2(toUser.x, toUser.z).magnitude;
            bool facingPlayer = length >= .28f && length <= 5f && Mathf.Abs(bearing) <= 80f;
            float desiredElevation = facingPlayer ? Mathf.Clamp(PitchOf(toUser), -13f, 28f) : -4f;
            float pitch = Mathf.Clamp(PitchOf(muzzle) - desiredElevation, -61f, 14f) * bowBlend;
            AddPlayPitch("DEF-neck.01", pitch * .36f);
            AddPlayPitch("DEF-neck.02", pitch * .35f);
            AddPlayPitch("DEF-head", pitch * .29f);
            if (facingPlayer)
            {
                float yaw = Mathf.Clamp(HeadingChange(muzzle, toUser), -26f, 26f) * bowBlend;
                Yaw("DEF-neck.02", yaw * .42f);
                Yaw("DEF-head", yaw * .58f);
            }
        }

        private static float PitchOf(Vector3 direction) =>
            Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;

        public System.Func<Vector3, float, bool> HasPlayClearance { private get; set; }
        public bool LastPlaySucceeded { get; private set; }

        public bool CanPlayBurst() => FindBurstDirection() != 0;

        private bool BeginPlayMotion(string action)
        {
            LastPlaySucceeded = false;
            if (action == "sniff" || action == "invite") return true;
            if (action == "burst")
            {
                burstDirection = FindBurstDirection();
                burstOrigin = transform.position;
                burstHeading = transform.rotation;
                burstStopped = false;
                return burstDirection != 0;
            }

            Vector3 ahead = transform.forward;
            if (!IsPlaySpaceClear(transform.position - ahead * .27f, .26f) ||
                !IsPlaySpaceClear(transform.position + ahead * .47f, .31f))
                return false;
            foreach (Foot foot in feet) foot.PlayStart = foot.Contact;
            return true;
        }

        private bool IsPlaySpaceClear(Vector3 point, float radius)
        {
            if ((Limit(point) - point).sqrMagnitude > .0001f) return false;
            if (user != null && FlatDistance(user.position, point) < radius + .39f) return false;
            return HasPlayClearance == null || HasPlayClearance(point, radius);
        }

        private int FindBurstDirection()
        {
            if (!Ready || HasPlayClearance == null) return 0;
            if (IsBurstRouteClear(1)) return 1;
            return IsBurstRouteClear(-1) ? -1 : 0;
        }

        private bool IsBurstRouteClear(int side)
        {
            for (int point = 0; point <= 12; point++)
            {
                float progress = point / 12f;
                Vector3 position = BurstPoint(transform.position, transform.rotation, side, progress);
                Quaternion heading = Quaternion.AngleAxis(side * BurstTurn * progress, Vector3.up) * transform.rotation;
                if (!IsPlaySpaceClear(position + heading * Vector3.forward * .19f, .51f)) return false;
            }
            return true;
        }

        private static Vector3 BurstPoint(Vector3 start, Quaternion heading, int side, float progress)
        {
            float angle = BurstTurn * Mathf.Deg2Rad * progress;
            float radius = BurstLength / (BurstTurn * Mathf.Deg2Rad);
            Vector3 local = new Vector3(side * radius * (1f - Mathf.Cos(angle)), 0f, radius * Mathf.Sin(angle));
            return start + heading * local;
        }

        private void UpdatePlayMotion()
        {
            if (burstStopped) return;
            float progress = EaseBetween(0f, BurstDuration, actionAge);
            Vector3 next = BurstPoint(burstOrigin, burstHeading, burstDirection, progress);
            Quaternion heading = Quaternion.AngleAxis(burstDirection * BurstTurn * progress, Vector3.up) * burstHeading;
            if (!IsPlaySpaceClear(next + heading * Vector3.forward * .19f, .51f))
            {
                burstStopped = true;
                return;
            }
            turnIntent = burstDirection * EaseBetween(0f, .24f, actionAge) *
                (1f - EaseBetween(BurstDuration - .40f, BurstDuration, actionAge));
            transform.SetPositionAndRotation(next, heading);
        }

        private void ApplyPlayPose()
        {
            if (!IsPerforming) return;
            if (IsPlayBow(CurrentAction))
            {
                ApplyPlayBowUpperBody();
                return;
            }
            if (CurrentAction == "sniff")
            {
                float sniff = EaseBetween(0f, .43f, actionAge) * (1f - EaseBetween(.97f, SniffDuration, actionAge));
                AddPlayPitch("DEF-neck.01", 15f * sniff);
                AddPlayPitch("DEF-neck.02", 14f * sniff);
                AddPlayPitch("DEF-head", 8f * sniff);
                Yaw("DEF-head", 6f * Mathf.Sin(actionAge * 13f) * sniff);
            }
            if (CurrentAction != "pounce") return;

            float load = EaseBetween(0f, .2f, actionAge) * (1f - EaseBetween(.2f, .47f, actionAge));
            float swing = PounceAir(actionAge);
            float absorb = EaseBetween(.50f, .63f, actionAge) * (1f - EaseBetween(.63f, .86f, actionAge));
            AddPlayPitch("DEF-pelvis", -7f * load + 4f * absorb);
            AddPlayPitch("DEF-spine.01", -5f * swing);
            AddPlayPitch("DEF-chest", -6f * load - 8f * swing + 10f * absorb);
            AddPlayPitch("DEF-neck.01", 6f * swing + 5f * absorb);
            AddPlayPitch("DEF-neck.02", 4f * load + 7f * absorb);
            AddPlayPitch("DEF-head", 3f * load + 6f * absorb);
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? ".L" : ".R";
                AddPlayPitch("DEF-front_upper" + suffix, -10f * swing + 4f * absorb);
                AddPlayPitch("DEF-front_forearm" + suffix, -21f * swing - 17f * absorb);
                AddPlayPitch("DEF-hind_thigh" + suffix, -11f * load);
                AddPlayPitch("DEF-hind_shin" + suffix, 14f * load);
            }
            visual.position += transform.forward * (-.028f * load + .074f * swing + .048f * absorb) +
                Vector3.up * (-.035f * load + .024f * swing - .055f * absorb);
        }

        private void ApplyWalkingTorso()
        {
            if (locomotionBlend <= .003f || CurrentAction == "spin") return;
            float stride = gaitCycle * (2f * Mathf.PI);
            float weight = locomotionBlend * Mathf.Lerp(1f, .52f, Mathf.Abs(torsoTurn));
            float pelvisRoll = Mathf.Cos(stride - .7f) * Mathf.Lerp(3.2f, 1.9f, trotBlend) * weight;
            float pelvisYaw = Mathf.Sin(stride - .4f) * 2.2f * weight;
            float pitch = Mathf.Cos(stride * 2f - .65f) * weight;
            // Shoulder blades follow each foreleg in turn as it reaches and then bears weight.
            float leftPhase = stride - Mathf.Lerp(.25f, .5f, trotBlend) * 2f * Mathf.PI;
            float rightPhase = stride - Mathf.Lerp(.75f, 1f, trotBlend) * 2f * Mathf.PI;
            AddPlayPitch("DEF-scapula.L", -5f * weight * Mathf.Sin(leftPhase));
            AddPlayPitch("DEF-scapula.R", -5f * weight * Mathf.Sin(rightPhase));
            AddPlayPitch("DEF-neck.02", -.8f * pitch);
            AddPlayPitch("DEF-pelvis", pitch * Mathf.Lerp(1.6f, 2.5f, trotBlend));
            AddPlayPitch("DEF-spine.01", -pitch * .75f * trotBlend);
            AddPlayPitch("DEF-chest", -pitch * .95f * trotBlend);
            AddPlayPitch("DEF-neck.01", 4.5f * weight * trotBlend);
            Yaw("DEF-pelvis", pelvisYaw);
            Yaw("DEF-spine.01", -pelvisYaw * .62f);
            Yaw("DEF-chest", -.52f * weight * Mathf.Sin(stride - .8f));
            ApplyGaitRoll(pelvis, pelvisRoll);
            ApplyGaitRoll(Bone("DEF-spine.01"), -pelvisRoll * .52f);
            ApplyGaitRoll(Bone("DEF-chest"), -pelvisRoll * .63f);
        }

        private void ApplyGaitRoll(Transform joint, float roll)
        {
            if (joint == null || joint.parent == null) return;
            Vector3 axis = joint.parent.InverseTransformDirection(transform.forward);
            joint.localRotation = Quaternion.AngleAxis(roll, axis) * joint.localRotation;
        }

        private void AddPlayPitch(string name, float degrees)
        {
            Transform joint = Bone(name);
            if (joint == null || joint.parent == null) return;
            Vector3 axis = joint.parent.InverseTransformDirection(transform.right);
            joint.localRotation = Quaternion.AngleAxis(degrees, axis) * joint.localRotation;
        }

        private void PlacePounceFoot(Foot foot)
        {
            if (foot.Front)
            {
                float recoveryAt = foot.Right ? .88f : .74f;
                float recover = Mathf.InverseLerp(recoveryAt, recoveryAt + .21f, actionAge);
                Vector3 seed = foot.PlayStart;
                seed.y = transform.position.y + PawGroundClearance;
                Vector3 landed = Vector3.Lerp(seed, FootGoal(foot, .13f), EaseBetween(.2f, .50f, actionAge));
                landed = Vector3.Lerp(landed, FootGoal(foot), EaseBetween(0f, 1f, recover));
                float lift = .105f * PounceAir(actionAge) + .036f * Mathf.Sin(Mathf.PI * recover);
                lift += (foot.PlayStart.y - seed.y) * (1f - EaseBetween(0f, .18f, actionAge));
                foot.Contact = landed + Vector3.up * lift;
                foot.Landing = FootGoal(foot);
                foot.SwingStart = foot.Contact;
                foot.SwingProgress = 1f;
                foot.Planted = lift < .004f;
            }
            else
            {
                // Land an unfinished walking swing before loading the hind legs.
                if (!foot.Planted) AdvanceSwing(foot, Time.deltaTime * 8f);
            }
            PlaceFoot(foot, foot.Contact);
            Quaternion neutral = transform.rotation * foot.NeutralRotation;
            foot.Tip.rotation = Quaternion.RotateTowards(foot.Tip.rotation, neutral, 55f);
        }

        private static float PounceAir(float age)
        {
            float progress = Mathf.InverseLerp(.2f, .50f, age);
            return 4f * progress * (1f - progress);
        }

        private static float EaseBetween(float start, float end, float age) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, age));
    }
}
