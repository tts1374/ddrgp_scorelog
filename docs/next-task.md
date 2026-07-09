# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは `codex/m8-score-db-schema-version-preview` です。

2026-07-09時点で、このブランチはM8 preview schema version、M8 file output CLI境界テスト、M8 preview metadata、M8 file output readback診断欄、readback preview契約一致診断まで含みます。次チャット開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- `git fetch --all --prune`
- このPRがmerge済みなら、最新 `main` から次の `codex/m8-...` ブランチを作る。
- 未mergeなら、`codex/m8-score-db-schema-version-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- `m8_score_db_write_preview.*` と `m8_score_db_file_output_preview.*` のsummary/reportに `created_by_preview=tools.vision_poc.m8_score_db_preview` と `preview_metadata_table=preview_metadata` を追加済み。
- in-memory preview と明示file output previewのSQLite schema作成時に、軽量な `preview_metadata` 表を作成する。
- `preview_metadata` には `created_by_preview`、`schema_name`、`schema_version`、`schema_version_source`、`schema_table` を保存する。
- `write_m8_score_db_file_output_preview()` でDB書き込み後に `PRAGMA user_version` と `preview_metadata` を読み戻す `read_m8_score_db_file_output_preview_metadata()` を追加済み。
- file output summary/reportに、実DBから読んだ `database_schema_version` と `database_preview_metadata` を追加済み。
- `database_schema_version` と `database_preview_metadata` はDB readback診断欄であり、summaryの定数欄 `schema_version` / `created_by_preview` と分けて読む。
- `summarize_m8_score_db_file_output_preview()` に `database_readback_matches_preview_contract` と `database_readback_mismatch_reasons` を追加済み。
- 一致条件は `database_schema_version == schema_version`、`database_preview_metadata.created_by_preview == created_by_preview`、`schema_name`、`schema_table`、`schema_version`、`schema_version_source` のpreview契約一致。
- 不一致fixtureはDBファイルを壊さず、summary関数へ人工readback dictを渡す単体テストで固定済み。
- 0件insertの明示file output DBでも、空 `plays`、`PRAGMA user_version=1`、`preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview`、readback欄、一致診断欄 `true` / 空理由が出ることを固定済み。
- `preview_metadata`、DB readback欄、一致診断欄はpreview生成物識別用であり、正式マイグレーション、本番DB保存成功、曲ID/譜面ID確定、保存値確定を意味しない。
- `tests/test_vision_poc_ocr.py` でsummary/report、実ファイルDB、CLI 0件insert経路、人工不一致readbackの preview metadata / readback欄 / 一致診断欄を固定した。
- `docs/design/03_event_and_save_boundary.md`、`04_data_model.md`、`05_storage_io_spec.md`、`06_regression_guard.md`、`tools/vision_poc/README.md` にDB readback一致診断欄の読み方を反映した。

2026-07-09時点のローカル確認:

- `python -m pytest tests\test_vision_poc_ocr.py -k "m8"`: 12 passed。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 63 passed。
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed。
- `python -m compileall tools\vision_poc`: passed。
- `python -m compileall master tools\vision_poc`: passed。
- `python -m pytest tests`: 198 passed。
- `git diff --check`: passed。
- M5なし:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m8_planned_play_records.json`: `target_count=60`、`planned_record_count=0`、`excluded_payload_status_counts={"unsupported_preview_status":60}`。
- M5あり:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m8_score_db_write_preview.json`: `target_count=60`、`inserted_count=60`、`row_count_after_insert=60`、`schema_version=1`、`created_by_preview=tools.vision_poc.m8_score_db_preview`、`write_preview_status_counts={"inserted_in_memory":60}`。
- 明示file output:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_readback_contract_20260709 --m8-score-db-output data\vision_poc_m8_readback_contract_20260709\ddrgp-scores.sqlite`
  - `m8_score_db_file_output_preview.json`: `target_count=60`、`inserted_count=60`、`row_count_after_insert=60`、`schema_version=1`、`created_by_preview=tools.vision_poc.m8_score_db_preview`、`database_schema_version=1`、`database_preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview`、`database_readback_matches_preview_contract=true`、`database_readback_mismatch_reasons=[]`、`write_preview_status_counts={"inserted_to_file_preview":60}`。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct。
