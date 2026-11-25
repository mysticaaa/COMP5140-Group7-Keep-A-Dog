using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class DogCompanion
    {
        private DogBreed breed = DogBreed.Shepherd;
        private float bodyScale = 1f;
        internal DogBreed Breed => breed;

        internal void SetBreedBeforeActivation(DogBreed choice)
        {
            breed = choice ?? DogBreed.Options[0];
            bodyScale = breed.Height / DogBreed.ShepherdHeight;
        }

        private void BindImportedRig()
        {
            Alias("DEF-head", "ValveBiped.Bip01_Head1", "ValveBiped.Bip01_head1");
            Alias("DEF-pelvis", "bip_pelvis");
            Alias("DEF-spine.01", "bip_spine2");
            Alias("DEF-spine.02", "bip_spine3");
            Alias("DEF-chest", "bip_spine4");
            Alias("DEF-neck.01", "bip_neck0");
            Alias("DEF-neck.02", "bip_neck1");
            for (int i = 0; i < tailBones.Length; i++) Alias("DEF-tail.0" + (i + 1), "bip_tail" + i);

            foreach (char side in new[] { 'L', 'R' })
            {
                string suffix = "." + side;
                string source = "bip_" + char.ToLowerInvariant(side) + "_";
                Alias("DEF-scapula" + suffix, source + "frontleg0");
                Alias("DEF-front_upper" + suffix, source + "frontleg1");
                Alias("DEF-front_forearm" + suffix, source + "frontleg2");
                Alias("DEF-front_paw" + suffix, source + "frontleg3");
                Alias("DEF-front_toes" + suffix, source + "front_foot0");
                Alias("DEF-hind_thigh" + suffix, source + "backleg0");
                Alias("DEF-hind_shin" + suffix, source + "backleg1");
                Alias("DEF-hind_hock" + suffix, source + "backleg2");
                Alias("DEF-hind_paw" + suffix, source + "rear_foot0");
            }
        }

        private void Alias(string role, params string[] sources)
        {
            foreach (string name in sources)
            {
                if (!bones.TryGetValue(name, out Transform joint)) continue;
                bones[role] = joint;
                return;
            }
        }

        private static readonly string[] Paws =
        {
            "DEF-front_toes.L", "DEF-front_toes.R", "DEF-hind_paw.L", "DEF-hind_paw.R"
        };

        private float ImportedStandingHeight(float fallback)
        {
            Transform toe = Bone("DEF-front_toes.L");
            // Exported skin bounds can include offsets for separate coat and eye meshes.
            // The head-to-foot distance measures the standing rig itself.
            return head != null && toe != null ? Mathf.Max(.01f, (head.position.y - toe.position.y) * 1.17f) : fallback;
        }

        private Vector3 ImportedPawCenter(Vector3 fallback)
        {
            Vector3 center = Vector3.zero;
            foreach (string name in Paws)
            {
                Transform paw = Bone(name);
                if (paw == null) return fallback;
                center += paw.position;
            }
            return center / Paws.Length;
        }

        private Vector3 ImportedMuzzleOffset()
        {
            Transform middle = Bone("head lip upper middle");
            Vector3 forward = transform.forward * (.018f * bodyScale);
            if (middle != null) return middle.position - head.position + forward;

            Transform left = Bone("head lip upper left 1");
            Transform right = Bone("head lip upper right 1");
            if (left != null && right != null)
                return (left.position + right.position) * 0.5f - head.position + forward;

            return (transform.forward * .17f - Vector3.up * .055f) * bodyScale;
        }
    }
}
