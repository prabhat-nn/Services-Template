#nullable enable

namespace Nexenova.Services.Editor
{
    /// <summary>Outcome of a single setup checklist step.</summary>
    public enum SetupStatus
    {
        Ok,
        Warning,
        Missing,
    }

    /// <summary>One row in the setup checklist: status, label and optional fix action.</summary>
    public sealed class StepResult
    {
        public SetupStatus Status { get; }
        public string Label { get; }
        public string Detail { get; }
        public string FixLabel { get; }
        public System.Action? Fix { get; }

        public StepResult(SetupStatus status, string label, string detail, string fixLabel = "", System.Action? fix = null)
        {
            Status = status;
            Label = label;
            Detail = detail;
            FixLabel = fixLabel;
            Fix = fix;
        }
    }
}
