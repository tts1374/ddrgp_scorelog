namespace DDRGpScoreViewer.WebIdentity;

internal enum PlayerIdentityState
{
    Unregistered,
    Registered,
    AuthInvalid,
}

internal enum PlayerIdentityRequestStatus
{
    Succeeded,
    AuthenticationInvalid,
    NetworkError,
    ServerError,
    InvalidResponse,
    LocalStorageError,
    InvalidState,
}

internal sealed class WebPlayerIdentitySnapshot
{
    public WebPlayerIdentitySnapshot(
        PlayerIdentityState state,
        string? publicPlayerId,
        string? appCredential,
        string? pendingRegistrationRequestId)
    {
        State = state;
        PublicPlayerId = publicPlayerId;
        AppCredential = appCredential;
        PendingRegistrationRequestId = pendingRegistrationRequestId;
    }

    public PlayerIdentityState State { get; }

    public string? PublicPlayerId { get; }

    public string? AppCredential { get; }

    public string? PendingRegistrationRequestId { get; }

    public override string ToString() =>
        $"State={State}; PublicPlayerId={PublicPlayerId ?? "none"}; CredentialPresent={AppCredential is not null}";
}

internal sealed record WebPlayer(
    string PublicPlayerId,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record PlayerIdentityOperationResult(
    PlayerIdentityRequestStatus Status,
    WebPlayerIdentitySnapshot Identity,
    WebPlayer? Player = null);
