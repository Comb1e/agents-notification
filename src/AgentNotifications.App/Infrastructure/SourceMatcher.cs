using System.Text.RegularExpressions;
using AgentNotifications.App.Configuration;

namespace AgentNotifications.App.Infrastructure;

public sealed class SourceMatcher
{
    private readonly IReadOnlyList<(SourceRuleSettings Rule, Regex? Regex)> rules;

    public SourceMatcher(IEnumerable<SourceRuleSettings> rules)
    {
        this.rules = rules
            .Where(rule => rule.Enabled)
            .Select(rule => (rule, rule.MatchMode == "regex"
                ? new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200))
                : null))
            .ToArray();
    }

    public string? Match(string appUserModelId)
    {
        if (appUserModelId.StartsWith("Comb1e.AgentNotifications_", StringComparison.OrdinalIgnoreCase)) return null;
        if (appUserModelId.Contains("!CodexCoreCommandRunner", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var (rule, regex) in rules)
        {
            var matches = regex?.IsMatch(appUserModelId)
                ?? string.Equals(rule.Pattern, appUserModelId, StringComparison.OrdinalIgnoreCase);
            if (matches) return rule.Name;
        }

        return null;
    }
}
