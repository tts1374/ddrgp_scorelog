# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。

## 推論レベル

high

## 作業ブランチ

`codex/vision-poc-ocr-tuning`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/vision-poc-ocr-tuning` であること
- 最新コミットが M3 chart-field template holdout 分割追加コミット以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- ローカル `samples/screenshots/metadata.csv` は112行、`samples/screenshots/organized/chart_field_templates/` は29枚で確認した。
- `python -m tools.vision_poc --no-ocr` は Total 112 / Correct 112 / false positives 0 / false negatives 0。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- `result_events_summary.json` は `confirmed_count=60`、`confirmed_result_count=61`、`duplicate_count=1`、`rejected_transition_count=3`。
- `m3_chart_fields_summary.json` は `chart_field_target_count=60`、`excluded_counts={duplicate:1,rejected_transition:3,unconfirmed:12,non_result:36}`。
- `m3_chart_field_extraction_summary.json` の `filename-baseline` は 180 attempts / 175 match / 5 mismatch / 156 skipped。difficulty 5件 mismatch はファイル名ラベルのドリフト検出として読む。
- `m3_chart_field_template_extraction_summary.json` の `roi-template-nearest` は 180/180 match。これは `chart_field_templates/` と confirmed-events result ROI の同分布 leave-one-out 診断であり、OCR、採用済みテンプレート照合、マスタ照合の成功扱いにしない。
- 今回、参照を `chart_field_templates/` のみに限定する `roi-template-holdout` を追加した。出力は `m3_chart_field_template_holdout_extraction.csv`、`m3_chart_field_template_holdout_extraction_summary.json`、`m3_chart_field_template_holdout_diagnostics.md`。
- holdout 実測は 180 attempts / 110 match / 70 mismatch / 156 skipped。`play_style` は 60/60 match、`difficulty` は 43/60 match、`level` は 7/60 match。
- holdout の `reference_source_image_counts` は `chart_field_templates=29`、`confirmed_events=0`。confirmed-events result ROI は評価専用で、参照に含めない。
- holdout mismatch は `missing_expected_template_reference` が中心。`difficulty` は `DIFFICULT` 参照がないため 17件が `DIFFICULT -> BASIC`、`level` は 10/11/12/13/16/17 など未収録レベルが多い。
- `docs/design/03_event_and_save_boundary.md`、`docs/design/06_regression_guard.md`、`docs/design/07_m3_chart_field_review.md`、`docs/implementation-roadmap.md`、`tools/vision_poc/README.md` に `roi-template-holdout` の読み方を追加した。
- `tests/test_vision_poc_ocr.py` に、holdout が confirmed-events result ROI を参照に含めないこと、テンプレートなし環境で `no_template_references` になることを追加した。
- `samples/screenshots/metadata.csv`、スクリーンショット画像、`samples/screenshots/organized/chart_field_templates/`、`data/` 配下のPoC出力はGit管理しない。
- 直近確認では `python -m tools.vision_poc --no-ocr`、`python -m ruff check tools\vision_poc pyproject.toml tests`、`python -m compileall tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 85 passed。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/vision-poc-prep.md`
- `docs/design/README.md`
- `docs/design/00_glossary.md`
- `docs/design/01_pipeline_fsm.md`
- `docs/design/02_frame_input_contract.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/06_regression_guard.md`
- `docs/design/07_m3_chart_field_review.md`
- `docs/adr/0001-foundational-poc-boundaries.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tests/test_vision_poc_classification.py`
- `tests/test_vision_poc_ocr.py`
- `tests/test_vision_poc_result_events.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像コミット
- 既存ローカル素材や生成物の削除・移動。ただし必要なら目的と対象を明確にしてから行う
- 本番キャプチャAPI
- 実キャプチャデバイス依存コード
- 常駐監視ループ
- 非同期処理
- DB保存
- OCR方式の大幅刷新
- ROI座標定義の大変更
- duplicate key の本格実装差し替え
- マスタDB生成
- マスタ照合
- 曲名OCR / artist OCR / rank OCR の本格精度改善
- ファジーマッチ、曲名正規化、候補絞り込み
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

## 後続作業

1. 現状確認
   - `git status --short --branch` と `git log --oneline -5` を確認する。
   - ローカル素材がある場合、`metadata.csv` が112行、`chart_field_templates/` が29枚であることを確認する。
   - `python -m tools.vision_poc --no-ocr` を実行し、112件全正解、`transition_countup_*` 除外、confirmed-events 境界が崩れていないことを確認する。

2. M3-2 holdout結果を読む
   - `roi-template-nearest` は同分布 leave-one-out 診断、`roi-template-holdout` は `chart_field_templates/` だけを参照する分割診断として読む。
   - `roi-template-holdout` の `play_style` 60/60 match は採用候補検討へ進めやすいが、まだ採用済みテンプレート照合ではない。
   - `difficulty` は `DIFFICULT` テンプレート不足、`level` は多数レベル不足が主因。追加テンプレート素材なしで採用判断へ進めない。
   - `m3_chart_field_template_holdout_diagnostics.md` の mismatch confusion と representative mismatches を確認し、追加テンプレートが必要なラベルを整理する。

