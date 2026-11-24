// Copyright (c) 2026 PSI Lab. MIT License. See LICENSE in the repository root.
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace SplatXR.QuestSample.Editor
{
    // Runs once when the sample project opens and again from the SplatXR menu.
    // Player settings are plain editor API. OpenXR feature assets are created by the
    // OpenXR package, because their script GUIDs depend on the package version.
    [InitializeOnLoad]
    public static class Quest3ProjectSetup
    {
        const string MarkerName = "Quest3AndroidSetup.txt";
        const string PipelinePath = "Assets/Settings/URP-Quest.asset";
        const string RendererPath = "Assets/Settings/URP-Quest_Renderer.asset";
        const string OpenXrLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string ScenePath = "Assets/Scenes/QuestSplatTest.unity";

        static Quest3ProjectSetup()
        {
            EditorApplication.delayCall += () => Apply(false);
        }

        [MenuItem("SplatXR/Quest Sample/Apply Quest 3 Android Settings")]
        public static void ApplyFromMenu()
        {
            Apply(true);
        }

        static void Apply(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            string markerPath = Path.Combine(ProjectRoot(), "ProjectSettings", MarkerName);
            if (!force && File.Exists(markerPath))
                return;

            try
            {
                ApplyPlayerSettings();
                bool urpOk = ApplyUrp();
                bool openXrOk = ApplyOpenXr();
                IncludeSplatShader();
                EnsureSceneInBuild();

                bool androidModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
                if (!androidModule)
                {
                    Debug.LogError("[SplatXR Quest] Android Build Support is not installed, so OpenXR Android settings could not be created. In Unity Hub, add Android Build Support (SDK, NDK, OpenJDK) to this editor, restart Unity, then run SplatXR > Quest Sample > Apply Quest 3 Android Settings.");
                    return;
                }

                if (!urpOk || !openXrOk)
                {
                    Debug.LogError("[SplatXR Quest] Setup did not finish. Fix the errors above, then run SplatXR > Quest Sample > Apply Quest 3 Android Settings.");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
                File.WriteAllText(markerPath, "Applied for a Quest 3 standalone (Android, Vulkan, OpenXR Multiview) build.\n");
                AssetDatabase.SaveAssets();
                Debug.Log("[SplatXR Quest] Quest 3 Android settings applied. Active build target will switch to Android if it is not already. Then open Assets/Scenes/QuestSplatTest.unity, add a PLY from SplatXR > Building Blocks Panel, and use File > Build Settings > Build And Run. See QUEST3_STANDALONE.md.");

                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    bool started = EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android);
                    if (!started)
                        Debug.LogError("[SplatXR Quest] Could not switch the active build target to Android. Use File > Build Settings, select Android, and click Switch Platform.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[SplatXR Quest] Setup failed: " + exception.GetType().Name + ": " + exception.Message + "\n" + exception.StackTrace);
            }
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "SplatXR";
            PlayerSettings.productName = "SplatXR Quest Sample";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.splatxr.questsample");
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            SetActiveInputHandler(1);

            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

            Debug.Log("[SplatXR Quest] Player settings: Android, IL2CPP, ARM64, Min SDK 32, Linear, Vulkan only, Input System, Development Build (script debugging off).");
        }

        static void SetActiveInputHandler(int value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[SplatXR Quest] ProjectSettings.asset is not ready. Set Active Input Handling to Input System Package manually.");
                return;
            }

            var serialized = new SerializedObject(assets[0]);
            var property = serialized.FindProperty("activeInputHandler");
            if (property == null)
            {
                Debug.LogWarning("[SplatXR Quest] Could not find activeInputHandler. Set Player Settings > Active Input Handling to Input System Package.");
                return;
            }

            if (property.intValue == value)
                return;

            property.intValue = value;
            serialized.ApplyModifiedProperties();
            Debug.Log("[SplatXR Quest] Active Input Handling set to Input System Package. If Unity asks to restart, restart, then run SplatXR > Quest Sample > Apply Quest 3 Android Settings if the console does not show that setup finished.");
        }

        static bool ApplyUrp()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                EnsureFolder("Assets/Settings");
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "URP-Quest_Renderer";
                var postProcess = LoadDefaultPostProcessData();
                if (postProcess != null)
                    rendererData.postProcessData = postProcess;
                AssetDatabase.CreateAsset(rendererData, RendererPath);

                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.name = "URP-Quest";
                pipeline.msaaSampleCount = 4;
                pipeline.supportsHDR = false;
                pipeline.renderScale = 1f;
                pipeline.supportsCameraDepthTexture = false;
                pipeline.supportsCameraOpaqueTexture = false;
                pipeline.shadowDistance = 20f;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                Debug.Log("[SplatXR Quest] Created " + PipelinePath + " (MSAA 4x, HDR off, render scale 1). Lower render scale toward 0.8 after the splat is visible if you need frame rate.");
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int previous = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.antiAliasing = 4;
                QualitySettings.vSyncCount = 0;
            }
            QualitySettings.SetQualityLevel(previous, false);
            EditorUtility.SetDirty(pipeline);
            Debug.Log("[SplatXR Quest] Assigned the same URP asset to Graphics and all " + names.Length + " quality levels. A mismatched quality level renders the skybox and skips the splat.");
            return true;
        }

        static PostProcessData LoadDefaultPostProcessData()
        {
            MethodInfo method = typeof(PostProcessData).GetMethod(
                "GetDefaultPostProcessData",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return null;
            return method.Invoke(null, null) as PostProcessData;
        }

        static bool ApplyOpenXr()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                return false;

            EnsureFolder("Assets/XR");
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perBuild);
            if (perBuild == null)
            {
                perBuild = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuild, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuild, true);
            }

            const BuildTargetGroup android = BuildTargetGroup.Android;
            if (!perBuild.HasSettingsForBuildTarget(android))
                perBuild.CreateDefaultSettingsForBuildTarget(android);
            if (!perBuild.HasManagerSettingsForBuildTarget(android))
                perBuild.CreateDefaultManagerSettingsForBuildTarget(android);

            XRGeneralSettings general = perBuild.SettingsForBuildTarget(android);
            general.InitManagerOnStart = true;
            XRManagerSettings manager = perBuild.ManagerSettingsForBuildTarget(android);
            EditorUtility.SetDirty(general);

            if (manager == null)
            {
                Debug.LogError("[SplatXR Quest] XR Manager settings for Android were not created.");
                return false;
            }

            if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXrLoader, android))
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, OpenXrLoader, android))
                {
                    Debug.LogError("[SplatXR Quest] Could not assign the OpenXR loader. In Project Settings > XR Plug-in Management > Android, enable OpenXR.");
                    return false;
                }
            }

            FeatureHelpers.RefreshFeatures(android);
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(android);
            if (settings == null)
            {
                Debug.LogError("[SplatXR Quest] OpenXR settings for Android are missing. Open Project Settings > XR Plug-in Management > OpenXR (Android tab) once, then run this menu item again.");
                return false;
            }

            // On Android this enum value is shown as Multiview.
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            Enable(settings.GetFeature<MetaQuestFeature>());
            Enable(settings.GetFeature<OculusTouchControllerProfile>());
            Enable(settings.GetFeature<MetaQuestTouchPlusControllerProfile>());

            MetaQuestFeature quest = settings.GetFeature<MetaQuestFeature>();
            if (quest == null)
            {
                Debug.LogError("[SplatXR Quest] Meta Quest Support feature was not found. Enable it under OpenXR Feature Groups on the Android tab.");
                return false;
            }

            quest.AddTargetDevice("eureka", "Quest 3", true);
            quest.AddTargetDevice("quest3s", "Quest 3S", true);
            SetField(quest, "symmetricProjection", true);
            SetField(quest, "optimizeBufferDiscards", true);
            // Left off on purpose. Multiview Render Regions can clip a full-screen transparent pass.
            SetField(quest, "optimizeMultiviewRenderRegions", false);
            EditorUtility.SetDirty(quest);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(perBuild);
            Debug.Log("[SplatXR Quest] OpenXR Android: loader on, initialize on startup, Multiview (Single Pass Instanced), Meta Quest Support, Oculus Touch and Quest Touch Plus profiles, symmetric projection on, multiview render regions off.");
            return true;
        }

        static void Enable(UnityEngine.XR.OpenXR.Features.OpenXRFeature feature)
        {
            if (feature == null)
            {
                Debug.LogWarning("[SplatXR Quest] An OpenXR feature was missing and could not be enabled. Check the OpenXR Android feature list.");
                return;
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogWarning("[SplatXR Quest] Could not set " + target.GetType().Name + "." + fieldName + ". Set it from the gear icon next to Meta Quest Support.");
                return;
            }

            field.SetValue(target, value);
        }

        static void IncludeSplatShader()
        {
            Shader shader = Shader.Find("SplatXR/SplatShader");
            if (shader == null)
            {
                Debug.LogWarning("[SplatXR Quest] SplatXR/SplatShader is not imported yet. After the package import finishes, run SplatXR > Quest Sample > Apply Quest 3 Android Settings.");
                return;
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0)
                return;

            var serialized = new SerializedObject(assets[0]);
            var array = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (array == null)
                return;

            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    return;
            }

            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            array.GetArrayElementAtIndex(index).objectReferenceValue = shader;
            serialized.ApplyModifiedProperties();
            Debug.Log("[SplatXR Quest] Added SplatXR/SplatShader to Always Included Shaders so the Android build cannot strip it.");
        }

        static void EnsureSceneInBuild()
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var scene in scenes)
            {
                if (scene.path == ScenePath && scene.enabled)
                    return;
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            for (int i = 0; i < scenes.Length; i++)
                updated[i] = scenes[i];
            updated[scenes.Length] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = updated;
            Debug.Log("[SplatXR Quest] Added " + ScenePath + " to the build scenes.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