- 生成した `data/vision_poc_m8_readback_contract_20260709/ddrgp-scores.sqlite` はローカルDBであり、コミット対象外。

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
- file output preview、`schema_version`、`created_by_preview`、`preview_metadata`、`database_schema_version`、`database_preview_metadata`、`database_readback_matches_preview_contract`、`database_readback_mismatch_reasons` を本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
- `payload_ready`、保存予定レコード、DB write previewを保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
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
- `database_schema_version` と `database_preview_metadata` によるfile output preview readback診断
- `database_readback_matches_preview_contract` と `database_readback_mismatch_reasons` によるfile output preview readback一致診断
- `--m8-score-db-output` のCLI境界テスト
- `payload_ready` 以外を保存予定レコードへ変換しない境界
- 保存予定レコード以外を DB write preview / file output preview へ入力しない境界
- `plays` 最小スキーマの in-memory fixtureとfile output fixture

## 次に必ず進める実作業

次は、readback一致診断欄を保ったまま、`runner.main()` 経由のM5あり明示file output previewが保存予定レコードを実ファイルpreview DBへinsertする経路を軽量fixtureで固定する。

第一候補:

- `tests/test_vision_poc_ocr.py` に、ローカルスクリーンショット素材へ依存しすぎない `runner.main()` 経由のM5あり明示file output fixtureを追加する。
- 目的は、CLIで `--m5-jacket-match`、`--m7a-digit-recognition`、`--m8-score-db-output` が揃ったとき、`m8_score_db_file_output_preview.json` が `inserted_count > 0`、`row_count_after_insert > 0`、`database_schema_version=1`、`database_readback_matches_preview_contract=true`、空 mismatch reasons を出すことを固定すること。
- 既存の `write_m8_cli_manifest_fixture()` や monkeypatch を使えるか確認し、重いローカル素材依存や実テンプレート画像依存を増やしすぎない。
- M5候補観測は本番確定IDではなく、CLI経路でM8 previewへ進むための候補材料としてだけ扱う。
- 実DBファイルは `tmp_path` 配下で `data/...sqlite` に出し、既存ファイル上書き拒否を維持する。
- 追加したCLI fixtureでも、`preview_metadata`、DB readback欄、readback一致診断欄を本番保存成功、曲ID/譜面ID確定、保存値確定の根拠にしない。
- 仕様語彙を足した場合は、`docs/design/03_event_and_save_boundary.md`、`04_data_model.md`、`05_storage_io_spec.md`、`06_regression_guard.md`、`tools/vision_poc/README.md` の該当箇所も更新する。

代替候補:

- `m8_score_db_file_output_preview.json` に `database_plays_row_count` と、その値が `row_count_after_insert` / `inserted_count` と一致するかを示す軽い診断欄を追加する。ただし現状 `row_count_after_insert` 自体が実DBから読んだ値なので、重複診断になりすぎないよう注意する。
- `database_preview_metadata` の不一致理由について、欠落keyと値不一致を分ける場合は単体テストで語彙を固定する。DBファイルを壊すfixtureにはしない。

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

M8 file output previewやschema/metadata/readback識別を触った場合は、追加で以下を確認する:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m8"
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m8_readback_next --m8-score-db-output data\vision_poc_m8_readback_next\ddrgp-scores.sqlite
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
- in-memory write previewとfile output previewのsummary/reportで `schema_version=1` と `created_by_preview=tools.vision_poc.m8_score_db_preview` が維持される。
- 実ファイルDBへ書く場合は `PRAGMA user_version=1` と `preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview` が維持される。
- file output preview summary/reportの `database_schema_version` と `database_preview_metadata` が実DB readback診断欄として維持される。
- file output preview summary/reportの `database_readback_matches_preview_contract` と `database_readback_mismatch_reasons` がpreview契約一致診断欄として維持される。
- readback一致診断欄を足す場合も、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱われていない。
- 0件insertの明示file outputでも、空 `plays`、`preview_metadata`、readback欄、一致診断欄 `true` / 空理由が出る。
- 実ファイルDBへ書く場合は明示オプションで `data/` 配下の新規ファイルに限定され、DBファイルをコミットしていない。
- 既存DBファイルを上書きしない。
- 明示オプションなしの既定実行では実ファイルDBと `m8_score_db_file_output_preview.*` を生成しない。
- preview metadata、DB readback欄、readback一致診断欄を、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱っていない。
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
