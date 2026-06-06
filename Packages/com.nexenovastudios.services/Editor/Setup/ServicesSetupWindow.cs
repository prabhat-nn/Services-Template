#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nexenova.Services.Editor
{
    /// <summary>
    /// One-click integration wizard for the Nexenova services package.
    /// Open via Nexenova ▸ Services Setup in any game that has the package installed.
    /// </summary>
    public sealed class ServicesSetupWindow : EditorWindow
    {
        private readonly List<StepResult> _steps = new();
        private readonly Dictionary<string, bool> _installed = new();
        private ServicesSettings[] _settingsAssets = System.Array.Empty<ServicesSettings>();
        private int _selectedSettings;
        private Vector2 _scroll;

        [MenuItem("Nexenova/Services Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<ServicesSetupWindow>("Services Setup");
            window.minSize = new Vector2(420f, 360f);
        }

        private void OnEnable() => RefreshAll();

        private void OnFocus() => RefreshAll();

        private ServicesSettings? ActiveSettings =>
            _settingsAssets.Length == 0 ? null : _settingsAssets[Mathf.Clamp(_selectedSettings, 0, _settingsAssets.Length - 1)];

        private void RefreshAll()
        {
            _settingsAssets = SettingsLocator.FindAll();
            _selectedSettings = Mathf.Clamp(_selectedSettings, 0, Mathf.Max(0, _settingsAssets.Length - 1));

            _installed.Clear();
            foreach (var package in ManifestPatcher.OptionalPackages)
                _installed[package.Id] = ManifestPatcher.IsPackageInstalled(package.Id);

            _steps.Clear();
            _steps.Add(BuildSettingsStep());
            _steps.Add(BuildBootSceneStep());
            _steps.Add(BuildNextSceneStep());
            _steps.Add(BuildUgsLinkStep());
            _steps.Add(BuildPurchasingStep());
            Repaint();
        }

        private StepResult BuildSettingsStep()
        {
            return _settingsAssets.Length switch
            {
                0 => new StepResult(SetupStatus.Missing, "ServicesSettings asset", "No ServicesSettings asset found.", "Create",
                    () => { SettingsLocator.SelectAndPing(SettingsLocator.CreateDefault()); RefreshAll(); }),
                1 => new StepResult(SetupStatus.Ok, "ServicesSettings asset",
                    AssetDatabase.GetAssetPath(_settingsAssets[0]), "Ping",
                    () => SettingsLocator.SelectAndPing(_settingsAssets[0])),
                _ => new StepResult(SetupStatus.Warning, "ServicesSettings asset",
                    $"{_settingsAssets.Length} assets found — pick the active one below.", "Ping",
                    () => { if (ActiveSettings != null) SettingsLocator.SelectAndPing(ActiveSettings); }),
            };
        }

        private StepResult BuildBootSceneStep()
        {
            if (!BootSceneGenerator.BootSceneExists())
            {
                var settings = ActiveSettings;
                return settings == null
                    ? new StepResult(SetupStatus.Missing, "Boot scene", "Create the ServicesSettings asset first.")
                    : new StepResult(SetupStatus.Missing, "Boot scene", $"No scene at {BootSceneGenerator.BootScenePath}.", "Generate",
                        () =>
                        {
                            if (BootSceneGenerator.GenerateBootScene(settings, out var message))
                                Debug.Log($"[Nexenova.Setup] {message}");
                            else
                                Debug.LogWarning($"[Nexenova.Setup] {message}");
                            RefreshAll();
                        });
            }

            return BootSceneGenerator.BootSceneAtIndexZero()
                ? new StepResult(SetupStatus.Ok, "Boot scene", $"{BootSceneGenerator.BootScenePath} at build index 0.")
                : new StepResult(SetupStatus.Warning, "Boot scene", "Boot scene exists but is not first in Build Settings.", "Move to 0",
                    () => { BootSceneGenerator.InsertBootAtIndexZero(); RefreshAll(); });
        }

        private StepResult BuildNextSceneStep()
        {
            var settings = ActiveSettings;
            if (settings == null)
                return new StepResult(SetupStatus.Warning, "Next scene", "No settings asset yet.");
            if (string.IsNullOrEmpty(settings.NextSceneName))
                return new StepResult(SetupStatus.Ok, "Next scene", "Scene loading disabled (empty name).");

            return SetupValidators.NextSceneInBuildSettings(settings)
                ? new StepResult(SetupStatus.Ok, "Next scene", $"'{settings.NextSceneName}' is in Build Settings.")
                : new StepResult(SetupStatus.Warning, "Next scene",
                    $"'{settings.NextSceneName}' is not an enabled scene in Build Settings.");
        }

        private StepResult BuildUgsLinkStep()
        {
            return SetupValidators.IsUgsLinked()
                ? new StepResult(SetupStatus.Ok, "UGS project link", $"Linked (project {CloudProjectSettings.projectId}).")
                : new StepResult(SetupStatus.Missing, "UGS project link",
                    "Project is not linked to Unity Gaming Services.", "Open Services",
                    SetupValidators.OpenServicesSettings);
        }

        private StepResult BuildPurchasingStep()
        {
            var settings = ActiveSettings;
            if (settings == null || !settings.EnablePurchasing)
                return new StepResult(SetupStatus.Ok, "Purchasing package", "Purchasing disabled or no settings asset.");

            return SetupValidators.PurchasingEnabledButPackageMissing(settings)
                ? new StepResult(SetupStatus.Warning, "Purchasing package",
                    "Purchasing is enabled in settings but com.unity.purchasing is not installed.", "Add IAP",
                    () => ManifestPatcher.InstallOptional(ManifestPatcher.OptionalPackages[0]))
                : new StepResult(SetupStatus.Ok, "Purchasing package", "com.unity.purchasing installed.");
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Validates and fixes the services integration. Package installs edit Packages/manifest.json and reload the editor, so Run Full Setup performs them last.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                RefreshAll();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Run Full Setup", GUILayout.Width(140f)))
                RunFullSetup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            foreach (var step in _steps)
                DrawStep(step);

            if (_settingsAssets.Length > 1)
            {
                EditorGUILayout.Space();
                var names = _settingsAssets.Select(AssetDatabase.GetAssetPath).ToArray();
                _selectedSettings = EditorGUILayout.Popup("Active settings", _selectedSettings, names);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Optional packages", EditorStyles.boldLabel);
            foreach (var package in ManifestPatcher.OptionalPackages)
                DrawPackageToggle(package);

            EditorGUILayout.EndScrollView();
        }

        private void DrawStep(StepResult step)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            var (icon, color) = step.Status switch
            {
                SetupStatus.Ok => ("✓", new Color(0.3f, 0.85f, 0.3f)),
                SetupStatus.Warning => ("!", new Color(0.95f, 0.75f, 0.2f)),
                _ => ("✗", new Color(0.95f, 0.35f, 0.3f)),
            };

            var iconStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = color } };
            EditorGUILayout.LabelField(icon, iconStyle, GUILayout.Width(16f));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(step.Label, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(step.Detail, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            if (step.Fix != null && GUILayout.Button(step.FixLabel, GUILayout.Width(110f)))
                step.Fix.Invoke();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPackageToggle(OptionalPackage package)
        {
            var installed = _installed.TryGetValue(package.Id, out var value) && value;
            var toggled = EditorGUILayout.ToggleLeft($"{package.DisplayName} ({package.Id})", installed);
            if (toggled == installed)
                return;

            if (toggled)
            {
                if (package.Id == "com.google.play.games" &&
                    !ManifestPatcher.HasOpenUpmRegistry(ManifestPatcher.Load(), out _))
                {
                    EditorUtility.DisplayDialog("OpenUPM registry required",
                        "Google Play Games resolves from the OpenUPM registry. Add the scoped registry to Packages/manifest.json manually (see SETUP.md), then enable this toggle again.",
                        "OK");
                    return;
                }
                ManifestPatcher.InstallOptional(package);
            }
            else if (EditorUtility.DisplayDialog("Remove package",
                         $"Remove {package.Id} from this project's manifest?", "Remove", "Cancel"))
                ManifestPatcher.UninstallOptional(package);

            RefreshAll();
        }

        private void RunFullSetup()
        {
            if (_settingsAssets.Length == 0)
                SettingsLocator.SelectAndPing(SettingsLocator.CreateDefault());
            RefreshAll();

            var settings = ActiveSettings;
            if (settings != null && !BootSceneGenerator.BootSceneExists())
            {
                BootSceneGenerator.GenerateBootScene(settings, out var message);
                Debug.Log($"[Nexenova.Setup] {message}");
            }
            else if (!BootSceneGenerator.BootSceneAtIndexZero() && BootSceneGenerator.BootSceneExists())
            {
                BootSceneGenerator.InsertBootAtIndexZero();
            }
            RefreshAll();

            if (!SetupValidators.IsUgsLinked())
                Debug.LogWarning("[Nexenova.Setup] Link the project to Unity Gaming Services: Project Settings ▸ Services.");

            RefreshAll();
        }
    }
}
