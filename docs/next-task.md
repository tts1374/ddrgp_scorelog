# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。

## 推論レベル

high

## 作業ブランチ

`codex/m4-master-db-generation`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m4-master-db-generation` であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- M3は2026-07-04時点で完了扱い。M3成果物は照合済みデータではなく、M5へ渡す観測値と失敗理由。
- M4入口として `master` パッケージを追加し、BEMANIWiki全曲リストHTMLからSQLiteマスタDBを生成できるようにした。
- `python -m master --output data\master\ddrgp-master.sqlite` で2026-07-04時点のBEMANIWiki実HTMLから 1282 songs / 9594 charts のSQLiteを生成できる。
- 実HTML件数は取得元更新で変わり得るため、件数固定の完了条件にはしない。構造変化検出とDB生成成功を確認する。
- 直近確認では 1282 songs / 9594 charts で通過した。直近の source hash は `79e4ba962d5e856e91978ddfce6623af8bb27ded25dab3fd673f8dc87f2ddd9f`。
- M4初期スキーマは `songs`、`charts`、`master_metadata`、`source_snapshots`。
- `.github/workflows/build-master-db.yml` を追加し、手動実行・週次実行でfixtureテスト、実HTMLからのSQLite生成、metadata件数検査、artifactアップロードを行える設計にした。
- Actions artifact名は `ddrgp-master-<run_number>`。中身は `data/master/ddrgp-master.sqlite` と `data/master/master-summary.json`。生成DBはGit管理しない。
- GitHub Actionsの手動実行確認は未完了。2026-07-04の確認では `origin/main` に `.github/workflows/build-master-db.yml` がまだ無く、`gh workflow list` は空、`gh api repos/tts1374/ddrgp_scorelog/actions/workflows` は `total_count: 0` だった。
- 上記のため、GitHub側でworkflowを認識させるには、workflowファイルをdefault branchへ入れる必要がある。現在の作業ブランチだけでは手動実行確認に進めない。
- Actions内DB検査相当として、`master_metadata` の `song_count` / `chart_count` と実テーブル件数が一致し、`source_snapshots` が1件であることをローカル生成DBで確認済み。
- 直近確認では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 96 passed。
- `master/README.md` の Current Boundaries を更新し、GitHub Actions入口は追加済み、Releases配布は未実装という現状に合わせた。
- 生成DB、PoC出力、OCR画像、`metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはGit管理しない。

## M4 parser/schemaメモ

- パーサは、2026-07-04時点のBEMANIWiki全曲リストにある2段ヘッダ表を対象にする。
- 対象ヘッダは `分類 / 曲名 / アーティスト / 出典 / BPM / MV/St / SINGLE / DOUBLE` と `Be / Ba / Di / Ex / Ch / Ba / Di / Ex / Ch`。
- 通常テストはネットワーク取得に依存させず、小さなHTML fixtureで固定する。
- fixtureでは、セル結合、注記付きレベル、削除/限定/パック記号、SP/DP片方のみ、CHALLENGEなし、同名曲・同アーティスト、複数バージョン表、同一 `chart_id` 衝突検出を維持している。
- 注記付きレベルは raw 表記を保持し、整数 `level` は最初に現れる数字列から取得する。`10(旧9)`、`10;`、`[SA] 12` で数字を連結しない。
- 同じ曲名・同じアーティストは同じ `song_id` として扱う。同一 `chart_id` の譜面行が食い違う場合は、静かな上書きではなく生成失敗として扱う。
- `song_id` / `chart_id` は現時点では安定hashだが、配布互換性が必要になったらID互換方針をdocsで固定する。
- M4はマスタDB生成入口であり、曲名正規化、ファジーマッチ、候補一覧、照合スコア、照合確信度、曲ID/譜面IDの一意照合はM5まで未確定。

## M3境界メモ

- `python -m tools.vision_poc --no-ocr` は過去確認で Total 112 / Correct 112 / false positives 0 / false negatives 0。
- confirmed-events 境界は `confirmed_result=true` かつ `duplicate=false` の60件。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- `play_style`、`difficulty`、`level` は `m3_chart_field_adoption_candidates_summary.json` で60/60 match、`adoption_candidate`。
- M3 song/artist OCR入口は `song_title empty_ocr=2`、`artist empty_ocr=22`。両者を同じ改善対象として混ぜない。
- M4 DBが生成できても、M3 OCR文字列や chart-field ready を曲ID/譜面IDの照合成功として扱わない。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/design/08_master_db_generation.md`
- `master/README.md`
- `.github/workflows/build-master-db.yml`
- `.github/workflows/README.md`
- `master/builder.py`
- `tests/test_master_builder.py`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/implementation-roadmap.md`

M3境界や画像PoCへ触る場合だけ追加で読む資料:

- `docs/design/00_glossary.md`
- `docs/design/03_event_and_save_boundary.md`
- `docs/design/06_regression_guard.md`
- `docs/design/07_m3_chart_field_review.md`
- `tools/vision_poc/README.md`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- 個人スコアDB保存、保存可否判定本番仕様、低確信度ログ本番仕様
- OCR方式の大幅刷新、ROI座標定義の大変更
- duplicate key の本格実装差し替え
- M5の曲名正規化、ファジーマッチ、候補絞り込み、OCR結果からの曲ID/譜面ID一意照合
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成
- 生成済み `data/master/ddrgp-master.sqlite` のコミット

## 後続作業

