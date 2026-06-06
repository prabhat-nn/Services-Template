#nullable enable

namespace Nexenova.Services.CloudSave
{
    internal sealed class PassThroughSaveMigrator : ISaveMigrator
    {
        public string Migrate(string key, int fromVersion, int toVersion, string payloadJson) => payloadJson;
    }
}
