#nullable enable
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nexenova.Services.Editor
{
    /// <summary>
    /// Generates the boot scene containing a wired ServicesLifetimeScope, places it at
    /// build index 0 and points the settings asset's next scene at the previous first scene.
    /// </summary>
    public static class BootSceneGenerator
    {
        public const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string DefaultNextSceneName = "Main";

        public static bool BootSceneExists() => AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath) != null;

        public static bool BootSceneAtIndexZero()
        {
            var scenes = EditorBuildSettings.scenes;
            return scenes.Length > 0 && scenes[0].enabled && scenes[0].path == BootScenePath;
        }

        public static bool GenerateBootScene(ServicesSettings settings, out string message)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                message = "Cancelled: unsaved scene changes.";
                return false;
            }

            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            var previousFirstSceneName = FirstEnabledSceneName();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Services");
            var scope = go.AddComponent<ServicesLifetimeScope>();

            var so = new SerializedObject(scope);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            if (!EditorSceneManager.SaveScene(scene, BootScenePath))
            {
                message = $"Failed to save scene at {BootScenePath}.";
                return false;
            }

            InsertBootAtIndexZero();
            WireNextSceneName(settings, previousFirstSceneName);

            if (previousSetup.Length > 0 && previousSetup.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);

            message = $"Created {BootScenePath} at build index 0.";
            return true;
        }

        public static void InsertBootAtIndexZero()
        {
            var others = EditorBuildSettings.scenes.Where(s => s.path != BootScenePath);
            EditorBuildSettings.scenes =
                new[] { new EditorBuildSettingsScene(BootScenePath, true) }.Concat(others).ToArray();
        }

        private static void WireNextSceneName(ServicesSettings settings, string? previousFirstSceneName)
        {
            if (string.IsNullOrEmpty(previousFirstSceneName))
                return;

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("nextSceneName");
            if (!string.IsNullOrEmpty(prop.stringValue) && prop.stringValue != DefaultNextSceneName)
                return;

            prop.stringValue = previousFirstSceneName;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static string? FirstEnabledSceneName()
        {
            var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled && s.path != BootScenePath);
            return first == null ? null : Path.GetFileNameWithoutExtension(first.path);
        }
    }
}
