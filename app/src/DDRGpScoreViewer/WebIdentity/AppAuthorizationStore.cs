using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace DDRGpScoreViewer.WebIdentity;

internal enum AppAuthorizationState
{
    AppAuthorizationPending,
    CredentialActivationPending,
}

internal sealed record PendingAppAuthorization(
    string Id,
    string RequestSecret,
    string Purpose,
    string? ExpectedPublicPlayerId,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    string? BrowserUrl = null,
    string? ComparisonCode = null,
    AppAuthorizationState State = AppAuthorizationState.AppAuthorizationPending,
    string? Credential = null,
    WebPlayer? Player = null)
{
    public override string ToString() => $"Purpose={Purpose}; State={State}; PendingCredentialPresent={Credential is not null}";
}

internal interface IAppAuthorizationStore
{
    PendingAppAuthorization? Load();
    void Save(PendingAppAuthorization authorization);
    void Clear();
}

internal sealed class FileAppAuthorizationStore(string path, IUserSecretProtector? protector = null) : IAppAuthorizationStore
{
    private readonly string path = Path.GetFullPath(path);
    private readonly IUserSecretProtector protector = protector ?? new DpapiCurrentUserSecretProtector();

    public PendingAppAuthorization? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var bytes = File.ReadAllBytes(path);
        try
        {
            var json = protector.Unprotect(bytes, UserSecretPurpose.AppAuthorization);
            return JsonSerializer.Deserialize<PendingAppAuthorization>(json)
                ?? throw new InvalidDataException("Pending App authorization is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Save(PendingAppAuthorization authorization)
    {
        var protectedBytes = protector.Protect(JsonSerializer.Serialize(authorization), UserSecretPurpose.AppAuthorization);
        try
        {
            FileWebPlayerIdentityStore.WriteBytesAtomically(path, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public void Clear()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
