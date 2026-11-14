#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wjy.DogTraining
{
    // Tests deformation bones without relying on exact vendor names.
    public static class DogRigAudit
    {
        private static readonly string[][] Groups =
        {
            new[] { "骨盆", "pelvis", "hips", "sacrum", "hip" },
            new[] { "胸", "chest", "spine", "thorax", "ribcage" },
            new[] { "颈", "neck", "cervical" },
            new[] { "头", "head", "skull" },
            new[] { "尾", "tail", "caudal" },
            new[] { "前肢骨链", "front", "forelimb", "foreleg", "forearm", "shoulder", "upperarm" },
            new[] { "后肢骨链", "hind", "rear", "thigh", "stifle", "hock" }
        };

        [MenuItem("CityDog/Inspect selected dog skeleton")]
        private static void InspectSelected()
        {
            var candidate = Selection.activeGameObject;
            if (candidate == null) { Debug.LogWarning("Select a dog model or prefab."); return; }
            Inspect(candidate);
        }

        public static void Run()
        {
            var path = Environment.GetEnvironmentVariable("CITY_DOG_RIG_ASSET");
            var model = string.IsNullOrWhiteSpace(path) ? Resources.Load<GameObject>("Dog1/GermanShepherd") :
                AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidOperationException("Missing rig: " + path);
            Inspect(model);
        }

        private static void Inspect(GameObject candidate)
        {
            var skeleton = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(mesh => mesh.bones).Where(bone => bone != null).Distinct().ToArray();
            if (skeleton.Length == 0) throw new InvalidOperationException("No skinned bones found in " + candidate.name);
            Debug.Log("DOG_RIG candidate " + candidate.name + " deformation bones=" + skeleton.Length);
            foreach (string[] group in Groups)
            {
                var found = skeleton.Where(bone => group.Skip(1).Any(alias =>
                    bone.name.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(bone => bone.name).ToArray();
                Debug.Log("DOG_RIG " + group[0] + ": " + (found.Length == 0 ? "needs manual mapping" : string.Join(", ", found)));
            }
            foreach (var bone in skeleton)
                if (bone.parent != null) Debug.Log("DOG_RIG_LINK " + bone.parent.name + " -> " + bone.name +
                    ", localBind=" + bone.localPosition.ToString("F3"));
            string path = AssetDatabase.GetAssetPath(candidate);
            if (!string.IsNullOrEmpty(path))
            {
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .Select(clip => $"{clip.name} ({clip.length:F2}s)");
                Debug.Log("DOG_RIG_CLIPS " + string.Join(", ", clips));
            }
        }
    }
}
#endif
