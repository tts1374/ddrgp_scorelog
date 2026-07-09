# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは `codex/m8-score-db-schema-version-preview` です。

このブランチは、2026-07-09時点で未mergeの `codex/m8-file-db-output-preview` 先端から作成しています。次チャット開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- `git fetch --all --prune`
- このschema version PRがmerge済みなら、最新 `main` から次の `codex/m8-...` ブランチを作る。
- 未mergeなら、`codex/m8-score-db-schema-version-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- `--m8-score-db-output data\...\ddrgp-scores.sqlite` は、明示された場合だけ実ファイルSQLiteへ書くM8 file output previewとして実装済み。
- 出力先は `data/` 配下の新規ファイルに限定し、`data/` 外と既存ファイルへの書き込みは拒否する。
- `--m8-score-db-output` は `--m7a-digit-recognition` とセットでのみ使える。
- 入力は `m8_planned_play_records_rows` に限定し、非ready payload、M5未実行、identity不足、digit不足をfile output側で再判定しない。
- file output preview summary / Markdown は `m8_score_db_file_output_preview.json` と `m8_score_db_file_output_preview.md` として出す。
- file output preview status は `inserted_to_file_preview` と `skipped_invalid_planned_record`。`inserted_to_file_preview` は明示指定されたpreview DBへのinsert確認であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定ではない。
- 既定実行、`--m7a-digit-recognition` だけの実行、M5なし実行では保存予定レコードがfile outputへ進まない境界をテストで固定済み。
- M5なし相当のfile output previewでは、planned rows 0件の空 `plays` スキーマDBと `inserted_count=0` を確認する。
- M8 score DB previewのスキーマ識別として `schema_name=m8_score_db_preview`、`schema_version=1`、`schema_version_source=PRAGMA user_version` をsummary/reportへ追加した。
- in-memory write previewとfile output previewの両方で `schema_version=1` を出す。実ファイルSQLiteには `PRAGMA user_version=1` を設定する。
- 0件insertのfile output DBでも `plays` スキーマと `PRAGMA user_version=1` を作る。
- `schema_version=1` はpreviewスキーマ契約の識別子であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定を意味しない。
- `docs/design/03_event_and_save_boundary.md`、`04_data_model.md`、`05_storage_io_spec.md`、`06_regression_guard.md`、`docs/implementation-roadmap.md`、`tools/vision_poc/README.md` にschema version / `PRAGMA user_version` / 本番保存ではない読み方を反映した。
- 生成した `data/vision_poc_m8_schema_version_preview_20260709/ddrgp-scores.sqlite` はローカルDBであり、コミット対象外。

2026-07-09時点のローカル確認:

- `python -m pytest tests\test_vision_poc_ocr.py -k "m8"`: 6 passed。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 57 passed。
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed。
- `python -m compileall master tools\vision_poc`: passed。
- `python -m pytest tests`: 192 passed。
- `git diff --check`: passed。
- M5なし:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m8_planned_play_records.json`: `target_count=60`、`planned_record_count=0`、`excluded_payload_status_counts={"unsupported_preview_status":60}`。
- M5あり:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m8_planned_play_records.json`: `target_count=60`、`planned_record_count=60`。
  - `m8_score_db_write_preview.json`: `target_count=60`、`inserted_count=60`、`row_count_after_insert=60`、`schema_version=1`、`write_preview_status_counts={"inserted_in_memory":60}`。
- 明示file output:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_schema_version_preview_20260709 --m8-score-db-output data\vision_poc_m8_schema_version_preview_20260709\ddrgp-scores.sqlite`
  - `m8_score_db_file_output_preview.json`: `target_count=60`、`inserted_count=60`、`row_count_after_insert=60`、`schema_name=m8_score_db_preview`、`schema_version=1`、`schema_version_source=PRAGMA user_version`、`write_preview_status_counts={"inserted_to_file_preview":60}`。
  - DB実体: `PRAGMA user_version=1`、`plays` 行数 60。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct。

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
- file output previewや `schema_version` を本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
- `payload_ready`、保存予定レコード、DB write previewを保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- M7aの `recognized_digits` を保存値確定として扱うこと
- OCR結果やM7a認識結果から保存値を本番確定すること
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- プロジェクト専用Skill/Subagentの作成

