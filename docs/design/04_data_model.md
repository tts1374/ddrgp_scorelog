# データモデル設計

最終系で扱うデータの概念モデルです。現時点ではDB実装前の設計メモであり、SQLiteスキーマを固定するものではありません。マスタDBと個人スコアDBは分離し、PoC出力やローカル素材はGit管理しません。

## 目的

- マスタ情報、プレー履歴、解析ログ、失敗画像を分ける。
- マスタDB更新で個人スコアDBを上書きしない。
- 1プレー1レコード保存を前提にする。
- 低確信度や解析失敗をDB保存せず、追跡可能なログとして残す。

## データ領域

### マスタDB

ファイル名候補:

```text
ddrgp-master.sqlite
```

役割:

- BEMANIWiki 由来の曲・譜面情報を保持する。
- GitHub Actions で生成する。
- GitHub Releases の成果物として配布する。
- 個人スコアDBとは分離する。

主な概念:

- song
- chart
- master metadata
- source snapshot

### 個人スコアDB

ファイル名候補:

```text
ddrgp-scores.sqlite
```

役割:

- 1プレー1レコードで保存する。
- 自己ベストだけではなく全プレー履歴を保持する。
- マスタDB更新後も過去履歴を参照できる。

主な概念:

- play
- analysis result
- source capture reference
- app version

### PoC出力

配置:

```text
data/
```

例:

- `data/vision_poc/`
- `data/vision_poc_timestamped/`
- `data/vision_poc_manifest/`
- `data/vision_poc_capture_dry_run/`

Git管理しない。

### ログ

配置:

```text
logs/
```

または本番アプリのローカルアプリデータ配下。

Git管理しない。

## マスタDB概念モデル

### `songs`

楽曲単位。

M4初期スキーマのフィールド:

- `song_id`
- `title`
- `artist`
- `version`
- `bpm`
- `category`
- `source_version`
- `movie_stage`
- `availability`
- `free_play_available`
- `grand_prix_play_available`
- `official_availability_match`
- `notes`
- `created_at`
- `updated_at`

`title_reading` はM5以降の正規化・照合で必要性が見えた時点で追加を検討する。

M4の公式収録曲一覧に突合できた場合、`title` / `artist` は公式表記をcanonicalとして保持する。Wiki側表記と公式表記が異なる場合は `song_aliases` に残し、canonicalを上書きして失われないようにする。

### `song_aliases`

公式canonicalとは別に、取得元やローカル素材が持つ表記差を保持する。

M4初期スキーマのフィールド:

- `alias_id`
- `song_id`
- `alias_title`
- `alias_artist`
- `alias_type`
- `source`

初期用途は、BEMANIWiki由来の曲名/artistが公式表記と異なる場合の `wiki_source` alias。M5は通常 `songs.title` を読むが、ローカルmetadataや既存素材がalias表記の場合は `song_aliases` を補助的に参照できる。

### `charts`

譜面単位。

M4初期スキーマのフィールド:

- `chart_id`
- `song_id`
- `play_style`
- `difficulty`
- `level`
- `raw_level`
- `shock_arrow`
- `notes`
- `is_removed`
- `is_limited`

### `master_metadata`

生成物メタデータ。

候補フィールド:

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

### `source_snapshots`

取得元HTMLや解析元情報の追跡。

候補フィールド:

- `source_url`
- `fetched_at`
- `content_hash`
- `parser_version`
- `html_content`

M4初期実装では、source snapshot は生成DB内に保存する。DB自体は生成物としてGit管理しない。

## 個人スコアDB概念モデル

### `plays`

1プレー1レコード。

候補フィールド:

- `play_id`
- `played_at`
- `master_version`
- `song_id`
- `chart_id`
- `title_ocr_raw`
- `title_ocr_normalized`
- `play_style`
- `difficulty`
- `level`
- `score`
- `rank`
- `clear_type`
- `max_combo`
- `marvelous`
- `perfect`
- `great`
- `good`
- `miss`
- `ok`
- `ex_score`
- `capture_hash`
- `duplicate_key`
- `analysis_confidence`
- `app_version`
- `created_at`

### `analysis_logs`

保存可否の判断ログ。

候補フィールド:

- `analysis_id`
- `play_id`
- `event_type`
- `confirmed_result`
- `duplicate`
- `confirmation_mode`
- `timestamp_ms`
- `candidate_duration_ms`
- `ocr_status`
- `master_match_status`
- `skip_reason`
- `log_path`
- `capture_path`

本番でDBへ入れるか、JSONログのまま残すかは別途決める。

## 解析結果モデル

保存判定前に一時的に集約する概念。

```text
AnalyzedResult
  event
  score_fields
  chart_identity_fields
  ocr_confidence
  master_match_candidates
  save_decision
```

### `save_decision`

候補:

- `save`
- `skip_unconfirmed`
- `skip_duplicate`
- `skip_transition`
- `skip_ocr_failed`
- `skip_low_confidence`
- `skip_master_ambiguous`
- `skip_master_not_found`

M7 PoCでは、DB保存前の最終 `save_decision` ではなく、`m7_save_decision_preview.*` の preview status として以下だけを出す。

- `preview_save_candidate`
- `blocked_readiness`
- `needs_identity_review`
- `needs_digit_review`
- `missing_required_material`

`preview_save_candidate` はM8へ渡す候補材料が揃ったプレビュー状態であり、`save`、DB保存成功、曲ID/譜面ID確定を意味しない。

## 重複保存防止

PoCでは簡易 `duplicate_key` を使うが、本番では以下を組み合わせる。

- 曲ID
- 譜面ID
- スコア
- 判定数
- キャプチャ画像ハッシュ
- 短時間内の同一結果

同一リザルトと判定した場合は追加保存しない。

## 未決事項

- M4以降のスキーマ互換方針とマイグレーション方式
- ローカルアプリデータ配下の最終保存パス
- 解析ログをDB内に持つか、JSONファイル参照にするか
- 失敗画像の保存期間
- 画像ハッシュ方式
- duplicate key の本格方式
