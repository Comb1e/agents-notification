using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentNotifications.App.Infrastructure;

public sealed class Redactor
{
    private readonly IReadOnlyList<Regex> patterns;

    public Redactor(IEnumerable<string> patterns)
    {
        this.patterns = patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
            .ToArray();
    }

    public string Apply(string? value)
    {
        var result = value ?? string.Empty;
        foreach (var pattern in patterns) result = pattern.Replace(result, "[REDACTED]");
        return result;
    }
}

public static class TextTools
{
    public static string LimitRunes(string? value, int maximumRunes)
    {
        if (string.IsNullOrEmpty(value) || maximumRunes <= 0) return string.Empty;
        var runes = value.EnumerateRunes().Take(maximumRunes + 1).ToArray();
        if (runes.Length <= maximumRunes) return value;
        if (maximumRunes == 1) return "…";
        return string.Concat(runes.Take(maximumRunes - 1).Select(rune => rune.ToString())) + "…";
    }

    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string EscapeFeishuMarkdown(string value) => Regex.Replace(value, @"([\\`*_{}\[\]()<>#+.!|~-])", @"\$1");
}
