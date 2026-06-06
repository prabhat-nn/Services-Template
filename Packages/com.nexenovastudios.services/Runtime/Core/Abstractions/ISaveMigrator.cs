#nullable enable

namespace Nexenova.Services
{
    /// <summary>
    /// Hook for upgrading cloud-save payloads written by older app versions.
    /// The default implementation is pass-through; games register their own to
    /// transform <paramref name="payloadJson"/> between schema versions.
    /// </summary>
    public interface ISaveMigrator
    {
        string Migrate(string key, int fromVersion, int toVersion, string payloadJson);
    }
}
