-- A separate OAuth purpose keeps public-record reset independent of account deletion.
CREATE TABLE web_oauth_requests_new (
    state_digest TEXT PRIMARY KEY,
    nonce TEXT NOT NULL,
    browser_digest TEXT NOT NULL,
    purpose TEXT NOT NULL CHECK (purpose IN ('login', 'register', 'login-connect', 'link', 'unlink', 'delete', 'public-bests-delete')),
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
INSERT INTO web_oauth_requests_new SELECT * FROM web_oauth_requests;
DROP TABLE web_oauth_requests;
ALTER TABLE web_oauth_requests_new RENAME TO web_oauth_requests;
