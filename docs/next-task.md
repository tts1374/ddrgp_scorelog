# 次チャット用タスク

`C:\work\ddrgp_scorelog` で作業してください。必ず `AGENTS.md` のプロジェクトルールに従ってください。

## 推論レベル

high

## 作業ブランチ

`codex/m5-master-match-poc`

作業開始時に以下を確認してください。

- `git status --short --branch`
- `git log --oneline -5`
- 現在ブランチが `codex/m5-master-match-poc` であること
- `docs/next-task.md` は次チャット用の作業指示ファイルとして扱うこと

## 今回までの作業結果

- M4はマスタDB生成入口として完了扱い。M4 DBはM5照合PoCの入力として使ってよい。
- `python -m master --output data\master\ddrgp-master.sqlite` で、2026-07-05時点の実HTMLから 1282 songs / 9594 charts のSQLiteを生成できることを確認済み。
- 今回のローカルsource hashは `1fb8f6e7f067947314f3a99c8a4218864a1e429e9e0bb69071b38583640798a7`。取得元更新で変わり得るため、件数固定ではなく構造変化検出とDB生成成功を見る。
- M5入口として `tools/vision_poc/master_match.py` と `--m5-master-match` を追加済み。
- `--m5-master-match` は同じ実行内のM3保存候補行を使い、M4 SQLiteの `charts` を `play_style` / `difficulty` / `level` で絞り、曲名OCR文字列との類似度を出す。
- M5出力は `master_match_candidates.csv`、`master_match_summary.json`、`master_match_report.md`。
- `match_status` は `matched` / `ambiguous` / `not_found` / `insufficient_input`。`matched` はPoC上の一意候補であり、DB保存可能や本番採用済み照合ではない。
- 曲名正規化は現時点でNFKC、casefold、空白除去、代表的な句読点除去だけ。
- fixtureテスト `tests/test_master_match.py` はネットワーク、画像、`metadata.csv` に依存しない。
- `python -m tools.vision_poc --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc --no-rois --no-ocr` では confirmed-events 60件、classification 112/112、M5は `insufficient_input=60` / `ocr_not_run=60`。
- `python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_ocr --no-rois` では confirmed-events 60件、classification 112/112、M5は `matched=4`、`not_found=54`、`insufficient_input=2`。`not_found` はすべて `below_score_threshold`、`insufficient_input` は `empty_ocr`。
- OCRありM5では、曲名OCR文字列にartistや余分な記号が混ざるケースが多く、次の改善対象は本格OCR刷新ではなくM5側の入力観察、最小正規化、候補スコア読み方の強化。
- 直近確認では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は 108 passed。
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
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成
- 生成済み `data/master/ddrgp-master.sqlite` のコミット

M5内でまだ成功扱いにしないもの:

- OCR結果から曲ID/譜面IDを保存用に確定すること
- ファジーマッチ結果を本番採用済み照合として扱うこと
- 曖昧一致や低確信度をDB保存可能として扱うこと
- `artist` を曲名照合の一意主キーとして扱うこと

## 今回必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- M5の次作業として、OCRありM5の `below_score_threshold` を観察し、軽量な改善を1つ以上実装する。
- 推奨候補は、曲名OCR文字列からartist混入を減らす最小クリーニング、上位候補を複数残すCSV列、`not_found` 理由の細分化、しきい値前後の代表レポート、またはfixtureテスト追加。
- 大きなOCR方式刷新やROI座標変更には進まない。
- `matched` はPoC上の一意候補という意味に限定し、保存可能とは書かない。
- `docs/next-task.md` の更新は、実作業と検証が終わった後の引き継ぎ更新として行う。

## 後続作業

1. 現状確認
   - `git status --short --branch` と `git log --oneline -5` を確認する。
   - `python -m master --output data\master\ddrgp-master.sqlite` を実行し、M5入力用のマスタDBをローカル生成する。
   - `python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json` を実行し、DB検査とsummary出力を確認する。
   - 実HTML件数が直近の 1282 songs / 9594 charts と違う場合は、取得元更新かパーサ崩れかを `master_metadata`、`source_snapshots`、HTMLヘッダで確認する。

2. M5入力境界の確認
   - duplicate、`rejected_transition`、未確定候補、non-result がM5対象外であることを維持する。
   - M3の `song_title` はOCR生文字列または入口失敗理由として扱い、正解値や正規化済みタイトルとして扱わない。
   - M3の `artist` は補助観測値。左右切れがあるため、最初の一意照合主キーにしない。
   - `play_style` / `difficulty` / `level` は候補絞り込み条件として使うが、マスタ照合成功とは別に読む。

3. M5 PoC改善
   - OCRありM5出力 `data\master_match_poc_ocr\master_match_candidates.csv` を再生成して読む。
   - `below_score_threshold` の代表行で、OCR文字列、正規化文字列、top候補、scoreを確認する。
   - まずは標準ライブラリベースで改善する。外部依存を増やす場合はPoC専用optional dependencyに分ける。
   - 改善した正規化やスコア方針はfixtureテストで固定する。
   - 出力は `data/` 配下に置く。例: `data/master_match_poc_ocr/`。

4. M5 docs/tests
   - M5の照合境界、正規化方針、`match_status`、`failure_reason` を変えた場合は `docs/design/09_master_match_poc.md` と `tools/vision_poc/README.md` を更新する。
   - fixtureテストはネットワーク、画像、`metadata.csv` に依存させない。
   - M4 DB schemaや保存境界の意味を変える場合は、`docs/design/04_data_model.md`、`05_storage_io_spec.md`、`08_master_db_generation.md` も更新する。

## 検証コマンド

最低限:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
python -m tools.vision_poc --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc --no-rois --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
```

M5 OCR入口確認:

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_ocr --no-rois
Get-Content data\master_match_poc_ocr\master_match_summary.json
Get-Content data\master_match_poc_ocr\m3_song_artist_ocr_entry_failures_summary.json
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
- M5 PoCのCSV/summaryで、候補数、最上位候補、score、`match_status`、`failure_reason`を確認できる。
- `matched` / `ambiguous` / `not_found` / `insufficient_input` の意味が保存可否と混同されていない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が112件全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
