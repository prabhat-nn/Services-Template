#nullable enable
using System.IO;
using System.Linq;
using UnityEditor;

namespace Nexenova.Services.Editor
{
    /// <summary>Read-only checks for the parts of setup the wizard cannot fully automate.</summary>
    public static class SetupValidators
    {
        public static bool IsUgsLinked() => !string.IsNullOrEmpty(CloudProjectSettings.projectId);

        public static void OpenServicesSettings()
        {
            var window = SettingsService.OpenProjectSettings("Project/Services");
            if (window == null)
                SettingsService.OpenProjectSettings("Project/Services/General");
        }

        public static bool PurchasingEnabledButPackageMissing(ServicesSettings settings) =>
            settings.EnablePurchasing && !ManifestPatcher.IsPackageInstalled("com.unity.purchasing");

        public static bool NextSceneInBuildSettings(ServicesSettings settings)
        {
            if (string.IsNullOrEmpty(settings.NextSceneName))
                return true;
            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => Path.GetFileNameWithoutExtension(s.path))
                .Contains(settings.NextSceneName);
        }
    }
}
