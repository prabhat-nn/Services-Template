#nullable enable
using System;
using UnityEngine;

namespace Nexenova.Services.Core
{
    internal sealed class UnityServiceLogger : IServiceLogger
    {
        private readonly bool _verbose;

        public UnityServiceLogger(bool verbose)
        {
            _verbose = verbose;
        }

        public void Info(string tag, string message)
        {
            if (_verbose)
                Debug.Log($"[Nexenova.{tag}] {message}");
        }

        public void Warning(string tag, string message)
        {
            Debug.LogWarning($"[Nexenova.{tag}] {message}");
        }

        public void Error(string tag, string message, Exception? exception = null)
        {
            Debug.LogError($"[Nexenova.{tag}] {message}");
            if (exception != null)
                Debug.LogException(exception);
        }
    }
}
