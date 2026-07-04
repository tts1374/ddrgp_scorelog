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
- 最新コミットがM4ブランチ整理コミット以降であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- M3は2026-07-04時点で完了扱い。M3成果物は照合済みデータではなく、M5へ渡す観測値と失敗理由。
- `python -m tools.vision_poc --no-ocr` は Total 112 / Correct 112 / false positives 0 / false negatives 0。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- confirmed-events 境界は `confirmed_result=true` かつ `duplicate=false` の60件。
- ローカル `samples/screenshots/organized/chart_field_templates/` は37枚。
- `play_style`、`difficulty`、`level` は `m3_chart_field_adoption_candidates_summary.json` で60/60 match、`adoption_candidate`。
- M3 song/artist OCR入口は `song_title empty_ocr=2`、`artist empty_ocr=22`。両者を同じ改善対象として混ぜない。
- M4入口として `master` パッケージを追加した。
- M4入口コミットは `main` にfast-forward merge済みで、以降のM4継続作業用に `codex/m4-master-db-generation` を作成済み。
- `codex/vision-poc-ocr-tuning` はM3/M4入口までの履歴を持つが、次作業では使わない。
- `python -m master --output data\master\ddrgp-master.sqlite` で2026-07-04時点のBEMANIWiki実HTMLから 1282 songs / 9594 charts のSQLiteを生成できることを確認済み。
- M4初期スキーマは `songs`、`charts`、`master_metadata`、`source_snapshots`。
- M4 fixtureテストはネットワークに依存せず、セル結合、CHALLENGEなし、SP/DP差分、複数バージョン表を固定する。
- M4はマスタDB生成入口であり、曲名正規化、ファジーマッチ、候補一覧、照合スコア、照合確信度、曲ID/譜面IDの一意照合はM5まで未確定。
- 直近確認では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 93 passed。
- 生成DB、PoC出力、OCR画像、`metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはGit管理しない。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/requirements.md`
- `docs/design/README.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/08_master_db_generation.md`
- `master/README.md`
- `master/builder.py`
- `tests/test_master_builder.py`
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
   - `python -m tools.vision_poc --no-ocr` を実行し、112件全正解、`transition_countup_*` 除外、confirmed-events 60件、chart-field 3項目 ready を確認する。
   - `python -m master --output data\master\ddrgp-master.sqlite` を実行し、実HTMLからマスタDBが生成できることを確認する。

2. M4 parser/schemaの次補強
   - `master/builder.py` の初期パーサを読み、現在は楽曲リスト2段ヘッダ表だけを対象にしていることを確認する。
   - BEMANIWiki の表構造は変わり得るため、実装時は最新HTMLを再確認する。
   - 通常テストはネットワーク取得に依存させず、小さなHTML fixtureを追加して固定する。
   - 注記付きレベル、削除/限定/パック記号、SP/DP片方のみ、CHALLENGEなし、同名曲・同アーティストの扱いをfixtureで増やす。
   - `song_id` / `chart_id` は現時点では安定hashだが、配布互換性が必要になったらID互換方針をdocsで固定する。

3. M4の本番配布前整理
   - `source_snapshots.html_content` をDB内に持つ方針で十分か、外部snapshotファイルとhashだけにするか検討する。
   - GitHub Actions、定期実行、Releases配布はまだ未実装。進める場合は生成DBをコミットせずartifact/releaseとして扱う。
   - 取得元HTML構造変化を、ヘッダ未検出・0件生成・SQLite制約違反として検出できる状態を維持する。

4. M3/M4境界の維持
   - M4 DBが生成できても、M3 OCR文字列や chart-field ready を曲ID/譜面IDの照合成功として扱わない。
   - M5へ進む場合は、M4 DBを入力にした別PoCとして曲名正規化、ファジーマッチ、候補絞り込み、照合スコアを実装する。

## 検証コマンド

最低限:

```powershell
python -m tools.vision_poc --no-ocr
python -m master --output data\master\ddrgp-master.sqlite
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
```

M3-9確認:

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_entry_failures_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_save_candidate_summary.json
```

M4 DB確認:

```powershell
python -m pytest tests\test_master_builder.py
python -m master --output data\master\ddrgp-master.sqlite
```

必要に応じてSQLite件数確認:

```powershell
@'
import sqlite3
with sqlite3.connect("data/master/ddrgp-master.sqlite") as con:
    print(con.execute("select count(*) from songs").fetchone()[0])
    print(con.execute("select count(*) from charts").fetchone()[0])
'@ | python -
```

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像はローカル素材扱いでコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- M4スキーマ、HTML解析境界、生成DBの扱いを変えた場合は、`master/README.md`、`docs/design/08_master_db_generation.md`、必要に応じて `docs/design/04_data_model.md` / `05_storage_io_spec.md` を同じコミットに含める。
- コミットがある場合は `codex/m4-master-db-generation` を push する。

## 完了条件

- `python -m tools.vision_poc --no-ocr` が112件全正解。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- confirmed-events の保存境界が `confirmed_result=true` かつ `duplicate=false` のまま。
- duplicate / rejected_transition / unconfirmed / non-result が、M3 chart-field、M3 song/artist OCR、M3 save candidate summary の対象外のまま。
- `play_style` / `difficulty` / `level` の `ready` をDB保存可能、採用済みテンプレート照合成功、マスタ照合成功、ファジーマッチ成功、曲名正規化成功として扱っていない。
- `song_title` と `artist` のOCR入口失敗代表を別々に読み、同じ改善対象として混ぜていない。
- `python -m master --output data\master\ddrgp-master.sqlite` でマスタDBを生成できる。
- M4 fixtureテストがネットワークに依存せず通る。
- M4 DB生成をM5の照合成功として扱っていない。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
