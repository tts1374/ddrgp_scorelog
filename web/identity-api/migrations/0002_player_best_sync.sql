PRAGMA foreign_keys = ON;

ALTER TABLE players ADD COLUMN best_sync_revision INTEGER NOT NULL DEFAULT 0;
ALTER TABLE players ADD COLUMN public_bests_updated_at TEXT;

CREATE TABLE songs (
    song_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    artist TEXT NOT NULL,
    version TEXT NOT NULL
) STRICT;

CREATE TABLE charts (
    chart_id TEXT PRIMARY KEY,
    song_id TEXT NOT NULL,
    play_style TEXT NOT NULL CHECK (play_style IN ('SINGLE', 'DOUBLE')),
    difficulty TEXT NOT NULL,
    level INTEGER NOT NULL CHECK (level BETWEEN 1 AND 19),
    is_removed INTEGER NOT NULL CHECK (is_removed IN (0, 1)),
    FOREIGN KEY (song_id) REFERENCES songs(song_id)
) STRICT;

CREATE INDEX idx_charts_song_id ON charts(song_id);

CREATE TABLE web_master_metadata (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
) STRICT;

CREATE TABLE player_chart_bests (
    player_id TEXT NOT NULL,
    chart_id TEXT NOT NULL,
    best_score INTEGER NOT NULL CHECK (best_score BETWEEN 0 AND 1000000),
    best_ex_score INTEGER NOT NULL CHECK (best_ex_score >= 0),
    best_clear_type TEXT NOT NULL CHECK (
        best_clear_type IN ('MFC', 'PFC', 'GFC', 'FC', 'CLEAR', 'FAILED')
    ),
    best_flare_rank TEXT CHECK (
        best_flare_rank IS NULL OR
        best_flare_rank IN ('EX', 'IX', 'VIII', 'VII', 'VI', 'V', 'IV', 'III', 'II', 'I')
    ),
    updated_at TEXT NOT NULL,
    PRIMARY KEY (player_id, chart_id),
    FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE,
    FOREIGN KEY (chart_id) REFERENCES charts(chart_id)
) STRICT;

CREATE INDEX idx_player_chart_bests_chart_score
ON player_chart_bests(chart_id, best_score DESC);

CREATE INDEX idx_player_chart_bests_chart_ex_score
ON player_chart_bests(chart_id, best_ex_score DESC);

CREATE TABLE best_sync_snapshots (
    snapshot_id TEXT PRIMARY KEY,
    player_id TEXT NOT NULL,
    projection_version INTEGER NOT NULL,
    master_version TEXT NOT NULL,
    expected_item_count INTEGER NOT NULL CHECK (expected_item_count >= 0),
    base_sync_revision INTEGER NOT NULL CHECK (base_sync_revision >= 0),
    committed_revision INTEGER,
    status TEXT NOT NULL CHECK (
        status IN ('PENDING', 'COMMITTED', 'ABORTED', 'EXPIRED')
    ),
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX idx_best_sync_snapshots_active_player
ON best_sync_snapshots(player_id)
WHERE status = 'PENDING';

CREATE INDEX idx_best_sync_snapshots_expiry
ON best_sync_snapshots(status, expires_at);

CREATE TABLE best_sync_snapshot_chunks (
    snapshot_id TEXT NOT NULL,
    chunk_id TEXT NOT NULL,
    content_digest TEXT NOT NULL CHECK (length(content_digest) = 64),
    item_count INTEGER NOT NULL CHECK (item_count >= 0),
    created_at TEXT NOT NULL,
    PRIMARY KEY (snapshot_id, chunk_id),
    FOREIGN KEY (snapshot_id) REFERENCES best_sync_snapshots(snapshot_id) ON DELETE CASCADE
) STRICT;

CREATE TABLE best_sync_snapshot_items (
    snapshot_id TEXT NOT NULL,
    chunk_id TEXT NOT NULL,
    chart_id TEXT NOT NULL,
    best_score INTEGER NOT NULL CHECK (best_score BETWEEN 0 AND 1000000),
    best_ex_score INTEGER NOT NULL CHECK (best_ex_score >= 0),
    best_clear_type TEXT NOT NULL CHECK (
        best_clear_type IN ('MFC', 'PFC', 'GFC', 'FC', 'CLEAR', 'FAILED')
    ),
    best_flare_rank TEXT CHECK (
        best_flare_rank IS NULL OR
        best_flare_rank IN ('EX', 'IX', 'VIII', 'VII', 'VI', 'V', 'IV', 'III', 'II', 'I')
    ),
    PRIMARY KEY (snapshot_id, chart_id),
    FOREIGN KEY (snapshot_id, chunk_id)
        REFERENCES best_sync_snapshot_chunks(snapshot_id, chunk_id) ON DELETE CASCADE
) STRICT;

CREATE TABLE best_sync_commit_guards (
    snapshot_id TEXT PRIMARY KEY,
    player_id TEXT NOT NULL,
    guard INTEGER NOT NULL CHECK (guard = 1),
    FOREIGN KEY (snapshot_id) REFERENCES best_sync_snapshots(snapshot_id) ON DELETE CASCADE,
    FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE
) STRICT;
