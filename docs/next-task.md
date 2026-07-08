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

- `m7_save_readiness_review_rows` を入力にした `m7_save_decision_preview.csv`、`m7_save_decision_preview.json`、`m7_save_decision_preview.md` を追加した。
- preview status は `preview_save_candidate`、`blocked_readiness`、`needs_identity_review`、`needs_digit_review`、`missing_required_material` に限定した。
- `preview_save_candidate` はM8へ渡す候補材料が揃ったプレビュー状態であり、保存OK、DB保存成功、曲ID/譜面ID確定ではない。
- M5未実行の `ready_for_save_review` 行は `needs_identity_review` として止めるfixtureを追加した。
- M5候補観測が未解決、M7a digit review、M7 readiness上のM3 blocker、必須材料欠落をそれぞれ別statusで固定した。
- preview CSV/JSON/MarkdownにはM5の `identity_signal_*` と、選択M7a ROIごとの `recognized_digits`、`expected_value`、`match` を候補観測として出す。
- 既存の `m7_save_readiness_review.csv` の列は変えず、preview用のM7a詳細列は in-memory row と `m7_save_decision_preview.*` 側で扱う。
- 仕様更新として `docs/implementation-roadmap.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/04_data_model.md`、`docs/design/06_regression_guard.md`、`tools/vision_poc/README.md` を更新した。
- 2026-07-08のローカルM5なし実行:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m7_save_decision_preview.json` は `target_count=60`、`preview_candidate_count=0`、`preview_status_counts={"blocked_readiness":60}`。
- 2026-07-08のローカルM5あり実行:
  - `python -m tools.vision_poc --m5-jacket-match --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_m5_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - M5 jacket match: features 69、candidates 60、diagnostics 118。
  - `m7_save_readiness_review.json` は `target_count=60`、`readiness_status_counts={"ready_for_save_review":60}`。
  - `m7_save_decision_preview.json` は `target_count=60`、`preview_candidate_count=60`、`preview_status_counts={"preview_save_candidate":60}`。
  - `m7a_digit_save_candidate_summary.json` は8 ROI合計で各ROI 60/60 `recognized` / match、`aggregate_status_counts={"all_digits_recognized":60}`。
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
- 個人スコアDB保存、DB insert、低信頼度ログ本番仕様
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
- `m7_save_decision_preview.*` の最小入口とstatus語彙

## 次に必ず進める実作業

M7保存判定プレビューの最小入口はできたため、次はDB保存ではなく、プレビュー結果をM8へ渡す前に人間が確認しやすい診断を少し厚くする。

第一候補は、`m7_save_decision_preview.*` に以下の集計・代表を追加することです。

- `preview_save_candidate` の中で `identity_signal_source`、`m5_jacket_match_status`、`m5_identity_signal_status` ごとの件数をsummaryに出す。
- Markdownに `preview_save_candidate` の M5 source別・jacket status別代表を追加する。
- `needs_identity_review` は `m5_not_run`、`m5_identity_not_reviewable`、候補ID欠落を混同しない代表にする。
- `needs_digit_review` はM7a ROI別の `recognized_digits`、`expected_value`、`match`、`failure_reason` を代表で読めるようにする。
- 既存の `m7_save_readiness_review.*` の意味やCSV列を壊さない。
- fixtureでは、M5ありready、M5未実行、M5未解決、候補ID欠落、M7a digit review、必須材料欠落をネットワーク、画像、`metadata.csv` 非依存で固定する。
- 仕様語彙やsummaryの読み方を追加した場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- `m7_save_decision_preview.md` の代表表だけを先に拡張し、summary項目追加は次に回す。
- `m7_save_decision_preview.*` を触らず、`docs/design/04_data_model.md` 側でM8に渡す仮 payload の必須/候補フィールドを先に仕様化する。ただしdocs整理だけで終えないこと。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7_save_decision or m7a_digit_recognition_writes_confirmed_events_report"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7_save_decision or m7a"
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
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a"
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
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a/M7 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a/M7対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M7 save readiness review と M7 save decision preview が confirmed-events 対象だけを入力にしている。
- duplicate、`rejected_transition`、未確定候補、non-result が対象外のまま。
- `m7_save_readiness_review.csv` / `.json` / `.md` が保存判定前レビューに留まり、保存OK/NG判定やDB保存に進んでいない。
- `m7_save_decision_preview.csv` / `.json` / `.md` が保存判定プレビューに留まり、保存OK/NG判定やDB保存に進んでいない。
- `ready_for_save_review` と `preview_save_candidate` がDB保存可能、保存成功、曲ID/譜面ID確定として扱われていない。
- `identity_signal_*`、`m5_identity_reviewable`、`blocked_identity_signal` が曲ID/譜面ID確定として扱われていない。
- M7aの `recognized_digits`、`expected_value`、`match` が保存値確定として扱われていない。
- M3材料不足、M7a数字レビュー、M5候補観測未解決、必須材料欠落を混同せず読める。
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