1. 現状確認
   - `git status --short --branch` と `git log --oneline -5` を確認する。
   - `python -m master --output data\master\ddrgp-master.sqlite` を実行し、実HTMLからマスタDBが生成できることを確認する。
   - 実HTML件数が直近の 1282 songs / 9594 charts と違う場合は、取得元更新かパーサ崩れかを `master_metadata` とHTMLヘッダで確認する。

2. M4 CI/artifact確認
   - 次の主作業候補。
   - まず `origin/main` に `.github/workflows/build-master-db.yml` が入っているか確認する。
   - 現状のまま作業ブランチにworkflowがあるだけでは、GitHub側でworkflow一覧に出ず手動実行できない。
   - workflowファイルがdefault branchへ入った後、GitHub上で `Build master database` workflowを手動実行し、artifactに `ddrgp-master.sqlite` と `master-summary.json` が含まれることを確認する。
   - Actionsログで `tests/test_master_builder.py`、実HTML生成、metadata件数検査が通っていることを確認する。
   - artifact生成結果が安定したら、Releases配布を同じworkflowに足すか、別workflowに分けるかを決める。

3. M4 parser/schemaの継続確認
   - BEMANIWiki の表構造は変わり得るため、parserへ触る場合は最新HTMLを再確認する。
   - 通常テストはネットワーク取得に依存させず、小さなHTML fixtureで固定する。
   - fixture追加時は、実HTML件数ではなく、解析境界とDB制約の崩れを検出する観点で足す。

4. M4の本番配布前整理
   - `source_snapshots.html_content` をDB内に持つ方針で十分か、外部snapshotファイルとhashだけにするか検討する。
   - Releases配布はまだ未実装。進める場合は生成DBをコミットせずartifact/releaseとして扱う。
   - 取得元HTML構造変化を、ヘッダ未検出・0件生成・SQLite制約違反として検出できる状態を維持する。

5. M5へ進む前の境界確認
   - M4 DBを入力にした別PoCとして、曲名正規化、ファジーマッチ、候補絞り込み、照合スコアを設計する。
   - M4内ではOCR結果から曲ID/譜面IDを一意に決めない。
   - M3の `ready` や空でないOCR文字列を、マスタ照合成功として扱わない。

## 検証コマンド

最低限:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
```

M4 DB確認:

```powershell
python -m pytest tests\test_master_builder.py
python -m master --output data\master\ddrgp-master.sqlite
@'
import sqlite3
with sqlite3.connect("data/master/ddrgp-master.sqlite") as con:
    print(con.execute("select count(*) from songs").fetchone()[0])
    print(con.execute("select count(*) from charts").fetchone()[0])
    print(dict(con.execute("select key, value from master_metadata")))
'@ | python -
```

Actions内DB検査相当:

```powershell
@'
import json
import sqlite3
from pathlib import Path

db_path = Path("data/master/ddrgp-master.sqlite")
summary_path = Path("data/master/master-summary.json")

with sqlite3.connect(db_path) as con:
    metadata = dict(con.execute("SELECT key, value FROM master_metadata"))
    song_count = con.execute("SELECT COUNT(*) FROM songs").fetchone()[0]
    chart_count = con.execute("SELECT COUNT(*) FROM charts").fetchone()[0]
    snapshot_count = con.execute("SELECT COUNT(*) FROM source_snapshots").fetchone()[0]

if song_count <= 0 or chart_count <= 0:
    raise SystemExit("generated database must contain songs and charts")
if metadata.get("song_count") != str(song_count):
    raise SystemExit("master_metadata song_count does not match songs table")
if metadata.get("chart_count") != str(chart_count):
    raise SystemExit("master_metadata chart_count does not match charts table")
if snapshot_count != 1:
    raise SystemExit("generated database must contain exactly one source snapshot")

summary = {
    "database": str(db_path),
    "song_count": song_count,
    "chart_count": chart_count,
    "source_hash": metadata.get("source_hash"),
    "master_version": metadata.get("master_version"),
    "source_url": metadata.get("source_url"),
    "generated_at": metadata.get("generated_at"),
    "generator_version": metadata.get("generator_version"),
}
summary_path.write_text(
    json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
    encoding="utf-8",
)
print(json.dumps(summary, ensure_ascii=False, indent=2))
'@ | python -
```

GitHub Actions確認:

```powershell
gh api repos/tts1374/ddrgp_scorelog/actions/workflows
gh workflow list
gh workflow run build-master-db.yml --ref codex/m4-master-db-generation
gh run list --workflow build-master-db.yml --limit 5
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
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- M4スキーマ、HTML解析境界、生成DBの扱いを変えた場合は、`master/README.md`、`docs/design/08_master_db_generation.md`、必要に応じて `docs/design/04_data_model.md` / `05_storage_io_spec.md` を同じコミットに含める。
- コミットがある場合は `codex/m4-master-db-generation` を push する。

## 完了条件

- `python -m master --output data\master\ddrgp-master.sqlite` でマスタDBを生成できる。
- GitHub Actions workflowが、生成DBをコミットせずartifactとして扱う設計になっている。
- Actions内DB検査相当で、metadata件数と実テーブル件数、`source_snapshots` 件数を確認できる。
- M4 fixtureテストがネットワークに依存せず通る。
- M4 fixtureでセル結合、注記付きレベル、削除/限定/パック記号、SP/DP片方のみ、CHALLENGEなし、同名曲・同アーティスト、同一 `chart_id` 衝突検出を維持している。
- 実HTML件数差分が出た場合、取得元更新かパーサ崩れかを説明できる。
- GitHub Actions手動実行確認へ進む場合は、workflowファイルがdefault branchで認識されている。
- M4 DB生成をM5の照合成功として扱っていない。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が112件全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
