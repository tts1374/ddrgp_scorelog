# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

今回の作業ブランチは `codex/m8-score-db-schema-preview` です。

次の主作業はM8の続きなので、作業ブランチは `codex/m8-score-db-write-preview` を推奨します。

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- このM8 score DB schema preview PRがmerge済みなら、最新 `main` から `codex/m8-score-db-write-preview` を作る。
- 未mergeなら、`codex/m8-score-db-schema-preview` の先端を取り込んでから新ブランチを作るか、このPRのmergeを待つ。
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと。

## 今回までの作業結果

- M8 payload previewの後段として、保存予定レコードプレビューを追加した。
- `m8_planned_play_records.csv`、`m8_planned_play_records.json`、`m8_planned_play_records.md` を `--m7a-digit-recognition` 実行時に生成する。
- 入力は `m8_save_payload_preview_rows`。
- `payload_preview_status=payload_ready` の行だけを個人スコアDB `plays` 相当の最小row contractへ変換する。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` は保存予定レコードへ変換しない。
- 最小 `plays` スキーマを in-memory SQLite fixtureで固定した。実ファイルDB生成や本番insertには進んでいない。
- 保存予定レコードの最小列は `played_at_ms`、`song_id`、`chart_id`、`score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score`、`source_organized_file`、`source_confirmation_mode`、`analysis_payload_status`、`identity_signal_source`、`m5_identity_signal_status`、`m5_jacket_match_status`。
- `played_at_ms` は `timestamp_ms` 由来の暫定値で、timestampなし入力では `0` として扱う。
- `song_id` / `chart_id` はM5 `identity_signal_*` 由来の候補観測であり、曲ID/譜面ID確定ではない。
- スコア・判定数はM7a `recognized_digits` 由来の候補値であり、保存値確定ではない。
- `m8_save_payload_preview.*` の既存CSV列や意味は変更していない。
- 仕様更新として `docs/implementation-roadmap.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/04_data_model.md`、`docs/design/05_storage_io_spec.md`、`docs/design/06_regression_guard.md`、`tools/vision_poc/README.md` を更新した。

2026-07-09時点のローカル確認:

- M5なし:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m8_save_payload_preview.json`: `target_count=60`、`payload_candidate_count=0`、`payload_ready_count=0`、`payload_status_counts={"unsupported_preview_status":60}`、`excluded_preview_status_counts={"blocked_readiness":60}`。
  - `m8_planned_play_records.json`: `target_count=60`、`planned_record_count=0`、`excluded_payload_status_counts={"unsupported_preview_status":60}`。
- M5あり:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m8_save_payload_preview.json`: `target_count=60`、`payload_candidate_count=60`、`payload_ready_count=60`、`payload_status_counts={"payload_ready":60}`。
  - `m8_planned_play_records.json`: `target_count=60`、`planned_record_count=60`、`excluded_payload_status_counts={}`。
  - planned record representativesは `source_confirmation_mode=frames`、`played_at_ms=0` の暫定値を出す。
- `python -m tools.vision_poc --no-ocr`: 221/221 correct。
- `python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"`: 52 passed。
- `python -m ruff check master tools\vision_poc pyproject.toml tests`: passed。
- `python -m compileall master tools\vision_poc`: passed。
- `python -m pytest tests`: 187 passed。
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
- `payload_ready` や保存予定レコードを保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定として扱うこと
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
- M7a digit review representatives
- M7a / Tesseract comparison review representatives
- ローカルテンプレートあり環境の8 ROI 480/480 match確認
- M7 readiness への任意M5 `identity_signal_*` / `jacket_match_status` 接続
- M5 identity reviewable時に `song_title` / `artist` OCR不足だけで `blocked_m3_material` にしない挙動
- `m7_save_decision_preview.*` のstatus語彙と基本CSV列
- `m8_save_payload_preview.*` のCSV / JSON / Markdown出力
- M8 dry-run status語彙と、`preview_save_candidate` 以外をpayload材料にしない境界
- `m8_planned_play_records.*` のCSV / JSON / Markdown出力
- `payload_ready` 以外を保存予定レコードへ変換しない境界
- `plays` 最小スキーマの in-memory fixture

## 次に必ず進める実作業

次は、保存予定レコードを土台にして、DB insert境界をさらに小さく固定する。ただし本番保存処理や常時保存には進まない。

第一候補は、明示的なdry-run DB書き込みプレビューを追加することです。

- 入力は `m8_planned_play_records_rows` に限定する。
- 実ファイルDBへ自動保存する既定挙動にはしない。
- まずは in-memory SQLite に planned rows をinsertし、insert対象件数、insert後件数、除外件数、代表行を `m8_score_db_write_preview.*` のようなレポートで確認できるようにする。
- 実ファイルDB出力を付ける場合は明示オプションにし、出力先は必ず `data/` 配下に限定し、DBファイルはGit管理しない。
- `payload_ready`、保存予定レコード、DB書き込みプレビューの成功を、保存OK、曲ID/譜面ID確定、保存値確定、本番DB保存成功として扱わない。
- duplicate key の本格差し替えはまだ行わない。必要ならDBスキーマ上の将来列や未決事項としてだけ整理する。
- timestampなし入力で `played_at_ms=0` になる暫定仕様を、次のDB insert境界でどう扱うかを整理する。必要なら timestamped / manifest 入力での planned rows fixtureを追加する。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界を追加・変更した場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- DBファイル書き込みへ進む前に、`m8_planned_play_records.*` の timestamped / manifest fixtureを追加し、`confirmation_mode=time` と `played_at_ms` が保存予定レコードに保持されることを固定する。
- `plays` 最小スキーマに `source_timestamp_ms` や `schema_version` を足すかどうかを、fixtureとdocsで小さく決める。ただしdocs整理だけで終えないこと。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness
python -m tools.vision_poc --no-ocr
python -m pytest tests
git diff --check
```

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check master tools\vision_poc pyproject.toml tests
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

- `m8_save_payload_preview_rows` の `payload_ready` 行だけが保存予定レコード変換またはDB書き込みプレビューの入力になっている。
- `unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` が保存予定レコードやDB書き込みプレビューへ進まない。
- DB書き込みプレビューfixtureがネットワーク、画像、`metadata.csv`、ローカルDBファイルに依存せず通る。
- 実ファイルDBへ書く場合は明示オプションで `data/` 配下に限定され、DBファイルをコミットしていない。
- `payload_ready`、保存予定レコード、DB書き込みプレビューがDB保存成功、曲ID/譜面ID確定、保存値確定として扱われていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- 既存の `m7_save_readiness_review.*`、`m7_save_decision_preview.*`、`m8_save_payload_preview.*`、`m8_planned_play_records.*` のCSV列や意味を壊していない。
- duplicate、`rejected_transition`、未確定候補、non-result が上流対象外のまま。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `preview_save_candidate`、`payload_ready`、保存予定レコードへ進む。
- M5未実行時は、preview上で `blocked_readiness` または `needs_identity_review` として止まり、M8 payload ready / 保存予定レコード対象にならない。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
