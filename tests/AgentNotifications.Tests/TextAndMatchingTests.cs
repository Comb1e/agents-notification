using AgentNotifications.App.Configuration;
using AgentNotifications.App.Infrastructure;

namespace AgentNotifications.Tests;

public sealed class TextAndMatchingTests
{
    [Theory]
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!App", "ChatGPT")]
    [InlineData("com.qoder.app", "Qoder")]
    [InlineData("electron.app.ZCode", "ZCode")]
    [InlineData("dev.zcode.app", "ZCode")]
    public void Matches_supported_app_ids(string appId, string expected)
    {
        Assert.Equal(expected, Matcher().Match(appId));
    }

    [Theory]
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!CodexCoreCommandRunner")]
    [InlineData("com.qoder.app.beta")]
    [InlineData("electron.app.ZCode.helper")]
    [InlineData("dev.zcode.app.helper")]
    public void Rejects_near_miss_app_ids(string appId)
    {
        Assert.Null(Matcher().Match(appId));
    }

    [Fact]
    public void Limits_at_unicode_rune_boundaries()
    {
        var result = TextTools.LimitRunes("A😀BC", 3);
        Assert.Equal("A😀…", result);
        Assert.Equal(3, result.EnumerateRunes().Count());
        Assert.Equal("…", TextTools.LimitRunes("😀x", 1));
    }

    [Fact]
    public void Redacts_each_configured_pattern()
    {
        var redactor = new Redactor(["token=[^ ]+", "secret"]);
        Assert.Equal("[REDACTED] [REDACTED]", redactor.Apply("token=abc SECRET"));
    }

    [Fact]
    public void Retry_delay_uses_bounded_jitter()
    {
        var settings = new RuntimeSettings { RetryBaseMilliseconds = 1_000, RetryMaximumMinutes = 1 };
        var policy = new RetryPolicy(settings, new FakeRandom(0));
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.DelayForAttempt(1));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.DelayForAttempt(20));
    }

    [Fact]
    public void Escapes_feishu_markdown_control_characters()
    {
        Assert.Equal(@"a\*b \[x\]", TextTools.EscapeFeishuMarkdown("a*b [x]"));
    }

    private static SourceMatcher Matcher() => new([
        BuiltInSourceRules.CreateChatGpt(true),
        BuiltInSourceRules.CreateQoder(true),
        BuiltInSourceRules.CreateZCode(true),
    ]);
}
