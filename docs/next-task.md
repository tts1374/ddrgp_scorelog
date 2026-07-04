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
- 最新コミットが M3-5 保存候補向け集約レポート追加コミット以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- ローカル `samples/screenshots/metadata.csv` は112行、`samples/screenshots/organized/chart_field_templates/` は29枚。
- `python -m tools.vision_poc --no-ocr` は Total 112 / Correct 112 / false positives 0 / false negatives 0。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- `result_events_summary.json` は `confirmed_count=60`、`confirmed_result_count=61`、`duplicate_count=1`、`rejected_transition_count=3`。
- M3-5用に `m3_save_candidate_summary.csv`、`m3_save_candidate_summary.json`、`m3_save_candidate_summary.md` を追加した。
- M3-5集約は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` の60件だけを1行単位で対象にする。
- M3-5集約対象fieldは `song_title`、`artist`、`play_style`、`difficulty`、`level`。
- M3-5 status 語彙は `ready`、`missing_reference`、`ocr_unavailable`、`ocr_failed`、`empty_ocr`、`no_expected_value`、`not_adopted`。
- `python -m tools.vision_poc --no-ocr` 由来の `m3_save_candidate_summary.json` では、`song_title` / `artist` はOCR未実行として各60件 `ocr_unavailable`、`play_style` は60件 `ready`、`difficulty` / `level` は各60件 `missing_reference`。
- `python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist` 由来の `m3_save_candidate_summary.json` では、`song_title` が58件 `ready` / 2件 `empty_ocr`、`artist` が38件 `ready` / 22件 `empty_ocr`、`play_style` が60件 `ready`、`difficulty` / `level` が各60件 `missing_reference`。
- 同実行の `m3_song_artist_ocr_summary.json` は `target_count=60`、`total_attempts=120`、`by_status={ok:120}`、`failure_reason_counts={empty_ocr:24}`。
- M3-5の `ready` はPoC内で次の確認へ渡せる状態であり、DB保存可能、マスタ照合成功、ファジーマッチ成功、曲名正規化成功を意味しない。
- `play_style` の `ready` は M3-3 の `adoption_candidate` を反映するだけで、本番採用済みテンプレート照合ではない。
- `song_title` / `artist` の `ready` は M3-4 OCR入口の観察結果であり、曲名正規化やマスタ照合の成功扱いにしない。
- 直近確認では `python -m tools.vision_poc --no-ocr`、`python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist`、`python -m ruff check tools\vision_poc pyproject.toml tests`、`python -m compileall tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 89 passed。
- `samples/screenshots/metadata.csv`、スクリーンショット画像、`samples/screenshots/organized/chart_field_templates/`、`data/` 配下のPoC出力はGit管理しない。

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
   - `python -m tools.vision_poc --no-ocr` を実行し、112件全正解、`transition_countup_*` 除外、confirmed-events 境界、M3-5集約の `target_count=60` が崩れていないことを確認する。
   - 必要に応じて `python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist` を実行し、M3-4 / M3-5レポートを確認する。

2. M3-5集約レポートを読む
   - `m3_save_candidate_summary.json` と `m3_save_candidate_summary.md` を確認する。
   - `song_title` は主要項目、`artist` は左右切れがある補助項目として読む。
   - `play_style` の `ready` は M3-3採用候補であり、本番採用済みテンプレート照合として扱わない。
   - `difficulty` / `level` の `missing_reference` は、追加テンプレート素材が不足している保存前ブロッカーとして読む。
   - `song_title` / `artist` の `empty_ocr` 代表行を、`m3_song_artist_ocr.md` と `m3_song_artist_ocr_images/` で見る。ただしPoC出力や画像はGit管理しない。
   - OCRエンジン差や日本語辞書有無の影響は、採用判断ではなく観察メモとして扱う。

3. M3-6 保存候補ブロッカーの代表整理へ進む
   - M3-5集約から、保存前に止める理由を field別に代表化する小さなMarkdownまたはsummary追加を検討する。
   - 例: `song_title empty_ocr`、`artist empty_ocr`、`difficulty missing_reference`、`level missing_reference` の代表 `organized_file` と `roi_path` を数件だけ出す。
   - 代表整理はレビュー補助に留め、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進まない。
   - 追加する場合は confirmed-events 境界だけを対象にし、duplicate / rejected_transition / unconfirmed / non-result を対象外に保つ。
   - `data/` 配下の実行結果をコミットせず、必要な読み方だけ `tools/vision_poc/README.md` または `docs/design/` に反映する。

