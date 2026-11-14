#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class Dog2Validation
{
    public static void CheckControllerProfiles()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Dog2XrSetup.Configure();
            var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            // Exercise the loader-library scan that previously discarded enabled profiles.
            UnityEngine.Object package;
            if (!EditorBuildSettings.TryGetConfigObject("com.unity.xr.openxr.settings4", out package)) throw new Exception("Missing package settings.");
            var provider = (UnityEngine.XR.OpenXR.IPackageSettings)package;
            provider.GetActiveLoaderLibraryPath();
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var touch = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile>();
            var plus = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile>();
            if (touch == null || plus == null || !touch.enabled || !plus.enabled)
                throw new Exception("Referenced Quest controller profiles are missing or disabled.");
        }
        Debug.Log("DOG2_CONTROLLER_PROFILE_REPAIR_PASS");
    }

    public static void Run() => Dog2AgentValidation.Run();
}
#endif
