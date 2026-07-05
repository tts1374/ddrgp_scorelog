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
- `jacket_match_candidates.csv` には、ローカル期待値診断列 `expected_song_title` / `expected_song_id` / `expected_jacket_distance` / `expected_jacket_rank` / `jacket_top_margin` を出す。
- jacketで `ambiguous` になった候補集合内だけを対象にする title画像特徴量PoCを追加済み。
  - result `song_title` ROIを横長の濃淡サムネイル、エッジサムネイル、右側サフィックス寄りの濃淡/エッジサムネイル、dHashに変換する。
  - 参照はローカルmetadataの期待曲名を M4 `songs.title` へ一意解決できた result 素材から作る。
  - 比較時は同じ `organized_file` の参照を除外する。
  - `title_rerank_status=resolved_candidate` はPoC観測語彙であり、保存可能、曲ID/譜面ID確定、`jacket_match_status=matched` への昇格を意味しない。
- 今回、jacketで `ambiguous` になった候補集合内だけを対象にする title OCR suffix 診断を追加した。
  - `--m3-song-artist-ocr` の result `song_title` OCR文字列から `TYPE1` / `TYPE2` / `TYPE3` suffixだけを観測する。
  - 出力列は `title_ocr_raw`、`title_ocr_text`、`title_ocr_suffix`、`title_ocr_top_*`、`title_ocr_top_candidates`、`title_ocr_rerank_status`、`title_ocr_rerank_reason`。
  - `title_ocr_rerank_status=resolved_candidate` はPoC観測語彙であり、保存可能、曲ID/譜面ID確定、`jacket_match_status=matched` への昇格を意味しない。
  - suffixが候補集合外にありそうでも候補集合外から曲を拾わず、`no_candidate_suffix_match` として観測する。
- 2026-07-05の今回ローカル確認では metadata は178行、classification は178/178全正解。`transition_countup` shape candidates は3件。
- 今回の `--m5-jacket-match` 結果は confirmed-events 60件、`jacket_feature_master accepted=59`、`matched=57`、`ambiguous=3`、`not_found=0`、`missing_feature=0`。
- 残り `ambiguous=3` は引き続き `osaka EVOLVED -毎度、おおきに！- (TYPE1/2/3)` の同一ジャケット3件。`jacket_top_margin=0.0000` で、画像特徴量だけでは一意化しない。
- title画像特徴量PoCの今回結果は `title_rerank_status_counts={"ambiguous_candidate": 3, "not_run": 57}`。osaka 3件は title画像特徴量でも近傍候補が残り、TYPE1/2/3を安定して一意化できなかった。
- title OCR suffix 診断の今回結果は `title_ocr_rerank_status_counts={"no_suffix": 3, "not_run": 57}`。
  - osaka TYPE1 OCR文字列例: `| osaka EVOLVED WM.8e8i!: (TYPE) NAOKI underground |`
  - osaka TYPE2 OCR文字列例: `osaka EVOLVED WM. Ba#IC!: (TYPED) NAOKI underground |`
  - osaka TYPE3 OCR文字列例: `osaka EVOLVED WR.gasic!: (TYPES) NAOKI underground`
  - 現行のM3 title OCR入口だけでは `TYPE1` / `TYPE2` / `TYPE3` suffixを安定取得できない観測。しきい値問題というより、result title ROIの表現/OCR入口の問題として読む。
- OCRありM5曲名照合の今回結果は confirmed-events 60件、`matched=19`、`not_found=39`、`insufficient_input=2`。`not_found` は `below_score_threshold`、`insufficient_input` は `empty_ocr`。
- OCRなしM5曲名照合の今回結果は confirmed-events 60件、`insufficient_input=60` / `ocr_not_run=60`。
- M3 song/artist OCR入口失敗代表は `failure_count=24`、`affected_candidate_count=22`、`song_title empty_ocr=2`、`artist empty_ocr=22`。
- `matched`、jacket `matched`、title画像 `resolved_candidate`、title OCR `resolved_candidate` はPoC上の観測語彙で、DB保存可能、本番採用済み照合、曲ID/譜面ID確定を意味しない。
- 今回の `python -m master --output data\master\ddrgp-master.sqlite` では 1282 songs / 9594 charts を生成できた。
- 今回source hashは `ce38cabac579c99778b2964fba2b31da5c40182fba45efc9295f73db81741f9a`。取得元更新でhashは変わり得るため、件数固定ではなく構造変化検出とDB生成成功を見る。
- 今回コード検証では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は120 passed。
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

- OCR結果、ジャケットPoC結果、title補助結果から曲ID/譜面IDを保存用に確定すること
- ファジーマッチ結果、jacket `matched`、title画像 `resolved_candidate`、title OCR `resolved_candidate` を本番採用済み照合として扱うこと
- 曖昧一致や低確信度をDB保存可能として扱うこと
- `artist` を曲名照合の一意主キーとして扱うこと
- 同一ジャケット候補を画像特徴量だけで無理に一意化すること
- title画像特徴量やtitle OCRを候補集合外から曲を拾うために使うこと

## 次に必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- osaka TYPE1/2/3 は、jacket特徴量、result title画像特徴量、現行M3 title OCR入口のいずれでも安定一意化できていない。
- 次は以下のどちらかを小さく実装して切り分ける。
  - song_select grid/detail 側のタイトル表示ROIを定義し、result title ROIではなくsong_select由来のtitle参照で再順位付けできるか確認する。
  - 大きなOCR方式刷新ではなく、osaka suffixだけを対象にした小さな前処理/パース診断を追加し、`TYPE)` / `TYPED` / `TYPES` のような崩れを採用判断なしで観測する。
- title OCRやtitle画像特徴量は、jacketで `ambiguous` になったsong_id集合内の再順位付けだけに使う。候補集合外から曲を拾わない。
- `jacket_match_candidates.csv` の `expected_jacket_distance` / `expected_jacket_rank` / `jacket_top_margin`、`title_top_candidates`、`title_ocr_text`、`title_ocr_rerank_status` を見て、しきい値問題か特徴量/OCR表現問題かを分ける。
- title補助で解消候補が出ても、保存可能判定へ接続せず、PoC観測語彙として docs / README / tests に反映する。
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

title補助の今回観測確認:

```powershell
Import-Csv data\master_match_poc_jacket\jacket_match_candidates.csv |
  Where-Object {$_.jacket_match_status -eq 'ambiguous'} |
  Select-Object organized_file,expected_song_title,expected_jacket_rank,jacket_top_margin,title_rerank_status,title_top_title,title_top_distance,title_ocr_text,title_ocr_suffix,title_ocr_rerank_status,title_ocr_top_title,title_ocr_rerank_reason |
  Format-List
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
- M5照合境界、正規化方針、候補スコア、ジャケット特徴量方針、title補助方針、`match_status`、`failure_reason` を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
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
- `jacket_match_candidates.csv` で expected song / expected song_id / expected distance / expected rank / top margin を確認できる。
- title画像特徴量またはtitle OCRを追加する場合は、jacket ambiguous候補集合内の再順位付けに限定し、保存可能判定と混同していない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
