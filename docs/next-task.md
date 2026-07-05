# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。`docs/next-task.md` は次チャット用の引き継ぎ仕様として扱い、実装・検証が終わった後に更新してください。

## 推論レベル

high

## 作業ブランチ

`codex/m5-master-match-poc`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m5-master-match-poc` であること

## 今回までの作業結果

- M4 DBはM5照合PoCの入力として使ってよい。2026-07-05の直近確認では `python -m master --output data\master\ddrgp-master.sqlite` で 1282 songs / 9594 charts を生成できた。
- 直近source hashは `49fd88ea80c4a19b6e1c194c5fae30091ed932ba6ceac7b8464770522245c137`。前回メモのhashから変化したが、件数、snapshot数、metadata/snapshot hash整合、parser versionは `python -m master.inspect` で正常確認済み。取得元更新でhashは変わり得るため、件数固定ではなく構造変化検出とDB生成成功を見る。
- M5の軽量改善として、曲名OCR文字列にartistや余分な記号が後続混入するケース向けに、正規化後のマスタ曲名が5文字以上でOCR正規化文字列に含まれる場合だけ包含一致として類似度を最大扱いにした。短いOCR断片がマスタ曲名に含まれるだけでは最大扱いにしない。
- 曲名正規化では従来のNFKC、casefold、空白除去、代表的な句読点除去に加え、曲名OCRで出やすい curly quote 系の `‘’“”` を除去対象にした。
- `master_match_candidates.csv` に `top_candidates` を追加し、上位5候補を `score:title / artist [chart_id]` 形式で観察できるようにした。これは失敗代表の観察用であり、保存可能判定ではない。
- fixtureテストに、artist suffix混入、包含一致の過剰boost防止、`top_candidates` 出力を追加済み。
- OCRありM5の直近結果は confirmed-events 60件、classification 112/112、`matched=19`、`not_found=39`、`insufficient_input=2`。`not_found` はすべて `below_score_threshold`、`insufficient_input` は `empty_ocr`。
- OCRなしM5の直近結果は confirmed-events 60件、classification 112/112、`insufficient_input=60` / `ocr_not_run=60`。
- 直近検証では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 110 passed。
- 生成DB、PoC出力、OCR画像、`metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはGit管理しない。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/08_master_db_generation.md`
- `docs/design/09_master_match_poc.md`
- `docs/design/07_m3_chart_field_review.md`
- `tools/vision_poc/README.md`
- `tools/vision_poc/master_match.py`
- `tools/vision_poc/runner.py`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`
- `tests/test_master_match.py`

M3評価レポートや画像PoCの境界へ触る場合だけ追加で読む資料:

- `docs/design/00_glossary.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/06_regression_guard.md`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- 個人スコアDB保存、保存可否判定本番仕様、低確信度ログ本番仕様
- OCR方式の大幅刷新、ROI座標定義の大変更
- duplicate key の本格実装差し替え
- ジャケット特徴量照合の実装
- 全曲ジャケット画像取得ツールの実装
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成
- 生成済み `data/master/ddrgp-master.sqlite` のコミット

M5内でまだ成功扱いにしないもの:

- OCR結果から曲ID/譜面IDを保存用に確定すること
- ファジーマッチ結果を本番採用済み照合として扱うこと
- 曖昧一致や低確信度をDB保存可能として扱うこと
- `artist` を曲名照合の一意主キーとして扱うこと

## 次に必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- OCRありM5出力 `data\master_match_poc_ocr\master_match_candidates.csv` を再生成し、残った `below_score_threshold=39` を `top_candidates` と合わせて代表確認する。
- 推奨候補は、記号・装飾文字差の軽量正規化、先頭ゴミ/末尾ゴミの観察、`below_score_threshold` の理由細分化、しきい値前後の代表レポート、または追加fixtureテスト。
- 例として残件には `BREVK DOWN!` vs `BRE∀K DOWN！`、`CRAZYYLOVE` vs `CRAZY♥LOVE`、`RËVOLUTIФN`、`Lachryma《Re:Queen’M》`、日本語タイトルのOCR崩れがある。まずは標準ライブラリベースの小さい正規化・観察強化に留める。
- 大きなOCR方式刷新やROI座標変更には進まない。
- `matched` はPoC上の一意候補という意味に限定し、保存可能とは書かない。
- `docs/next-task.md` の更新は、実作業と検証が終わった後の引き継ぎ更新として行う。

## 検証コマンド

最低限:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
python -m tools.vision_poc --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc --no-rois --no-ocr
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_ocr --no-rois
Get-Content data\master_match_poc\master_match_summary.json
Get-Content data\master_match_poc_ocr\master_match_summary.json
Get-Content data\master_match_poc_ocr\m3_song_artist_ocr_entry_failures_summary.json
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
```

M4 DB確認:

```powershell
python -m pytest tests\test_master_builder.py
python -m master --output data\master\ddrgp-master.sqlite
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
@'
import sqlite3
with sqlite3.connect("data/master/ddrgp-master.sqlite") as con:
    print(con.execute("select count(*) from songs").fetchone()[0])
    print(con.execute("select count(*) from charts").fetchone()[0])
    print(dict(con.execute("select key, value from master_metadata")))
'@ | python -
```

画像PoCやM3境界を触った場合の回帰:

```powershell
python -m tools.vision_poc --no-ocr
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_entry_failures_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_save_candidate_summary.json
```

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像はローカル素材扱いでコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- M5照合境界、正規化方針、候補スコア、`match_status`、`failure_reason` を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M4 DBをM5入力として生成・検査できる。
- M5の入力境界が、confirmed-events由来の保存候補だけを対象にしている。
- M3の `ready` やOCR文字列を、マスタ照合成功として扱っていない。
- M4 DBから曲・譜面候補を読み、`play_style` / `difficulty` / `level` で候補を絞れる。
- 曲名OCR文字列の正規化方針がテストとdocsで説明できる。
- M5 PoCのCSV/summaryで、候補数、最上位候補、上位候補一覧、score、`match_status`、`failure_reason`を確認できる。
- `matched` / `ambiguous` / `not_found` / `insufficient_input` の意味が保存可否と混同されていない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が112件全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
