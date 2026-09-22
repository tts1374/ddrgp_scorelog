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

    void SaveRegistered(
        string publicPlayerId,
        string appCredential,
        string? displayName = null);

    void SetDisplayName(string displayName);

    void SetAuthenticationInvalid(bool invalid);

    void Clear();
}

internal enum UserSecretPurpose
{
    AppCredential,
    RegistrationRequestId,
}

internal interface IUserSecretProtector
{
    byte[] Protect(string secret, UserSecretPurpose purpose);

    string Unprotect(byte[] protectedSecret, UserSecretPurpose purpose);
}

internal sealed class DpapiCurrentUserSecretProtector : IUserSecretProtector
{
    private static readonly byte[] AppCredentialEntropy =
        Encoding.UTF8.GetBytes("DDRGpScoreViewer.WebPlayer.AppCredential.v1");
    private static readonly byte[] RegistrationRequestEntropy =
        Encoding.UTF8.GetBytes("DDRGpScoreViewer.WebPlayer.RegistrationRequestId.v1");

    public byte[] Protect(string secret, UserSecretPurpose purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            return ProtectedData.Protect(
                plaintext,
                EntropyFor(purpose),
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Unprotect(byte[] protectedSecret, UserSecretPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        var plaintext = ProtectedData.Unprotect(
            protectedSecret,
            EntropyFor(purpose),
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

    private static byte[] EntropyFor(UserSecretPurpose purpose) => purpose switch
    {
        UserSecretPurpose.AppCredential => AppCredentialEntropy,
        UserSecretPurpose.RegistrationRequestId => RegistrationRequestEntropy,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };
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
    private readonly IUserSecretProtector protector;

    public FileWebPlayerIdentityStore(
        string metadataPath,
        string credentialPath,
        IUserSecretProtector? protector = null)
    {
        this.metadataPath = Path.GetFullPath(metadataPath);
        this.credentialPath = Path.GetFullPath(credentialPath);
        this.protector = protector ?? new DpapiCurrentUserSecretProtector();
    }

    public WebPlayerIdentitySnapshot Load()
    {
        var metadata = ReadMetadata();
        var pendingRegistrationRequestId = UnprotectPendingRegistrationRequest(metadata);
        string? credential = null;
        if (metadata.PublicPlayerId is not null && File.Exists(credentialPath))
        {
            var protectedCredential = File.ReadAllBytes(credentialPath);
            try
            {
                credential = protector.Unprotect(
                    protectedCredential,
                    UserSecretPurpose.AppCredential);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedCredential);
            }
        }

        if (metadata.PublicPlayerId is not null && credential is not null)
        {
            return new WebPlayerIdentitySnapshot(
                metadata.AuthenticationInvalid
                    ? PlayerIdentityState.AuthInvalid
                    : PlayerIdentityState.Registered,
                metadata.PublicPlayerId,
                credential,
                pendingRegistrationRequestId,
                metadata.DisplayName);
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
            pendingRegistrationRequestId);
    }

    public void SavePendingRegistration(string registrationRequestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationRequestId);
        var current = ReadMetadata();
        var protectedRequestId = protector.Protect(
            registrationRequestId,
            UserSecretPurpose.RegistrationRequestId);
        try
        {
            WriteMetadata(current with
            {
                ProtectedPendingRegistrationRequest = Convert.ToBase64String(
                    protectedRequestId),
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedRequestId);
        }
    }

    public void SaveRegistered(
        string publicPlayerId,
        string appCredential,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicPlayerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appCredential);
        var protectedCredential = protector.Protect(
            appCredential,
            UserSecretPurpose.AppCredential);
        try
        {
            WriteBytesAtomically(credentialPath, protectedCredential);
            WriteMetadata(new StoredWebPlayerIdentity(
                PublicPlayerId: publicPlayerId,
                ProtectedPendingRegistrationRequest: null,
                AuthenticationInvalid: false,
                DisplayName: displayName));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedCredential);
        }
    }

    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var current = ReadMetadata();
        if (current.PublicPlayerId is null || !File.Exists(credentialPath))
        {
            throw new InvalidOperationException(
                "A display name cannot be stored before registration.");
        }
        WriteMetadata(current with { DisplayName = displayName });
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
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
        if (File.Exists(credentialPath))
        {
            File.Delete(credentialPath);
        }
    }

    private StoredWebPlayerIdentity ReadMetadata()
    {
        if (!File.Exists(metadataPath))
        {
            return new StoredWebPlayerIdentity(null, null, false, null);
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

    private string? UnprotectPendingRegistrationRequest(StoredWebPlayerIdentity metadata)
    {
        if (metadata.ProtectedPendingRegistrationRequest is null)
        {
            return null;
        }

        byte[] protectedRequestId;
        try
        {
            protectedRequestId = Convert.FromBase64String(
                metadata.ProtectedPendingRegistrationRequest);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "The protected registration request ID is not valid base64.",
                exception);
        }

        try
        {
            return protector.Unprotect(
                protectedRequestId,
                UserSecretPurpose.RegistrationRequestId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedRequestId);
        }
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
        [property: JsonPropertyName("protected_pending_registration_request")]
        string? ProtectedPendingRegistrationRequest,
        [property: JsonPropertyName("authentication_invalid")]
        bool AuthenticationInvalid,
        [property: JsonPropertyName("display_name")] string? DisplayName);
}
