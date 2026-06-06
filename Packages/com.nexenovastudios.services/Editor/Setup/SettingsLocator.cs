#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nexenova.Services.Editor
{
    /// <summary>Finds, creates and pings ServicesSettings assets in the consuming project.</summary>
    public static class SettingsLocator
    {
        public const string DefaultAssetPath = "Assets/Settings/ServicesSettings.asset";

        public static ServicesSettings[] FindAll() =>
            AssetDatabase.FindAssets("t:ServicesSettings")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ServicesSettings>)
                .Where(s => s != null)
                .ToArray();

        public static ServicesSettings CreateDefault()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var settings = ScriptableObject.CreateInstance<ServicesSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static void SelectAndPing(Object asset)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
