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

- M7a `score_digits` は、0から1,000,000までの可変桁表示、カンマ無視、1桁から7桁fixture、3桁実素材 `chart_field_template_129_double_challenge_lv19.png` の読み方を維持している。
- M7a `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` は、ROI左側ラベルや下線を数字扱いせず、右側数字領域へ寄せたcomponent分割を維持している。
- `good` と `miss` は左側ラベル由来成分が残りやすいため、M7aの右側focusを `0.55` にしている。判定数ROIは明るい青背景を数字扱いしないよう高明度かつチャンネル差が大きい成分を除外する。`miss` はさらに白数字向けの明度 + チャンネル差maskと最小高さもROI別に少し絞っている。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の4桁fixture、テンプレート不足時の `missing_reference` + 分割数診断、共有 `judgment_counts` テンプレートfallbackを追加/維持している。
- `marvelous`、`perfect`、`great`、`good`、`miss` は `digit_templates/judgment_counts/` を共有候補として読む。`max_combo` と今後の `ex_score` は `digit_templates/combo_ex_score/` を共有候補として読む。
- `miss` は共有 `judgment_counts` だけだと分割数は期待桁数と一致するが、実素材60件中58件が `ambiguous` になる。ROI別 `samples/screenshots/organized/digit_templates/miss/` のローカルテンプレートが必要。
- ローカル素材として `samples/screenshots/organized/digit_templates/` や `data\m7a_shared_judgment_template_trial` 配下のテンプレートを使う場合がある。これはGit管理しない。
- 2026-07-08のローカル `score_digits + max_combo + marvelous + perfect + great + good + miss` 実行結果:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --no-ocr --no-rois --output data\vision_poc_m7a_digit_miss`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `target_count=60`
  - `total_attempts=420`
  - `status_counts={"recognized":420}`
  - `match_count=420`
  - `mismatch_count=0`
  - `miss`: 60/60 match、`segment_count_counts={"1":51,"2":9}`、`expected_digit_length_counts={"1":51,"2":9}`
- 2026-07-08の共有 `judgment_counts` テンプレート確認:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_miss`
  - `target_count=60`
  - `total_attempts=420`
  - `status_counts={"ambiguous":58,"recognized":362}`
  - `match_count=362`
  - `mismatch_count=0`
  - `miss`: 58/60 `ambiguous`、2/60 `recognized`、`failure_reason_counts={"distance_above_threshold":58}`、分割数は期待桁数と一致。
- 2026-07-08のTesseract比較あり実行結果:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_miss_ocr_compare`
  - M7aは7 ROI合計420/420 match。
  - `tesseract_comparison.available_attempts=59`
  - `same_normalized_count=56`
  - `different_normalized_count=3`
  - `unavailable_count=361`
  - unavailable は既定OCR対象が `score_digits` のため、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の各60件と、OCR未取得の `score_digits` 1件が比較対象なしになる。
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

M7aの次ステップとして、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の可変桁・右寄せ分割、4桁fixture、共有テンプレートfallback、420/420 matchを保ったまま、`ex_score` を小さい範囲から検証する。

- 次は `ex_score` を第一候補にする。
- `ex_score` は桁見た目やTesseract profile差分が別物なので、最初から採用前提にしない。
- `max_combo` とフォントデザインが近い可能性があるため、M7aでは `combo_ex_score` 共有テンプレートを第一候補として検証する。
- `combo_ex_score` 共有テンプレートだけで読めない場合は、分割数、期待値桁数、`missing_reference` / `failed_segmentation` / `ambiguous` の代表を確認し、必要な場合だけROI別 `digit_templates/ex_score/` テンプレートをローカル素材として追加する。
- テンプレート素材はローカル素材として置き、Git管理しない。
- テンプレート画像は数字前景の周囲に背景余白を含める。
- `samples/screenshots/organized/digit_templates/<roi>/0.png` から `9.png` やvariant画像のようなROI別配置を優先する。
- `score_digits` の可変桁仕様を壊さない。特に1桁から7桁までのfixtureと、3桁実素材 `chart_field_template_129_double_challenge_lv19.png` の読み方を固定したままにする。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の右側数字領域分割を壊さない。
- ローカルテンプレートあり環境では `score_digits + max_combo + marvelous + perfect + great + good + miss` の420/420 matchを維持する。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の4桁fixtureを壊さない。
- 共有 `judgment_counts` と `combo_ex_score` の探索順を壊さない。ROI別テンプレートがある場合はROI別を優先し、共有テンプレートは共通化候補として扱う。
- 判定数ROIの明るい青背景除外を壊さない。`marvelous`、`perfect`、`great`、`good`、`miss` のfixtureでは、青背景artifactを数字segmentとして数えない。
- 実装を変えた場合は、ネットワーク、画像、`metadata.csv` に依存しないfixtureテストを追加または更新する。
- 仕様語彙や読み方を変えた場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k m7a
python -m tools.vision_poc --m7a-digit-recognition --no-ocr --no-rois --output data\vision_poc_m7a_digit
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good --no-ocr --no-rois --output data\vision_poc_m7a_digit_good
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_good
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --no-ocr --no-rois --output data\vision_poc_m7a_digit_miss
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_miss
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_miss_ocr_compare
python -m tools.vision_poc --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

次チャットで最低限実行するコマンド:

```powershell
python -m tools.vision_poc --m7a-digit-recognition --no-ocr --no-rois --output data\vision_poc_m7a_digit
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --no-ocr --no-rois --output data\vision_poc_m7a_digit_miss
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_miss
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
- 判定数ROIの明るい青背景artifactを数字として数えないfixtureを壊していない。
- `score_digits + max_combo + marvelous + perfect + great + good + miss` の420/420 matchを壊していない。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の4桁fixtureを壊していない。
- 共有 `judgment_counts` テンプレートfallbackの探索順を壊していない。`miss` は実素材ではROI別テンプレートが必要な状態として読む。
- `combo_ex_score` が `max_combo` / `ex_score` の共有候補として探索されることを壊していない。
- 次に広げたROIについて、非OCR方式の認識候補、距離またはconfidence、status、failure_reason を出せる。
- `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` を混同せず読める。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- M7保存判定やM8 DB保存を実装していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
