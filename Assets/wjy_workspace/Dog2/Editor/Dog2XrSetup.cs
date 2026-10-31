#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

[InitializeOnLoad]
public static class Dog2XrSetup
{
    public const string Root = "Assets/wjy_workspace/Dog2/XRSettings";
    private static string SessionKey => "WjyDog2ConfiguredV3:" + Application.dataPath;
    static Dog2XrSetup()
    {
        EditorApplication.delayCall += ConfigureOnce;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) EnableControllerProfiles();
        };
    }
    private static void EnableControllerProfiles()
    {
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        if (settings == null) return;
        int enabled = 0;
        foreach (var feature in settings.GetFeatures())
        {
            if (!(feature is OculusTouchControllerProfile) && !(feature is MetaQuestTouchPlusControllerProfile)) continue;
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
            enabled++;
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[Dog2 XR] Enabled referenced Quest controller profiles: " + enabled);
    }
    private static void ConfigureOnce()
    {
        if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += ConfigureOnce; return; }
        try { Configure(); SessionState.SetBool(SessionKey, true); }
        catch (Exception e) { Debug.LogError("Dog2 OpenXR configuration failed: " + e); }
    }
    [MenuItem("WJY/Configure Dog2 PC VR")]
    public static void Configure()
    {
        Directory.CreateDirectory(Root);
        var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(Root + "/XRGeneralSettings.asset");
        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, Root + "/XRGeneralSettings.asset");
        }
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        general.InitManagerOnStart = false;
        general.Manager.automaticLoading = false; general.Manager.automaticRunning = false;
        var loader = AssetDatabase.LoadAssetAtPath<OpenXRLoader>(Root + "/OpenXRLoader.asset");
        if (loader == null) { loader = ScriptableObject.CreateInstance<OpenXRLoader>(); AssetDatabase.CreateAsset(loader, Root + "/OpenXRLoader.asset"); }
        general.Manager.TryAddLoader(loader);
        // Register the package container before refreshing features, preventing Assets/XR default assets.
        UnityEngine.Object package;
        if (!EditorBuildSettings.TryGetConfigObject("com.unity.xr.openxr.settings4", out package) || package == null)
        {
            var type = typeof(FeatureHelpers).Assembly.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings", true);
            package = AssetDatabase.LoadAssetAtPath(Root + "/OpenXRSettings.asset", type);
            if (package == null) { package = ScriptableObject.CreateInstance(type); AssetDatabase.CreateAsset(package, Root + "/OpenXRSettings.asset"); }
            EditorBuildSettings.AddConfigObject("com.unity.xr.openxr.settings4", package, true);
        }
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        // Remove only orphan duplicates produced by the old custom-path scanning bug.
        var referenced = new System.Collections.Generic.HashSet<UnityEngine.XR.OpenXR.Features.OpenXRFeature>(settings.GetFeatures());
        var referencedTypes = new System.Collections.Generic.HashSet<Type>();
        foreach (var feature in referenced) if (feature != null) referencedTypes.Add(feature.GetType());
        int removed = 0;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(package)))
            if (asset is UnityEngine.XR.OpenXR.Features.OpenXRFeature feature &&
                !referenced.Contains(feature) && referencedTypes.Contains(feature.GetType()))
            {
                UnityEngine.Object.DestroyImmediate(feature, true);
                removed++;
            }
        if (removed > 0) Debug.Log("[Dog2 XR] Removed orphan duplicate features: " + removed);
        AssetDatabase.SaveAssets();
        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
        settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        if (settings.GetFeature<OculusTouchControllerProfile>() == null ||
            settings.GetFeature<MetaQuestTouchPlusControllerProfile>() == null)
        {
            var features = new System.Collections.Generic.List<UnityEngine.XR.OpenXR.Features.OpenXRFeature>(settings.GetFeatures());
            if (settings.GetFeature<OculusTouchControllerProfile>() == null)
            {
                var feature = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
                feature.name = "OculusTouchControllerProfile Standalone";
                AssetDatabase.AddObjectToAsset(feature, package); features.Add(feature);
            }
            if (settings.GetFeature<MetaQuestTouchPlusControllerProfile>() == null)
            {
                var feature = ScriptableObject.CreateInstance<MetaQuestTouchPlusControllerProfile>();
                feature.name = "MetaQuestTouchPlusControllerProfile Standalone";
                AssetDatabase.AddObjectToAsset(feature, package); features.Add(feature);
            }
            var serialized = new SerializedObject(settings);
            var list = serialized.FindProperty("features");
            list.arraySize = features.Count;
            for (int i = 0; i < features.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = features[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        var touch = settings.GetFeature<OculusTouchControllerProfile>();
        if (touch != null) touch.enabled = true;
        var touchPlus = settings.GetFeature<MetaQuestTouchPlusControllerProfile>();
        if (touchPlus != null) touchPlus.enabled = true;
        settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        EditorUtility.SetDirty(perTarget); EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager);
        EditorUtility.SetDirty(package); EditorUtility.SetDirty(settings);
        if (touch != null) EditorUtility.SetDirty(touch);
        if (touchPlus != null) EditorUtility.SetDirty(touchPlus);
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(scene => scene.path == "Assets/Scenes/Dog2.unity"))
        {
            scenes.Add(new EditorBuildSettingsScene("Assets/Scenes/Dog2.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("DOG2_OPENXR_CONFIGURED: Standalone OpenXR, Quest controllers, manual startup in Dog2 only.");
    }
}
#endif