完了済みとして蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7 readiness / M7 decision preview
- `m8_save_payload_preview.*`、`m8_planned_play_records.*`、`m8_score_db_write_preview.*`
- `--m8-score-db-output` による明示file output preview
- `schema_version=1` と `PRAGMA user_version=1` によるM8 previewスキーマ識別
- `payload_ready` 以外を保存予定レコードへ変換しない境界
- 保存予定レコード以外を DB write preview / file output preview へ入力しない境界
- `plays` 最小スキーマの in-memory fixtureとfile output fixture

## 次に必ず進める実作業

次は、M8 file output previewの読み方を保ったまま、CLI境界を小さく強化する。

第一候補:

- `runner.main()` 経由のCLI fixtureで、`--m8-score-db-output` 単独指定が `--m7a-digit-recognition` 必須エラーになることを固定する。
- CLI fixtureで、`--m8-score-db-output` の `data/` 外指定を拒否し、DBファイルを作らないことを固定する。
- CLI fixtureで、既存DBファイル指定を拒否し、既存ファイルを上書きしないことを固定する。
- CLI fixtureで、明示オプションなしの `--m7a-digit-recognition` 実行では `m8_score_db_file_output_preview.*` と実DBファイルを作らないことを固定する。
- 可能ならCLI fixtureで、M5なし0件insert相当の明示file outputが空 `plays` スキーマ、`PRAGMA user_version=1`、`inserted_count=0` を維持することを固定する。
- 既存の関数単体テストと重複しすぎないよう、CLI引数境界・ファイル生成有無・上書き拒否を主眼にする。
- docs/READMEでは、CLI境界も本番保存成功、曲ID/譜面ID確定、保存値確定ではないことを維持する。

代替候補:

- 本番insertへ進む前の小さなmetadata足場として、preview DBまたはsummaryに `created_by_preview` / `created_at` 相当の識別を足す。ただし、正式マイグレーションや保存値確定には進まないこと。
- `source_timestamp_ms` を足す場合は、既存の `played_at_ms` 暫定仕様と混同しないようにする。timestamped / manifest は元の `timestamp_ms` を保持し、timestampなしは空または `0` のどちらかを仕様化する。

主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

## 検証コマンド

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
python -m tools.vision_poc --no-ocr
python -m pytest tests
git diff --check
```

M8 file output previewやschema versionを触った場合は、追加で以下を確認する:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m8"
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_schema_version_preview_next --m8-score-db-output data\vision_poc_m8_schema_version_preview_next\ddrgp-scores.sqlite
```

上記file output確認は、同じDBファイルが既に存在すると拒否される。再実行する場合は別の `data/` 配下パスを使うか、生成物であることを確認してから掃除する。DBファイルはコミットしない。

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

- `m8_score_db_write_preview_rows` とfile output previewの入力が `m8_planned_play_records_rows` に限定されている。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` が保存予定レコード、DB write preview、file output previewへ進まない。
- timestamped / manifest 相当の `confirmation_mode=time` と `played_at_ms` が planned rows / write preview / file output preview まで保持される。
- timestampなし入力の `played_at_ms=0` 暫定仕様が壊れていない。
- in-memory write previewとfile output previewのsummary/reportで `schema_version=1` が維持される。
- 実ファイルDBへ書く場合は `PRAGMA user_version=1` が維持される。
- 実ファイルDBへ書く場合は明示オプションで `data/` 配下の新規ファイルに限定され、DBファイルをコミットしていない。
- 既存DBファイルを上書きしない。
- 明示オプションなしの既定実行では実ファイルDBと `m8_score_db_file_output_preview.*` を生成しない。
- file output previewや `schema_version` が、DB保存成功、曲ID/譜面ID確定、保存値確定として扱われていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- 既存の `m7_save_readiness_review.*`、`m7_save_decision_preview.*`、`m8_save_payload_preview.*`、`m8_planned_play_records.*`、`m8_score_db_write_preview.*` のCSV列や意味を壊していない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `preview_save_candidate`、`payload_ready`、保存予定レコード、in-memory write preview、明示file output previewへ進む。
- M5未実行時は、preview上で `blocked_readiness` または `needs_identity_review` として止まり、M8 payload ready / 保存予定レコード / write preview / file output preview対象にならない。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
