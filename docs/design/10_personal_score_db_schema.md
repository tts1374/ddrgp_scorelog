# M8 正式個人スコアDBスキーマ設計

M8 preview完了後の正式 `ddrgp-scores.sqlite` 初期スキーマ案と、migration境界を固定する。ここで扱うのはスキーマ契約と互換チェックであり、本番insert、既定自動保存、duplicate key本格実装、低信頼度ログ本番保存はまだ実装しない。

## 目的

- M8 preview最小 `plays` と正式個人スコアDB `plays` を別物として扱う。
- 1プレー1レコードの正式履歴テーブル、保存スキップ/解析ログ、DB metadata、migration metadata、source capture reference の責務を分ける。
- `PRAGMA user_version` だけで正式DB判定をせず、metadata table と必須tableを合わせて互換チェックする。
- M8 preview DB、未知スキーマ、既存の壊れたDBを正式DBとして開かない。

## 実装済みの小さな契約

正式スキーマ候補のコード側契約は `tools/vision_poc/personal_score_db_schema.py` に置く。

- `PERSONAL_SCORE_DB_SCHEMA_VERSION = 1`
- `score_db_metadata`
- `schema_migrations`
- `source_captures`
- `plays`
- `analysis_logs`
- `create_personal_score_db_schema()`
- `personal_score_db_compatibility_errors()`
- `initialize_personal_score_db_if_empty()`
- `prepare_personal_score_db_for_write()`
- `prepare_personal_score_db_file_for_write(path)`

これはCLIから本番DBへinsertする入口ではない。次フェーズで保存処理を実装する前に、正式DBとして作るべきtableと、preview DBを拒否する条件をテストできるようにするためのschema contractである。

## M8 previewとの境界

M8 preview最小 `plays` は以下の用途に限定する。

- `m8_planned_play_records.*` のrow contractをSQLiteへinsertできるか確認する。
- in-memory write previewと明示file output previewの内部整合を確認する。
- `schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema` でpreview専用であることを示す。

正式個人スコアDBの `plays` は別物であり、以下を直接持たない。

- `source_organized_file`
- `source_confirmation_mode`
- `analysis_payload_status`
- `identity_signal_source`
- `m5_identity_signal_status`
- `m5_jacket_match_status`
- M7aの `recognized_digits` / `expected_value` / `match`
- OCR raw / normalized

これらは保存判定前の候補観測、review材料、または解析ログ側の材料であり、正式 `plays` の保存値確定列として扱わない。

## 正式 `plays`

正式 `plays` は1プレー1レコードの履歴を持つ。初期候補列は以下。

- `play_id`: 本番保存時に生成する安定ID。
- `played_at`: リザルト確定時刻。timestampなしPoCの `played_at_ms=0` をそのまま正式値にしない。
- `master_version`: 保存時に参照したマスタDB version。
- `song_id` / `chart_id`: 保存判定後のID。M5 `identity_signal_*` をそのまま確定ID扱いしない。
- `score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score`: 保存判定後の数値。
- `rank`、`clear_type`: 初期は空で逃がさず、未取得時の扱いをinsert前に決める。
- `capture_hash`: 元キャプチャ参照と重複防止用のhash。
- `source_capture_id`: `source_captures` への参照。
- `duplicate_key`: 本番重複判定用key。現行PoCのscore由来簡易keyとは別物にする。
- `analysis_confidence`: 保存判定後の総合信頼度。
- `app_version`
- `created_at`

`plays` は自己ベストではなく全履歴を保持する。自己ベスト集計や表示用viewは後続で追加する。

## 解析ログと保存スキップ

`analysis_logs` は保存成功、保存スキップ、低信頼度、例外を追跡するためのtable候補である。

`analysis_logs` に置くもの:

- `analysis_status`: `saved` / `skipped` / `low_confidence` / `error`
- `save_boundary_status`
- `skip_reason`
- `event_type`
- `confirmed_result`
- `duplicate`
- `confirmation_mode`
- `timestamp_ms`
- `candidate_duration_ms`
- `identity_signal_status`
- `digit_review_status`
- `analysis_confidence`
- `analysis_summary_json`
- `log_path`

`analysis_logs` は保存判定の説明と再調査の入口であり、正式保存値を二重管理する場所ではない。OCR raw、M5 identity signal、M7a recognized digits などの詳細材料をどこまでJSONに含めるかは、低信頼度ログ本番仕様の段階で決める。

## Source Capture Reference

`source_captures` は、保存またはスキップ判断の元になったフレーム参照を保持する。

候補列:

- `capture_id`
- `capture_hash`
- `captured_at`
- `source_kind`: `manifest` / `timestamped` / `capture` / `manual` / `unknown`
- `source_path`
- `manifest_image_path`
- `frame_index`
- `created_at`

画像そのものはGit管理しない。正式アプリではローカルアプリデータ配下またはログディレクトリに置き、DBにはhashと参照だけを残す方針にする。

## Metadata と Migration

正式DB判定は以下の全てを見る。

- `PRAGMA user_version`
- `score_db_metadata.schema_name=personal_score_db`
- `score_db_metadata.schema_contract_scope=production_personal_score_db`
- `score_db_metadata.production_schema_status=production_schema`
- 必須tableの存在
- `schema_migrations` の適用履歴

`PRAGMA user_version=1` だけでは正式DB扱いしない。M8 preview DBも `user_version=1` を使うため、`preview_metadata` があるDB、`score_db_metadata` がないDB、`production_schema_status=not_production_schema` のDBは正式DBとして拒否する。

初期migrationは `001_initial_personal_score_db_schema` とし、以後の変更は次の原則に従う。

