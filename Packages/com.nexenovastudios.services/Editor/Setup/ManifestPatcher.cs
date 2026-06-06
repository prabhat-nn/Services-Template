#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;

namespace Nexenova.Services.Editor
{
    /// <summary>Optional package the wizard can add to or remove from the consuming game's manifest.</summary>
    public sealed class OptionalPackage
    {
        public string DisplayName { get; }
        public string Id { get; }
        public string Version { get; }
        public string? CompanionId { get; }
        public string? CompanionVersion { get; }

        public OptionalPackage(string displayName, string id, string version, string? companionId = null, string? companionVersion = null)
        {
            DisplayName = displayName;
            Id = id;
            Version = version;
            CompanionId = companionId;
            CompanionVersion = companionVersion;
        }
    }

    /// <summary>
    /// Reads and patches the consuming project's Packages/manifest.json:
    /// OpenUPM scoped registry, optional package dependencies, and UPM re-resolution.
    /// </summary>
    public static class ManifestPatcher
    {
        public const string RegistryUrl = "https://package.openupm.com";
        public const string RegistryName = "package.openupm.com";

        public static readonly string[] RequiredScopes =
        {
            "com.cysharp.unitask",
            "jp.hadashikick.vcontainer",
            "com.google.play.games",
            "com.google.external-dependency-manager",
            "com.lupidan.apple-signin-unity",
            "com.gitamend.improvedtimers",
        };

        public static readonly OptionalPackage[] OptionalPackages =
        {
            new("In-App Purchasing", "com.unity.purchasing", "4.13.2"),
            new("Google Play Games", "com.google.play.games", "2.1.0", "com.google.external-dependency-manager", "1.2.187"),
            new("Apple Sign-In", "com.lupidan.apple-signin-unity", "1.5.0"),
            new("Improved Timers", "com.gitamend.improvedtimers", "1.0.2"),
            new("Unity Utils", "com.gitamend.unityutils", "https://github.com/adammyhre/Unity-Utils.git"),
        };

        public static string ManifestPath => Path.GetFullPath(Path.Combine("Packages", "manifest.json"));

        public static JObject Load() => JObject.Parse(File.ReadAllText(ManifestPath));

        public static void Save(JObject manifest) =>
            File.WriteAllText(ManifestPath, manifest.ToString(Formatting.Indented) + "\n");

        public static bool HasOpenUpmRegistry(JObject manifest, out string[] missingScopes)
        {
            missingScopes = RequiredScopes;
            var registry = FindOpenUpmRegistry(manifest);
            if (registry == null)
                return false;

            var scopes = registry["scopes"] as JArray ?? new JArray();
            var present = scopes.Select(s => s.ToString()).ToHashSet(StringComparer.Ordinal);
            missingScopes = RequiredScopes.Where(s => !present.Contains(s)).ToArray();
            return missingScopes.Length == 0;
        }

        public static JObject ApplyOpenUpmRegistry(JObject manifest)
        {
            if (manifest["scopedRegistries"] is not JArray registries)
            {
                registries = new JArray();
                manifest["scopedRegistries"] = registries;
            }

            var registry = FindOpenUpmRegistry(manifest);
            if (registry == null)
            {
                registry = new JObject
                {
                    ["name"] = RegistryName,
                    ["url"] = RegistryUrl,
                    ["scopes"] = new JArray(),
                };
                registries.Add(registry);
            }

            if (registry["scopes"] is not JArray scopes)
            {
                scopes = new JArray();
                registry["scopes"] = scopes;
            }

            var present = scopes.Select(s => s.ToString()).ToHashSet(StringComparer.Ordinal);
            foreach (var scope in RequiredScopes)
            {
                if (!present.Contains(scope))
                    scopes.Add(scope);
            }

            return manifest;
        }

        public static JObject ApplyDependency(JObject manifest, string id, string version)
        {
            if (manifest["dependencies"] is not JObject dependencies)
            {
                dependencies = new JObject();
                manifest["dependencies"] = dependencies;
            }

            dependencies[id] = version;
            return manifest;
        }

        public static JObject RemoveDependency(JObject manifest, string id)
        {
            (manifest["dependencies"] as JObject)?.Remove(id);
            return manifest;
        }

        public static bool HasDependency(JObject manifest, string id) =>
            (manifest["dependencies"] as JObject)?.ContainsKey(id) == true;

        public static bool IsPackageInstalled(string id)
        {
            var registered = PackageInfo.GetAllRegisteredPackages();
            if (registered != null && registered.Length > 0)
                return registered.Any(p => p.name == id);
            return HasDependency(Load(), id);
        }

        public static void EnsureOpenUpmRegistry()
        {
            Save(ApplyOpenUpmRegistry(Load()));
            Client.Resolve();
        }

        public static void InstallOptional(OptionalPackage package)
        {
            var manifest = ApplyOpenUpmRegistry(Load());
            ApplyDependency(manifest, package.Id, package.Version);
            if (package.CompanionId != null)
                ApplyDependency(manifest, package.CompanionId, package.CompanionVersion!);
            Save(manifest);
            Client.Resolve();
        }

        public static void UninstallOptional(OptionalPackage package)
        {
            var manifest = RemoveDependency(Load(), package.Id);
            if (package.CompanionId != null)
                RemoveDependency(manifest, package.CompanionId);
            Save(manifest);
            Client.Resolve();
        }

        private static JObject? FindOpenUpmRegistry(JObject manifest) =>
            (manifest["scopedRegistries"] as JArray)?
            .OfType<JObject>()
            .FirstOrDefault(r => string.Equals(r["url"]?.ToString().TrimEnd('/'), RegistryUrl, StringComparison.OrdinalIgnoreCase));
    }
}
