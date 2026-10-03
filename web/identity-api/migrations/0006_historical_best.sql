ALTER TABLE player_credentials ADD COLUMN activation_state TEXT NOT NULL DEFAULT 'active'
    CHECK (activation_state IN ('pending', 'active'));
ALTER TABLE best_sync_snapshots ADD COLUMN credential_id TEXT REFERENCES player_credentials(id);

CREATE TABLE best_sync_write_guards (
    id TEXT PRIMARY KEY,
    guard INTEGER NOT NULL CHECK (guard = 1),
    changed_count INTEGER NOT NULL DEFAULT 0
) STRICT;

CREATE TABLE best_replacement_authorizations (
    snapshot_id TEXT PRIMARY KEY REFERENCES best_sync_snapshots(snapshot_id) ON DELETE CASCADE,
    credential_id TEXT NOT NULL REFERENCES player_credentials(id) ON DELETE CASCADE,
    content_digest TEXT NOT NULL,
    content_json TEXT NOT NULL,
    base_revision INTEGER NOT NULL,
    expires_at TEXT NOT NULL,
    consumed_at TEXT
) STRICT;
