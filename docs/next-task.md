# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。

## 推論レベル

high

## 作業ブランチ

`codex/vision-poc-ocr-tuning`

最新確認コミットは、このファイルを含む `Add M3 chart-field extraction baseline` コミットを想定します。このコミットまでで、M3 chart-field 評価の足場に `m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` が追加されています。

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/vision-poc-ocr-tuning` であること
- 最新コミットが `Add M3 chart-field extraction baseline` 以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- `tools/vision_poc/runner.py` の `result_candidate` は、低スコア/低ランクで score/rank 色特徴が弱くても、`RESULTS` ヘッダー + 詳細リザルト枠を基準に保存候補へ残す。
- 105の0点D、106のD、110のB、112のB+ などの低スコア/低ランク result は `result_candidate=true` を維持している。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま保存不可候補として扱う。
- `PRIMARY_ROIS` は、M3入口の目視確認用に `play_style`、`difficulty`、`level`、`rank`、`song_title`、`artist` を `data/vision_poc/rois/<画像名>/` へ出力する。
- `artist` ROI は補助ROI。短い名前は読めるが、長い名前は左右が切れる場合がある。M3入口ではROI座標の大変更に進まない。
- M3 metadata expected coverage の対象は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` のみ。
- `m3_metadata_expected_coverage.md` と `m3_metadata_expected_template.csv` は、数字OCR expected coverage と別の期待値充足レポートとして維持されている。
- `m3_chart_fields.csv` と `m3_chart_fields_summary.json` は全イベント行を出し、`play_style`、`difficulty`、`level` の chart-field 対象一覧を confirmed-events 境界だけに限定する。
- duplicate、`event_type=rejected_transition`、未確定 `result_candidate`、non-result は M3 metadata expected coverage / M3 chart-field inventory / M3 chart-field extraction の対象外。
- 今回 `m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` を追加した。
- 現行 extractor は `filename-baseline`。`organized_file` の `sp_basic_lv06` のような命名から `play_style`、`difficulty`、`level` を正規化して取り出す初期baselineであり、ROI画像特徴、OCR、テンプレート照合、マスタ照合の成功ではない。
- `play_style` は `SP`/`SINGLE` を `SINGLE`、`DP`/`DOUBLE` を `DOUBLE` に正規化する。`difficulty` は大文字の有限候補、`level` は先頭ゼロなしの数値文字列に正規化する。
- `m3_chart_field_extraction.csv` はイベント × field の行を出し、`expected_value`、`extracted_value`、`match`、`status`、`failure_reason`、`roi_path` を持つ。
- 対象外イベントは `status=skipped` とし、`failure_reason` で `duplicate`、`rejected_transition`、`unconfirmed`、`non_result` を区別する。
- 対象イベントで期待値がない場合は `no_expected_value`、抽出できない場合は `empty_extraction`、不一致は `mismatch` として扱う。
- ローカル `samples/screenshots/metadata.csv` は112行。追加分105-112の organized file は存在確認済み。
- Git管理対象外ファイルである `samples/screenshots/metadata.csv` はコミットしない。直近の期待値追記もローカル真値整備として扱う。
- 現在のローカル metadata では confirmed-events 対象は60件。
- M3 metadata expected coverage は `song_title` / `artist` / `play_style` / `difficulty` / `level` が60/60件で `evaluated`。
- `rank` / `expected_rank` は12/60件が埋まっており、残り48件は rank のみ不足。これは数字OCR expected coverage とは別扱い。
- `m3_metadata_expected_template.csv` は48行で、いずれも `missing_fields=rank` のみ。duplicate、`rejected_transition`、未確定候補、non-result の混入はない。
- rank はM3入口では当面補助ROIの部分評価として扱う。残り48件の `expected_rank` を無理に埋める作業には進まない。
- 直近確認では `python -m tools.vision_poc --no-ocr` が Total 112 / Correct 112 / false positives 0 / false negatives 0。`transition_countup_*` は shape 3件、result_candidate 0件。
- `data/vision_poc/result_events_summary.json` は `confirmed_count=60`、`confirmed_result_count=61`、`duplicate_count=1`、`rejected_transition_count=3`。
- `data/vision_poc/m3_chart_fields_summary.json` は `chart_field_target_count=60`、`excluded_counts={duplicate:1,rejected_transition:3,unconfirmed:12,non_result:36}`。
- `data/vision_poc/m3_chart_field_extraction_summary.json` は `extractor=filename-baseline`、`chart_field_target_count=60`、`total_attempts=180`、`match=180`、`skipped=156`。
- `tools/vision_poc/README.md`、`docs/design/03_event_and_save_boundary.md`、`docs/design/06_regression_guard.md` に M3 chart-field extraction baseline の読み方を追記済み。
- `tests/test_vision_poc_ocr.py` で、chart-field extraction が confirmed-events 境界だけを対象にし、duplicate / rejected_transition / unconfirmed を skipped にすることを確認している。
- 直近確認では `python -m ruff check tools\vision_poc pyproject.toml tests`、`python -m compileall tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 80 passed。

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
   - `data/vision_poc/result_events_summary.json` で `confirmed_count=60`、`confirmed_result_count=61`、`duplicate_count=1`、`rejected_transition_count=3` を確認する。
   - `data/vision_poc/m3_metadata_expected_coverage.md` と `data/vision_poc/m3_metadata_expected_template.csv` を読み、5項目60/60 evaluated、rank 12/60 partially_evaluated、template 48行すべて `missing_fields=rank` であることを確認する。
   - `data/vision_poc/m3_chart_fields_summary.json` を読み、chart-field 対象が confirmed-events 60件だけであること、対象外理由が `duplicate` / `rejected_transition` / `unconfirmed` / `non_result` に分かれていることを確認する。
   - `data/vision_poc/m3_chart_field_extraction_summary.json` を読み、`filename-baseline` が target 60件 × 3 field = 180 attempt で match 180、skipped 156 であることを確認する。

2. M3 chart-field 抽出PoCの次の最小単位を決める
   - 次の実装対象は `play_style`、`difficulty`、`level` のROI画像特徴または軽いテンプレート比較に限定する。
   - 既存の `filename-baseline` は比較用baselineとして維持し、画像由来 extractor を追加する場合は extractor 名を分ける。
   - `play_style` は `SINGLE` / `DOUBLE`、`difficulty` は有限候補、`level` は1-19の小さな値として扱える入口を検討する。
   - `rank` は補助/部分評価として維持し、ランクOCRやランクテンプレート照合には進まない。
   - `song_title` と `artist` は当面、ROI切り出しと期待値カバレッジの確認に留める。曲名OCR、正規化、マスタ候補絞り込みには進まない。

3. 軽い画像由来 chart-field 抽出評価の候補
   - まずは `play_style`、`difficulty`、`level` の期待値とROI画像パスがそろっている confirmed-events だけを対象にする。
   - 画像特徴を試す場合も、出力は既存の `m3_chart_field_extraction.csv` / summary と同じ status 語彙に寄せる。
   - テンプレート比較や色特徴を試す場合は、ローカル画像に依存しない単体テストで境界や集計を先に固定する。
   - 画像特徴を実装する場合でも、ROI座標定義の大変更やOCR方式刷新には進まない。
   - 出力先は `data/vision_poc/` 配下に限定し、生成物はGit管理しない。

4. テスト補強
   - ローカル画像や `metadata.csv` に依存しない小さなテストを優先する。
   - confirmed-events 境界だけを対象にすること、duplicate / rejected_transition / unconfirmed / non-result を除外することをテストする。
   - 数字OCR expected coverage、M3 metadata expected coverage、M3 chart-field inventory / extraction を混同しないことをテストする。
   - 既存の `test_m3_metadata_expected_report_uses_confirmed_events_boundary` で足りる範囲は増やしすぎない。

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
Import-Csv data\vision_poc\m3_chart_fields.csv | Group-Object chart_field_target
Import-Csv data\vision_poc\m3_chart_fields.csv | Group-Object exclusion_reason
Import-Csv data\vision_poc\m3_chart_field_extraction.csv | Group-Object field_name,status
```

