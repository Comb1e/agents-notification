using System.Text.Json;
using AgentNotifications.App.Configuration;
using AgentNotifications.App.Domain;
using AgentNotifications.App.Feishu;

namespace AgentNotifications.Tests;

public sealed class SettingsAndCardTests
{
    [Fact]
    public void Rejects_invalid_configured_regex()
    {
        var settings = ValidSettings();
        settings.RedactionPatterns = ["["];
        Assert.Throws<InvalidDataException>(() => SettingsStore.Validate(settings));
    }

    [Fact]
    public void Rejects_out_of_range_runtime_values()
    {
        var settings = ValidSettings();
        settings.Runtime.DispatcherPollMilliseconds = 0;
        Assert.Throws<InvalidDataException>(() => SettingsStore.Validate(settings));
    }

    [Fact]
    public async Task Migrates_and_persists_legacy_zcode_source_rule()
    {
        var directory = Path.Combine(Path.GetTempPath(), "agent-notifications-settings-tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(
            directory,
            Path.Combine(directory, "config.json"),
            Path.Combine(directory, "secrets.dat"),
            Path.Combine(directory, "deliveries.db"),
            Path.Combine(directory, "logs"));
        try
        {
            var store = new SettingsStore(paths, Path.Combine(directory, "unused-defaults.json"));
            var settings = ValidSettings();
            settings.SourceRules =
            [
                new SourceRuleSettings
                {
                    Name = "ZCode",
                    Pattern = BuiltInSourceRules.ZCodeLegacyPattern,
                    MatchMode = "exact",
                    Enabled = false,
                },
            ];
            await store.SaveAsync(settings);

            var migrated = await store.LoadAsync();

            var rule = Assert.Single(migrated.SourceRules);
            Assert.Equal(BuiltInSourceRules.ZCodePattern, rule.Pattern);
            Assert.Equal("regex", rule.MatchMode);
            Assert.False(rule.Enabled);
            var persisted = await File.ReadAllTextAsync(paths.SettingsPath);
            Assert.Contains(BuiltInSourceRules.ZCodePattern.Replace("\\", "\\\\"), persisted, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Does_not_replace_custom_zcode_source_rule()
    {
        var rules = new List<SourceRuleSettings>
        {
            new() { Name = "ZCode", Pattern = "custom.zcode.app", MatchMode = "exact", Enabled = true },
        };

        Assert.False(BuiltInSourceRules.MigrateLegacy(rules));
        Assert.Equal("custom.zcode.app", rules[0].Pattern);
        Assert.Equal("exact", rules[0].MatchMode);
    }

    [Fact]
    public void Card_escapes_markdown_and_respects_rune_limits()
    {
        var now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        var builder = new FeishuCardBuilder(new RuntimeSettings { CardTitleMaximumRunes = 3, CardBodyMaximumRunes = 3 });
        var record = new DeliveryRecord(
            "key", "source", "Qoder", 1, now, now, "A😀BC", "x*yZ", "hash", null,
            DeliveryState.Sending, 1, now, now.AddHours(1), null, now);

        using var document = JsonDocument.Parse(builder.Build(record));
        var root = document.RootElement;
        Assert.True(root.GetProperty("config").GetProperty("update_multi").GetBoolean());
        Assert.Equal("A😀…", root.GetProperty("header").GetProperty("title").GetProperty("content").GetString());
        var content = root.GetProperty("body").GetProperty("elements")[0].GetProperty("content").GetString();
        Assert.Contains(@"x\*…", content);
    }

    private static AppSettings ValidSettings() => new()
    {
        SourceRules = [new SourceRuleSettings { Name = "Qoder", Pattern = "com.qoder.app", MatchMode = "exact" }],
    };
}
