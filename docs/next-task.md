# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは `codex/m8-score-db-schema-version-preview` です。

次チャット開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- `git fetch --all --prune`
- このPRがmerge済みなら、最新 `main` から次の `codex/m8-...` ブランチを作る。
- 未mergeなら、`codex/m8-score-db-schema-version-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- M8 explicit file output preview summary/report に、実DBの `PRAGMA table_info(plays)` 由来の `database_plays_schema_columns` を追加済み。
- `database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` を追加済み。
- `play_id` と `created_at` はDB側補助列であり、planned row contractへ含めないことを維持している。
- schema mismatch reason は `database_plays_schema.play_id_missing`、`database_plays_schema.created_at_missing`、`database_plays_schema_column_order_mismatch`、`database_plays_integer_fields_mismatch` などで読み分ける。
- 0件insertの明示file output、M5ありCLI file output、通常の明示file outputでschema readback診断が `true` / 空理由になるfixtureを追加済み。
- schema mismatch summary fixtureを追加し、metadata/readback契約やrow count診断とは独立してschema mismatchを読めるようにした。
- 実SQLite connection上で正しいpreview metadataを作ったあと、不一致 `plays` schemaへ差し替える負例fixtureを追加済み。
- 今回、実SQLite connection上で正しいpreview DBを作ったあと、`preview_metadata.created_by_preview` を削除し、`preview_metadata.production_schema_status` を不一致値へ更新する実DB readback負例fixtureを追加した。
- 今回、実SQLite connection上で正しいpreview DBを作ったあと、`plays` へ余分な1行を直接insertする実DB readback負例fixtureを追加した。
- metadata readback負例では、`database_readback_matches_preview_contract=false`、`database_readback_mismatch_reasons=["database_preview_metadata.created_by_preview_missing", "database_preview_metadata.production_schema_status_mismatch"]` になり、row count診断とschema readback診断は `true` / 空理由のまま維持される。
- row count readback負例では、`database_plays_row_count_matches_insert_counts=false`、`database_plays_row_count_mismatch_reasons=["database_plays_row_count_inserted_count_mismatch", "database_plays_row_count_after_insert_mismatch"]` になり、metadata契約診断とschema readback診断は `true` / 空理由のまま維持される。
- `docs/design/03_event_and_save_boundary.md`、`docs/design/04_data_model.md`、`docs/design/05_storage_io_spec.md`、`docs/design/06_regression_guard.md`、`tools/vision_poc/README.md` には、schema/readback診断はfile output preview DBの内部整合確認であり、正式個人スコアDBスキーマ確定ではないことを反映済み。

2026-07-09時点のローカル確認:

- `python -m pytest tests\test_vision_poc_ocr.py -k "real_db_readback or schema_mismatch or row_count_mismatch"`: 3 passed。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m8"`: 19 passed。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 70 passed。
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed。
- `python -m compileall master tools\vision_poc`: passed。
- `python -m pytest tests`: 205 passed。
- `git diff --check`: passed。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct、false positives 0、false negatives 0。
- `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`: 221/221 correct、false positives 0、false negatives 0。
- `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`: 221/221 correct、false positives 0、false negatives 0、M5 jacket match features 69、candidates 60、diagnostics 118。
- M5あり明示file output:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_real_readback_20260709_194808_codex --m8-score-db-output data\vision_poc_m8_real_readback_20260709_194808_codex\ddrgp-scores.sqlite`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m8_score_db_file_output_preview.json`: `target_count=60`、`inserted_count=60`、`row_count_after_insert=60`、`database_plays_row_count=60`、`database_readback_matches_preview_contract=true`、`database_plays_row_count_matches_insert_counts=true`、`database_plays_insert_columns_match_planned_contract=true`、`database_plays_integer_fields_match_preview_contract=true`、`database_plays_schema_mismatch_reasons=[]`。
- 生成した `data/vision_poc_m8_real_readback_20260709_194808_codex/ddrgp-scores.sqlite` はローカルDBであり、コミット対象外。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tests/test_vision_poc_ocr.py`
- `tests/test_vision_poc_result_events.py`

実ファイルDB出力、正式スキーマ、またはマスタID保存に触る場合は追加で読む資料:

- `docs/design/08_master_db_generation.md`
- `docs/design/09_master_match_poc.md`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`
- `tests/test_master_match.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- Windows常駐アプリUI
- 既定自動保存、常時保存処理、本番用の自動DB insert
- 低信頼度ログ本番仕様
- 正式マイグレーション、正式個人スコアDBスキーマ、duplicate key の本格差し替え
- preview metadata、DB readback欄、readback一致診断欄、row count readback欄、schema readback診断欄を、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱うこと
- preview `plays` の列一致fixtureやschema readback診断を、正式個人スコアDBスキーマ確定の根拠として扱うこと
- `payload_ready`、保存予定レコード、DB write preview、file output previewを保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- M7aの `recognized_digits` を保存値確定として扱うこと
- OCR結果やM7a認識結果から保存値を本番確定すること
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- M4 Releases配布の実装
- プロジェクト専用Skill/Subagentの作成

完了済みとして蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7 readiness / M7 decision preview
- `m8_save_payload_preview.*`、`m8_planned_play_records.*`、`m8_score_db_write_preview.*`
- `--m8-score-db-output` による明示file output preview
- `schema_version=1` と `PRAGMA user_version=1` によるM8 previewスキーマ識別
- `created_by_preview=tools.vision_poc.m8_score_db_preview` と `preview_metadata` によるpreview生成物識別
- `schema_contract_scope=preview_minimal_plays` と `production_schema_status=not_production_schema` によるpreview専用最小スキーマ識別
- `database_schema_version` と `database_preview_metadata` によるfile output preview readback診断
- `database_readback_matches_preview_contract` と `database_readback_mismatch_reasons` によるfile output preview readback一致診断
- `database_plays_row_count` と `database_plays_row_count_matches_insert_counts` / `database_plays_row_count_mismatch_reasons` によるfile output preview row count readback診断
- `database_preview_metadata.<key>_missing` と `<key>_mismatch` による期待key欠落/値不一致の分離
- `database_plays_schema_columns`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` によるfile output preview schema readback診断
- 実DB readback経路による `preview_metadata` 欠落/不一致 fixture
- 実DB readback経路による `plays` row count不一致 fixture
- 実DB readback経路による `PRAGMA table_info(plays)` schema mismatch fixture
- `M8_PLANNED_PLAY_RECORD_FIELDNAMES` と preview `plays` のinsert対象列一致fixture
- preview `plays` のinteger列と `M8_SCORE_DB_WRITE_PREVIEW_INTEGER_FIELDS` の一致fixture
- `--m8-score-db-output` のCLI境界テスト
- M5なしCLI明示file outputの0件insert空DB fixture
- M5ありCLI明示file outputの1件以上insert fixture

