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
- 最新コミットが M3-3 chart-field adoption candidates 追加コミット以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- ローカル `samples/screenshots/metadata.csv` は112行、`samples/screenshots/organized/chart_field_templates/` は29枚で確認した。
- `python -m tools.vision_poc --no-ocr` は Total 112 / Correct 112 / false positives 0 / false negatives 0。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- `result_events_summary.json` は `confirmed_count=60`、`confirmed_result_count=61`、`duplicate_count=1`、`rejected_transition_count=3`。
- `m3_chart_fields_summary.json` は `chart_field_target_count=60`、`excluded_counts={duplicate:1,rejected_transition:3,unconfirmed:12,non_result:36}`。
- `roi-template-nearest` は同分布 leave-one-out 診断として 180/180 match。OCR、採用済みテンプレート照合、マスタ照合の成功扱いにしない。
- `roi-template-holdout` は参照を `chart_field_templates/` だけに限定し、confirmed-events result ROI を評価専用にする分割診断。実測は 180 attempts / 110 match / 70 mismatch / 156 skipped。
- 今回、M3-3用に `m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` を追加した。
- 採用候補レビューでは `play_style` が `adoption_readiness=adoption_candidate`、`recommended_extractor=roi-template-holdout`、60/60 match。
- `difficulty` は 43/60 match、17 mismatch。`DIFFICULT` テンプレート不足により `needs_template_references`、保存前判断向け語彙では `missing_reference`。
- `level` は 7/60 match、53 mismatch。不足参照値は 6/9/10/11/12/13/16/17 で、`needs_template_references`、保存前判断向け語彙では `missing_reference`。
- M3 chart-field の保存前判断向け failure reason は、参照不足を `missing_reference`、期待値不足を `no_expected_value`、抽出空を `empty_extraction`、参照あり不一致を `low_confidence` として読む。
- `docs/design/03_event_and_save_boundary.md`、`docs/design/06_regression_guard.md`、`docs/design/07_m3_chart_field_review.md`、`docs/implementation-roadmap.md`、`tools/vision_poc/README.md` にM3-3 adoption candidatesの読み方を追加した。
- `tests/test_vision_poc_ocr.py` に、holdout由来の採用候補と保存前向け failure reason 語彙のテストを追加した。
- `samples/screenshots/metadata.csv`、スクリーンショット画像、`samples/screenshots/organized/chart_field_templates/`、`data/` 配下のPoC出力はGit管理しない。
- 直近確認では `python -m tools.vision_poc --no-ocr`、`python -m ruff check tools\vision_poc pyproject.toml tests`、`python -m compileall tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 86 passed。

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
- ファジーマッチ、曲名正規化、候補絞り込み
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

## 後続作業

1. 現状確認
   - `git status --short --branch` と `git log --oneline -5` を確認する。
   - ローカル素材がある場合、`metadata.csv` が112行、`chart_field_templates/` が29枚であることを確認する。
   - `python -m tools.vision_poc --no-ocr` を実行し、112件全正解、`transition_countup_*` 除外、confirmed-events 境界が崩れていないことを確認する。

2. M3-3 adoption candidates を読む
   - `m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` を確認する。
   - `play_style` は `roi-template-holdout` の採用候補として読めるが、本番採用済みテンプレート照合ではない。
   - `difficulty` と `level` は `needs_template_references` のまま。追加テンプレート素材なしで採用候補へ進めない。
   - 追加テンプレート素材が必要な場合も、画像はコミットせず、必要ラベルと判断だけを docs に残す。

3. M3-4 曲名・artist ROIの入口へ進む
   - `song_title` / `artist` の confirmed-events 対象だけを扱う。
   - まずはOCR生文字列、正規化前文字列、engine/status/error、期待値、ROIパス、failure_reason を出す小さなレポートを検討する。
   - `song_title` は主要項目、`artist` は左右切れがある補助項目として読む。
   - 長い曲名、日本語、記号、2行表示、artist切れを代表ケースとして扱う。
   - マスタ照合、ファジーマッチ、曲名正規化の本格実装には進まない。
   - OCRエンジンがない環境では落とさず、`engine_unavailable` または同等の failure_reason として記録する。

4. テスト補強
   - ローカル画像や `metadata.csv` に依存しない小さなテストを優先する。
   - confirmed-events 境界だけを対象にし、duplicate / rejected_transition / unconfirmed / non-result を対象外に保つことを固定する。
   - 数字OCR expected coverage、M3 metadata expected coverage、M3 chart-field adoption candidates、曲名/artist OCR入口を混同しないことをテストまたはdocsで固定する。

## 検証コマンド

最低限:

```powershell
python -m tools.vision_poc --no-ocr
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall tools\vision_poc
python -m pytest tests
```

M3 chart-field 足場の確認:

```powershell
python -m tools.vision_poc --no-ocr
Get-Content data\vision_poc\result_events_summary.json
Get-Content data\vision_poc\m3_chart_fields_summary.json
Get-Content data\vision_poc\m3_chart_field_template_holdout_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_adoption_candidates_summary.json
Get-Content data\vision_poc\m3_chart_field_adoption_candidates.md
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
- duplicate / rejected_transition / unconfirmed / non-result が保存直前OCR評価対象外、M3 metadata expected coverage 対象外、M3 chart-field inventory / extraction / diagnostics / adoption candidates 対象外のまま。
- `m3_chart_field_template_holdout_extraction.csv`、summary、diagnostics が confirmed-events 境界だけを抽出評価対象にし、confirmed-events result ROI を参照に含めない。
- `m3_chart_field_adoption_candidates_summary.json` と `.md` が `play_style` の `adoption_candidate`、`difficulty` / `level` の `needs_template_references`、保存前向け `missing_reference` を読み分ける。
- `roi-template-nearest`、`roi-template-holdout`、各diagnostics、adoption candidatesをOCR、マスタ照合、採用済みテンプレート照合の成功扱いにしない。
- `docs/design/07_m3_chart_field_review.md` をローカル期待値レビュー結果として読み、`metadata.csv` 実体や画像はコミットしない。
- テンプレート素材がない環境で `no_template_references` として壊れずに実行できる。
- `song_title` / `artist` / `play_style` / `difficulty` / `level` は confirmed-events 対象60件で `evaluated` のまま。
- `play_style` / `difficulty` / `level` は M3 chart-field 対象60件で `evaluated` のまま。
- `rank` / `expected_rank` は、数字OCR expected coverage と混同せず、当面は補助/部分評価として扱われている。
- M3-4で曲名/artist OCR入口を追加する場合、confirmed-events 境界、expected coverage、chart-field adoption candidates と混同しない。
- 仕様や判定方針を追加で変えた場合は、関連する `docs/` または `tools/vision_poc/README.md` が更新されている。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
