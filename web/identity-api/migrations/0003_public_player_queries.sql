PRAGMA foreign_keys = ON;

CREATE INDEX idx_charts_style_active_level
ON charts(play_style, is_removed, level, chart_id);

CREATE INDEX idx_songs_version_song
ON songs(version, song_id);

CREATE INDEX idx_player_chart_bests_player_chart
ON player_chart_bests(player_id, chart_id);
