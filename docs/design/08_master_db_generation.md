# M4 マスタDB生成設計

M4では、BEMANIWiki 由来の楽曲・譜面情報と、公式収録曲一覧由来のプレー可否を、M5のマスタ照合PoCが参照できるSQLite DBへ変換する。ここでは本番配布や照合ロジックへ進みすぎず、HTML入力、解析境界、DBスキーマ、生成物の扱いを固定する。

## 目的

- BEMANIWiki の全曲リストHTMLから `songs` と `charts` を生成する。
- 公式収録曲一覧HTMLから `free_play_available` / `grand_prix_play_available` を付与する。
- マスタDBと個人スコアDBを分離する。
- 取得元HTMLのhashとsnapshotを残し、表構造変化を検出しやすくする。
- M5の曲名正規化、ファジーマッチ、候補絞り込みが参照できる安定した初期スキーマを作る。

## 入力

譜面マスタ取得元URL:

```text
https://bemaniwiki.com/index.php?DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88
```

2026-07-04時点の対象表は、以下の2段ヘッダを持つ。

```text
分類 / 曲名 / アーティスト / 出典 / BPM / MV/St / SINGLE / DOUBLE
Be / Ba / Di / Ex / Ch / Ba / Di / Ex / Ch
```

パーサはこのヘッダを持つ表だけを楽曲リストとして扱う。セル結合されたバージョン見出しは `songs.version` / `songs.category` に入れる。レベルが `-` または空の譜面は未存在として `charts` に作らない。

注記付きレベルは raw 表記を `charts.raw_level` に保持し、整数 `charts.level` は最初に現れる数字列から取得する。`10(旧9)` や `10;` は `10`、`[SA] 12` は `12` として扱い、数字を連結しない。`[SA]`、`SA`、`Shock`、`ショック` を含む表記は `charts.shock_arrow=true` とする。

曲名やartistなどの表セルに含まれる脚注リンクは、リンク本文が `*2` のような `*` + 数字だけの場合に限り、マスタ本文から除外する。これはBEMANIWikiの脚注番号を曲名へ混入させないための処理で、`neko*neko` のように曲名本文へ含まれるアスタリスクは残す。

プレー可否取得元URL:

```text
https://p.eagate.573.jp/game/eacddr/konaddr/info/mlist.html
```

公式収録曲一覧では、`タイトル` / `アーティスト` / `フリープレー` / `グランプリプレー` を持つ表だけをプレー可否ソースとして扱う。`グランプリプレー` 列に `〇` がある曲を `songs.grand_prix_play_available=true` とする。アーケードプレーのみの表は、GP対象曲判定には使わない。

公式リストとWiki譜面マスタの突合は、まず曲名+artistの正規化一致で行い、artistが空または表記差がある場合は曲名が公式リスト内で一意な場合だけ曲名一致で補完する。`Ё` / `Ë` のような装飾記号差や一部のキリル/ラテン混在差はalias正規化でも照合し、`alias_title_artist` / `alias_unique_title` として区別する。公式に突合できた曲は `songs.title` / `songs.artist` を公式表記へ寄せ、Wiki由来表記差は `song_aliases` に `wiki_source` として保存する。突合結果は `official_availability_match` に残す。公式リストにない曲や曖昧な曲は `grand_prix_play_available=false` のままにし、M5の通常候補から除外する。

## 出力

ローカル生成先の既定:

```text
data/master/ddrgp-master.sqlite
```

生成DBはGit管理しない。将来の配布用DBは GitHub Releases 成果物として扱う。

CI生成では `.github/workflows/build-master-db.yml` を使う。workflowは手動実行と週次定期実行を持ち、fixtureテスト、実HTMLからのSQLite生成、`python -m master.inspect` による必須metadataキー検査、`master_metadata` と実テーブル件数の整合検査、`source_snapshots` 件数検査、source hash / source URL の整合検査を行う。生成DBと `master-summary.json` は `ddrgp-master-<run_number>` artifact として保存し、Git管理対象にはしない。`master-summary.json` にはテーブル件数、snapshot件数、Wiki/公式source URL、parser version、公式プレー可否の突合件数を出力する。

Releases配布は、artifactで生成結果と取得元構造変化検出を確認できる状態が安定してから追加する。

## 初期スキーマ

### `songs`

