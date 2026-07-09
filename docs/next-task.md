# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは `codex/m8-score-db-write-preview` です。

次の主作業もM8の続きなので、作業ブランチは `codex/m8-timestamped-write-preview` を推奨します。

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- このM8 score DB write preview PRがmerge済みなら、最新 `main` から `codex/m8-timestamped-write-preview` を作る。
- 未mergeなら、`codex/m8-score-db-write-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- `m8_score_db_write_preview.csv`、`m8_score_db_write_preview.json`、`m8_score_db_write_preview.md` を `--m7a-digit-recognition` 実行時に生成するようにした。
- 入力は `m8_planned_play_records_rows` に限定した。
- 非ready payloadは上流の `m8_planned_play_records.*` で止まり、write preview へ進まない。
- 保存予定レコードだけを新規 in-memory SQLite `plays` テーブルへinsertする。
- summaryで `insert_target_count`、`inserted_count`、`row_count_after_insert`、`excluded_count`、`write_preview_status_counts`、`write_preview_reason_counts` を確認できる。
- write preview status は `inserted_in_memory`、`skipped_invalid_planned_record`。
- `skipped_invalid_planned_record` は planned row contract の不足や整数列不正を示す。非ready payloadをここで再判定するための語彙ではない。
- 実ファイルDB生成、明示オプション、常時保存、本番insert、低信頼度ログ本番仕様には進んでいない。
- `inserted_in_memory` はDB保存成功、曲ID/譜面ID確定、保存値確定ではない。
- timestampなし入力では、従来どおり `played_at_ms=0` の暫定値が planned/write preview まで渡る。
- 仕様更新として `docs/implementation-roadmap.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/04_data_model.md`、`docs/design/05_storage_io_spec.md`、`docs/design/06_regression_guard.md`、`tools/vision_poc/README.md` を更新した。

2026-07-09時点のローカル確認:

- M5なし:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m8_planned_play_records.json`: `target_count=60`、`planned_record_count=0`、`excluded_payload_status_counts={"unsupported_preview_status":60}`。
  - `m8_score_db_write_preview.json`: `target_count=0`、`insert_target_count=0`、`inserted_count=0`、`row_count_after_insert=0`、`excluded_count=0`、`write_preview_status_counts={}`。
- M5あり:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m8_planned_play_records.json`: `target_count=60`、`planned_record_count=60`、`excluded_payload_status_counts={}`。
  - `m8_score_db_write_preview.json`: `target_count=60`、`insert_target_count=60`、`inserted_count=60`、`row_count_after_insert=60`、`excluded_count=0`、`write_preview_status_counts={"inserted_in_memory":60}`。
  - representativesは `source_confirmation_mode=frames`、`played_at_ms=0` の暫定値を出す。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 53 passed。
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed。
- `python -m compileall master tools\vision_poc`: passed。
- `python -m pytest tests`: 188 passed。
- `git diff --check`: passed。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/09_master_match_poc.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tools/vision_poc/master_match.py`
- `tests/test_vision_poc_ocr.py`
- `tests/test_vision_poc_result_events.py`
- `tests/test_master_match.py`

M4/M5 DB生成、曲名正規化、または個人スコアDBの実SQLiteファイル生成へ踏み込む場合は追加で読む資料:

- `docs/design/08_master_db_generation.md`
- `docs/design/07_m3_chart_field_review.md`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- Windows常駐アプリUI
- 本番用の自動DB insert、常時保存処理、低信頼度ログ本番仕様
- 実ファイルDBへの既定自動保存
- `payload_ready`、保存予定レコード、DB write previewを保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- M7aの `recognized_digits` を保存値確定として扱うこと
- OCR結果やM7a認識結果から保存値を本番確定すること
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- プロジェクト専用Skill/Subagentの作成

M7a/M5/M7/M8 dry-runで完了済みとして扱い、次チャットで蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7a横持ち集約
- M7 readiness / M7 decision preview
- `m8_save_payload_preview.*` のCSV / JSON / Markdown出力
- `m8_planned_play_records.*` のCSV / JSON / Markdown出力
- `m8_score_db_write_preview.*` のCSV / JSON / Markdown出力
- `payload_ready` 以外を保存予定レコードへ変換しない境界
- 保存予定レコード以外を DB write preview へ入力しない境界
- `plays` 最小スキーマの in-memory fixture

## 次に必ず進める実作業

次は、実ファイルDB生成へ進む前に、timestamped / manifest 入力での planned rows と write preview の時刻境界をfixtureで固定する。

第一候補:

- `tests/test_vision_poc_ocr.py` に、manifest mode の小さいfixtureを追加する。
- M5 jacket match 全体に依存しない形で、`m8_save_payload_preview_rows` または `m8_planned_play_record_rows` へ渡す最小行を作る。
- `confirmation_mode=time` が planned rows の `source_confirmation_mode` に保持されることを確認する。
- `timestamp_ms` がある行では `played_at_ms` にその値が入ることを確認する。
- write preview で同じ `played_at_ms` が in-memory insert representative / CSV / summary に残ることを確認する。
- timestampなし入力では `played_at_ms=0` の暫定値を維持することを確認する。
- 必要なら `m8_score_db_write_preview.json` の reading notes や docs に、timestamped / manifest の読み方を追記する。

代替候補:

- 明示オプション付きの実ファイルDB出力設計を先に小さく固める。ただし実装する場合は既定自動保存にせず、出力先を必ず `data/` 配下に限定し、DBファイルをGit管理しない。
- `plays` 最小スキーマに `source_timestamp_ms` や `schema_version` を足すかどうかをfixtureとdocsで決める。ただしdocs整理だけで終えないこと。

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

M3 OCRは主ルートではなく、必要な場合だけ補助・診断として確認する:

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-rois --output data\vision_poc_m7_m3ocr_m5
```

M4/M5境界やmaster DB生成へ触った場合は、`tests\test_master_match.py`、`tests\test_master_builder.py`、M5 jacket match のPoC実行も再確認すること。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像はコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7/M8 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 次作業でローカル個人スコアDBを生成した場合も、DBファイルはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7/M8対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチをpushする。

## 完了条件

- `m8_score_db_write_preview_rows` の入力が `m8_planned_play_records_rows` に限定されている。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` が保存予定レコードやDB write previewへ進まない。
- DB write preview fixtureがネットワーク、画像、`metadata.csv`、ローカルDBファイルに依存せず通る。
- timestamped / manifest 相当の `confirmation_mode=time` と `played_at_ms` が planned rows / write preview まで保持される。
- timestampなし入力の `played_at_ms=0` 暫定仕様が壊れていない。
- 実ファイルDBへ書く場合は明示オプションで `data/` 配下に限定され、DBファイルをコミットしていない。
- `payload_ready`、保存予定レコード、DB write previewがDB保存成功、曲ID/譜面ID確定、保存値確定として扱われていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- 既存の `m7_save_readiness_review.*`、`m7_save_decision_preview.*`、`m8_save_payload_preview.*`、`m8_planned_play_records.*`、`m8_score_db_write_preview.*` のCSV列や意味を壊していない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `preview_save_candidate`、`payload_ready`、保存予定レコード、in-memory write previewへ進む。
- M5未実行時は、preview上で `blocked_readiness` または `needs_identity_review` として止まり、M8 payload ready / 保存予定レコード / write preview対象にならない。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
