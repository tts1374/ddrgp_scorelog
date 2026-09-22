PRAGMA foreign_keys = ON;

DELETE FROM player_chart_bests WHERE player_id = 'pl_e2e';
DELETE FROM players WHERE id = 'pl_e2e';
DELETE FROM charts WHERE chart_id IN ('chart_e2e_a', 'chart_e2e_b');
DELETE FROM songs WHERE song_id IN ('song_e2e_a', 'song_e2e_b');

INSERT INTO players
  (id, public_player_id, display_name, created_at, updated_at, public_bests_updated_at)
VALUES
  ('pl_e2e', 'p_e2eeeeeeeeeeeeeeeeeeee', '2TEN', '2026-09-20T00:00:00Z', '2026-09-20T00:00:00Z', '2026-09-21T01:02:03Z');

INSERT INTO songs (song_id, title, artist, version) VALUES
  ('song_e2e_a', 'MAX 300', 'Ω', 'DDRMAX'),
  ('song_e2e_b', 'VOLAQUAS', 'BEMANI Sound Team', 'DanceDanceRevolution WORLD');

INSERT INTO charts (chart_id, song_id, play_style, difficulty, level, is_removed) VALUES
  ('chart_e2e_a', 'song_e2e_a', 'SINGLE', 'EXPERT', 15, 0),
  ('chart_e2e_b', 'song_e2e_b', 'SINGLE', 'EXPERT', 17, 0);

INSERT INTO player_chart_bests
  (player_id, chart_id, best_score, best_ex_score, best_clear_type, best_flare_rank, updated_at)
VALUES
  ('pl_e2e', 'chart_e2e_a', 990000, 1250, 'PFC', 'IX', '2026-09-21T01:02:03Z');