## 次に必ず進める実作業

次は、M8 file output preview readback診断の負例がMarkdown report上でも読みやすく見えることを固定する。実DB readback経路の metadata mismatch / row count mismatch / schema mismatch はテストで固定済みなので、次は report 生成側またはまとめ表示側を小さく補強する。

第一候補:

- 実DB readback負例fixtureで得たsummaryを `write_m8_score_db_file_output_preview_report` に渡す。
- Markdown reportに `database readback mismatch reasons`、`database plays row count mismatch reasons`、`database plays schema mismatch reasons` がそのまま出ることをfixture化する。
- metadata mismatch fixtureでは、`database_preview_metadata.created_by_preview_missing` と `database_preview_metadata.production_schema_status_mismatch` がreport上で読めることを確認する。
- row count mismatch fixtureでは、`database_plays_row_count_inserted_count_mismatch` と `database_plays_row_count_after_insert_mismatch` がreport上で読めることを確認する。
- schema mismatch fixtureでは、既存の `database_plays_schema.*` / `database_plays_integer_fields_mismatch` 語彙がreport上で読めることを確認する。
- summary/reportの読み方は、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定ではなく、明示file output preview DBのreadback診断に限定する。

代替候補:

- 上記report fixtureが十分なら、`database_schema_version` 自体の実DB readback不一致を `PRAGMA user_version` 変更で固定する小さなfixtureを追加する。
- ただし、本番スキーマ確定、正式マイグレーション、正式DB保存成功判定へは進まない。

