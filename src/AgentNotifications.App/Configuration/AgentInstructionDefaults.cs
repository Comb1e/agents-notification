namespace AgentNotifications.App.Configuration;

public static class AgentInstructionDefaults
{
    public const string UserProfilesDirectory = @"C:\Users";
    public const int PollMilliseconds = 1_000;
    public const int SettleMilliseconds = 1_000;
    public const int MaximumSourceBytes = 1_048_576;
    public static readonly IReadOnlySet<string> InstructionFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AGENTS.md", "CLAUDE.md", "GEMINI.md", "GROK.md", "ZCODE.md", "QODER.md", "copilot-instructions.md",
    };
}
