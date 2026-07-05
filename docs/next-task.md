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

- M5ジャケット特徴量PoCとして `--m5-jacket-match` を追加済み。
- `song_select` grid右上プレビューROIは 1280x720 基準で `(812, 28, 150, 150)`。result側は既存 `jacket` ROIを使う。
- 特徴量は中心正方形から 16x16 RGBサムネイル、RGB 8-bin histogram、8x8 dHashを作る。距離しきい値は `0.24`、曖昧判定deltaは `0.015` の初期観測値。
- `jacket_feature_master.csv`、`jacket_feature_master_summary.json`、`jacket_feature_label_template.csv`、`jacket_match_candidates.csv`、`jacket_match_summary.json`、`jacket_match_report.md` を出力する。
- 2026-07-05のローカル素材追加後、metadataは176行、classificationは176/176全正解。`transition_countup` shape candidates は3件。
- 2026-07-05の直近ジャケットPoC結果は confirmed-events 60件、`jacket_feature_master accepted=58`、`matched=56`、`ambiguous=4`、`not_found=0`、`missing_feature=0`、`insufficient_input=0`。
- 追加キャプチャにより、以前の `not_found` / `missing_feature` は解消済み。`Taking It To The Sky (PLUS step)` と `めうめうぺったんたん！！ (ZAQUVA Remix)` のgrid/result素材もローカルmetadataへ反映済み。
- `result_098_sp_basic_lv07_if_score972200.png` はファイル名とmetadataが `If` になっていたが、実画面表示は `桜 / Reven-G / SINGLE BASIC Lv7` だったため、ローカルmetadataだけ修正済み。画像ファイル名はローカル素材名として残している。
- 残り `ambiguous=4` は、`osaka EVOLVED -毎度、おおきに！- (TYPE1/2/3)` の同一ジャケット3件と、`桜` が追加した `めうめうぺったんたん！！ (ZAQUVA Remix)` featureに近くなった1件。
- `osaka EVOLVED TYPE1/2/3` はジャケット画像だけで一意化しない。title画像特徴量またはtitle OCRで、jacket候補集合内だけを再順位付けする対象にする。
- `桜` の曖昧は、expected candidate distance / rank / margin診断を追加して、正解featureが何位か、近距離候補がどの程度危険かを見る。現時点では `桜` のsong_select grid特徴量は未追加。
- OCRありM5曲名照合の直近結果は confirmed-events 60件、`matched=19`、`not_found=39`、`insufficient_input=2`。`not_found` は `below_score_threshold`、`insufficient_input` は `empty_ocr`。
- OCRなしM5曲名照合は confirmed-events 60件、`insufficient_input=60` / `ocr_not_run=60`。
- `matched` はPoC上の一意候補という意味だけで、DB保存可能、本番採用済み照合、曲ID/譜面ID確定を意味しない。
- 2026-07-05の直近確認では `python -m master --output data\master\ddrgp-master.sqlite` で 1282 songs / 9594 charts を生成できた。
- 直近source hashは `82c0522f51b00fb624b5281addd70d108649d8ca2e8c598b3ea094edffa5d40f`。取得元更新でhashは変わり得るため、件数固定ではなく構造変化検出とDB生成成功を見る。
- 直近コード検証では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 115 passed。
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
- 全曲ジャケット画像取得ツールの実装
- grid内の小ジャケットセル検出
- song_select detail画面を必須にすること
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成
- 生成済み `data/master/ddrgp-master.sqlite` のコミット

M5内でまだ成功扱いにしないもの:

- OCR結果やジャケットPoC結果から曲ID/譜面IDを保存用に確定すること
- ファジーマッチ結果やジャケット `matched` を本番採用済み照合として扱うこと
- 曖昧一致や低確信度をDB保存可能として扱うこと
- `artist` を曲名照合の一意主キーとして扱うこと
- 同一ジャケット候補を画像特徴量だけで無理に一意化すること

## 次に必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- `jacket_match_candidates.csv` に expected song / expected song_id / expected distance / expected rank / top margin を観察できる診断列、または同等の補助CSV/Markdownを追加する。
- `桜` の `ambiguous` について、正解featureの有無、距離、順位、近距離候補との差分を確認し、しきい値問題か特徴量重み問題かを分ける。
- jacketで `ambiguous` になった場合だけ使う title画像特徴量PoCを追加する。まずは result `song_title` ROI と song_select gridのタイトル表示またはresult参照素材を比較対象にする。
- title画像特徴量は候補集合外から曲を拾うためには使わない。`play_style / difficulty / level` と jacket候補集合内の再順位付けだけに使う。
- `osaka EVOLVED TYPE1/2/3` は同一ジャケット候補として残し、title画像特徴量またはtitle OCRで `TYPE1` / `TYPE2` / `TYPE3` を区別できるか確認する。
- `matched`、title画像特徴量による解消候補、OCRによる解消候補はいずれもPoC観測語彙であり、保存可能とは書かない。
- しきい値や特徴量重みを変える場合は、保存可能判定に接続せず、`docs/design/09_master_match_poc.md` と `tools/vision_poc/README.md` も更新する。
- 大きなOCR方式刷新やROI座標定義の大変更には進まない。
- `docs/next-task.md` の更新は、実作業と検証が終わった後の引き継ぎ更新として行う。

## 検証コマンド

最低限:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
python -m tools.vision_poc --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc --no-rois --no-ocr
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_ocr --no-rois
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --m5-jacket-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_jacket --no-rois
Get-Content data\master_match_poc\master_match_summary.json
Get-Content data\master_match_poc_ocr\master_match_summary.json
Get-Content data\master_match_poc_jacket\jacket_feature_master_summary.json
Get-Content data\master_match_poc_jacket\jacket_match_summary.json
Get-Content data\master_match_poc_ocr\m3_song_artist_ocr_entry_failures_summary.json
python -m tools.vision_poc --no-ocr
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
- M5照合境界、正規化方針、候補スコア、ジャケット特徴量方針、`match_status`、`failure_reason` を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M4 DBをM5入力として生成・検査できる。
- M5の入力境界が、confirmed-events由来の保存候補だけを対象にしている。
- M3の `ready` やOCR文字列を、マスタ照合成功として扱っていない。
- M4 DBから曲・譜面候補を読み、`play_style` / `difficulty` / `level` で候補を絞れる。
- 曲名OCR文字列の正規化方針がテストとdocsで説明できる。
- M5 PoCのCSV/summaryで、候補数、最上位候補、上位候補一覧、score、`match_status`、`failure_reason`を確認できる。
- song_select grid右上プレビュー由来のjacket feature masterを `data/` 配下へ生成できる。
- ラベル不足のsong_select grid行をテンプレCSVへ出し、metadata実体を変更していない。
- result confirmed-events のジャケットROIを、chart-fieldで絞った候補song_idのfeatureだけと比較できる。
- jacket matchの `matched` / `ambiguous` / `not_found` / `insufficient_input` / `missing_feature` の意味が保存可否と混同されていない。
- title画像特徴量を追加する場合は、jacket ambiguous候補集合内の再順位付けに限定し、保存可能判定と混同していない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
