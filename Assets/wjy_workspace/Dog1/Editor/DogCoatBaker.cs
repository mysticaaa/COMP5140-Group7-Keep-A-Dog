#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogCoatBaker
{
    private const string SessionKey = "CityDogCoatBaker";
    private const string AssetPath = "Assets/wjy_workspace/Dog1/Resources/Dog1/GermanShepherdCoat.asset";
    private const string SourcePath = "Assets/wjy_workspace/Dog1/Resources/Dog1/GermanShepherd.fbx";
    private static readonly string[] Poses = { "sit", "crouch", "bow", "paw", "pickup" };
    private static bool started;
    private static double deadline;

    static DogCoatBaker() => EditorApplication.update += Tick;

    public static void Run()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
        SessionState.SetBool(SessionKey + "RestoreImporter", !importer.isReadable);
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 60;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException()); return; }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        started = true;
        trainer.StartCoroutine(Bake(trainer.dog));
    }

    private static IEnumerator Bake(DogCompanion dog)
    {
        var steps = RunBake(dog);
        while (true)
        {
            bool next = false;
            object current = null;
            Exception error = null;
            try { next = steps.MoveNext(); if (next) current = steps.Current; }
            catch (Exception caught) { error = caught; }
            if (!next) { Finish(error); yield break; }
            yield return current;
        }
    }

    private static IEnumerator RunBake(DogCompanion dog)
    {
        dog.SetWanderingEnabled(false);
        Vector3 actor = new Vector3(1.05f, 0, -1f);
        dog.SetPracticeArea(actor, new Vector3(3.15f, .11f, -.45f), new Vector3(2.5f, 0, 1.25f));
        Camera.main.transform.position = actor + Vector3.forward * 2.8f;
        var prefab = Resources.Load<GameObject>("Dog1/GermanShepherd");
        var source = prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        var skin = dog.VisualRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = source.name;
        skin.sharedMesh = source;
        // Render every pose with the same camera, bones and lighting.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var pickup = typeof(DogCompanion).GetField("loweringToPickUp", flags);

        string output = Environment.GetEnvironmentVariable("CITY_DOG_SKIN_CAPTURES");
        if (!string.IsNullOrEmpty(output)) Directory.CreateDirectory(output);
        for (int index = 0; index < Poses.Length; index++)
        {
            string pose = Poses[index];
            if (pose == "pickup") pickup.SetValue(dog, true);
            else dog.StartCoroutine(dog.PerformAction(pose, .25f));
            yield return new WaitForSeconds(.92f);

            Time.timeScale = 0;
            yield return null;
            Capture(dog, output, pose + "-before");
            var baseline = new Mesh();
            skin.BakeMesh(baseline);
            int moved = Correct(mesh, source, skin, "Coat_" + pose);
            if (moved == 0) throw new InvalidOperationException("No corrective vertices for " + pose);

            skin.sharedMesh = mesh;
            skin.SetBlendShapeWeight(source.blendShapeCount + index, 100);
            yield return null;
            Capture(dog, output, pose + "-after");
            var corrected = new Mesh();
            skin.BakeMesh(corrected);
            Report(pose, source, baseline, corrected, moved);
            Time.timeScale = 1;
            skin.SetBlendShapeWeight(source.blendShapeCount + index, 0);
            skin.sharedMesh = source;
            UnityEngine.Object.DestroyImmediate(baseline);
            UnityEngine.Object.DestroyImmediate(corrected);
            if (pose == "pickup") pickup.SetValue(dog, false);
            while (dog.IsPerforming) yield return null;
            yield return new WaitForSeconds(.4f);
        }

        var current = AssetDatabase.LoadAssetAtPath<Mesh>(AssetPath);
        if (current == null) AssetDatabase.CreateAsset(mesh, AssetPath);
        else
        {
            EditorUtility.CopySerialized(mesh, current);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("DOG_COAT_BAKE_OK");
    }

    private static int Correct(Mesh target, Mesh source, SkinnedMeshRenderer skin, string shape)
    {
        Vector3[] vertices = source.vertices, normals = source.normals;
        var delta = new Vector3[vertices.Length];
        var normalDelta = new Vector3[vertices.Length];
        var jointMatrices = new Matrix4x4[skin.bones.Length];
        Quaternion[] real = new Quaternion[jointMatrices.Length], dual = new Quaternion[jointMatrices.Length];
        Vector3[] columns = new Vector3[4];
        var bind = source.bindposes;
        Matrix4x4 toLocal = skin.transform.worldToLocalMatrix;
        for (int i = 0; i < jointMatrices.Length; i++)
        {
            Matrix4x4 matrix = toLocal * skin.bones[i].localToWorldMatrix * bind[i];
            jointMatrices[i] = matrix;
            real[i] = matrix.rotation;
            Vector3 shift = matrix.GetColumn(3);
            dual[i] = Scale(new Quaternion(shift.x, shift.y, shift.z, 0) * real[i], .5f);
        }
        float limit = skin.transform.InverseTransformVector(Vector3.up * .034f).magnitude;
        float strength = shape == "Coat_bow" ? .48f : .80f;
        int moved = 0, cursor = 0;
        using (var counts = source.GetBonesPerVertex())
        using (var weights = source.GetAllBoneWeights())
        {
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                int count = counts[vertex];
                if (count <= 1) { cursor += count; continue; }
                Array.Clear(columns, 0, columns.Length);
                Quaternion q = default, d = default, reference = real[weights[cursor].boneIndex];
                for (int i = 0; i < count; i++)
                {
                    var influence = weights[cursor++];
                    int bone = influence.boneIndex;
                    for (int column = 0; column < 4; column++)
                        columns[column] += (Vector3)jointMatrices[bone].GetColumn(column) * influence.weight;
                    float sign = Quaternion.Dot(reference, real[bone]) < 0 ? -1 : 1;
                    q = Add(q, Scale(real[bone], sign * influence.weight));
                    d = Add(d, Scale(dual[bone], sign * influence.weight));
                }
                float norm = Mathf.Sqrt(Quaternion.Dot(q, q));
                if (norm < .0001f) continue;
                q = Scale(q, 1f / norm);
                d = Scale(d, 1f / norm);
                Quaternion translation = Scale(d * new Quaternion(-q.x, -q.y, -q.z, q.w), 2f);
                Vector3 desired = q * vertices[vertex] + new Vector3(translation.x, translation.y, translation.z);
                var blend = new Matrix4x4(
                    new Vector4(columns[0].x, columns[0].y, columns[0].z, 0),
                    new Vector4(columns[1].x, columns[1].y, columns[1].z, 0),
                    new Vector4(columns[2].x, columns[2].y, columns[2].z, 0),
                    new Vector4(columns[3].x, columns[3].y, columns[3].z, 1));
                if (Mathf.Abs(blend.determinant) < .075f) continue;

                Vector3 displacement = (desired - blend.MultiplyPoint3x4(vertices[vertex])) * strength;
                if (displacement.sqrMagnitude < limit * limit * .00035f) continue;
                Matrix4x4 inverse = blend.inverse;
                displacement = Vector3.ClampMagnitude(displacement, limit);
                delta[vertex] = Vector3.ClampMagnitude(inverse.MultiplyVector(displacement), limit * 1.6f);
                if (vertex < normals.Length)
                {
                    Vector3 original = normals[vertex];
                    Vector3 correctedNormal = inverse.MultiplyVector(q * original).normalized;
                    float weight = Mathf.Clamp01(displacement.magnitude / (limit * .2f));
                    normalDelta[vertex] = (correctedNormal - original) * (.55f * weight);
                }
                moved++;
            }
        }
        target.AddBlendShapeFrame(shape, 100, delta, normalDelta, null);
        return moved;
    }

    private static Quaternion Scale(Quaternion q, float n) => new Quaternion(q.x * n, q.y * n, q.z * n, q.w * n);
    private static Quaternion Add(Quaternion a, Quaternion b) => new Quaternion(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

    private static void Report(string pose, Mesh source, Mesh baseline, Mesh corrected, int moved)
    {
        Vector3[] rest = source.vertices, original = baseline.vertices, after = corrected.vertices;
        int[] triangles = source.triangles;
        int before = 0, fixedCount = 0, stretchedBefore = 0, stretchedAfter = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            float area = Area(rest, a, b, c);
            if (area < 1e-8f) continue;
            float x = Area(original, a, b, c) / area;
            float y = Area(after, a, b, c) / area;
            if (x < .5f) before++;
            if (y < .5f) fixedCount++;
            if (x > 2) stretchedBefore++;
            if (y > 2) stretchedAfter++;
        }
        Debug.Log($"DOG_COAT {pose}: vertices moved={moved}, triangles below half bind area {before} -> {fixedCount}, above double {stretchedBefore} -> {stretchedAfter}");
    }

    private static float Area(Vector3[] points, int a, int b, int c) =>
        Vector3.Cross(points[b] - points[a], points[c] - points[a]).magnitude;

    private static void Capture(DogCompanion dog, string output, string frame)
    {
        if (string.IsNullOrEmpty(output)) return;
        var view = new GameObject("Coat review camera");
        var camera = view.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.transform.position = dog.transform.position + dog.transform.right * 1.78f + dog.transform.forward * .52f + Vector3.up * .73f;
        camera.transform.LookAt(dog.transform.position + Vector3.up * .35f);
        camera.fieldOfView = 34;
        var target = new RenderTexture(900, 560, 24);
        var image = new Texture2D(900, 560, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 900, 560), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, frame + ".png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(view);
        }
    }

    private static void Finish(Exception error)
    {
        Time.timeScale = 1;
        SessionState.SetBool(SessionKey, false);
        if (error != null) Debug.LogException(error);
        if (SessionState.GetBool(SessionKey + "RestoreImporter", false))
        {
            SessionState.SetBool(SessionKey + "RestoreImporter", false);
            var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
