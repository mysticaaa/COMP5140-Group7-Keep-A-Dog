using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

public static class WjyPrototypeSetup
{
    [MenuItem("WJY/Build Dog Training Prototype")]
    public static void Build()
    {
        const string scenePath = "Assets/Scenes/SampleScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var previous = GameObject.Find("WJY Dog Training Prototype");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
        var root = new GameObject("WJY Dog Training Prototype");
        var controller = root.AddComponent<DogTrainingPrototype>();
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Dog Placeholder Sphere";
        sphere.transform.SetParent(root.transform);
        sphere.transform.position = new Vector3(1.8f, 1f, 0f);
        sphere.transform.localScale = Vector3.one * 1.7f;
        const string materialPath = "Assets/wjy_workspace/DogPlaceholder.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Unlit/Color"));
            material.color = Color.white;
            AssetDatabase.CreateAsset(material, materialPath);
        }
        controller.dogRenderer = sphere.GetComponent<Renderer>();
        controller.dogRenderer.sharedMaterial = material;
        var camera = Camera.main;
        if (camera != null)
        {
            camera.transform.position = new Vector3(0f, 1f, -7f);
            camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.1f, 0.15f);
            camera.fieldOfView = 45f;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log("WJY_PROTOTYPE_SETUP_OK");
    }

    public static void Validate()
    {
        foreach (string action in new[] { "crouch", "sit", "spin" })
        {
            if (DogTrainingPrototype.ParseDecision("{\"action\":\"" + action + "\"}", action).action != action)
                throw new Exception("Action parse failed.");
            if (DogTrainingPrototype.ParseDecision("{\"action\":\"refuse\"}", action).action != "refuse")
                throw new Exception("Refusal parse failed.");
        }
        foreach (string invalid in new[] { "", "{}", "{\"action\":\"spin\"}", "{\"action\":\"jump\"}", "plain text" })
        {
            bool rejected = false;
            try { DogTrainingPrototype.ParseDecision(invalid, "sit"); }
            catch (Exception) { rejected = true; }
            if (!rejected) throw new Exception("Invalid response was accepted: " + invalid);
        }
        Debug.Log("WJY_RESPONSE_VALIDATION_OK");
    }
}