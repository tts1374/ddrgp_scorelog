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
- 最新コミットが M3-9 song/artist OCR入口失敗代表整理コミット以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- ローカル `samples/screenshots/metadata.csv` は112行、`samples/screenshots/organized/chart_field_templates/` は37枚。
- `python -m tools.vision_poc --no-ocr` は Total 112 / Correct 112 / false positives 0 / false negatives 0。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- confirmed-events 境界は `confirmed_result=true` かつ `duplicate=false` の60件。
- `m3_chart_field_adoption_candidates_summary.json` では `play_style`、`difficulty`、`level` がすべて 60/60 match、`adoption_candidate`。
- M3-5集約では `play_style`、`difficulty`、`level` が60件すべて `ready`。
- `ready` はPoC内で次確認へ渡せる状態であり、本番採用済みテンプレート照合、DB保存、マスタ照合、ファジーマッチ、曲名正規化の成功ではない。
- 今回、M3-9として `m3_song_artist_ocr_entry_failures_summary.json` と `m3_song_artist_ocr_entry_failures.md` を追加した。
- M3-9レポートは M3-4 OCR入口行から `engine_unavailable`、`ocr_failed`、`empty_ocr` だけを入口失敗代表として集約する。
- `no_expected_value` は期待値整備の問題として、M3-9の入口失敗代表には含めない。
- OCRあり実行では `song_title empty_ocr=2`、`artist empty_ocr=22`。
- M3-9新レポートでは entry failures 24、affected confirmed-events 22。
- `song_title` は `primary_song_identifier`、`artist` は `auxiliary_clipped_reference` として別々に読む。
- `song_title` の代表は `8000000` と `天弓ノ舞`。
- `artist` の代表は `TAG`、`DKC Crew`、`Ryu☆`。
- `song_title` と `artist` を同じ改善対象として混ぜない。
- M3-9整理はOCR入口の観察であり、曲名正規化、ファジーマッチ、マスタ照合、DB保存可否判定の成功/失敗ではない。
- テンプレート画像、OCR画像、PoC出力、`metadata.csv`、`data/`、`logs/`、ローカルDBはGit管理しない。
- 直近確認では `python -m tools.vision_poc --no-ocr`、`python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist`、`python -m ruff check tools\vision_poc pyproject.toml tests`、`python -m compileall tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 90 passed。

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
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- DB保存、保存可否判定本番仕様、低確信度ログ本番仕様
- OCR方式の大幅刷新、ROI座標定義の大変更
- duplicate key の本格実装差し替え
- マスタDB生成、マスタ照合、ファジーマッチ、曲名正規化、候補絞り込み
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成

## 後続作業

1. 現状確認
   - `git status --short --branch` と `git log --oneline -5` を確認する。
   - ローカル素材がある場合、`metadata.csv` が112行、`chart_field_templates/` が37枚であることを確認する。
   - `python -m tools.vision_poc --no-ocr` を実行し、112件全正解、`transition_countup_*` 除外、confirmed-events 60件、chart-field 3項目 ready を確認する。

2. M3-9結果を読む
   - `python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist` を実行する。
   - `m3_song_artist_ocr_summary.json` と `m3_song_artist_ocr_entry_failures_summary.json` / `.md` を読む。
   - `song_title empty_ocr=2` と `artist empty_ocr=22` が役割別に分離されていることを確認する。
   - `m3_save_candidate_summary.json`、`m3_save_candidate_blockers_summary.json`、`m3_save_candidate_blocker_resolution_plan.json` も読み、chart-field 3項目 ready と song/artist OCR入口失敗を混同していないことを確認する。

3. M3-10候補: song_title OCR入口の小さな前処理検討
   - まず `song_title` の2代表だけを主要項目の入口失敗として見る。
   - 代表ROIや `m3_song_artist_ocr_images/*/song_title_*` を目視する場合も、画像や `data/` 出力はGit管理しない。
   - 前処理を試す場合は `song_title` に限定した小さな変更にし、`artist` 補助項目の左右切れ改善と混ぜない。
   - OCR方式刷新、ROI座標定義の大変更、曲名正規化、ファジーマッチ、マスタ照合へ進まない。
   - 改善候補を入れる場合は、M3-9レポートが `song_title` / `artist` を分けて読み続けられることをテストで固定する。

4. テスト補強
   - ローカル画像や `metadata.csv` に依存しない manifest fixture / synthetic rows を優先する。
   - M3 song/artist OCR入口とM3-9入口失敗代表が confirmed-events 境界だけを対象にし、duplicate / rejected_transition / unconfirmed / non-result を対象外に保つことを固定する。
   - M3 song/artist OCR入口、M3 song/artist OCR entry failures、M3 chart-field adoption candidates、M3 save candidate summary、M3-6 blocker representatives、M3-7 resolution plan、数字OCR expected coverage を混同しないことをテストまたはdocsで固定する。

## 検証コマンド

最低限:

```powershell
python -m tools.vision_poc --no-ocr
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall tools\vision_poc
python -m pytest tests
```

M3-9確認:

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_entry_failures_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_entry_failures.md
Get-Content data\vision_poc_m3_song_artist\m3_save_candidate_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_save_candidate_blocker_resolution_plan.json
```

M3 chart-field 足場の確認:

```powershell
python -m tools.vision_poc --no-ocr
Get-Content data\vision_poc\result_events_summary.json
Get-Content data\vision_poc\m3_chart_field_adoption_candidates_summary.json
Get-Content data\vision_poc\m3_save_candidate_summary.json
Get-Content data\vision_poc\m3_save_candidate_blockers_summary.json
Get-Content data\vision_poc\m3_save_candidate_blocker_resolution_plan.json
if (Test-Path samples\screenshots\organized\chart_field_templates) { Get-ChildItem samples\screenshots\organized\chart_field_templates -File | Measure-Object }
```

M2 confirmed-events 回帰も見る場合:

```powershell
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data\vision_poc_timestamped\frame_manifest.csv --frame-root samples\screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像はローカル素材扱いでコミットしない。
- `docs/next-task.md` はユーザーから明示されているためコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/` 配下のPoC出力、ROI画像、OCR画像、解析ログはステージしない。
- M3出力や読み方を変えた場合は、関連する `tools/vision_poc/README.md` または `docs/design/` 更新を同じコミットに含める。
- 作業完了後、コミットがある場合は `codex/vision-poc-ocr-tuning` を push する。

## 完了条件

- 112件で `python -m tools.vision_poc --no-ocr` が全件正解。
- 低スコア、低ランク、0点 result が保存候補から落ちない。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- confirmed-events の保存境界が `confirmed_result=true` かつ `duplicate=false` のまま。
- duplicate / rejected_transition / unconfirmed / non-result が、M3 metadata expected coverage、M3 chart-field、M3 song/artist OCR、M3 song/artist OCR entry failures、M3 save candidate summary、M3-6 blocker representatives、M3-7 resolution plan の対象外のまま。
- `play_style` / `difficulty` / `level` の `ready` をDB保存可能、採用済みテンプレート照合成功、マスタ照合成功、ファジーマッチ成功、曲名正規化成功として扱っていない。
- `song_title` と `artist` のOCR入口失敗代表を別々に読み、同じ改善対象として混ぜていない。
- M3の残りをOCR入口代表失敗として整理し、曲名正規化、ファジーマッチ、マスタ照合、DB保存可否判定へ進んでいない。
- テンプレート素材がない環境で `no_template_references` として壊れずに実行できる。
- OCRエンジンがない環境で `engine_unavailable` として壊れずに実行できる。
- `docs/design/07_m3_chart_field_review.md` をローカル期待値レビュー結果として読み、`metadata.csv` 実体や画像はコミットしない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
