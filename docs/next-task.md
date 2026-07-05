# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。

## 推論レベル

high

## 作業ブランチ

`codex/m5-master-match-poc`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m5-master-match-poc` であること。未作成なら `main` から作成すること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- M3は2026-07-04時点で完了扱い。M3成果物は照合済みデータではなく、M5へ渡す観測値と失敗理由。
- M4はマスタDB生成入口として完了扱いにしてよい。
- `master` パッケージで、BEMANIWiki全曲リストHTMLから `songs` / `charts` / `master_metadata` / `source_snapshots` を持つSQLiteマスタDBを生成できる。
- `python -m master --output data\master\ddrgp-master.sqlite` で実HTMLから 1282 songs / 9594 charts のSQLiteを生成できることを確認済み。
- 実HTML件数とsource hashは取得元更新で変わり得るため、件数固定の完了条件にはしない。構造変化検出とDB生成成功を確認する。
- 2026-07-05のローカル確認では source hash `689ac5dc5f70b4992c4115263622c55d174d770c7bce4b87cb89e03e4b41edab`。
- 2026-07-05のGitHub Actions手動実行 run `28725098133` では、1282 songs / 9594 charts、source hash `9e6b60bc3951588d8c4efd534abdf38e3020d1cb23e330faa4c9514f03f0fbca`。
- `.github/workflows/build-master-db.yml` はdefault branchで認識済み。`Build master database` workflow ID は `307310266`。
- Actions run `28725098133` は成功し、`tests/test_master_builder.py` 11 passed、実HTML生成、`python -m master.inspect`、artifact upload がすべて通った。
- Actions artifact `ddrgp-master-1` には `ddrgp-master.sqlite` と `master-summary.json` が含まれることを実物ダウンロードで確認済み。
- ダウンロードした artifact DB に対して `python -m master.inspect` を再実行し、必須metadata、metadata件数、snapshot件数、source hash整合、source URL整合が通ることを確認済み。
- `master.inspect` のsummaryには `snapshot_count`、`snapshot_source_hash`、`snapshot_source_url`、`snapshot_parser_version` も出力し、artifact内の `master-summary.json` 単体でもsnapshot由来情報を確認できる。
- 直近確認では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 101 passed。
- 生成DB、PoC出力、OCR画像、`metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはGit管理しない。

## M4境界メモ

- M4 DBはM5照合PoCの入力として使ってよい。
- M4 DBが生成できても、M3 OCR文字列や chart-field ready を曲ID/譜面IDの照合成功として扱わない。
- M4の `song_id` / `chart_id` は現時点ではHTML由来テキストから作る安定hash。配布互換性が必要になったらID互換方針をdocsで固定する。
- Releases配布は未実装だが、M5入口のブロッカーではない。進める場合は別フェーズで、生成DBをコミットせずartifact/releaseとして扱う。

## M3境界メモ

