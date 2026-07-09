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
- このPRがmerge済みなら、最新 `main` から次フェーズ用の `codex/m8-personal-score-db-schema-design` ブランチを作る。
- 未mergeなら、`codex/m8-score-db-schema-version-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- M8 previewは、`m8_save_payload_preview.*`、`m8_planned_play_records.*`、`m8_score_db_write_preview.*`、明示file output preview、DB readback診断まで揃ったため一区切り。
- file output preview summary/reportでは、`database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns` と、metadata / row count / schema の一致診断を読める。
- 実SQLite readback経路の metadata mismatch / row count mismatch / schema mismatch fixtureは維持済み。
- 今回、これら3種の負例summaryを `write_m8_score_db_file_output_preview_report` に渡し、Markdown report上で mismatch reason がそのまま読めるfixtureを追加した。
- `docs/implementation-roadmap.md` に、M8 preview完了範囲と、次フェーズが正式個人スコアDBスキーマ設計・migration方針・本番insert境界・duplicate key本格化・保存成功/スキップログ設計であることを反映済み。
- 正式個人スコアDB保存、本番insert、正式migration、duplicate key本格差し替えにはまだ踏み込んでいない。

2026-07-09時点のローカル確認:

- `python -m pytest tests\test_vision_poc_ocr.py -k "m8_score_db_file_output_preview_report_shows_real_db_readback_mismatch_reasons or m8_score_db_file_output_preview_reports_metadata_mismatch_from_real_db_readback or m8_score_db_file_output_preview_reports_row_count_mismatch_from_real_db_readback or m8_score_db_file_output_preview_reports_schema_mismatch_from_real_db_readback"`: 4 passed。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m8"`: 20 passed。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 71 passed。
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed。
- `python -m compileall master tools\vision_poc`: passed。
- `python -m pytest tests`: 206 passed。
- `git diff --check`: passed。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct、false positives 0、false negatives 0。
- `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`: 221/221 correct、false positives 0、false negatives 0。
- `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`: 221/221 correct、false positives 0、false negatives 0、M5 jacket match features 69、candidates 60、diagnostics 118。
- M5あり明示file output:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_report_readback_20260709_codex --m8-score-db-output data\vision_poc_m8_report_readback_20260709_codex\ddrgp-scores.sqlite`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m8_score_db_file_output_preview.json`: `target_count=60`、`inserted_count=60`、`row_count_after_insert=60`、`database_plays_row_count=60`、`database_readback_matches_preview_contract=true`、`database_readback_mismatch_reasons=[]`、`database_plays_row_count_matches_insert_counts=true`、`database_plays_row_count_mismatch_reasons=[]`、`database_plays_insert_columns_match_planned_contract=true`、`database_plays_integer_fields_match_preview_contract=true`、`database_plays_schema_mismatch_reasons=[]`。
- 生成した `data/vision_poc_m8_report_readback_20260709_codex/ddrgp-scores.sqlite` はローカルDBであり、コミット対象外。

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

正式スコアDB、マスタID保存、またはmigration方針に触る場合は追加で読む資料:

- `docs/design/08_master_db_generation.md`
- `docs/design/09_master_match_poc.md`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`
- `tests/test_master_match.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- Windows常駐アプリUI
- 低信頼度ログ本番仕様の実装
- 正式個人スコアDBへの実insert、自動保存、既定保存
- duplicate key の本格差し替え実装
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
- `database_plays_schema_columns`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` によるfile output preview schema readback診断
- 実DB readback経路による metadata mismatch / row count mismatch / schema mismatch fixture
- Markdown report上の readback mismatch reason 表示fixture

## 次に必ず進める実作業

次はM8 preview後の正式個人スコアDBフェーズに入る。ただし、最初の1チャットでは本番insert実装へ進まず、正式スキーマとmigration境界の設計・回帰ガードを固める。

第一候補:

- `docs/design/10_personal_score_db_schema.md` などの新規design docを追加し、正式 `ddrgp-scores.sqlite` の初期スキーマ案を整理する。
- `plays`、保存スキップ/解析ログ、DB metadata、migration metadata、source capture reference の責務を分ける。
- M8 preview最小 `plays` と正式個人スコアDB `plays` を明確に別物として書く。
- 正式スキーマ候補には、`play_id`、`played_at`、`master_version`、`song_id`、`chart_id`、スコア/判定数、rank/clear_type候補、capture hash、duplicate key、analysis confidence、app version、created_at などを含めるかを検討する。
- migration方針は、`PRAGMA user_version`、metadata table、互換チェック、既存DB拒否/移行のどれを使うかを設計する。
- 実装可能な成果物として、スキーマ契約の小さなテスト、定数、またはREADME/設計と連動する検証コードを1つ以上追加する。docsだけで終える場合は、なぜ実装へ入らないのかをコミット前に明示する。

代替候補:

- まず正式DBの保存成功/スキップログ設計を `docs/design/` に追加し、M7 decision previewから正式保存境界へ渡す項目を整理する。
- ただし、本番DB insert、既定自動保存、duplicate key本格実装、低信頼度ログ本番保存はまだ実装しない。

主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

## 検証コマンド

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m8"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

正式スコアDB設計やmigration境界に触った場合は、追加で関連する新規テストを明示的に実行すること。

M8 preview既存契約を触った場合は、追加で以下を確認する。既存DBファイルがあると拒否されるため、実行ごとに新しい `data/` 配下パスを使うこと:

```powershell
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_next_check --m8-score-db-output data\vision_poc_m8_next_check\ddrgp-scores.sqlite
```

画像PoCや分類境界へ触った場合は、追加で以下も確認する。

```powershell
python -m tools.vision_poc --no-ocr
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
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

- M8 previewは完了済みとして扱い、正式個人スコアDBフェーズの設計または検証強化が1つ以上進んでいる。
- `docs/next-task.md` と必要な設計docs上で、次に残る作業が明確になっている。
- 正式個人スコアDB保存の実insertにはまだ踏み込んでいない。
- M8 preview最小 `plays` と正式個人スコアDBスキーマを混同していない。
- preview `plays` のinsert対象列が `M8_PLANNED_PLAY_RECORD_FIELDNAMES` と同じ順序で維持されている。
- preview `plays` の整数列が `M8_SCORE_DB_WRITE_PREVIEW_INTEGER_FIELDS` と矛盾していない。
- 0件insertの明示file outputでも、空 `plays`、`preview_metadata`、readback欄、一致診断欄、schema readback診断欄が `true` / 空理由になる。
- M5ありCLI明示file outputでは、`payload_ready` から保存予定レコードが生成され、実ファイルpreview DBへ1件以上insertされる。
- 実ファイルDBへ書く場合は明示オプションで `data/` 配下の新規ファイルに限定され、DBファイルをコミットしていない。
- 既存DBファイルを上書きしない。
- 明示オプションなしの既定実行では実ファイルDBと `m8_score_db_file_output_preview.*` を生成しない。
- readback診断欄を、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱っていない。
- `m8_score_db_write_preview_rows` とfile output previewの入力が `m8_planned_play_records_rows` に限定されている。
- timestamped / manifest 相当の `confirmation_mode=time` と `played_at_ms` が planned rows / write preview / file output preview まで保持される。
- timestampなし入力の `played_at_ms=0` 暫定仕様が壊れていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
