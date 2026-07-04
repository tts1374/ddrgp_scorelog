# M4 マスタDB生成設計

M4では、BEMANIWiki 由来の楽曲・譜面情報を、M5のマスタ照合PoCが参照できるSQLite DBへ変換する。ここでは本番配布や照合ロジックへ進みすぎず、HTML入力、解析境界、DBスキーマ、生成物の扱いを固定する。

## 目的

- BEMANIWiki の全曲リストHTMLから `songs` と `charts` を生成する。
- マスタDBと個人スコアDBを分離する。
- 取得元HTMLのhashとsnapshotを残し、表構造変化を検出しやすくする。
- M5の曲名正規化、ファジーマッチ、候補絞り込みが参照できる安定した初期スキーマを作る。

## 入力

取得元URL:

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

## 出力

ローカル生成先の既定:

```text
data/master/ddrgp-master.sqlite
```

生成DBはGit管理しない。将来の配布用DBは GitHub Releases 成果物として扱う。

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
- `notes`
- `created_at`
- `updated_at`

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
- `song_count`
- `chart_count`

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

fixtureテストでは、セル結合、注記付きレベル、削除/限定/パック記号、SP/DP片方のみ、CHALLENGEなし、同名曲・同アーティスト、複数バージョン表を扱う。実HTMLの件数確認はネットワークに依存するため、通常テストには含めない。

## M5へ渡すもの

M4で渡してよいもの:

- 曲名、artist、BPM、出典、分類記号。
- `song_id` と `chart_id`。
- SP/DP、難易度、レベルの譜面一覧。
- source hash と generator version。

M4ではまだ扱わないもの:

- OCR曲名の正規化。
- ファジーマッチ。
- 候補一覧と照合スコア。
- OCR結果から曲ID/譜面IDを一意に決める処理。
- 個人スコアDB保存。
