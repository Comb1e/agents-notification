namespace AgentNotifications.App.Configuration;

public static class BuiltInSourceRules
{
    public const string ChatGptName = "ChatGPT";
    public const string QoderName = "Qoder";
    public const string ZCodeName = "ZCode";

    public const string ChatGptPattern = "^OpenAI\\.Codex_[^!]+!App$";
    public const string QoderPattern = "com.qoder.app";
    public const string ZCodeLegacyPattern = "electron.app.ZCode";
    public const string ZCodePattern = "^(?:electron\\.app\\.ZCode|dev\\.zcode\\.app)$";

    public static SourceRuleSettings CreateChatGpt(bool enabled) => new()
    {
        Name = ChatGptName,
        Pattern = ChatGptPattern,
        MatchMode = "regex",
        Enabled = enabled,
    };

    public static SourceRuleSettings CreateQoder(bool enabled) => new()
    {
        Name = QoderName,
        Pattern = QoderPattern,
        MatchMode = "exact",
        Enabled = enabled,
    };

    public static SourceRuleSettings CreateZCode(bool enabled) => new()
    {
        Name = ZCodeName,
        Pattern = ZCodePattern,
        MatchMode = "regex",
        Enabled = enabled,
    };

    public static bool IsBuiltInName(string name) =>
        name.Equals(ChatGptName, StringComparison.OrdinalIgnoreCase)
        || name.Equals(QoderName, StringComparison.OrdinalIgnoreCase)
        || name.Equals(ZCodeName, StringComparison.OrdinalIgnoreCase);

    public static bool MigrateLegacy(IList<SourceRuleSettings> rules)
    {
        var changed = false;
        foreach (var rule in rules.Where(rule =>
                     rule.Name.Equals(ZCodeName, StringComparison.OrdinalIgnoreCase)
                     && rule.MatchMode.Equals("exact", StringComparison.OrdinalIgnoreCase)
                     && rule.Pattern.Equals(ZCodeLegacyPattern, StringComparison.OrdinalIgnoreCase)))
        {
            rule.Pattern = ZCodePattern;
            rule.MatchMode = "regex";
            changed = true;
        }

        return changed;
    }
}
