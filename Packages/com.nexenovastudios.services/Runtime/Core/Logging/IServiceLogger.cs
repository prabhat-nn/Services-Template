#nullable enable
using System;

namespace Nexenova.Services
{
    /// <summary>
    /// Package-internal logging seam. Every log line is tagged with the module name.
    /// Errors are logged exactly once, at the adapter boundary where they are mapped.
    /// </summary>
    public interface IServiceLogger
    {
        void Info(string tag, string message);
        void Warning(string tag, string message);
        void Error(string tag, string message, Exception? exception = null);
    }
}
