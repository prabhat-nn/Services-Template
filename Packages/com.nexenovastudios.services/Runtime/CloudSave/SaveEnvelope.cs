#nullable enable
using Newtonsoft.Json;

namespace Nexenova.Services.CloudSave
{
    /// <summary>
    /// Every cloud-save value is wrapped in this envelope so tampered or
    /// version-mismatched data is rejected (or migrated), never crashed on.
    /// </summary>
    internal sealed class SaveEnvelope
    {
        public int SchemaVersion { get; set; }

        /// <summary>Informational only — never trusted for game logic (client clocks lie).</summary>
        public long SavedAtUnixMs { get; set; }

        /// <summary>Payload serialized separately so migration can rewrite it as raw JSON.</summary>
        public string PayloadJson { get; set; } = string.Empty;
    }

    internal static class SaveJson
    {
        /// <summary>Hardened settings — polymorphic deserialization of remote data is forbidden.</summary>
        public static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
        };
    }
}