主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

## 検証コマンド

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m8"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
python -m tools.vision_poc --no-ocr
python -m pytest tests
git diff --check
```

M8 file output previewやschema/readback識別を触った場合は、追加で以下を確認する。既存DBファイルがあると拒否されるため、実行ごとに新しい `data/` 配下パスを使うこと:

```powershell
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_schema_readback_next --m8-score-db-output data\vision_poc_m8_schema_readback_next\ddrgp-scores.sqlite
```

M4/M5境界やmaster DB生成へ触った場合は、`tests\test_master_match.py`、`tests\test_master_builder.py`、M5 jacket match のPoC実行も再確認すること。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像はコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7/M8 PoC出力、ROI画像、OCR画像、解析ログ、`ddrgp-scores.sqlite` はステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7/M8対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチをpushする。

## 完了条件

- M8 file output preview readback診断のreport表示fixture、または残りの実DB readback負例fixtureが追加されている。
- 実DB readback経路による metadata mismatch / row count mismatch / schema mismatch fixtureが維持されている。
- `play_id` と `created_at` がplanned row contractへ混ざっていない。
- preview `plays` のinsert対象列が `M8_PLANNED_PLAY_RECORD_FIELDNAMES` と同じ順序で維持されている。
- preview `plays` の整数列が `M8_SCORE_DB_WRITE_PREVIEW_INTEGER_FIELDS` と矛盾していない。
- 0件insertの明示file outputでも、空 `plays`、`preview_metadata`、readback欄、一致診断欄、schema readback診断欄が `true` / 空理由になる。
- M5ありCLI明示file outputでは、`payload_ready` から保存予定レコードが生成され、実ファイルpreview DBへ1件以上insertされる。
- 実ファイルDBへ書く場合は明示オプションで `data/` 配下の新規ファイルに限定され、DBファイルをコミットしていない。
- 既存DBファイルを上書きしない。
- 明示オプションなしの既定実行では実ファイルDBと `m8_score_db_file_output_preview.*` を生成しない。
- readback診断欄を、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱っていない。
- `m8_score_db_write_preview_rows` とfile output previewの入力が `m8_planned_play_records_rows` に限定されている。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` が保存予定レコード、DB write preview、file output previewへ進まない。
- timestamped / manifest 相当の `confirmation_mode=time` と `played_at_ms` が planned rows / write preview / file output preview まで保持される。
- timestampなし入力の `played_at_ms=0` 暫定仕様が壊れていない。
- in-memory write previewとfile output previewのsummary/reportで `schema_version=1`、`schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema`、`created_by_preview=tools.vision_poc.m8_score_db_preview` が維持される。
- 実ファイルDBへ書く場合は `PRAGMA user_version=1`、`preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview`、`preview_metadata.schema_contract_scope=preview_minimal_plays`、`preview_metadata.production_schema_status=not_production_schema` が維持される。
- file output preview summary/reportの `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、readback一致診断欄、row count一致診断欄、schema readback診断欄が維持される。
- `database_preview_metadata.<key>_missing` は期待key欠落、`database_preview_metadata.<key>_mismatch` は値不一致として読み分けられる。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- 既存の `m7_save_readiness_review.*`、`m7_save_decision_preview.*`、`m8_save_payload_preview.*`、`m8_planned_play_records.*`、`m8_score_db_write_preview.*` のCSV列や意味を壊していない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `preview_save_candidate`、`payload_ready`、保存予定レコード、in-memory write preview、明示file output previewへ進む。
- M5未実行時は、preview上で `blocked_readiness` または `needs_identity_review` として止まり、M8 payload ready / 保存予定レコード / write preview対象にならない。明示file output時だけ0件insertの空preview DBとして確認できる。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