- 既存列の意味を静かに変えない。
- 破壊的変更は自動実行せず、拒否または明示migrationにする。
- unknown schema、preview schema、metadata欠損DBは本番保存前に拒否する。
- migration実行前に必ずbackup方針を決める。

## 互換チェックと拒否理由語彙

正式DBへ本番insertする前に、`inspect_personal_score_db_schema()` で既存DBまたは新規接続の状態を検査する。検査結果は以下を返す。

- `user_version`
- 既存table一覧
- `score_db_metadata`
- 欠落している必須table
- `personal_score_db_compatibility_errors()` と同じ拒否理由
- `migration_plan_status`
- `migration_plan_reason`

`assert_personal_score_db_compatible()` は同じ検査を行い、互換エラーがあれば `ValueError` で止める。これは正式DBとして開いてよいかの入口であり、migration実行や本番insertはまだ行わない。

互換チェックの主な拒否理由は以下。

- `schema_version_mismatch`: `PRAGMA user_version` が正式schema versionと一致しない。
- `m8_preview_database_not_supported`: `preview_metadata` を持つM8 preview DB。正式DBとしては拒否する。
- `unknown_database_not_supported`: tableはあるが `score_db_metadata` がなく、正式DBともpreview DBとも識別できない。
- `missing_table:<table>`: 正式DB必須tableが欠落している。
- `score_db_metadata_missing`: `score_db_metadata` がない。
- `score_db_metadata.<key>_missing`: 必須metadata keyがない。
- `score_db_metadata.<key>_mismatch`: 必須metadata valueが期待値と違う。

`migration_plan_status` は現時点では自動migrationではなく、次の扱い候補を示すだけにする。

- `compatible`: そのまま正式DBとして扱える。
- `initialize_empty_database`: user tableがない空DB。初期化候補だが、既存DBの自動migrationではない。
- `manual_migration_required`: 正式metadataで識別できるが、versionや必須tableが合わない。backup方針と明示確認を決めるまで自動変更しない。
- `reject_m8_preview_database`: M8 preview DB。正式DBへ自動昇格しない。
- `reject_unknown_database`: metadata欠損DB、metadata identity mismatch、未知schema。正式DBとして開かない。

metadata identity は `created_by`、`schema_name`、`schema_contract_scope`、`production_schema_status` を見る。これらが一致しないDBは、`user_version=1` や似たtable名があっても正式DBとして扱わない。`schema_version` だけの不一致は、正式metadata identityが揃っている場合に限り `manual_migration_required` の候補として読む。

## 初期化とオープン前段

`initialize_personal_score_db_if_empty()` は、検査結果が `initialize_empty_database` の場合だけ `create_personal_score_db_schema()` を実行する。初期化後は再検査し、正式metadata、`PRAGMA user_version`、必須tableがそろった `compatible` 状態として返す。

以下は自動変更しない。

- `compatible`: 既存の正式DBとして扱い、schema再作成やmetadata上書きはしない。
- `reject_m8_preview_database`: M8 preview DBを正式DBへ自動昇格しない。
- `reject_unknown_database`: metadata欠損DBやidentity mismatch DBを正式DBへ寄せない。
- `manual_migration_required`: backup方針と明示確認を決めるまで、欠落tableの作成や `user_version` 修正をしない。

`prepare_personal_score_db_for_write()` は、本番insert実装前のオープン前段である。空DBなら初期化してから互換性を確認し、`compatible` なら検査結果を返す。互換エラーが残るDBは `migration_plan_status` と拒否理由を含む `ValueError` で止める。これはまだ `plays` へのinsert、低信頼度ログ保存、既存DB migrationを行わない。

`prepare_personal_score_db_file_for_write(path)` は、正式DBファイルをパス単位で検査する前段である。新規ファイル、または既存の0 byte空ファイルだけSQLiteとして開いた後に `initialize_empty_database` へ進め、正式初期schemaを作成できる。既存の compatible DB はそのまま通し、schema再作成やmetadata上書きはしない。既存のM8 preview DB、unknown DB、metadata identity mismatch、`manual_migration_required` 候補、SQLiteとして読めないファイル、ディレクトリは拒否し、自動変更しない。戻り値には、対象path、既存ファイルだったか、既存サイズ、初期化結果、最終inspectionを含める。

このファイル境界は、正式 `ddrgp-scores.sqlite` をどこで検査して開くかを固定する足場であり、M8 preview の `--m8-score-db-output` とは別物として扱う。正式DBの本番insert、既定自動保存、既存DB migration実行にはまだ進まない。

## 未決事項

- `play_id` と `duplicate_key` の本格生成方式。
- `rank` / `clear_type` 未取得時の保存可否。
- OCR raw、normalized、M5候補観測、M7a候補値の詳細ログ粒度。
- 低信頼度ログと失敗画像をDB内tableへ寄せるか、JSONログ参照に留めるか。
- マスタDBの互換方針が固まった後の `master_version` と `song_id` / `chart_id` の扱い。

## 回帰ガード

- `tests/test_personal_score_db_schema.py` は正式schema contractを作成し、必須tableとmetadataを確認する。
- 同テストは M8 preview DB を正式個人スコアDBとして拒否する。
- 同テストは空DB、未知DB、metadata identity mismatch、必須table欠落、`user_version` mismatch の検査結果と `migration_plan_status` を固定する。
- 同テストは空DBだけ初期schemaを作成し、M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補を自動変更しないことを固定する。
- 同テストはファイルパス境界として、新規DBファイルと0 byte空ファイルだけ正式schemaへ初期化でき、compatible DBは変更せず、M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補、非SQLiteファイル、ディレクトリを自動変更しないことを固定する。
- 同テストは preview列、M7a raw候補、OCR raw/normalized が正式 `plays` に混入しないことを確認する。
