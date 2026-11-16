using System.Security.Cryptography;
using System.Text;

namespace AgentNotifications.App.Configuration;

public interface ISecretStore
{
    Task<string> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string secret, CancellationToken cancellationToken = default);
}

public sealed class DpapiSecretStore(AppPaths paths) : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AgentNotifications.v1");

    public async Task<string> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SecretsPath)) return string.Empty;
        var encrypted = await File.ReadAllBytesAsync(paths.SecretsPath, cancellationToken);
        var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clear);
    }

    public async Task SaveAsync(string secret, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var clear = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = paths.SecretsPath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken);
        File.Move(temporaryPath, paths.SecretsPath, true);
        CryptographicOperations.ZeroMemory(clear);
    }
}
