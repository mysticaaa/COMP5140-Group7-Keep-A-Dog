using UnityEngine;
using UnityEngine.Rendering;

namespace Wjy.DogTraining
{
    public sealed class DogTailFur : MonoBehaviour
    {
        private const string ResourcePath = "Dog1/GermanShepherdTailFur";
        private Material furMaterial;

        public static SkinnedMeshRenderer Attach(SkinnedMeshRenderer source, Material coat)
        {
            if (source == null || source.sharedMesh == null || coat == null || coat.mainTexture == null)
                return null;

            var mesh = Resources.Load<Mesh>(ResourcePath);
            if (mesh == null || mesh.bindposeCount != source.sharedMesh.bindposeCount ||
                mesh.bindposeCount != source.bones.Length)
                return null;

            Transform existing = source.transform.Find("Tail coat silhouette");
            if (existing != null) return existing.GetComponent<SkinnedMeshRenderer>();

            var layer = new GameObject("Tail coat silhouette");
            layer.layer = source.gameObject.layer;
            layer.transform.SetParent(source.transform, false);
            var tail = layer.AddComponent<SkinnedMeshRenderer>();
            // Both renderers reference the same bones and bind poses.
            tail.bones = source.bones;
            tail.rootBone = source.rootBone;
            tail.sharedMesh = mesh;
            tail.localBounds = source.localBounds;
            tail.quality = SkinQuality.Bone4;
            tail.shadowCastingMode = ShadowCastingMode.Off;
            tail.receiveShadows = true;
            var owner = layer.AddComponent<DogTailFur>();
            var material = new Material(coat)
            {
                name = "German shepherd tail fur",
                hideFlags = HideFlags.DontSave
            };
            material.SetFloat("_Glossiness", 0.03f);
            // Narrow cards sample the existing hair colors. The coat's tangent-space
            // bump map describes the full dog mesh and would light the cards incorrectly.
            material.SetTexture("_BumpMap", null);
            material.DisableKeyword("_NORMALMAP");
            tail.sharedMaterial = owner.furMaterial = material;
            return tail;
        }

        private void OnDestroy()
        {
            if (furMaterial == null) return;
            if (Application.isPlaying) Destroy(furMaterial);
            else DestroyImmediate(furMaterial);
        }
    }
}
