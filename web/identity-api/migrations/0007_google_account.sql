CREATE TABLE google_identities (
    issuer TEXT NOT NULL CHECK (issuer = 'https://accounts.google.com'),
    sub TEXT NOT NULL,
    player_id TEXT NOT NULL UNIQUE REFERENCES players(id) ON DELETE CASCADE,
    PRIMARY KEY (issuer, sub)
) STRICT;

CREATE TABLE web_sessions (
    digest TEXT PRIMARY KEY,
    issuer TEXT NOT NULL,
    sub TEXT NOT NULL,
    email TEXT NOT NULL,
    auth_time INTEGER,
    browser_digest TEXT NOT NULL,
    csrf_digest TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
) STRICT;
CREATE INDEX idx_web_sessions_identity ON web_sessions(issuer, sub);

CREATE TABLE app_authorizations (
    id TEXT PRIMARY KEY,
    secret_digest TEXT NOT NULL,
    purpose TEXT NOT NULL CHECK (purpose IN ('connect', 'unlink')),
    intent TEXT CHECK (intent IN ('register', 'login', 'link', 'unlink')),
    start_player_id TEXT REFERENCES players(id) ON DELETE CASCADE,
    start_credential_id TEXT REFERENCES player_credentials(id) ON DELETE CASCADE,
    expected_public_player_id TEXT,
    player_id TEXT REFERENCES players(id) ON DELETE CASCADE,
    credential_id TEXT REFERENCES player_credentials(id) ON DELETE CASCADE,
    session_digest TEXT,
    browser_digest TEXT,
    issuer TEXT,
    sub TEXT,
    status TEXT NOT NULL CHECK (status IN ('PENDING', 'APPROVED', 'ACTIVATED', 'LINKED', 'UNLINKED', 'CANCELLED', 'INVALIDATED')),
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
) STRICT;

CREATE TABLE web_oauth_requests (
    state_digest TEXT PRIMARY KEY,
    nonce TEXT NOT NULL,
    browser_digest TEXT NOT NULL,
    purpose TEXT NOT NULL CHECK (purpose IN ('login', 'register', 'login-connect', 'link', 'unlink', 'delete')),
    app_authorization_id TEXT REFERENCES app_authorizations(id) ON DELETE CASCADE,
    intent TEXT,
    start_session_digest TEXT,
    player_id TEXT REFERENCES players(id) ON DELETE CASCADE,
    issuer TEXT,
    sub TEXT,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    consumed_at TEXT
) STRICT;

CREATE TABLE web_operation_proofs (
    digest TEXT PRIMARY KEY,
    purpose TEXT NOT NULL,
    player_id TEXT REFERENCES players(id) ON DELETE CASCADE,
    issuer TEXT NOT NULL,
    sub TEXT NOT NULL,
    session_digest TEXT NOT NULL REFERENCES web_sessions(digest) ON DELETE CASCADE,
    browser_digest TEXT NOT NULL,
    app_authorization_id TEXT REFERENCES app_authorizations(id) ON DELETE CASCADE,
    confirmed_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    consumed_at TEXT
) STRICT;

CREATE TABLE account_deletion_confirmations (
    id TEXT PRIMARY KEY,
    digest TEXT NOT NULL UNIQUE,
    operation_digest TEXT UNIQUE,
    player_id TEXT,
    issuer TEXT,
    sub TEXT,
    session_digest TEXT,
    browser_digest TEXT,
    status TEXT NOT NULL CHECK (status IN ('UNCONFIRMED', 'CONFIRMED', 'DELETED')),
    confirmed_at TEXT,
    expires_at TEXT NOT NULL,
    consumed_at TEXT
) STRICT;

CREATE TABLE account_write_guards (
    id TEXT PRIMARY KEY,
    guard INTEGER NOT NULL CHECK (guard = 1)
) STRICT;
