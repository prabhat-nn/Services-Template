#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Nexenova.Services.Editor
{
    /// <summary>
    /// Feeds the package's <c>Runtime/link.xml</c> to the IL2CPP managed linker.
    ///
    /// Unity does not auto-scan <c>link.xml</c> files inside immutable (git/registry)
    /// package folders — only those under <c>Assets/</c> or injected via a build hook
    /// like this one. Without it, the <c>Nexenova.Services.*</c> assemblies get stripped
    /// on IL2CPP player builds and VContainer (which resolves constructors by reflection)
    /// throws at runtime: "Type does not found injectable constructor, type: EventBus",
    /// aborting the container build and freezing the boot scene. Works in the Editor
    /// (Mono, no stripping) but fails on device — this hook makes the package self-contained.
    /// </summary>
    internal sealed class ServicesLinkXmlProvider : IUnityLinkerProcessor
    {
        // GUID of Packages/com.nexenovastudios.services/Runtime/link.xml
        private const string LinkXmlGuid = "1a2b3c4d5e6f70819a2b3c4d5e6f7081";

        public int callbackOrder => 0;

        public string? GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(LinkXmlGuid);
            return string.IsNullOrEmpty(assetPath) ? null : Path.GetFullPath(assetPath);
        }
    }
}
