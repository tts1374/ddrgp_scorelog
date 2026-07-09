# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは `codex/m8-timestamped-write-preview` です。

次の主作業もM8の続きです。作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- このM8 timestamped write preview PRがmerge済みなら、最新 `main` から `codex/m8-file-db-output-preview` を作る。
- 未mergeなら、`codex/m8-timestamped-write-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- M8 timestamped / manifest 相当の時刻境界fixtureを追加した。
- `m8_save_payload_preview_rows` へ渡す最小の `preview_save_candidate` 行だけで、画像、ネットワーク、`metadata.csv`、ローカルDBに依存しないテストにした。
- `confirmation_mode=time` と `timestamp_ms=345678` が payload summary、planned rows、planned CSV、planned summary representative、write preview rows、write preview CSV、write preview summary representative まで保持されることを固定した。
- timestampなし入力では `played_at_ms=0` の暫定値を維持することを同じfixtureで固定した。
- `source_confirmation_mode` は timestamped / manifest 相当では `time`、timestampなし相当では `frames` のまま planned / write preview へ渡る。
- `m8_planned_play_records.json` と `m8_score_db_write_preview.json` の `reading_notes` に、timestamped / manifest 入力では `timestamp_ms` を `played_at_ms` として保持し、timestampなし入力では `played_at_ms=0` を暫定値として扱うことを追記した。
- 実ファイルDB生成、明示オプション、本番insert、常時保存、低信頼度ログ本番仕様にはまだ進んでいない。
- `inserted_in_memory` は引き続きDB保存成功、曲ID/譜面ID確定、保存値確定ではない。

2026-07-09時点のローカル確認:

- `python -m pytest tests\test_vision_poc_ocr.py -k "m8"`: 3 passed。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 54 passed。
- `python -m ruff check tools\vision_poc pyproject.toml tests`: passed。
- `python -m compileall master tools\vision_poc`: passed。
- `python -m pytest tests`: 189 passed。
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
- `python -m tools.vision_poc --no-ocr`: 221/221 correct。
- `git diff --check`: passed。

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

実ファイルDB出力やスキーマを触る場合は追加で読む資料:

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
- `payload_ready`、保存予定レコード、DB write preview、実ファイルDB出力プレビューを保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
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
- payload / planned / write preview の timestamped / manifest 相当 `played_at_ms` fixture
- `payload_ready` 以外を保存予定レコードへ変換しない境界
- 保存予定レコード以外を DB write preview へ入力しない境界
- `plays` 最小スキーマの in-memory fixture

## 次に必ず進める実作業

次は、実ファイルDBへの既定自動保存ではなく、明示オプション付きの実ファイルDB出力プレビューを小さく設計・実装する。

第一候補:

- CLIに明示オプションを追加する。名前は例として `--m8-score-db-output data\...\ddrgp-scores.sqlite` のように、指定された場合だけ実ファイルDBへ書く。
- 出力先は必ず `data/` 配下に限定し、`data/` 外や既存の任意DBへの誤書き込みを拒否する。
- 既定実行、`--m7a-digit-recognition` だけの実行、M5なし実行では実ファイルDBを作らない。
- 入力は `m8_planned_play_records_rows` に限定し、非ready payloadを実ファイルDB出力側で再判定しない。
- まずは既存 in-memory `plays` 最小スキーマと同じ列でよい。`schema_version` や `source_timestamp_ms` を足す場合は、fixtureとdocsで意味を固定する。
- テストは `tmp_path` 内に `data/...` を作り、DBファイルが生成されるケース、`data/` 外を拒否するケース、M5なしでinsert 0件になるケースを固定する。
- 実ファイルDB出力も本番保存成功、曲ID/譜面ID確定、保存値確定ではないことを JSON / Markdown / docs に明記する。

代替候補:

- 実ファイルDB出力に入る前に、`plays` 最小スキーマへ `schema_version`、`source_timestamp_ms`、`source_confirmation_mode` の扱いをfixtureで追加固定する。ただしdocs整理だけで終えず、コードまたはテストの実行可能な成果物変更を含める。

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

実ファイルDB出力オプションを追加した場合は、追加で以下を確認する:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m8"
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
- timestamped / manifest 相当の `confirmation_mode=time` と `played_at_ms` が planned rows / write preview まで保持される。
- timestampなし入力の `played_at_ms=0` 暫定仕様が壊れていない。
- 実ファイルDBへ書く場合は明示オプションで `data/` 配下に限定され、DBファイルをコミットしていない。
- 明示オプションなしの既定実行では実ファイルDBを生成しない。
- 実ファイルDB出力プレビューも、DB保存成功、曲ID/譜面ID確定、保存値確定として扱われていない。
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