3. M3-3 chart-field採用候補の仕様化へ進む
   - `play_style` / `difficulty` / `level` それぞれについて、採用候補にする extractor と採用不可理由を分ける。
   - `filename-baseline`、`roi-feature-nearest-centroid`、`roi-template-nearest`、`roi-template-holdout` の読み分けを維持する。
   - `missing_reference`、`missing_expected_template_reference`、`low_confidence`、`no_expected_value`、`skipped` など、保存前判断へ渡せる failure_reason 語彙を整理する。
   - 追加テンプレート素材が必要な場合も画像はコミットせず、必要ラベルや判断は docs に残す。

4. テスト補強
   - ローカル画像や `metadata.csv` に依存しない小さなテストを優先する。
   - holdout が confirmed-events result ROI を参照に含めないこと、duplicate / rejected_transition / unconfirmed / non-result を除外することを維持する。
   - 数字OCR expected coverage、M3 metadata expected coverage、M3 chart-field inventory / filename extraction / image feature extraction / template nearest / template holdout / diagnostics、review notes を混同しないことをテストまたはdocsで固定する。

## 検証コマンド

最低限:

```powershell
python -m tools.vision_poc --no-ocr
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall tools\vision_poc
python -m pytest tests
```

M3 metadata / chart-field 足場の確認:

```powershell
python -m tools.vision_poc --no-ocr
Get-Content data\vision_poc\result_events_summary.json
Get-Content data\vision_poc\m3_metadata_expected_coverage.md
Get-Content data\vision_poc\m3_chart_fields_summary.json
Get-Content data\vision_poc\m3_chart_field_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_image_feature_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_template_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_template_holdout_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_template_holdout_diagnostics.md
Import-Csv data\vision_poc\m3_chart_field_template_holdout_extraction.csv | Group-Object field_name,status
Import-Csv data\vision_poc\m3_chart_field_template_holdout_extraction.csv | Where-Object { $_.status -eq 'mismatch' } | Group-Object field_name,failure_reason,expected_value,extracted_value | Sort-Object Count -Descending
if (Test-Path samples\screenshots\organized\chart_field_templates) { Get-ChildItem samples\screenshots\organized\chart_field_templates -File | Measure-Object }
```

M2 confirmed-events 回帰も見る場合:

```powershell
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

M3用の新しいCLI、CSV、JSON、Markdownレポート、または出力ディレクトリを追加した場合は、その実行コマンドも `tools/vision_poc/README.md` と最終報告に明記してください。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像はローカル素材扱いでコミットしない。
- `docs/next-task.md` はユーザーから明示されているためコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/` 配下のPoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 分類条件、保存境界、expected coverage、M3 ROIの読み方を変えた場合は、関連する `tools/vision_poc/README.md` または `docs/design/` 更新を同じコミットに含める。
- 作業完了後、コミットがある場合は `codex/vision-poc-ocr-tuning` を push する。

## 完了条件

- 112件で `python -m tools.vision_poc --no-ocr` が全件正解。
- 低スコア、低ランク、0点 result が保存候補から落ちない。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- confirmed-events の保存境界が `confirmed_result=true` かつ `duplicate=false` のまま。
- duplicate / rejected_transition / unconfirmed / non-result が保存直前OCR評価対象外、M3 metadata expected coverage 対象外、M3 chart-field inventory / extraction / diagnostics 対象外のまま。
- `m3_chart_field_template_extraction.csv`、summary、diagnostics が confirmed-events 境界だけを抽出評価対象にする。
- `m3_chart_field_template_holdout_extraction.csv`、summary、diagnostics が confirmed-events 境界だけを抽出評価対象にし、confirmed-events result ROI を参照に含めない。
- confirmed-events result ROI を参照に使う場合、同一フレームを leave-one-out で除外する。
- `roi-template-nearest`、`roi-template-holdout`、各diagnosticsをOCR、マスタ照合、採用済みテンプレート照合の成功扱いにしない。
- `docs/design/07_m3_chart_field_review.md` をローカル期待値レビュー結果として読み、`metadata.csv` 実体や画像はコミットしない。
- テンプレート素材がない環境で `no_template_references` として壊れずに実行できる。
- 追加テンプレート素材を使う場合も、`metadata.csv` の112件分類回帰セットと混同しない。
- `song_title` / `artist` / `play_style` / `difficulty` / `level` は confirmed-events 対象60件で `evaluated` のまま。
- `play_style` / `difficulty` / `level` は M3 chart-field 対象60件で `evaluated` のまま。
- `rank` / `expected_rank` は、数字OCR expected coverage と混同せず、当面は補助/部分評価として扱われている。
- 仕様や判定方針を追加で変えた場合は、関連する `docs/` または `tools/vision_poc/README.md` が更新されている。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
