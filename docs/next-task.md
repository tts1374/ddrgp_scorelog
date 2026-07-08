# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

`codex/m7-save-readiness-review`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m7-save-readiness-review` であること
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと

## 今回までの作業結果

- `m7_save_decision_preview.*` の診断を拡張した。
- `preview_save_candidate_identity_signal_source_counts`、`preview_save_candidate_m5_jacket_match_status_counts`、`preview_save_candidate_m5_identity_signal_status_counts` をsummaryへ追加した。
- Markdownに `Preview Candidate M5 Representatives` を追加し、M5 source / jacket status / identity signal status ごとに代表を読めるようにした。
- `needs_identity_review` の `preview_reason` を `m5_not_run`、`m5_identity_not_reviewable`、`identity_signal_id_missing` に分け、M5未実行、M5候補観測未解決、候補ID欠落を混同しないfixtureを追加した。
- Markdownに `Identity Review Representatives` を追加し、identity review理由別の代表を読めるようにした。
- Markdownに `Digit Review Representatives` を追加し、M7a ROI別の `recognized_digits`、`expected_value`、`match`、`failure_reason` を代表で読めるようにした。
- `m7_save_readiness_review.csv` の列と意味は変更していない。
- `m7_save_decision_preview.csv` の列は維持し、追加診断はJSON / Markdown中心にした。
- `preview_save_candidate` は引き続きM8へ渡す候補材料が揃ったプレビュー状態であり、保存OK、DB保存成功、曲ID/譜面ID確定ではない。
- 仕様更新として `docs/implementation-roadmap.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/04_data_model.md`、`docs/design/06_regression_guard.md`、`tools/vision_poc/README.md` を更新した。
- 2026-07-08のローカルM5なし実行:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m7_save_decision_preview.json` は `target_count=60`、`preview_candidate_count=0`、`preview_status_counts={"blocked_readiness":60}`。
- 2026-07-08のローカルM5あり実行:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m7_save_decision_preview.json` は `target_count=60`、`preview_candidate_count=60`、`preview_status_counts={"preview_save_candidate":60}`。
  - preview candidate M5 source counts は `{"jacket_feature":57,"title_linehash_dict":3}`。
  - preview candidate jacket status counts は `{"ambiguous":3,"matched":57}`。
  - preview candidate identity signal status counts は `{"composite_resolved_candidate":3,"jacket_resolved_candidate":57}`。
- `python -m tools.vision_poc --no-ocr` は 221/221 correct。
- `python -m pytest tests` は 186 passed。

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

M4/M5 DB生成や曲名正規化へ踏み込む場合は追加で読む資料:

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
- 個人スコアDBへのinsert、DBスキーマ本実装、低信頼度ログ本番仕様
- OCR結果やM7a認識結果から保存値を本番確定すること
- `ready_for_save_review` や `preview_save_candidate` をDB保存可能、保存成功、曲ID/譜面ID確定として扱うこと
- M5 `identity_signal_*` から曲ID/譜面IDを保存用確定すること
- M7aの `recognized_digits` を保存値確定として扱うこと
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

M7a/M5/M7 readiness/previewで完了済みとして扱い、次チャットで蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7a横持ち集約
- M7a digit review representatives
- M7a / Tesseract comparison review representatives
- ローカルテンプレートあり環境の8 ROI 480/480 match確認
- M7 readiness への任意M5 `identity_signal_*` / `jacket_match_status` 接続
- M5 identity reviewable時に `song_title` / `artist` OCR不足だけで `blocked_m3_material` にしない挙動
- `m7_save_decision_preview.*` のstatus語彙と基本CSV列
- `m7_save_decision_preview.*` のM5 source / jacket status / identity status診断、identity review理由別代表、digit review ROI別代表

## 次に必ず進める実作業

M7保存判定プレビューの診断は厚くなったため、次はDB保存ではなく、M8へ渡す候補payloadのdry-run入口を作る。

第一候補は、`m7_save_decision_preview_rows` の `preview_save_candidate` だけを入力にした `m8_save_payload_preview.*` などの新しいPoC出力を追加することです。

- 入力は `preview_status=preview_save_candidate` の行だけにする。
- 出力はCSV / JSON / Markdownの3点を基本にする。
- payload preview status は保存OKではなく、例として `payload_ready`、`missing_identity_candidate`、`missing_digit_value`、`unsupported_preview_status` など、M8本実装前のdry-run語彙に留める。
- payload候補には `organized_file`、`timestamp_ms`、`confirmation_mode`、M5の `identity_signal_song_id` / `identity_signal_chart_id`、`identity_signal_source`、`m5_jacket_match_status`、M7a 8 ROIの数字候補、元の `preview_status` / `preview_reason` を含める。
- `payload_ready` でもDB保存可能、保存成功、曲ID/譜面ID確定、保存値確定とは扱わない。
- `preview_save_candidate` 以外を入力にしないか、診断summary上で除外件数としてだけ数える。
- fixtureでは、M5ありready、候補ID欠落、数字欠落、preview対象外statusをネットワーク、画像、`metadata.csv` 非依存で固定する。
- 既存の `m7_save_readiness_review.*` と `m7_save_decision_preview.*` のCSV列や意味を壊さない。
- 仕様語彙やsummaryの読み方を追加した場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- 先に `docs/design/04_data_model.md` にM8 payload previewのフィールド契約を追加しつつ、最小fixture関数だけ実装する。ただしdocs整理だけで終えないこと。
- `m8_save_payload_preview.*` のMarkdown代表だけを先に追加し、CSVの細かい列は次に回す。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a"
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

M4/M5境界へさらに触った場合は、M5完了確認として `tests\test_master_match.py` と M5 jacket match のPoC実行も再確認すること。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像はコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7/M8 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7/M8対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M7 save readiness review と M7 save decision preview が confirmed-events 対象だけを入力にしている。
- duplicate、`rejected_transition`、未確定候補、non-result が対象外のまま。
- `m7_save_readiness_review.csv` / `.json` / `.md` が保存判定前レビューに留まり、保存OK/NG判定やDB保存に進んでいない。
- `m7_save_decision_preview.csv` / `.json` / `.md` が保存判定プレビューに留まり、保存OK/NG判定やDB保存に進んでいない。
- M8 payload previewを追加する場合も、DB insert、DBスキーマ本実装、保存値本番確定に進んでいない。
- `ready_for_save_review`、`preview_save_candidate`、`payload_ready` がDB保存可能、保存成功、曲ID/譜面ID確定として扱われていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- M3材料不足、M7a数字レビュー、M5候補観測未解決、候補ID欠落、必須材料欠落を混同せず読める。
- M5あり実行では、M5 identity reviewable + M7a all digits の行が `preview_save_candidate` へ進む。
- M5未実行時は、preview上で `needs_identity_review` またはM7 readiness上の blockerとして止まる。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
