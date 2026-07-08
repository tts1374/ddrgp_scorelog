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

- M7a `score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の縦持ち認識結果に加えて、confirmed-events 保存候補1件を1行にする横持ち集約を維持している。
- M7a横持ち集約の `aggregate_status=needs_digit_review` 行から、ROI別 `status` / `failure_reason` ごとに代表化するレビュー補助レポートを追加した。
- 追加出力:
  - `m7a_digit_save_candidate_review.json`
  - `m7a_digit_save_candidate_review.md`
- 代表には `organized_file`、ROI名、`recognized_digits`、`expected_value`、`status`、`failure_reason`、`match`、`confidence`、`distance`、`segment_count` を含める。
- `m7a_digit_save_candidate_review.*` の入力は既存の `m7a_digit_save_candidate_summary_rows` の行だけ。confirmed-events 境界は変えず、duplicate、`rejected_transition`、未確定候補、non-result は対象外のまま。
- `missing_reference` はテンプレート不足、`ambiguous` は距離/余白不足、`failed_segmentation` は桁分割失敗、`not_evaluated` は期待値不足または未試行として読む。
- 保存OK/NG判定、DB保存、曲ID/譜面ID確定には進んでいない。
- fixtureでは、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` の代表化と、`all_digits_recognized` 時に review 対象0になることを確認した。
- ドキュメント更新済み:
  - `tools/vision_poc/README.md`
  - `docs/design/03_event_and_save_boundary.md`
  - `docs/design/06_regression_guard.md`
  - `docs/implementation-roadmap.md`
  - `docs/next-task.md`
- 2026-07-08のローカル `score_digits + max_combo + marvelous + perfect + great + good + miss + ex_score` 実行結果:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7a_digit_ex_score`
  - 分類: 221/221 correct、false positives 0、false negatives 0。
  - `target_count=60`
  - `total_attempts=480`
  - `status_counts={"recognized":480}`
  - `match_count=480`
  - `mismatch_count=0`
  - `m7a_digit_save_candidate_summary.json` は `aggregate_status_counts={"all_digits_recognized":60}`。
  - `m7a_digit_save_candidate_review.json` は `review_candidate_count=0`。
- 2026-07-08のTesseract比較あり実行結果:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ex_score_ocr_compare`
  - M7aは8 ROI合計480/480 match。
  - `tesseract_comparison.available_attempts=59`
  - `same_normalized_count=56`
  - `different_normalized_count=3`
  - `unavailable_count=421`
  - `m7a_digit_save_candidate_summary.json` は `aggregate_status_counts={"all_digits_recognized":60}`。
  - `m7a_digit_save_candidate_review.json` は `review_candidate_count=0`。
- 2026-07-08の共有 `judgment_counts` root確認:
  - `python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_ex_score`
  - `status_counts={"ambiguous":58,"recognized":422}`
  - `match_count=422`
  - `mismatch_count=0`
  - `m7a_digit_save_candidate_summary.json` は `aggregate_status_counts={"all_digits_recognized":2,"needs_digit_review":58}`。
  - `m7a_digit_save_candidate_review.json` は `review_candidate_count=58`。
  - `miss` は `distance_above_threshold=54`、`low_margin=4` の2グループで代表化できる。
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

M7aレビュー代表レポートの次ステップとして、M7aと既存Tesseract OCRの比較差分を代表化する小さなレポートを追加する。

- 第一候補は `m7a_tesseract_comparison_review.json` / `.md` のような代表レポート。
- 入力は同一実行内の `m7a_digit_results` と `score_ocr_results` に限定する。
- 既存 `m7a_digit_recognition_summary.json` の `tesseract_comparison` counts は維持し、詳細代表を別出力で補う。
- confirmed-events 境界は変えない。duplicate、`rejected_transition`、未確定候補、non-result は対象外のままにする。
- `same_normalized`、`different_normalized`、`tesseract_unavailable` のように、M7a数字列とTesseract正規化数字列の比較状態を分ける。
- 主目的は `different_normalized` の代表3件と、OCR未取得が大量にある理由をROI別に確認しやすくすること。
- 代表には `organized_file`、ROI名、M7a `recognized_digits`、M7a `status` / `failure_reason`、Tesseract raw / normalized / status / error、`expected_value`、M7a match、Tesseract match を含める。
- これはTesseract差分レビュー補助であり、保存OK/NG判定、DB保存、OCR方式刷新には進まない。
- この作業で重大な未整理差分や回帰が見つからない限り、M7aは完了扱いにする。次フェーズはM7の保存判定またはM8の個人スコアDB保存へ進む。
- ローカルテンプレートあり環境では、M7a 8 ROI 480/480 matchと `aggregate_status_counts={"all_digits_recognized":60}` を維持する。
- Tesseract比較あり実行では、現状の `available_attempts=59`、`same_normalized_count=56`、`different_normalized_count=3`、`unavailable_count=421` の読み方を壊さない。
- 実装を変えた場合は、ネットワーク、画像、`metadata.csv` に依存しないfixtureテストを追加または更新する。
- 仕様語彙や読み方を変えた場合は、`docs/implementation-roadmap.md`、関連する `docs/design/`、`tools/vision_poc/README.md` も同じ作業で更新する。

## 検証コマンド

今回通したコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k m7a
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7a_digit_ex_score
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_ex_score
python -m tools.vision_poc --no-ocr
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ex_score_ocr_compare
python -m pytest tests
git diff --check
```

次チャットで最低限実行するコマンド:

```powershell
python -m pytest tests\test_vision_poc_ocr.py -k m7a
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --no-ocr --no-rois --output data\vision_poc_m7a_digit_ex_score
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ex_score_ocr_compare
python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect great good miss ex_score --m7a-digit-template-root data\m7a_shared_judgment_template_trial --no-ocr --no-rois --output data\vision_poc_m7a_digit_shared_judgment_ex_score
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

- M7a digit recognition と M7a横持ち集約が confirmed-events 対象だけを入力にしている。
- `m7a_digit_save_candidate_summary.csv` / `.json` / `.md` が保存候補イベント単位の読み取り材料に留まり、保存判定やDB保存を実装していない。
- `m7a_digit_save_candidate_review.json` / `.md` が `needs_digit_review` の代表整理に留まり、保存OK/NG判定やDB保存に進んでいない。
- 次に追加するTesseract比較差分代表も、保存OK/NG判定やDB保存に進んでいない。
- Tesseract比較差分代表の追加後、重大な未整理差分や回帰がなければM7aを完了扱いにできる。
- `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` を混同せず読める。
- `score_digits` の可変桁分割、0から1,000,000までの1桁から7桁fixture、3桁実素材確認、ローカル60/60 matchを壊していない。
- `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の右側数字領域分割、4桁fixture、ローカル60/60 matchを壊していない。
- 判定数ROIの明るい青背景artifactを数字として数えないfixtureを壊していない。
- `score_digits + max_combo + marvelous + perfect + great + good + miss + ex_score` の480/480 matchを壊していない。
- 共有 `judgment_counts` テンプレートfallbackの探索順を壊していない。`miss` は実素材ではROI別テンプレートが必要な状態として読む。
- `combo_ex_score` が `max_combo` / `ex_score` の共有候補として探索され、`ex_score` が既存 `max_combo` テンプレートfallbackでも読めることを壊していない。
- 既存Tesseract OCR出力を壊していない。
- M5の通常候補、診断出力、coverage summary、`identity_signal_*` の意味を変更していない。
- fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
