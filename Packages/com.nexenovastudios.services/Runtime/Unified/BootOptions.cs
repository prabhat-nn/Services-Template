#nullable enable

namespace Nexenova.Services
{
    /// <summary>Scene-flow options for the built-in boot controller.</summary>
    public sealed class BootOptions
    {
        public string NextSceneName { get; }
        public bool ProceedToSceneOnFailure { get; }

        public BootOptions(ServicesSettings s)
        {
            NextSceneName = s.NextSceneName;
            ProceedToSceneOnFailure = s.ProceedToSceneOnFailure;
        }

        internal BootOptions(string nextSceneName, bool proceedToSceneOnFailure)
        {
            NextSceneName = nextSceneName;
            ProceedToSceneOnFailure = proceedToSceneOnFailure;
        }
    }
}