- `python -m tools.vision_poc --no-ocr` は過去確認で Total 112 / Correct 112 / false positives 0 / false negatives 0。
- confirmed-events 境界は `confirmed_result=true` かつ `duplicate=false` の60件。
- `transition_countup_*` は `result_shape_candidate=true` でも `result_candidate=false`、`event_type=rejected_transition` のまま。
- `play_style`、`difficulty`、`level` は `m3_chart_field_adoption_candidates_summary.json` で60/60 match、`adoption_candidate`。
- M3 song/artist OCR入口は `song_title empty_ocr=2`、`artist empty_ocr=22`。両者を同じ改善対象として混ぜない。
- `play_style` / `difficulty` / `level` の `ready` はM3の抽出候補であり、M5でマスタ候補を絞る入力として使う。DB保存可能やマスタ照合成功とは扱わない。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/08_master_db_generation.md`
- `docs/design/07_m3_chart_field_review.md`
- `tools/vision_poc/README.md`
- `master/README.md`
- `master/builder.py`
- `master/inspect.py`
- `tests/test_master_builder.py`

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
- M4 Releases配布の実装
- M3 OCR前処理の大幅改善
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成
- 生成済み `data/master/ddrgp-master.sqlite` のコミット

M5内でまだ成功扱いにしないもの:

- OCR結果から曲ID/譜面IDを保存用に確定すること
- ファジーマッチ結果を本番採用済み照合として扱うこと
- 曖昧一致や低確信度をDB保存可能として扱うこと

## 今回必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- M5の最初の実作業として、M4マスタDBとM3保存候補レポートを入力にする「マスタ照合PoCの入口」を追加する。
- 最初の実装は小さく保つ。候補は、M4 DB読み込みヘルパー、曲名正規化の最小関数、譜面条件での候補絞り込み、またはCSV/summary出力のいずれか。
- M5では、照合結果を `matched` / `ambiguous` / `not_found` / `insufficient_input` のような状態として観察する。保存成功や本番確定とは扱わない。
- 主作業がローカル素材不足でブロックされた場合は、同じM5内でネットワークや画像素材に依存しないfixtureテスト、CLI設計、docs整備へ切り替える。
- `docs/next-task.md` の更新は、実作業と検証が終わった後の引き継ぎ更新として行う。

## 後続作業

1. 現状確認
   - `git status --short --branch` と `git log --oneline -5` を確認する。
   - `python -m master --output data\master\ddrgp-master.sqlite` を実行し、M5入力用のマスタDBをローカル生成する。
   - `python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json` を実行し、DB検査とsummary出力を確認する。
   - 実HTML件数が直近の 1282 songs / 9594 charts と違う場合は、取得元更新かパーサ崩れかを `master_metadata`、`source_snapshots`、HTMLヘッダで確認する。

2. M5入力境界の確認
   - M3レポートのどのCSV/summaryをM5入力に使うか確認する。
   - M3の `song_title` はOCR生文字列または入口失敗理由として扱い、正規化済みの正解値として扱わない。
   - M3の `artist` は補助観測値。左右切れがあるため、最初の一意照合主キーにしない。
   - `play_style` / `difficulty` / `level` は候補絞り込み条件として使うが、マスタ照合成功とは別に読む。
   - duplicate、`rejected_transition`、未確定候補、non-result はM5評価対象外に保つ。

3. M5 PoCの最小実装
   - 推奨入口は `tools/vision_poc` 配下のM5用サブコマンド、または小さな `matching` / `master_match` パッケージ。既存パターンを読んでから決める。
   - まずはSQLiteから曲・譜面候補を読む関数を追加する。
   - 次に曲名OCR文字列向けの最小正規化を追加する。全角/半角、空白、記号、大小文字の扱いはテストで固定する。
   - `play_style` / `difficulty` / `level` で `charts` を絞り、候補曲数と候補譜面数を出す。
   - 曲名類似度は最初は軽量な標準ライブラリベースでよい。外部依存を増やす場合はPoC専用optional dependencyにする。
   - 出力は `data/` 配下に置く。例: `data/master_match_poc/`。

4. M5レポート出力
   - 候補行CSVとsummary JSON/Markdownを出す。
   - 行ごとに、入力OCR文字列、正規化文字列、chart-field条件、候補数、最上位候補、score、match_status、failure_reasonを出す。
   - `matched` はPoC上の一意候補という意味に限定し、DB保存可能とは書かない。
   - `ambiguous`、`not_found`、`insufficient_input` を低確信度ログや保存不可理由へ渡せる語彙に寄せる。

5. M5 docs/tests
   - M5の照合境界を `docs/design/` または `tools/vision_poc/README.md` に追記する。
   - fixtureテストはネットワーク、画像、`metadata.csv` に依存させない。
   - M4 DB schemaや保存境界の意味を変える場合は、`docs/design/04_data_model.md`、`05_storage_io_spec.md`、`08_master_db_generation.md` も更新する。

## 検証コマンド

最低限:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
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

M5実装後に追加する想定:

```powershell
python -m pytest tests
python -m tools.vision_poc --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc
Get-Content data\master_match_poc\master_match_summary.json
```

上記M5コマンド名は未実装の仮名。実装時にCLI名を決めたら、このファイルとREADMEを更新する。

画像PoCやM3境界を触った場合の回帰:

```powershell
python -m tools.vision_poc --no-ocr
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events --no-rois --output data\vision_poc_m3_song_artist
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_song_artist_ocr_entry_failures_summary.json
Get-Content data\vision_poc_m3_song_artist\m3_save_candidate_summary.json
```

GitHub Actions再確認が必要な場合:

```powershell
gh api repos/tts1374/ddrgp_scorelog/actions/workflows
gh workflow list
gh workflow run build-master-db.yml --ref main
gh run list --workflow build-master-db.yml --limit 5
gh run watch <run-id> --exit-status
gh run download <run-id> --name ddrgp-master-<run_number> --dir <download-dir>
python -m master.inspect <download-dir>\ddrgp-master.sqlite
```

## コミット/Push方針

- `metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはコミットしない。
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像はローカル素材扱いでコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- M5照合境界、正規化方針、候補スコア、match_status、failure_reasonを変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。新規作業ブランチの推奨名は `codex/m5-master-match-poc`。

## 完了条件

- M4 DBをM5入力として生成・検査できる。
- M5の入力境界が、confirmed-events由来の保存候補だけを対象にしている。
- M3の `ready` やOCR文字列を、マスタ照合成功として扱っていない。
- M4 DBから曲・譜面候補を読み、`play_style` / `difficulty` / `level` で候補を絞れる。
- 曲名OCR文字列の最小正規化方針がテストとdocsで説明できる。
- M5 PoCのCSV/summaryで、候補数、最上位候補、match_status、failure_reasonを確認できる。
- `matched` / `ambiguous` / `not_found` / `insufficient_input` の意味が保存可否と混同されていない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が112件全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
