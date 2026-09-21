using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentNotifications.App.Configuration;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly AppPaths paths;
    private readonly string defaultsPath;

    public SettingsStore(AppPaths paths, string? defaultsPath = null)
    {
        this.paths = paths;
        this.defaultsPath = defaultsPath ?? Path.Combine(AppContext.BaseDirectory, "defaults.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var hasStoredSettings = File.Exists(paths.SettingsPath);
        var path = hasStoredSettings ? paths.SettingsPath : defaultsPath;
        AppSettings settings;
        await using (var stream = File.OpenRead(path))
        {
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Settings file is empty");
        }

        var migrated = BuiltInSourceRules.MigrateLegacy(settings.SourceRules);
        Validate(settings);
        if (hasStoredSettings && migrated) await SaveAsync(settings, cancellationToken);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        Directory.CreateDirectory(paths.DataDirectory);
        var temporaryPath = paths.SettingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, paths.SettingsPath, true);
    }

    public static void Validate(AppSettings settings)
    {
        if (settings.Version != 1) throw new InvalidDataException("Unsupported settings version");
        if (settings.Feishu.Domain is not ("feishu" or "lark")) throw new InvalidDataException("Feishu domain must be feishu or lark");
        if (settings.Feishu.RecipientType is not ("email" or "open_id" or "user_id" or "union_id" or "chat_id"))
            throw new InvalidDataException("Unsupported recipient type");
        if (settings.Runtime.DebounceMilliseconds is < 100 or > 30_000) throw new InvalidDataException("Invalid debounce interval");
        if (settings.Runtime.ReconcileSeconds is < 2 or > 300) throw new InvalidDataException("Invalid reconciliation interval");
        if (settings.Runtime.DispatcherPollMilliseconds is < 50 or > 30_000) throw new InvalidDataException("Invalid dispatcher poll interval");
        if (settings.Runtime.RetryBaseMilliseconds is < 100 or > 60_000) throw new InvalidDataException("Invalid retry base interval");
        if (settings.Runtime.RetryMaximumMinutes is < 1 or > 1440) throw new InvalidDataException("Invalid retry maximum");
        if (settings.Runtime.DeliveryExpiryHours is < 1 or > 168) throw new InvalidDataException("Invalid delivery expiry");
        if (settings.Runtime.MetadataRetentionDays is < 1 or > 90) throw new InvalidDataException("Invalid metadata retention");
        if (settings.Runtime.LogRetentionDays is < 1 or > 90) throw new InvalidDataException("Invalid log retention");
        if (settings.Runtime.LogMaximumBytes is < 1_000_000 or > 2_000_000_000) throw new InvalidDataException("Invalid log byte limit");
        if (settings.Runtime.CardTitleMaximumRunes is < 1 or > 1_000) throw new InvalidDataException("Invalid card title limit");
        if (settings.Runtime.CardBodyMaximumRunes is < 1 or > 20_000) throw new InvalidDataException("Invalid card body limit");
        foreach (var rule in settings.SourceRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.Pattern))
                throw new InvalidDataException("Source rules require a name and pattern");
            if (rule.MatchMode is not ("exact" or "regex")) throw new InvalidDataException("Source rule matchMode must be exact or regex");
            if (rule.MatchMode == "regex") ValidateRegex(rule.Pattern, "source rule");
        }
        foreach (var pattern in settings.RedactionPatterns) ValidateRegex(pattern, "redaction pattern");
    }

    private static void ValidateRegex(string pattern, string name)
    {
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)); }
        catch (ArgumentException exception) { throw new InvalidDataException($"Invalid {name}: {exception.Message}", exception); }
    }
}
