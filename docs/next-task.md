# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。

## 推論レベル

high

## 作業ブランチ

`codex/vision-poc-ocr-tuning`

最新確認コミットは、このファイルを含む「M3 chart-field ROI画像特徴diagnostics追加」コミットを想定します。このコミットまでで、既存の `filename-baseline`、ROI画像特徴由来の `roi-feature-nearest-centroid` CSV/summary に加えて、mismatch診断用の `m3_chart_field_image_feature_diagnostics.md` が追加されています。

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/vision-poc-ocr-tuning` であること
- 最新コミットが M3 chart-field ROI画像特徴diagnostics追加以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- `python -m tools.vision_poc --no-ocr` は Total 112 / Correct 112 / false positives 0 / false negatives 0。
- 105の0点D、106のD、110のB、112のB+ などの低スコア/低ランク result は `result_candidate=true` を維持している。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- `result_events_summary.json` は `confirmed_count=60`、`confirmed_result_count=61`、`duplicate_count=1`、`rejected_transition_count=3`。
- `m3_metadata_expected_coverage.md` は `song_title` / `artist` / `play_style` / `difficulty` / `level` が60/60件で `evaluated`。
- `rank` / `expected_rank` は12/60件のみで `partially_evaluated`。`m3_metadata_expected_template.csv` は48行、すべて `missing_fields=rank`。
- `m3_chart_fields_summary.json` は `chart_field_target_count=60`、`excluded_counts={duplicate:1,rejected_transition:3,unconfirmed:12,non_result:36}`。
- `m3_chart_field_extraction.csv` / summary は `filename-baseline` のまま維持している。ローカル `organized_file` 名から `play_style`、`difficulty`、`level` を正規化する比較用baselineで、ROI/OCR/テンプレート照合/マスタ照合の成功ではない。
- `filename-baseline` は target 60件 x 3 field = 180 attempt、match 180、skipped 156。
- `m3_chart_field_image_feature_extraction.csv` / summary は `roi-feature-nearest-centroid`。confirmed-events 対象のROI画像から明度、白/黄/シアン/緑比率、エッジ比率などの特徴を取り、期待値ラベルごとの leave-one-out centroid に最も近い値を出す診断用baseline。
- `roi-feature-nearest-centroid` は target 60件 x 3 field = 180 attempt、match 117、mismatch 63、skipped 156。
- field別では `play_style` 59/60 match、`difficulty` 41/60 match、`level` 17/60 match。
- 新しく `m3_chart_field_image_feature_diagnostics.md` を追加した。field別サマリ、mismatch混同表、代表mismatchとROIパス、読み方メモを出す。
- diagnostics の直近実測では `play_style` の mismatch は `result_047_dp_basic_lv09_score834500_duplicate_01.png` の `DOUBLE -> SINGLE` 1件。
- diagnostics の `difficulty` は `DIFFICULT -> CHALLENGE` が15件、`EXPERT -> CHALLENGE` が3件、`DIFFICULT -> EXPERT` が1件。色特徴だけで分けにくい組み合わせとして読む。
- diagnostics の `level` は混同が広く、17/60 match に留まるため、単純ROI画像特徴baselineを採用候補にしない。次はレベルROIだけを対象にした数字テンプレート比較、またはOCR前処理とは分けた軽い形状特徴が候補。
- `tools/vision_poc/README.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/06_regression_guard.md` に diagnostics の読み方を追記済み。
- `tests/test_vision_poc_ocr.py` で、diagnostics が mismatch混同表、代表ROI、`level` 弱さの読み方を出すことを確認している。
- 直近確認では `python -m tools.vision_poc --no-ocr`、`python -m ruff check tools\vision_poc pyproject.toml tests`、`python -m compileall tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 82 passed。

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
- `docs/adr/0001-foundational-poc-boundaries.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/runner.py`
- `tests/test_vision_poc_classification.py`
- `tests/test_vision_poc_ocr.py`
- `tests/test_vision_poc_result_events.py`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
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
- 曲名OCRの本格精度改善
- artist OCRの本格精度改善
- ランクOCR/ランクテンプレート照合の本格実装
- ファジーマッチ、曲名正規化、候補絞り込み
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

## 後続作業

