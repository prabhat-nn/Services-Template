#nullable enable
using System.Linq;
using Newtonsoft.Json.Linq;
using Nexenova.Services.Editor;
using NUnit.Framework;

namespace Nexenova.Services.Tests
{
    public sealed class ManifestPatcherTests
    {
        private static JObject EmptyManifest() => JObject.Parse("{\"dependencies\":{\"com.unity.ugui\":\"2.0.0\"}}");

        [Test]
        public void ApplyOpenUpmRegistry_WhenAbsent_AddsRegistryWithAllScopes()
        {
            var manifest = ManifestPatcher.ApplyOpenUpmRegistry(EmptyManifest());

            Assert.IsTrue(ManifestPatcher.HasOpenUpmRegistry(manifest, out var missing));
            Assert.IsEmpty(missing);
            var registry = (JObject)((JArray)manifest["scopedRegistries"]!)[0];
            Assert.AreEqual(ManifestPatcher.RegistryUrl, registry["url"]!.ToString());
        }

        [Test]
        public void ApplyOpenUpmRegistry_WhenPartialScopes_MergesMissingOnes()
        {
            var manifest = EmptyManifest();
            manifest["scopedRegistries"] = new JArray(new JObject
            {
                ["name"] = "package.openupm.com",
                ["url"] = ManifestPatcher.RegistryUrl,
                ["scopes"] = new JArray("com.cysharp.unitask", "com.someone.custom"),
            });

            ManifestPatcher.ApplyOpenUpmRegistry(manifest);

            Assert.IsTrue(ManifestPatcher.HasOpenUpmRegistry(manifest, out _));
            var scopes = ((JArray)((JObject)((JArray)manifest["scopedRegistries"]!)[0])["scopes"]!)
                .Select(s => s.ToString()).ToArray();
            CollectionAssert.Contains(scopes, "com.someone.custom");
            CollectionAssert.IsSubsetOf(ManifestPatcher.RequiredScopes, scopes);
        }

        [Test]
        public void ApplyOpenUpmRegistry_WhenComplete_IsIdempotent()
        {
            var manifest = ManifestPatcher.ApplyOpenUpmRegistry(EmptyManifest());
            var before = manifest.ToString();

            ManifestPatcher.ApplyOpenUpmRegistry(manifest);

            Assert.AreEqual(before, manifest.ToString());
        }

        [Test]
        public void HasOpenUpmRegistry_WhenAbsent_ReportsAllScopesMissing()
        {
            Assert.IsFalse(ManifestPatcher.HasOpenUpmRegistry(EmptyManifest(), out var missing));
            CollectionAssert.AreEquivalent(ManifestPatcher.RequiredScopes, missing);
        }

        [Test]
        public void ApplyDependency_AddsWithoutTouchingOtherKeys()
        {
            var manifest = ManifestPatcher.ApplyDependency(EmptyManifest(), "com.unity.purchasing", "4.13.2");

            Assert.IsTrue(ManifestPatcher.HasDependency(manifest, "com.unity.purchasing"));
            Assert.IsTrue(ManifestPatcher.HasDependency(manifest, "com.unity.ugui"));
            Assert.AreEqual("4.13.2", manifest["dependencies"]!["com.unity.purchasing"]!.ToString());
        }

        [Test]
        public void RemoveDependency_RemovesOnlyTarget()
        {
            var manifest = ManifestPatcher.ApplyDependency(EmptyManifest(), "com.unity.purchasing", "4.13.2");

            ManifestPatcher.RemoveDependency(manifest, "com.unity.purchasing");

            Assert.IsFalse(ManifestPatcher.HasDependency(manifest, "com.unity.purchasing"));
            Assert.IsTrue(ManifestPatcher.HasDependency(manifest, "com.unity.ugui"));
        }

        [Test]
        public void RemoveDependency_MissingKey_IsNoOp()
        {
            var manifest = EmptyManifest();
            Assert.DoesNotThrow(() => ManifestPatcher.RemoveDependency(manifest, "com.absent.package"));
        }
    }
}
