#nullable enable
using System.Collections.Generic;

namespace Nexenova.Services.RemoteConfig
{
    /// <summary>
    /// Declarative numeric range rule: values outside [Min, Max] are clamped and logged.
    /// Config values are data, never behavior — there is deliberately no rule kind that
    /// could turn a config string into executable behavior.
    /// </summary>
    public sealed class ConfigRule
    {
        public string Key { get; }
        public double Min { get; }
        public double Max { get; }

        public ConfigRule(string key, double min, double max)
        {
            Key = key;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// A named group of rules. The package registers its own set for the keys it consumes;
    /// games register additional instances to validate their keys:
    /// <c>builder.RegisterInstance(new ConfigRuleSet("game", myRules));</c>
    /// </summary>
    public sealed class ConfigRuleSet
    {
        public string Name { get; }
        public IReadOnlyList<ConfigRule> Rules { get; }

        public ConfigRuleSet(string name, IReadOnlyList<ConfigRule> rules)
        {
            Name = name;
            Rules = rules;
        }
    }

    internal sealed class ConfigValidator
    {
        private readonly Dictionary<string, ConfigRule> _rules = new();
        private readonly IServiceLogger _logger;

        public ConfigValidator(IEnumerable<ConfigRuleSet> ruleSets, IServiceLogger logger)
        {
            _logger = logger;
            foreach (var set in ruleSets)
            foreach (var rule in set.Rules)
                _rules[rule.Key] = rule;
        }

        public long Clamp(string key, long value)
        {
            if (!_rules.TryGetValue(key, out var rule))
                return value;
            var clamped = (long)System.Math.Clamp(value, rule.Min, rule.Max);
            if (clamped != value)
                _logger.Warning("RemoteConfig", $"'{key}' value {value} out of range [{rule.Min}, {rule.Max}] — clamped to {clamped}.");
            return clamped;
        }

        public double Clamp(string key, double value)
        {
            if (!_rules.TryGetValue(key, out var rule))
                return value;
            var clamped = System.Math.Clamp(value, rule.Min, rule.Max);
            if (!clamped.Equals(value))
                _logger.Warning("RemoteConfig", $"'{key}' value {value} out of range [{rule.Min}, {rule.Max}] — clamped to {clamped}.");
            return clamped;
        }
    }

    /// <summary>Rules for the keys this package consumes itself.</summary>
    internal static class PackageConfigRules
    {
        public static ConfigRuleSet Create() => new(
            "nexenova.package",
            new[]
            {
                new ConfigRule(ConfigKeys.EconomyMaxGrantPerCall, 1, long.MaxValue),
                new ConfigRule(ConfigKeys.EconomyMaxGrantedPerMinute, 1, long.MaxValue),
            });
    }
}
