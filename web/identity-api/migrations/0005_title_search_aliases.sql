CREATE TABLE song_title_search_aliases (
  song_id TEXT NOT NULL REFERENCES songs(song_id) ON DELETE CASCADE,
  search_key TEXT NOT NULL,
  PRIMARY KEY (song_id, search_key)
);
