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
- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBがコミット対象に入っていないこと

## 今回までの作業結果

- M7a `score_digits` は、0から1,000,000までの可変桁表示、カンマ無視、1桁から7桁fixture、3桁実素材 `chart_field_template_129_double_challenge_lv19.png` の読み方を維持している。
- M7a `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` は、ROI左側ラベルや下線を数字扱いせず、右側数字領域へ寄せたcomponent分割を維持している。
- M7a `ex_score` を `max_combo` 由来の読み方まで拡張した。`ex_score` はROI左側の `EX SCORE` ラベルを数字扱いしないよう右側55%へfocusし、component分割する。
- `ex_score` はテンプレート探索で、ROI別 `digit_templates/ex_score/`、共有 `digit_templates/combo_ex_score/`、既存 `digit_templates/max_combo/`、root の順に読む。`combo_ex_score` がなくても既存 `max_combo` テンプレートfallbackで読める。
- fixtureでは `ex_score` の4桁分割/認識、テンプレート不足時の4桁segment診断、共有 `combo_ex_score`、`max_combo` fallbackを追加した。
- ローカル `metadata.csv` の `organized/result/result_087_sp_basic_lv06_888_score986610.png` は、EX SCORE ROI目視が `593` だったため、Git管理外のローカル期待値を `898` から `593` に修正した。`metadata.csv` はコミットしない。
- `ex_score` ROI別ローカルテンプレートは不要。今回試作した `samples/screenshots/organized/digit_templates/ex_score/` は削除済み。`ex_score` は既存 `max_combo` テンプレートfallbackで確認している。
- 2026-07-08のローカル `score_digits + max_combo + marvelous + perfect + great + good + miss + ex_score` 実行結果:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7a_digit_ex_score`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `target_count=60`
  - `total_attempts=480`
  - `status_counts={"recognized":480}`
  - `match_count=480`
  - `mismatch_count=0`
  - `ex_score`: 60/60 match、`segment_count_counts={"1":1,"3":30,"4":29}`、`expected_digit_length_counts={"1":1,"3":30,"4":29}`
- 2026-07-08のTesseract比較あり実行結果:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ex_score_ocr_compare`
  - M7aは8 ROI合計480/480 match。
  - `tesseract_comparison.available_attempts=59`
  - `same_normalized_count=56`
  - `different_normalized_count=3`
  - `unavailable_count=421`
  - unavailable は既定OCR対象が `score_digits` のため、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の各60件と、OCR未取得の `score_digits` 1件が比較対象なしになる。
- 2026-07-08の共有 `judgment_counts` root確認:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_ex_score`
  - `ex_score` は `max_combo` fallbackで60/60 match。
  - `miss` は共有 `judgment_counts` だけだと58/60 `ambiguous`、2/60 `recognized` の既知状態を維持。
- 更新済み:
  - `tools/vision_poc/runner.py`
  - `tests/test_vision_poc_ocr.py`
  - `tools/vision_poc/README.md`
  - `docs/design/03_event_and_save_boundary.md`
  - `docs/design/06_regression_guard.md`
  - `docs/implementation-roadmap.md`
  - `docs/next-task.md`

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

M7aの次ステップとして、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の480/480 matchを保ったまま、保存候補イベントごとのM7a数値集約レポートを小さく追加する。

- 第一候補は `m7a_digit_save_candidate_summary.csv` / `.json` / `.md` のような横持ち集約出力。
- 入力は既存 `m7a_digit_recognition` の confirmed-events 対象だけに限定する。
- 1行は1つの保存候補イベントにし、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の `recognized_digits`、`status`、`failure_reason`、`match`、`confidence` または `distance` を確認できる形にする。
- 集約statusは保存OK/NG判定ではなく、M8へ渡す数値読み取り材料として扱う。DB保存や保存可否判定は実装しない。
- duplicate、`event_type=rejected_transition`、未確定候補、non-result はM7a集約対象外のままにする。
- `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` を混同しない。特に `missing_reference` はテンプレート不足、`ambiguous` は距離/余白不足、`not_evaluated` は期待値不足として読む。
- `score_digits` の可変桁仕様を壊さない。1桁から7桁までのfixtureと、3桁実素材 `chart_field_template_129_double_challenge_lv19.png` の読み方を固定したままにする。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の右側数字領域分割を壊さない。
- ローカルテンプレートあり環境では `score_digits + max_combo + marvelous + perfect + great + good + miss + ex_score` の480/480 matchを維持する。
- 共有 `judgment_counts` と `combo_ex_score`、および `ex_score` の `max_combo` fallback探索順を壊さない。ROI別テンプレートがある場合はROI別を優先する。
- 判定数ROIの明るい青背景除外を壊さない。`marvelous`、`perfect`、`great`、`good`、`miss` のfixtureでは、青背景artifactを数字segmentとして数えない。
- 実装を変えた場合は、ネットワーク、画像、`metadata.csv` に依存しないfixtureテストを追加または更新する。
- 仕様語彙や読み方を変えた場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k m7a
python -m tools.vision_poc --m7a-digit-recognition --no-ocr --no-rois --output data\vision_poc_m7a_digit
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --no-ocr --no-rois --output data\vision_poc_m7a_digit_miss
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_miss
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7a_digit_ex_score
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ex_score_ocr_compare
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_ex_score
python -m tools.vision_poc --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k m7a
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7a_digit_ex_score
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ex_score_ocr_compare
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
- `score_digits` の可変桁分割、0から1,000,000までの1桁から7桁fixture、3桁実素材確認、ローカル60/60 matchを壊していない。
- `max_combo` の右側数字領域分割、分割数診断、ローカル60/60 matchを壊していない。
- `marvelous` の右側数字領域分割、分割数診断、ローカル60/60 matchを壊していない。
- `perfect` の右側数字領域分割、分割数診断、ローカル60/60 matchを壊していない。
- `great` の右側数字領域分割、分割数診断、ローカル60/60 matchを壊していない。
- `good` の右側数字領域分割、分割数診断、ローカル60/60 matchを壊していない。
- `miss` の右側数字領域分割、分割数診断、ローカル60/60 matchを壊していない。
- `ex_score` の右側数字領域分割、`max_combo` fallback、分割数診断、ローカル60/60 matchを壊していない。
- 判定数ROIの明るい青背景artifactを数字として数えないfixtureを壊していない。
- `score_digits + max_combo + marvelous + perfect + great + good + miss + ex_score` の480/480 matchを壊していない。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の4桁fixtureを壊していない。
- 共有 `judgment_counts` テンプレートfallbackの探索順を壊していない。`miss` は実素材ではROI別テンプレートが必要な状態として読む。
- `combo_ex_score` が `max_combo` / `ex_score` の共有候補として探索され、`ex_score` が既存 `max_combo` テンプレートfallbackでも読めることを壊していない。
- 次に追加するM7a集約出力は、保存候補イベント単位の読み取り材料に留め、M7保存判定やM8 DB保存を実装していない。
- `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` を混同せず読める。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
