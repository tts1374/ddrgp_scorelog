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
- 直近source hashは `fdde31591016dd54d1c9d18d21939f6b692594b604e3d29217b3b82dae5d1b0e`。取得元更新でhashは変わり得るため、件数固定ではなく構造変化検出とDB生成成功を見る。
- M4パーサは `m4-initial-html-table-v2`。BEMANIWikiの脚注リンク本文が `*2` のような `*` + 数字だけの場合は曲名本文から除外し、`neko*neko` のような本文アスタリスクは残す。実HTML再生成後、`IX` と `Timepiece phase II` は脚注なしで一意に引けることを確認済み。
- M5の軽量改善として、曲名OCR文字列にartistや余分な記号が後続混入するケース向けに、正規化後のマスタ曲名が5文字以上でOCR正規化文字列に含まれる場合だけ包含一致として類似度を最大扱いにした。短いOCR断片がマスタ曲名に含まれるだけでは最大扱いにしない。
- 曲名正規化では従来のNFKC、casefold、空白除去、代表的な句読点除去に加え、曲名OCRで出やすい curly quote 系の `‘’“”` を除去対象にした。
- `master_match_candidates.csv` に `top_candidates` を追加し、上位5候補を `score:title / artist [chart_id]` 形式で観察できるようにした。これは失敗代表の観察用であり、保存可能判定ではない。
- fixtureテストに、artist suffix混入、包含一致の過剰boost防止、`top_candidates` 出力を追加済み。
- OCRありM5の直近結果は confirmed-events 60件、classification 112/112、`matched=19`、`not_found=39`、`insufficient_input=2`。`not_found` はすべて `below_score_threshold`、`insufficient_input` は `empty_ocr`。
- OCRなしM5の直近結果は confirmed-events 60件、classification 112/112、`insufficient_input=60` / `ocr_not_run=60`。
- 方針相談の結果、曲名OCR単独での曲ID確定は厳しいため、次の主信号候補をジャケット特徴量へ寄せる。
- 初回ジャケットPoCは `song_select` の detail ではなく grid 画面を対象にする。ただしgrid内の小ジャケットセル検出は避け、右上に出る大きい選択中ジャケットプレビューを使う。
- `song_select` grid右上プレビューからローカル特徴量マスタを作り、metadata の `song_title` / `expected_song_title` を M4 `songs.title` へ照合して `song_id` に紐づける方針にする。
- ローカルmetadataには追加grid素材を反映済み。`song_select_view=grid` かつ曲名ラベル付きは23件あり、全件M4 `songs.title` に一意一致する。`IX` は脚注除去後のM4表記に合わせて `IX` として扱う。
- result確定時は resultジャケットROIを特徴量化し、`play_style` / `difficulty` / `level` で絞った候補song_idの特徴量だけと比較する。
- 特徴量マスタとPoC出力は `data/` 配下のCSV/JSONにし、画像本体、metadata、ローカルDBはGit管理しない。
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
- 全曲ジャケット画像取得ツールの実装
- grid内の小ジャケットセル検出
- song_select detail画面を必須にすること
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
- M5ジャケット特徴量PoCを実装する。初回対象は `screen_type=song_select` かつ `organized_file` に `grid` を含む行の右上選択中ジャケットプレビュー。
- 既存metadataでは `song_select` の `song_title` / `expected_song_title` が空の行があるため、未ラベルsong_select一覧を `data/` 配下のテンプレCSVへ出す。metadata実体は編集・コミットしない。
- ラベルがあるsong_select行だけ、M4 `songs.title` へ既存の曲名正規化で照合し、1曲に決まる場合だけ `song_id` 付きの jacket feature master に採用する。
- result confirmed-events の `jacket` ROIを特徴量化し、`play_style` / `difficulty` / `level` で絞った候補song_idのfeatureだけと比較する。
- 出力は `jacket_feature_master.csv`、`jacket_feature_master_summary.json`、`jacket_feature_label_template.csv`、`jacket_match_candidates.csv`、`jacket_match_summary.json`、`jacket_match_report.md` を想定する。
- 特徴量は新規依存を増やさず、Pillow / numpy の範囲で、縮小RGBサムネイル、色ヒストグラム、dHash系の軽量特徴から始める。
- `jacket_match_status` は `matched` / `ambiguous` / `not_found` / `insufficient_input` / `missing_feature` のPoC観測語彙として扱う。`matched` は保存可能や本番採用済み照合ではない。
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
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --m5-jacket-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_jacket --no-rois
Get-Content data\master_match_poc\master_match_summary.json
Get-Content data\master_match_poc_ocr\master_match_summary.json
Get-Content data\master_match_poc_jacket\jacket_match_summary.json
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
- ラベル不足のsong_select行をテンプレCSVへ出し、metadata実体を変更していない。
- result confirmed-events のジャケットROIを、chart-fieldで絞った候補song_idのfeatureだけと比較できる。
- jacket matchの `matched` / `ambiguous` / `not_found` / `insufficient_input` / `missing_feature` の意味が保存可否と混同されていない。
- `matched` / `ambiguous` / `not_found` / `insufficient_input` の意味が保存可否と混同されていない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が112件全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
