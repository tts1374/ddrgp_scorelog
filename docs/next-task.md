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

- M7a完了後の `main` から `codex/m7-save-readiness-review` を作成した。
- M7保存判定の入口として、M3保存候補材料とM7a数字材料を confirmed-events 1件単位に束ねる保存判定前レビューを追加した。
- 追加出力:
  - `m7_save_readiness_review.csv`
  - `m7_save_readiness_review.json`
  - `m7_save_readiness_review.md`
- 入力は `m3_save_candidate_summary_rows` と `m7a_digit_save_candidate_summary_rows` に限定する。
- 対象境界は confirmed-events、つまり `confirmed_result=true` かつ `duplicate=false` のまま。
- duplicate、`rejected_transition`、未確定候補、non-result は対象外のまま。
- readiness status は `ready_for_save_review`、`blocked_m3_material`、`blocked_digit_review`、`missing_required_material`。
- `ready_for_save_review` は保存判定へ進むためのPoC材料が揃った状態であり、保存OK、DB保存成功、曲ID/譜面ID確定ではない。
- fixtureでは、M3 ready + M7a all digits、M3 blocker、M7a digit review、M7a材料欠落の4状態を確認した。
- M7a CLI fixtureでも `m7_save_readiness_review.*` が出力されることを確認した。
- 2026-07-08のローカル実行:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `m7_save_readiness_review.json` は `target_count=60`、`readiness_status_counts={"blocked_m3_material":60}`。
  - 同じ実行の `m3_save_candidate_summary.json` は `song_title` / `artist` が `ocr_unavailable=60`。これは `--no-ocr` で曲名/artist OCRを走らせていないため。
  - 同じ実行の `m7a_digit_save_candidate_summary.json` は `aggregate_status_counts={"all_digits_recognized":60}`。
- `python -m tools.vision_poc --no-ocr` は 221/221 correct。

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

M5/M4境界へ触る場合は追加で読む資料:

- `docs/design/09_master_match_poc.md`
- `docs/design/08_master_db_generation.md`
- `docs/design/07_m3_chart_field_review.md`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tools/vision_poc/master_match.py`
- `tests/test_master_builder.py`
- `tests/test_master_match.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7aテンプレート画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- 個人スコアDB保存、低信頼度ログ本番仕様
- OCR結果やM7a認識結果から保存値を本番確定すること
- 曲ID/譜面IDの保存用確定
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

M7aで完了済みとして扱い、次チャットで蒸し返さないもの:

- M7a 8 ROIの数字認識入口
- M7a横持ち集約
- M7a digit review representatives
- M7a / Tesseract comparison review representatives
- ローカルテンプレートあり環境の8 ROI 480/480 match確認

## 次に必ず進める実作業

M7保存判定前レビューを、M5候補観測またはM3 OCR実行ありの材料へ接続する。

第一候補は、`m7_save_readiness_review.*` にM5の `identity_signal_*` / `jacket_match_status` の参照状態を取り込む小さな拡張です。

- `--m5-jacket-match` 実行時の `jacket_match_rows` を、保存判定前レビューへ任意入力として渡す。
- `identity_signal_status` が `jacket_resolved_candidate` または `composite_resolved_candidate` の場合だけ、M5側材料がレビュー可能であることを示す。
- `matched` や `identity_signal_*` は曲ID/譜面ID確定や保存OKではなく、M7へ渡す候補観測として扱う。
- M5未実行時は、現状どおりM3 + M7a材料だけで `m7_save_readiness_review.*` を出す。
- readiness status の語彙を増やす場合は、保存完了と誤読しない名前にする。
- 実装を変えた場合は、ネットワーク、画像、`metadata.csv` に依存しないfixtureテストを追加または更新する。
- 仕様語彙や読み方を変えた場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。
- 主作業完了後、今回の結果を踏まえて `docs/next-task.md` を次チャット用に更新する。

代替候補:

- `--m3-song-artist-ocr` ありのローカル実行で、`blocked_m3_material` がどの程度解消するかを確認し、M7 readiness report の代表にM3 blocker内訳をより読みやすく出す。
- `m7_save_readiness_review.md` に `blocked_m3_material` / `blocked_digit_review` / `missing_required_material` の次アクション表を追加する。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7a_digit_recognition_writes_confirmed_events_report"
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7a"
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --no-ocr
```

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_readiness or m7a"
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7_save_readiness
python -m tools.vision_poc --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

M4/M5境界へ触った場合は、M5完了確認コマンドも再実行すること。

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

- M7 save readiness review が confirmed-events 対象だけを入力にしている。
- duplicate、`rejected_transition`、未確定候補、non-result が対象外のまま。
- `m7_save_readiness_review.csv` / `.json` / `.md` が保存判定前レビューに留まり、保存OK/NG判定やDB保存に進んでいない。
- `ready_for_save_review` がDB保存可能、保存成功、曲ID/譜面ID確定として扱われていない。
- M3材料不足、M7a数字レビュー、必須材料欠落を混同せず読める。
- M7a 8 ROI 480/480 matchを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
