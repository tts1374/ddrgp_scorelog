PRAGMA foreign_keys = ON;

ALTER TABLE songs ADD COLUMN title_search_key TEXT NOT NULL DEFAULT '';

-- Backfill the characters in the current shared master. New imports write the
-- complete Unicode-normalized key with each song upsert.
UPDATE songs SET title_search_key = lower(title);
UPDATE songs SET title_search_key = replace(title_search_key, 'ñ', 'n') WHERE instr(title_search_key, 'ñ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'é', 'e') WHERE instr(title_search_key, 'é') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'ā', 'a') WHERE instr(title_search_key, 'ā') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ø', 'o') WHERE instr(title_search_key, 'Ø') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'ø', 'o') WHERE instr(title_search_key, 'ø') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ë', 'e') WHERE instr(title_search_key, 'Ë') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Æ', 'ae') WHERE instr(title_search_key, 'Æ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'æ', 'ae') WHERE instr(title_search_key, 'æ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ü', 'u') WHERE instr(title_search_key, 'Ü') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'ü', 'u') WHERE instr(title_search_key, 'ü') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'ä', 'a') WHERE instr(title_search_key, 'ä') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ё', 'ё') WHERE instr(title_search_key, 'Ё') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ф', 'ф') WHERE instr(title_search_key, 'Ф') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Λ', 'λ') WHERE instr(title_search_key, 'Λ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Δ', 'δ') WHERE instr(title_search_key, 'Δ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ω', 'ω') WHERE instr(title_search_key, 'Ω') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ａ', 'ａ') WHERE instr(title_search_key, 'Ａ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｓ', 'ｓ') WHERE instr(title_search_key, 'Ｓ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｃ', 'ｃ') WHERE instr(title_search_key, 'Ｃ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｋ', 'ｋ') WHERE instr(title_search_key, 'Ｋ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｐ', 'ｐ') WHERE instr(title_search_key, 'Ｐ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｄ', 'ｄ') WHERE instr(title_search_key, 'Ｄ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｔ', 'ｔ') WHERE instr(title_search_key, 'Ｔ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｍ', 'ｍ') WHERE instr(title_search_key, 'Ｍ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｏ', 'ｏ') WHERE instr(title_search_key, 'Ｏ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｈ', 'ｈ') WHERE instr(title_search_key, 'Ｈ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｅ', 'ｅ') WHERE instr(title_search_key, 'Ｅ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ｒ', 'ｒ') WHERE instr(title_search_key, 'Ｒ') > 0;
UPDATE songs SET title_search_key = replace(title_search_key, 'Ⅱ', 'ⅱ') WHERE instr(title_search_key, 'Ⅱ') > 0;
