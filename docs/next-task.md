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
- 最新コミットが今回の `roi-template-nearest` 追加コミット以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- `python -m tools.vision_poc --no-ocr` は Total 112 / Correct 112 / false positives 0 / false negatives 0。
- 低スコア/低ランク result は `result_candidate=true` を維持している。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- `result_events_summary.json` は `confirmed_count=60`、`confirmed_result_count=61`、`duplicate_count=1`、`rejected_transition_count=3`。
- `m3_metadata_expected_coverage.md` は `song_title` / `artist` / `play_style` / `difficulty` / `level` が60/60件で `evaluated`。
- `rank` / `expected_rank` は12/60件のみで `partially_evaluated`。
- `m3_chart_fields_summary.json` は `chart_field_target_count=60`、`excluded_counts={duplicate:1,rejected_transition:3,unconfirmed:12,non_result:36}`。
- `filename-baseline` は target 60件 x 3 field = 180 attempt、match 180、skipped 156。これはファイル名由来であり、ROI/OCR/テンプレート照合/マスタ照合の成功ではない。
- `roi-feature-nearest-centroid` は target 60件 x 3 field = 180 attempt、match 117、mismatch 63、skipped 156。field別では `play_style` 59/60、`difficulty` 41/60、`level` 17/60。
- `m3_chart_field_image_feature_diagnostics.md` は画像特徴baselineの mismatch 診断レポートで、採用根拠ではない。
- 今回、ローカルテンプレート素材 `samples/screenshots/organized/chart_field_templates/` を参照する `roi-template-nearest` を追加した。
- 新出力は `m3_chart_field_template_extraction.csv` と `m3_chart_field_template_extraction_summary.json`。
- `--chart-field-template-root <dir>` でテンプレート参照先を差し替えられる。存在しない場合は `empty_extraction` / `no_template_references` で落とさない。
- テンプレート素材はローカル素材扱いでGit管理しない。通常の112件分類回帰セットにも追加しない。
- 直近ローカル実測では `roi-template-nearest` は template image 29、target 60件 x 3 field = 180 attempt、match 107、mismatch 73、skipped 156。
- `roi-template-nearest` field別では `play_style` 60/60 match、`difficulty` 40/60 match、`level` 7/60 match。
- template value counts は `play_style={DOUBLE:10,SINGLE:19}`、`difficulty={BASIC:10,BEGINNER:9,EXPERT:3,CHALLENGE:7}`。`DIFFICULT` テンプレートはまだない。
- template level counts は `1:3,2:3,3:3,4:3,5:5,7:1,8:1,14:2,15:3,18:2,19:3`。確認対象にある `6,9,10,11,12,13,16,17` などは参照不足。
- 参照不足に起因する mismatch は `failure_reason=missing_expected_template_reference` として、参照ありの `mismatch` と分けて読める。
- `roi-template-nearest` は現時点では `play_style` の強い候補、`difficulty` / `level` の診断用PoCとして扱う。採用済みテンプレート照合やマスタ照合の成功扱いにしない。
- `tools/vision_poc/README.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/06_regression_guard.md` に読み方を追記済み。
- `tests/test_vision_poc_ocr.py` に、テンプレート素材なし環境と confirmed-events 境界を保つテンプレート比較テストを追加済み。
- 直近確認では `python -m tools.vision_poc --no-ocr`、`python -m ruff check tools\vision_poc pyproject.toml tests`、`python -m compileall tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 83 passed。

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
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像コミット
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
   - `samples/screenshots/metadata.csv` がローカルに存在する場合、112行であることを確認する。
   - `samples/screenshots/organized/chart_field_templates/` が存在する場合、113-141の29枚があることを確認する。
   - `python -m tools.vision_poc --no-ocr` を実行し、112件全正解、`transition_countup_*` 除外、confirmed-events 境界が崩れていないことを確認する。
   - `m3_chart_field_template_extraction_summary.json` を読み、`template_value_counts` と `missing_expected_template_reference` の内訳を見る。

2. M3 chart-field template比較の次の最小単位を決める
   - `play_style` は `roi-template-nearest` が 60/60 match なので、採用候補に進める前の副作用確認対象にする。
   - `difficulty` は `DIFFICULT` テンプレートがないため、まず参照不足として読む。DIFFICULT素材を追加するか、既存confirmed-eventsから参照セットを作るかは別判断にする。
   - `EXPERT -> BASIC` など参照あり mismatch は、ROI全体比較が背景や別要素に引っ張られている可能性を代表ROIで見る。
   - `level` は参照不足が多く、素のROI全体比較では 7/60 match に留まる。採用候補にしない。
   - `level` の次案は、数字部分に寄せた二値/エッジ比較、参照テンプレートのカバレッジ拡充、または数字OCR前処理とは分けた軽い形状特徴。
   - 新しい extractor 名を追加する場合は既存 `filename-baseline`、`roi-feature-nearest-centroid`、`roi-template-nearest` を比較用baselineとして維持する。

3. テスト補強
   - ローカル画像や `metadata.csv` に依存しない小さなテストを優先する。
   - confirmed-events 境界だけを対象にすること、duplicate / rejected_transition / unconfirmed / non-result を除外することを維持する。
   - 数字OCR expected coverage、M3 metadata expected coverage、M3 chart-field inventory / filename extraction / image feature extraction / template extraction / diagnostics を混同しないことをテストする。

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
Get-Content data\vision_poc\m3_chart_field_image_feature_diagnostics.md
Get-Content data\vision_poc\m3_chart_field_template_extraction_summary.json
Import-Csv data\vision_poc\m3_chart_field_template_extraction.csv | Group-Object field_name,status
Import-Csv data\vision_poc\m3_chart_field_template_extraction.csv | Where-Object { $_.status -eq 'mismatch' } | Group-Object field_name,failure_reason,expected_value,extracted_value | Sort-Object Count -Descending
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
- `m3_chart_field_template_extraction.csv` と summary が confirmed-events 境界だけを抽出評価対象にする。
- `roi-template-nearest` をOCR、マスタ照合、採用済みテンプレート照合の成功扱いにしない。
- テンプレート素材がない環境で `no_template_references` として壊れずに実行できる。
- 追加テンプレート素材を使う場合も、`metadata.csv` の112件分類回帰セットと混同しない。
- `song_title` / `artist` / `play_style` / `difficulty` / `level` は confirmed-events 対象60件で `evaluated` のまま。
- `play_style` / `difficulty` / `level` は M3 chart-field 対象60件で `evaluated` のまま。
- `rank` / `expected_rank` は、数字OCR expected coverage と混同せず、当面は補助/部分評価として扱われている。
- 仕様や判定方針を追加で変えた場合は、関連する `docs/` または `tools/vision_poc/README.md` が更新されている。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
