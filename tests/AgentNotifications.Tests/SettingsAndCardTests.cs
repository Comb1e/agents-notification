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
    public void Card_escapes_markdown_and_respects_rune_limits()
    {
        var now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        var builder = new FeishuCardBuilder(new RuntimeSettings { CardTitleMaximumRunes = 3, CardBodyMaximumRunes = 3 });
        var record = new DeliveryRecord(
            "key", "source", "Qoder", 1, now, now, "A😀BC", "x*yZ", "hash", null,
            DeliveryState.Sending, 1, now, now.AddHours(1), null, now);

        using var document = JsonDocument.Parse(builder.Build(record));
        var root = document.RootElement;
        Assert.Equal("A😀…", root.GetProperty("header").GetProperty("title").GetProperty("content").GetString());
        var content = root.GetProperty("body").GetProperty("elements")[0].GetProperty("content").GetString();
        Assert.Contains(@"x\*…", content);
    }

    private static AppSettings ValidSettings() => new()
    {
        SourceRules = [new SourceRuleSettings { Name = "Qoder", Pattern = "com.qoder.app", MatchMode = "exact" }],
    };
}
