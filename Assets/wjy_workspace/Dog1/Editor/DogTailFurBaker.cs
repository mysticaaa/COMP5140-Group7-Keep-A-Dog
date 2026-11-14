#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class DogTailFurBaker
{
    private const string Directory = "Assets/wjy_workspace/Dog1/Resources/Dog1/";
    private const string AssetPath = Directory + "GermanShepherdTailFur.asset";
    private const int ClusterCount = 320;

    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Directory + "GermanShepherd.fbx");
        var coat = AssetDatabase.LoadAssetAtPath<Mesh>(Directory + "GermanShepherdCoat.asset");
        if (prefab == null || coat == null || !coat.isReadable)
            throw new InvalidOperationException("Tail fur requires the readable GermanShepherdCoat bake and original FBX.");

        GameObject dog = UnityEngine.Object.Instantiate(prefab);
        dog.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var skin = dog.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null || skin.sharedMesh.vertexCount != coat.vertexCount ||
                skin.bones.Length != coat.bindposeCount)
                throw new InvalidOperationException("The coat asset and German shepherd rig are incompatible.");

            var mesh = Bake(skin, coat);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(AssetPath);
            if (existing == null)
                AssetDatabase.CreateAsset(mesh, AssetPath);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = existing;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"DOG_TAIL_FUR_BAKE_OK asset={AssetPath} vertices={mesh.vertexCount} tris={mesh.GetIndexCount(0) / 3}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(dog);
        }
    }

    public static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Directory + "GermanShepherd.fbx");
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(Directory + "mat_001_BaseColor.png");
        if (prefab == null || color == null)
            throw new InvalidOperationException("Tail fur validation requires the German shepherd FBX and its color texture.");
        var dog = UnityEngine.Object.Instantiate(prefab);
        var coat = new Material(Shader.Find("Standard")) { mainTexture = color };
        var before = new Mesh();
        var after = new Mesh();
        Material furMaterial = null;
        try
        {
            var skin = dog.GetComponentInChildren<SkinnedMeshRenderer>();
            var bodyMesh = skin.sharedMesh;
            var fur = Wjy.DogTraining.DogTailFur.Attach(skin, coat);
            if (fur == null) throw new InvalidOperationException("The baked tail fur resource did not attach.");
            furMaterial = fur.sharedMaterial;
            if (skin.sharedMesh != bodyMesh || furMaterial.mainTexture != color || fur.rootBone != skin.rootBone ||
                fur.transform.parent != skin.transform || fur.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                throw new InvalidOperationException("Tail fur must use the body rig and coat texture without replacing or shadowing the body.");

            fur.BakeMesh(before);
            Transform tail = Array.Find(skin.bones, bone => bone != null && bone.name == "DEF-tail.02");
            var rest = tail.localRotation;
            tail.localRotation = rest * Quaternion.Euler(9f, 11f, -7f);
            fur.BakeMesh(after);
            var positions = before.vertices;
            var wagged = after.vertices;
            int moved = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 delta = wagged[i] - positions[i];
                if (float.IsNaN(delta.sqrMagnitude) || float.IsInfinity(delta.sqrMagnitude))
                    throw new InvalidOperationException("The skinned tail fur contains invalid points.");
                if (delta.sqrMagnitude > before.bounds.size.sqrMagnitude * 0.000001f) moved++;
            }
            if (positions.Length < 1000 || moved < positions.Length / 2)
                throw new InvalidOperationException($"Tail fur does not follow the solved tail: moved {moved}/{positions.Length} vertices.");
            Debug.Log($"DOG_TAIL_FUR_VALIDATE_OK moved={moved}/{positions.Length}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(dog);
            if (furMaterial != null) UnityEngine.Object.DestroyImmediate(furMaterial);
            UnityEngine.Object.DestroyImmediate(coat);
            UnityEngine.Object.DestroyImmediate(before);
            UnityEngine.Object.DestroyImmediate(after);
        }
    }

    private static Mesh Bake(SkinnedMeshRenderer skin, Mesh source)
    {
        Transform[] bones = skin.bones;
        var isTail = new bool[bones.Length];
        var joints = new Vector3[5];
        for (int joint = 0; joint < 4; joint++)
        {
            int index = Array.FindIndex(bones, bone => bone != null && bone.name == "DEF-tail.0" + (joint + 1));
            if (index < 0)
                throw new InvalidOperationException("The German shepherd tail chain is incomplete.");
            isTail[index] = true;
            // Bind poses map from the source mesh into each joint's local frame.
            joints[joint] = source.bindposes[index].inverse.MultiplyPoint3x4(Vector3.zero);
        }

        float totalTail = 0f;
        for (int i = 0; i < 3; i++) totalTail += Vector3.Distance(joints[i], joints[i + 1]);
        // Use a short virtual endpoint beyond the fourth joint to project the tip.
        joints[4] = joints[3] + (joints[3] - joints[2]) * 0.58f;
        if (totalTail < Mathf.Epsilon)
            throw new InvalidOperationException("Tail bind pose has no length.");

        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector2[] uvs = source.uv;
        BoneWeight[] weights = source.boneWeights;
        int[] indices = source.triangles;
        if (normals.Length != vertices.Length || uvs.Length != vertices.Length || weights.Length != vertices.Length)
            throw new InvalidOperationException("Tail fur needs normals, UVs and skin weights.");

        var triangles = new List<SurfaceTriangle>();
        float totalArea = 0f;
        int eligibleVertices = 0;
        foreach (BoneWeight weight in weights)
            if (TailInfluence(weight, isTail) >= 0.78f) eligibleVertices++;
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            float influence = Mathf.Min(TailInfluence(weights[a], isTail),
                Mathf.Min(TailInfluence(weights[b], isTail), TailInfluence(weights[c], isTail)));
            if (influence < 0.78f) continue;
            float area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
            if (area <= Mathf.Epsilon) continue;
            totalArea += area;
            triangles.Add(new SurfaceTriangle(a, b, c, totalArea));
        }
        if (triangles.Count < 10 || totalArea <= Mathf.Epsilon)
            throw new InvalidOperationException($"Only {triangles.Count} tail-weighted triangles found ({eligibleVertices} vertices).");

        var output = new FurGeometry(ClusterCount);
        var random = new System.Random(173);
        var blend = new WeightBlend(bones.Length);
        int count = 0;
        for (int i = 0; i < ClusterCount * 3 && count < ClusterCount; i++)
        {
            var triangle = PickTriangle(triangles, (float)random.NextDouble() * totalArea);
            float u = Mathf.Sqrt((float)random.NextDouble());
            float v = (float)random.NextDouble();
            Vector3 barycentric = new Vector3(1f - u, u * (1f - v), u * v);
            Vector3 basePoint = At(vertices, triangle, barycentric);
            Vector3 normal = At(normals, triangle, barycentric).normalized;
            TailDirection(basePoint, joints, out Vector3 growth, out float along);
            float coverage = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.065f, 0.38f, along));
            if (coverage < 0.14f || normal.sqrMagnitude < 0.9f) continue;
            growth = Vector3.ProjectOnPlane(growth, normal).normalized;
            if (growth.sqrMagnitude < 0.9f) continue;

            // Most of each tuft lies along the existing coat. Radial lift stays
            // below about 11 mm at the in-game dog's scale, breaking the edge subtly.
            float lengthNoise = (float)random.NextDouble();
            float lift = totalTail * Mathf.Lerp(0.018f, 0.043f, lengthNoise) * coverage;
            float reach = totalTail * Mathf.Lerp(0.034f, 0.093f, lengthNoise) * coverage;
            float width = totalTail * Mathf.Lerp(0.007f, 0.015f, (float)random.NextDouble());
            float tipTaper = Mathf.Lerp(1f, 0.55f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.83f, 1f, along)));
            Vector3 across = Vector3.Cross(normal, growth).normalized;
            Vector3 axis = (reach * tipTaper * growth + lift * tipTaper * normal).normalized;
            across = (across + growth * Mathf.Lerp(-0.19f, 0.19f, (float)random.NextDouble())).normalized;
            Vector3 root = basePoint - normal * (totalTail * 0.0048f);
            Vector3 end = basePoint + (reach * growth + lift * normal) * tipTaper;
            Vector2 uv = uvs[triangle.A] * barycentric.x + uvs[triangle.B] * barycentric.y + uvs[triangle.C] * barycentric.z;
            BoneWeight weight = blend.Interpolate(weights[triangle.A], weights[triangle.B], weights[triangle.C], barycentric);
            output.AddTuft(root, end, across, normal, axis, width, uv, weight);
            count++;
        }
        if (count < ClusterCount / 2)
            throw new InvalidOperationException($"Only {count} valid tail tuft roots. Inspect the tail bind pose.");
        return output.ToMesh(source.bindposes);
    }

    private static Vector3 At(Vector3[] data, SurfaceTriangle triangle, Vector3 barycentric) =>
        data[triangle.A] * barycentric.x + data[triangle.B] * barycentric.y + data[triangle.C] * barycentric.z;

    private static float TailInfluence(BoneWeight weight, bool[] mask) =>
        (mask[weight.boneIndex0] ? weight.weight0 : 0f) +
        (mask[weight.boneIndex1] ? weight.weight1 : 0f) +
        (mask[weight.boneIndex2] ? weight.weight2 : 0f) +
        (mask[weight.boneIndex3] ? weight.weight3 : 0f);

    private static SurfaceTriangle PickTriangle(List<SurfaceTriangle> triangles, float area)
    {
        int low = 0, high = triangles.Count - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (area <= triangles[mid].CumulativeArea) high = mid;
            else low = mid + 1;
        }
        return triangles[low];
    }

    private static void TailDirection(Vector3 sample, Vector3[] joints, out Vector3 direction, out float along)
    {
        float nearest = float.PositiveInfinity;
        direction = Vector3.zero;
        along = 0f;
        for (int i = 0; i < joints.Length - 1; i++)
        {
            Vector3 segment = joints[i + 1] - joints[i];
            float t = Mathf.Clamp01(Vector3.Dot(sample - joints[i], segment) / Mathf.Max(segment.sqrMagnitude, Mathf.Epsilon));
            float distance = (sample - (joints[i] + t * segment)).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            along = (i + t) / (joints.Length - 1);
            Vector3 before = joints[i + 1] - joints[Mathf.Max(0, i - 1)];
            Vector3 after = joints[Mathf.Min(joints.Length - 1, i + 2)] - joints[i];
            direction = Vector3.Lerp(before.normalized, after.normalized, t);
        }
    }

    private readonly struct SurfaceTriangle
    {
        internal readonly int A, B, C;
        internal readonly float CumulativeArea;

        internal SurfaceTriangle(int a, int b, int c, float area)
        {
            A = a;
            B = b;
            C = c;
            CumulativeArea = area;
        }
    }

    private sealed class WeightBlend
    {
        private readonly float[] weights;
        private readonly int[] indexes = new int[4];
        private readonly float[] top = new float[4];

        internal WeightBlend(int boneCount) => weights = new float[boneCount];

        internal BoneWeight Interpolate(BoneWeight a, BoneWeight b, BoneWeight c, Vector3 barycentric)
        {
            Array.Clear(weights, 0, weights.Length);
            Add(a, barycentric.x);
            Add(b, barycentric.y);
            Add(c, barycentric.z);

            float sum = 0f;
            for (int slot = 0; slot < 4; slot++)
            {
                int best = 0;
                for (int bone = 1; bone < weights.Length; bone++)
                    if (weights[bone] > weights[best]) best = bone;
                indexes[slot] = best;
                top[slot] = weights[best];
                sum += top[slot];
                weights[best] = 0f;
            }
            return new BoneWeight
            {
                boneIndex0 = indexes[0], weight0 = top[0] / sum,
                boneIndex1 = indexes[1], weight1 = top[1] / sum,
                boneIndex2 = indexes[2], weight2 = top[2] / sum,
                boneIndex3 = indexes[3], weight3 = top[3] / sum
            };
        }

        private void Add(BoneWeight weight, float factor)
        {
            weights[weight.boneIndex0] += factor * weight.weight0;
            weights[weight.boneIndex1] += factor * weight.weight1;
            weights[weight.boneIndex2] += factor * weight.weight2;
            weights[weight.boneIndex3] += factor * weight.weight3;
        }
    }

    private sealed class FurGeometry
    {
        private readonly List<Vector3> vertices, normals;
        private readonly List<Vector2> uvs;
        private readonly List<BoneWeight> weights;
        private readonly List<int> triangles;

        internal FurGeometry(int clusterCount)
        {
            int vertexCapacity = clusterCount * 12;
            vertices = new List<Vector3>(vertexCapacity);
            normals = new List<Vector3>(vertexCapacity);
            uvs = new List<Vector2>(vertexCapacity);
            weights = new List<BoneWeight>(vertexCapacity);
            triangles = new List<int>(vertexCapacity);
        }

        internal void AddTuft(Vector3 root, Vector3 tip, Vector3 tangent, Vector3 coatNormal,
            Vector3 axis, float width, Vector2 uv, BoneWeight weight)
        {
            Vector3 right = tangent * width;
            Vector3 split = Vector3.Cross(axis, tangent).normalized * width * 0.68f;
            AddVane(root, tip, right, coatNormal, uv, weight);
            AddVane(root, tip, split, coatNormal, uv, weight);
        }

        private void AddVane(Vector3 root, Vector3 tip, Vector3 width,
            Vector3 coatNormal, Vector2 uv, BoneWeight weight)
        {
            Vector3 normal = Vector3.Cross(width, tip - root).normalized;
            AddTriangle(root - width, root + width, tip, (coatNormal + normal * 0.27f).normalized, uv, weight);
            AddTriangle(root + width, root - width, tip, (coatNormal - normal * 0.27f).normalized, uv, weight);
        }

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 uv, BoneWeight weight)
        {
            triangles.Add(vertices.Count);
            triangles.Add(vertices.Count + 1);
            triangles.Add(vertices.Count + 2);
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            for (int vertex = 0; vertex < 3; vertex++)
            {
                normals.Add(normal);
                uvs.Add(uv);
                weights.Add(weight);
            }
        }

        internal Mesh ToMesh(Matrix4x4[] bindposes)
        {
            var mesh = new Mesh { name = "GermanShepherdTailFur" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.bindposes = bindposes;
            mesh.boneWeights = weights.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
