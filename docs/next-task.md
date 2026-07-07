# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。`docs/next-task.md` の更新だけで作業完了扱いにしないでください。

## 推論レベル

high

## 作業ブランチ

`codex/m7a-digit-recognition-poc`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m7a-digit-recognition-poc` であること
- `origin/main` にM5 PR #3 (`bf614d1`) がマージ済みであること

## 今回までの作業結果

- M5 PR #3 は `main` へマージ済み。M7aブランチは `origin/main` 上に載せ直し済み。
- M5は完了扱い。`jacket_match_status=matched`、`identity_signal_status=*resolved_candidate`、coverage系summaryは保存OKや曲ID/譜面ID確定ではない。
- M7a「スコア系数字認識のOCR脱却」の最小入口を追加した。
  - `--m7a-digit-recognition`
  - `--m7a-digit-rois`。既定は `score_digits`、`all` で主要数字ROIへ拡張可能。
  - `--m7a-digit-template-root`。既定は `samples/screenshots/organized/digit_templates`。
  - 出力は `m7a_digit_recognition.csv`、`m7a_digit_recognition_summary.json`、`m7a_digit_recognition_report.md`。
  - 既存 `score_ocr.csv` / `score_ocr_summary.json` は変更しない。
  - 同じ実行でTesseract結果がある場合だけ `tesseract_comparison` で正規化済み数字列を比較する。
- M7aは confirmed-events 境界だけを対象にする。
  - 条件は `confirmed_result=true` かつ `duplicate=false`。
  - duplicate、未確定候補、`rejected_transition`、non-result は対象外。
- M7a status語彙:
  - `recognized`: bitmapテンプレート比較で数字候補が出た。
  - `ambiguous`: 距離またはmarginがしきい値不足。
  - `missing_reference`: digit template不足。
  - `failed_segmentation`: 桁分割失敗。
  - `not_evaluated`: 数字候補は出たが期待値がなく成功判定できない。
- fixtureテストで、confirmed-events対象、Tesseract比較、`missing_reference`、`failed_segmentation`、`not_evaluated` を固定した。
- docs/README更新済み。
  - `tools/vision_poc/README.md`
  - `docs/design/03_event_and_save_boundary.md`
  - `docs/implementation-roadmap.md`
- 2026-07-07のローカルM7a実行結果:
  - コマンド: `python -m tools.vision_poc --m7a-digit-recognition --no-ocr --no-rois --output data\vision_poc_m7a_digit`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `target_count=60`
  - `total_attempts=60`
  - `status_counts={"missing_reference": 60}`
  - `failure_reason_counts={"missing_digit_templates=0123456789": 60}`
  - `skipped_duplicate_count=37`
  - `skipped_rejected_transition_count=3`
  - `skipped_unconfirmed_count=124`
  - `tesseract_comparison.available_attempts=0`。`--no-ocr` 実行のため。
- 上記 `missing_reference` は、ローカル digit template が未配置であるための想定結果。実装失敗ではない。

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

M5/M4境界へ触る場合は追加で読む資料:

- `docs/design/08_master_db_generation.md`
- `docs/design/07_m3_chart_field_review.md`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- `samples/screenshots/organized/digit_templates/` などのM7a digit template画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- 個人スコアDB保存、保存可否判定本番仕様、低信頼度ログ本番仕様
- OCR結果やM7a認識結果から保存値を本番確定すること
- 曲ID/譜面IDの保存用確定
- ROI座標定義の大変更
- Tesseract OCR全体の撤去やOCR方式全面刷新
- duplicate key の本格実装差し替え
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

M5で完了済みとして扱い、次チャットで蒸し返さないもの:

- M5参照カバレッジの出力名と語彙整理
- `jacket_match_summary.json` と `jacket_reference_coverage_summary.json` の読み分け
- `RЁVOLUTIФN` / `RËVOLUTIФN` のM4 canonical/alias整理
- `Inner Spirit -GIGA HiTECH MIX-` と `RЁVOLUTIФN` のjacket参照追加後のM5確認
- title line-hashをjacket ambiguous候補集合内だけに使う境界

## 次に必ず進める実作業

M7aの次ステップとして、`score_digits` のローカルdigit templateを整備し、実素材の confirmed-events で `recognized` を出せるか確認する。

- テンプレート素材はローカル素材として置き、Git管理しない。
- まず `score_digits` だけを対象にする。
- テンプレート画像は数字前景の周囲に背景余白を含める。
- `samples/screenshots/organized/digit_templates/score_digits/0.png` から `9.png` のような配置を優先する。
- `python -m tools.vision_poc --m7a-digit-recognition --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ocr_compare` を実行し、M7a結果と既存Tesseract結果を比較する。
- `missing_reference` が解消した後、`recognized` / `ambiguous` / `failed_segmentation` / mismatch の代表を確認する。
- 過剰な `ambiguous` や明らかな誤認識があれば、テンプレート余白、桁分割、距離しきい値を小さく調整する。
- `score_digits` が読める状態になるまで、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` へ広げない。
- 実装を変えた場合は、ネットワーク、画像、`metadata.csv` に依存しないfixtureテストを追加または更新する。
- 仕様語彙や読み方を変えた場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。

## 検証コマンド

今回通したコマンド:

```powershell
python -m tools.vision_poc --no-ocr
python -m tools.vision_poc --m7a-digit-recognition --no-ocr --no-rois --output data\vision_poc_m7a_digit
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

次チャットで最低限実行するコマンド:

```powershell
python -m tools.vision_poc --m7a-digit-recognition --no-ocr --no-rois --output data\vision_poc_m7a_digit
python -m tools.vision_poc --m7a-digit-recognition --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ocr_compare
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
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5/M7a PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 仕様語彙、出力ファイル名、summaryの読み方、保存境界、OCR/M7a対象境界を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M7a digit recognition が confirmed-events 対象だけを入力にしている。
- `score_digits` について、非OCR方式の認識候補、距離またはconfidence、status、failure_reason を出せる。
- `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` を混同せず読める。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- M7保存判定やM8 DB保存を実装していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
