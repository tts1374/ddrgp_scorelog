using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDRGpScoreViewer.WebIdentity;

internal interface IWebPlayerIdentityStore
{
    WebPlayerIdentitySnapshot Load();

    void SavePendingRegistration(string registrationRequestId);

    void SaveRegistered(string publicPlayerId, string appCredential);

    void SetAuthenticationInvalid(bool invalid);

    void Clear();
}

internal interface IUserCredentialProtector
{
    byte[] Protect(string credential);

    string Unprotect(byte[] protectedCredential);
}

internal sealed class DpapiCurrentUserCredentialProtector : IUserCredentialProtector
{
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("DDRGpScoreViewer.WebPlayer.AppCredential.v1");

    public byte[] Protect(string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        var plaintext = Encoding.UTF8.GetBytes(credential);
        try
        {
            return ProtectedData.Protect(
                plaintext,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Unprotect(byte[] protectedCredential)
    {
        ArgumentNullException.ThrowIfNull(protectedCredential);
        var plaintext = ProtectedData.Unprotect(
            protectedCredential,
            OptionalEntropy,
            DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

internal sealed class FileWebPlayerIdentityStore : IWebPlayerIdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
    };
    private readonly string metadataPath;
    private readonly string credentialPath;
    private readonly IUserCredentialProtector protector;

    public FileWebPlayerIdentityStore(
        string metadataPath,
        string credentialPath,
        IUserCredentialProtector? protector = null)
    {
        this.metadataPath = Path.GetFullPath(metadataPath);
        this.credentialPath = Path.GetFullPath(credentialPath);
        this.protector = protector ?? new DpapiCurrentUserCredentialProtector();
    }

    public WebPlayerIdentitySnapshot Load()
    {
        var metadata = ReadMetadata();
        string? credential = null;
        if (File.Exists(credentialPath))
        {
            credential = protector.Unprotect(File.ReadAllBytes(credentialPath));
        }

        if (metadata.PublicPlayerId is not null && credential is not null)
        {
            return new WebPlayerIdentitySnapshot(
                metadata.AuthenticationInvalid
                    ? PlayerIdentityState.AuthInvalid
                    : PlayerIdentityState.Registered,
                metadata.PublicPlayerId,
                credential,
                metadata.PendingRegistrationRequestId);
        }

        if (metadata.PublicPlayerId is not null)
        {
            throw new InvalidDataException(
                "Web Player identity metadata exists without its protected App Credential.");
        }

        // A protected credential may have been committed immediately before a process
        // interruption. The persisted request ID lets registration retry overwrite it
        // with the same credential without exposing it as a registered identity.
        return new WebPlayerIdentitySnapshot(
            PlayerIdentityState.Unregistered,
            null,
            null,
            metadata.PendingRegistrationRequestId);
    }

    public void SavePendingRegistration(string registrationRequestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationRequestId);
        var current = ReadMetadata();
        WriteMetadata(current with
        {
            PendingRegistrationRequestId = registrationRequestId,
        });
    }

    public void SaveRegistered(string publicPlayerId, string appCredential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicPlayerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appCredential);
        var protectedCredential = protector.Protect(appCredential);
        WriteBytesAtomically(credentialPath, protectedCredential);
        WriteMetadata(new StoredWebPlayerIdentity(
            PublicPlayerId: publicPlayerId,
            PendingRegistrationRequestId: null,
            AuthenticationInvalid: false));
    }

    public void SetAuthenticationInvalid(bool invalid)
    {
        var current = ReadMetadata();
        if (current.PublicPlayerId is null || !File.Exists(credentialPath))
        {
            throw new InvalidOperationException(
                "Authentication state cannot be changed before registration.");
        }
        WriteMetadata(current with { AuthenticationInvalid = invalid });
    }

    public void Clear()
    {
        if (File.Exists(credentialPath))
        {
            File.Delete(credentialPath);
        }
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
    }

    private StoredWebPlayerIdentity ReadMetadata()
    {
        if (!File.Exists(metadataPath))
        {
            return new StoredWebPlayerIdentity(null, null, false);
        }
        var stored = JsonSerializer.Deserialize<StoredWebPlayerIdentity>(
            File.ReadAllText(metadataPath, Encoding.UTF8),
            JsonOptions) ?? throw new InvalidDataException(
                "Web Player identity metadata is empty.");
        if (stored.AuthenticationInvalid && stored.PublicPlayerId is null)
        {
            throw new InvalidDataException(
                "Web Player identity metadata has an invalid authentication state.");
        }
        return stored;
    }

    private void WriteMetadata(StoredWebPlayerIdentity metadata)
    {
        var json = JsonSerializer.Serialize(metadata, JsonOptions) + "\n";
        WriteBytesAtomically(metadataPath, new UTF8Encoding(false).GetBytes(json));
    }

    private static void WriteBytesAtomically(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                $"Identity storage parent directory could not be determined: {path}");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record StoredWebPlayerIdentity(
        [property: JsonPropertyName("public_player_id")] string? PublicPlayerId,
        [property: JsonPropertyName("pending_registration_request_id")]
        string? PendingRegistrationRequestId,
        [property: JsonPropertyName("authentication_invalid")]
        bool AuthenticationInvalid);
}