M2 confirmed-events 回帰も見る場合:

```powershell
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

M3用の新しいCLI、CSV、JSON、Markdownレポート、または出力ディレクトリを追加した場合は、その実行コマンドも `tools/vision_poc/README.md` と最終報告に明記してください。

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `docs/next-task.md` は今回ユーザーから明示されているためコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/` 配下のPoC出力、ROI画像、OCR画像、解析ログはステージしない。
- 分類条件、保存境界、expected coverage、M3 ROIの読み方を変えた場合は、関連する `tools/vision_poc/README.md` または `docs/design/` 更新を同じコミットに含める。
- 作業完了後、コミットがある場合は `codex/vision-poc-ocr-tuning` を push する。

## 完了条件

- 112件で `python -m tools.vision_poc --no-ocr` が全件正解。
- 低スコア、低ランク、0点 result が保存候補から落ちない。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- confirmed-events の保存境界が `confirmed_result=true` かつ `duplicate=false` のまま。
- duplicate / rejected_transition / unconfirmed / non-result が保存直前OCR評価対象外、M3 metadata expected coverage 対象外、M3 chart-field inventory / extraction 対象外のまま。
- M3用ROIは、少なくとも `play_style`、`difficulty`、`level`、`rank`、`song_title`、`artist` を目視確認できる形で `rois/` に出力できる。
- `artist` ROI は補助ROIとして扱い、長いアーティスト名の左右切れを理由にROI座標定義の大変更へ進んでいない。
- `m3_metadata_expected_coverage.md` と `m3_metadata_expected_template.csv` が数字OCR expected coverage と別の読み方として維持されている。
- `m3_chart_fields.csv` と `m3_chart_fields_summary.json` が数字OCR expected coverage、曲名OCR、artist OCR、rank OCR、テンプレート照合、マスタ照合の成功扱いになっていない。
- `m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` が confirmed-events 境界だけを抽出評価対象にし、`filename-baseline` をROI/OCR/テンプレート照合/マスタ照合の成功扱いにしていない。
- `song_title` / `artist` / `play_style` / `difficulty` / `level` は confirmed-events 対象60件で `evaluated` のまま。
- `play_style` / `difficulty` / `level` は M3 chart-field 対象60件で `evaluated` のまま。
- `rank` / `expected_rank` は、数字OCR expected coverage と混同せず、当面は補助/部分評価として扱われている。
- 仕様や判定方針を追加で変えた場合は、関連する `docs/` または `tools/vision_poc/README.md` が更新されている。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