4. テスト補強
   - ローカル画像や `metadata.csv` に依存しない manifest fixture を優先する。
   - confirmed-events 境界だけを対象にし、duplicate / rejected_transition / unconfirmed / non-result を対象外に保つことを固定する。
   - 数字OCR expected coverage、M3 metadata expected coverage、M3 chart-field adoption candidates、M3 song/artist OCR入口、M3 save candidate summary、今後追加する代表整理レポートを混同しないことをテストまたはdocsで固定する。

## 検証コマンド

最低限:

```powershell
python -m tools.vision_poc --no-ocr
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall tools\vision_poc
python -m pytest tests
```

M3-4 / M3-5確認:

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_save_candidate_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_save_candidate_summary.md
Import-Csv data\vision_poc_m3_song_artist\m3_save_candidate_summary.csv | Group-Object song_title_status,artist_status,play_style_status,difficulty_status,level_status
```

M3 chart-field 足場の確認:

```powershell
python -m tools.vision_poc --no-ocr
Get-Content data\vision_poc\result_events_summary.json
Get-Content data\vision_poc\m3_chart_fields_summary.json
Get-Content data\vision_poc\m3_chart_field_template_holdout_extraction_summary.json
Get-Content data\vision_poc\m3_chart_field_adoption_candidates_summary.json
Get-Content data\vision_poc\m3_save_candidate_summary.json
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
- 分類条件、保存境界、expected coverage、M3 ROI、M3 save candidate summary の読み方を変えた場合は、関連する `tools/vision_poc/README.md` または `docs/design/` 更新を同じコミットに含める。
- 作業完了後、コミットがある場合は `codex/vision-poc-ocr-tuning` を push する。

## 完了条件

- 112件で `python -m tools.vision_poc --no-ocr` が全件正解。
- 低スコア、低ランク、0点 result が保存候補から落ちない。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- confirmed-events の保存境界が `confirmed_result=true` かつ `duplicate=false` のまま。
- duplicate / rejected_transition / unconfirmed / non-result が保存直前OCR評価対象外、M3 metadata expected coverage 対象外、M3 chart-field inventory / extraction / diagnostics / adoption candidates 対象外、M3 song/artist OCR対象外、M3 save candidate summary 対象外のまま。
- `m3_chart_field_template_holdout_extraction.csv`、summary、diagnostics が confirmed-events 境界だけを抽出評価対象にし、confirmed-events result ROI を参照に含めない。
- `m3_chart_field_adoption_candidates_summary.json` と `.md` が `play_style` の `adoption_candidate`、`difficulty` / `level` の `needs_template_references`、保存前向け `missing_reference` を読み分ける。
- `m3_song_artist_ocr.csv`、summary、Markdown が confirmed-events 境界だけを対象にし、`pre_normalized_text` を曲名正規化、ファジーマッチ、マスタ照合の成功扱いにしない。
- `m3_save_candidate_summary.csv`、summary、Markdown が confirmed-events 1件を1行にし、`song_title` / `artist` / `play_style` / `difficulty` / `level` を保存前向け状態へ集約する。
- M3-5の `ready` をDB保存可能、マスタ照合成功、ファジーマッチ成功、曲名正規化成功として扱っていない。
- OCRエンジンがない環境で `engine_unavailable` として壊れずに実行できる。
- `roi-template-nearest`、`roi-template-holdout`、各diagnostics、adoption candidatesをOCR、マスタ照合、採用済みテンプレート照合の成功扱いにしない。
- `docs/design/07_m3_chart_field_review.md` をローカル期待値レビュー結果として読み、`metadata.csv` 実体や画像はコミットしない。
- テンプレート素材がない環境で `no_template_references` として壊れずに実行できる。
- `song_title` / `artist` / `play_style` / `difficulty` / `level` は confirmed-events 対象60件で `evaluated` のまま。
- `play_style` / `difficulty` / `level` は M3 chart-field 対象60件で `evaluated` のまま。
- `rank` / `expected_rank` は、数字OCR expected coverage と混同せず、当面は補助/部分評価として扱われている。
- 仕様や判定方針を追加で変えた場合は、関連する `docs/` または `tools/vision_poc/README.md` が更新されている。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
