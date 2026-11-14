using UnityEngine;

namespace Wjy.DogTraining
{
    internal sealed class DogCoatCorrectives
    {
        internal const string Resource = "Dog1/GermanShepherdCoat";
        internal static readonly string[] Poses = { "sit", "crouch", "bow", "paw", "pickup" };

        private readonly SkinnedMeshRenderer skin;
        private readonly Mesh correctedMesh;
        private readonly int[] shapeIndices;

        private DogCoatCorrectives(SkinnedMeshRenderer skin, Mesh mesh)
        {
            this.skin = skin;
            correctedMesh = mesh;
            shapeIndices = new int[Poses.Length];
            for (int i = 0; i < Poses.Length; i++)
                shapeIndices[i] = mesh.GetBlendShapeIndex("Coat_" + Poses[i]);
            skin.sharedMesh = mesh;
        }

        public static DogCoatCorrectives Attach(SkinnedMeshRenderer skin)
        {
            skin.quality = SkinQuality.Bone4;
            if (skin.sharedMesh == null) return null;
            Mesh mesh = Resources.Load<Mesh>(Resource);
            if (mesh == null || mesh.vertexCount != skin.sharedMesh.vertexCount ||
                mesh.bindposeCount != skin.sharedMesh.bindposeCount || mesh.name != skin.sharedMesh.name)
                return null;
            return new DogCoatCorrectives(skin, mesh);
        }

        public void Apply(float sit, float crouch, float bow, float paw, float pickup)
        {
            if (skin.sharedMesh != correctedMesh) return;
            sit = ShapeWeight(sit);
            crouch = ShapeWeight(crouch);
            bow = ShapeWeight(bow);
            paw = ShapeWeight(paw);
            pickup = ShapeWeight(pickup);
            float scale = 100f / Mathf.Max(1f, sit + crouch + bow + paw + pickup);
            SetWeight(0, sit * scale);
            SetWeight(1, crouch * scale);
            SetWeight(2, bow * scale);
            SetWeight(3, paw * scale);
            SetWeight(4, pickup * scale);
        }

        private static float ShapeWeight(float pose) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, 1, pose));

        private void SetWeight(int pose, float weight)
        {
            if (shapeIndices[pose] >= 0) skin.SetBlendShapeWeight(shapeIndices[pose], weight);
        }
    }
}
