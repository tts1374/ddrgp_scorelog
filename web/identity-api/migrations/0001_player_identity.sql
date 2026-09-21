PRAGMA foreign_keys = ON;

CREATE TABLE players (
    id TEXT PRIMARY KEY,
    public_player_id TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL CHECK (
        length(display_name) BETWEEN 1 AND 64
    ),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
) STRICT;

CREATE TABLE player_credentials (
    id TEXT PRIMARY KEY,
    player_id TEXT NOT NULL,
    type TEXT NOT NULL CHECK (type = 'app'),
    secret_digest TEXT NOT NULL CHECK (length(secret_digest) = 64),
    created_at TEXT NOT NULL,
    last_used_at TEXT,
    revoked_at TEXT,
    FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE
) STRICT;

CREATE INDEX idx_player_credentials_player_id
ON player_credentials(player_id);

CREATE TABLE player_registration_requests (
    request_digest TEXT PRIMARY KEY CHECK (length(request_digest) = 64),
    player_id TEXT NOT NULL UNIQUE,
    credential_id TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL,
    FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE,
    FOREIGN KEY (credential_id) REFERENCES player_credentials(id) ON DELETE CASCADE
) STRICT;
