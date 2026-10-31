#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Wjy.DogTraining;

public abstract class DeepSeekConnectionInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "model", "endpoint", "apiKey");
        EditorGUILayout.Space(); EditorGUILayout.LabelField("DeepSeek connection", EditorStyles.boldLabel);
        var url = serializedObject.FindProperty("endpoint");
        var model = serializedObject.FindProperty("model");
        var key = serializedObject.FindProperty("apiKey");
        EditorGUI.BeginChangeCheck();
        int index = EditorGUILayout.Popup("URL 预设", DeepSeekConnectionPresets.FindUrl(url.stringValue), DeepSeekConnectionPresets.Urls);
        if (EditorGUI.EndChangeCheck() && index >= 0)
        {
            string u = url.stringValue, m = model.stringValue;
            DeepSeekConnectionPresets.SelectUrl(index, ref u, ref m);
            url.stringValue = u; model.stringValue = m;
        }
        string edited = EditorGUILayout.TextField("URL 手动编辑", url.stringValue);
        if (edited != url.stringValue)
        {
            string u = url.stringValue, m = model.stringValue;
            DeepSeekConnectionPresets.EditUrl(edited, ref u, ref m);
            url.stringValue = u; model.stringValue = m;
        }
        EditorGUI.BeginChangeCheck();
        int modelIndex = EditorGUILayout.Popup("Model 预设", DeepSeekConnectionPresets.FindModel(model.stringValue), DeepSeekConnectionPresets.Models);
        if (EditorGUI.EndChangeCheck() && modelIndex >= 0) model.stringValue = DeepSeekConnectionPresets.Models[modelIndex];
        model.stringValue = EditorGUILayout.TextField("Model 手动编辑", model.stringValue);
        key.stringValue = EditorGUILayout.PasswordField("API Key", key.stringValue);
        serializedObject.ApplyModifiedProperties();
    }
}
[CustomEditor(typeof(DogTrainingPrototype))]
public sealed class SampleConnectionInspector : DeepSeekConnectionInspector { }
[CustomEditor(typeof(Dog1Training))]
public sealed class Dog1ConnectionInspector : DeepSeekConnectionInspector { }
[CustomEditor(typeof(Dog2VrTraining))]
public sealed class Dog2ConnectionInspector : DeepSeekConnectionInspector { }
#endif