1. 現状確認
   - `samples/screenshots/metadata.csv` がローカルに存在する場合、112行であること、追加分105-112の organized file が存在することを確認する。
   - `python -m tools.vision_poc --no-ocr` を実行し、112件全正解、低スコア result の保存候補維持、`transition_countup_*` 除外が崩れていないことを確認する。
   - `result_events_summary.json`、`m3_metadata_expected_coverage.md`、`m3_metadata_expected_template.csv`、`m3_chart_fields_summary.json`、`m3_chart_field_extraction_summary.json`、`m3_chart_field_image_feature_extraction_summary.json`、`m3_chart_field_image_feature_diagnostics.md` を読む。
   - 新旧baselineを混同しない。`filename-baseline` はファイル名由来、`roi-feature-nearest-centroid` はROI画像特徴由来の診断用。

2. M3 chart-field 画像由来抽出PoCの次の最小単位を決める
   - `play_style` はまず diagnostics の1件 mismatch と `rois/result_047_dp_basic_lv09_score834500_duplicate_01/play_style.png` を確認する。
   - `difficulty` は diagnostics の混同表を起点に、`DIFFICULT -> CHALLENGE` と `EXPERT -> CHALLENGE` の代表ROIを見て、色特徴だけで分けられる候補と分けられない候補を整理する。
   - `level` は単純ROI特徴を採用候補にしない。次に進むなら、レベルROIだけを対象にした数字テンプレート比較、またはOCR前処理とは分けた軽い形状特徴を小さく追加する。
   - 画像由来 extractor を追加する場合は extractor 名を分け、既存 `filename-baseline` と `roi-feature-nearest-centroid` を比較用baselineとして維持する。
   - 出力は既存の chart-field status 語彙に寄せる。

3. テスト補強
   - ローカル画像や `metadata.csv` に依存しない小さなテストを優先する。
   - confirmed-events 境界だけを対象にすること、duplicate / rejected_transition / unconfirmed / non-result を除外することを維持する。
   - 数字OCR expected coverage、M3 metadata expected coverage、M3 chart-field inventory / filename extraction / image feature extraction / diagnostics を混同しないことをテストする。

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
Get-Content data\vision_poc\m3_metadata_expected_template.csv
Get-Content data\vision_poc\m3_chart_fields_summary.json
Get-Content data\vision_poc\m3_chart_field_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_image_feature_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_image_feature_diagnostics.md
Import-Csv data\vision_poc\m3_chart_fields.csv | Group-Object chart_field_target
Import-Csv data\vision_poc\m3_chart_fields.csv | Group-Object exclusion_reason
Import-Csv data\vision_poc\m3_chart_field_extraction.csv | Group-Object field_name,status
Import-Csv data\vision_poc\m3_chart_field_image_feature_extraction.csv | Group-Object field_name,status
Import-Csv data\vision_poc\m3_chart_field_image_feature_extraction.csv | Where-Object { $_.status -eq 'mismatch' } | Group-Object field_name,expected_value,extracted_value | Sort-Object Count -Descending
```

M2 confirmed-events 回帰も見る場合:

```powershell
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

M3用の新しいCLI、CSV、JSON、Markdownレポート、または出力ディレクトリを追加した場合は、その実行コマンドも `tools/vision_poc/README.md` と最終報告に明記してください。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
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
- M3用ROIは、少なくとも `play_style`、`difficulty`、`level`、`rank`、`song_title`、`artist` を目視確認できる形で `rois/` に出力できる。
- `m3_metadata_expected_coverage.md` と `m3_metadata_expected_template.csv` が数字OCR expected coverage と別の読み方として維持されている。
- `m3_chart_fields.csv` と `m3_chart_fields_summary.json` が数字OCR expected coverage、曲名OCR、artist OCR、rank OCR、テンプレート照合、マスタ照合の成功扱いになっていない。
- `m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` が `filename-baseline` をROI/OCR/テンプレート照合/マスタ照合の成功扱いにしていない。
- `m3_chart_field_image_feature_extraction.csv` と `m3_chart_field_image_feature_extraction_summary.json` が `roi-feature-nearest-centroid` をOCR/テンプレート照合/マスタ照合の成功扱いにしていない。
- `m3_chart_field_image_feature_diagnostics.md` が mismatch の診断レポートであり、OCR/テンプレート照合/マスタ照合の成功扱いになっていない。
- `song_title` / `artist` / `play_style` / `difficulty` / `level` は confirmed-events 対象60件で `evaluated` のまま。
- `play_style` / `difficulty` / `level` は M3 chart-field 対象60件で `evaluated` のまま。
- `rank` / `expected_rank` は、数字OCR expected coverage と混同せず、当面は補助/部分評価として扱われている。
- 仕様や判定方針を追加で変えた場合は、関連する `docs/` または `tools/vision_poc/README.md` が更新されている。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
