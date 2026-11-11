using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FoxTable.Editor
{
    public static class FoxTableBuild
    {
        private const string ScenePath = "Assets/FoxTable/Scenes/Table.unity";

        [MenuItem("FoxTable/1. Prepare project")]
        public static void Prepare()
        {
            LocalGameChecks.Run();
            WorkspaceChecks.Run();
            if (!Directory.Exists("Assets/FoxTable/Scenes"))
                AssetDatabase.CreateFolder("Assets/FoxTable", "Scenes");
            if (!File.Exists(ScenePath))
            {
                // Generate through Unity rather than hand-writing scene YAML/file IDs.
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    throw new BuildFailedException("Scene preparation cancelled; existing scenes were not changed.");
                var originalSetup = EditorSceneManager.GetSceneManagerSetup();
                bool restoreSetup = !Application.isBatchMode && originalSetup.Length > 0;
                foreach (var entry in originalSetup)
                    restoreSetup &= !string.IsNullOrEmpty(entry.path);
                // Cloud Build starts with an unsaved Untitled scene, which prevents additive creation.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new BuildFailedException("Could not save the FoxTable startup scene.");
                if (restoreSetup) EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath, true)};
            PlayerSettings.companyName = "FoxTable";
            PlayerSettings.productName = "FoxTable";
            PlayerSettings.bundleVersion = "0.4.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.foxtable.mobile");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            AssetDatabase.SaveAssets();
            Debug.Log("FoxTable project prepared. Open Table scene and press Play; Android Build Support is required for APK.");
        }

        [MenuItem("FoxTable/2. Build Android APK")]
        public static void BuildAndroid()
        {
            Prepare();
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] {ScenePath}, locationPathName = "Builds/FoxTable-0.4.0.apk",
                target = BuildTarget.Android, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("FoxTable APK build failed: " + report.summary.result);
        }

        [MenuItem("FoxTable/3. Build Android development APK (LAN HTTP)")]
        public static void BuildDevelopment()
        {
            Prepare();
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] {ScenePath}, locationPathName = "Builds/FoxTable-0.4.0-dev.apk",
                target = BuildTarget.Android, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("FoxTable development APK build failed");
        }
    }
}