- `song_id`: HTML由来テキストから作る安定hash。
- `title`
- `artist`
- `version`: セル結合の分類見出し。
- `source_version`: 表の `出典` 列。
- `bpm`
- `category`
- `movie_stage`: 表の `MV/St` 列。
- `availability`: 表の `分類` 列。
- `free_play_available`: 公式収録曲一覧の `フリープレー` 列が `〇` か。
- `grand_prix_play_available`: 公式収録曲一覧の `グランプリプレー` 列が `〇` か。
- `official_availability_match`: 公式収録曲一覧との突合状態。`title_artist` / `unique_title` / `alias_title_artist` / `alias_unique_title` / `ambiguous_title` / `ambiguous_alias_title_artist` / `ambiguous_alias_title` / `not_found` / `not_checked`。
- `notes`
- `created_at`
- `updated_at`

### `song_aliases`

- `alias_id`
- `song_id`
- `alias_title`
- `alias_artist`
- `alias_type`: 現時点では `wiki_source`。
- `source`: 現時点では `bemaniwiki`。

公式canonicalへ寄せた際にWiki側の曲名/artist表記が異なる場合だけ保存する。M5などの消費側は、通常は `songs.title` / `songs.artist` を公式canonicalとして読み、ローカルmetadataや旧表記がWiki側表記を持つ場合だけ `song_aliases` を解決補助として使う。

### `charts`

- `chart_id`: `song_id + play_style + difficulty` 由来の安定hash。
- `song_id`
- `play_style`: `SINGLE` または `DOUBLE`。
- `difficulty`: `BEGINNER`、`BASIC`、`DIFFICULT`、`EXPERT`、`CHALLENGE`。
- `level`: 1から19の整数。
- `raw_level`: 注記を含む元レベル表記。
- `shock_arrow`: 元レベル表記にショックアローらしい記号があるか。
- `is_removed`: `分類` 列から削除候補として読めるか。
- `is_limited`: `分類` 列が空でないか。
- `notes`: 初期実装では `分類` 列の内容。

同じ曲名・同じアーティストは同じ `song_id` として扱う。同一 `chart_id` の譜面行が複数回出て、保持値が食い違う場合は、HTML構造または入力解釈の変化として生成を失敗させる。

### `master_metadata`

- `master_version`
- `source_url`
- `generated_at`
- `generator_version`
- `source_hash`
- `official_source_url`
- `official_source_hash`
- `song_count`
- `chart_count`
- `free_play_available_song_count`
- `grand_prix_play_available_song_count`
- `official_availability_matched_song_count`
- `song_alias_count`

### `source_snapshots`

- `snapshot_id`
- `source_url`
- `fetched_at`
- `content_hash`
- `parser_version`
- `html_content`

## 構造変化検出

以下の場合は生成を失敗させる。

- 楽曲リストの2段ヘッダを持つ表が見つからない。
- `songs` または `charts` が0件になる。
- 同一 `chart_id` の譜面行が食い違う。
- SQLite制約に反するレベルや譜面種別が出る。
- CI生成後の `master_metadata` 件数と実テーブル件数が一致しない。
- CI生成後の `source_snapshots` がWikiのみなら1件、公式プレー可否込みなら2件ではない。
- CI生成後の `master_metadata.source_hash` と `source_snapshots.content_hash` が一致しない。
- CI生成後の `master_metadata.source_url` と `source_snapshots.source_url` が一致しない。
- CI生成後の `master_metadata.official_source_hash` と公式 `source_snapshots.content_hash` が一致しない。
- CI生成後の `master_metadata.official_source_url` と公式 `source_snapshots.source_url` が一致しない。
- CI生成後の `master_metadata` に必須キーがない、または必須値が空。

fixtureテストでは、セル結合、注記付きレベル、脚注リンク除去、曲名本文のアスタリスク保持、削除/限定/パック記号、SP/DP片方のみ、CHALLENGEなし、同名曲・同アーティスト、複数バージョン表を扱う。実HTMLの件数確認はネットワークに依存するため、通常テストには含めない。

## M5へ渡すもの

M4で渡してよいもの:

- 曲名、artist、BPM、出典、分類記号。
- 公式収録曲一覧由来の `free_play_available` / `grand_prix_play_available`。
- `song_id` と `chart_id`。
- SP/DP、難易度、レベルの譜面一覧。
- source hash と generator version。

M4ではまだ扱わないもの:

- OCR曲名の正規化。
- ファジーマッチ。
- 候補一覧と照合スコア。
- OCR結果から曲ID/譜面IDを一意に決める処理。
- 個人スコアDB保存。
