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

- M4で公式canonical/alias突合を追加した。
  - 公式収録曲一覧へ突合できた場合、`songs.title` / `songs.artist` を公式表記へ寄せる。
  - Wiki側表記差は `song_aliases` に `wiki_source` として保存する。
  - `Ё` / `Ë` のような装飾記号差と一部のキリル/ラテン混在差をalias正規化し、`official_availability_match=alias_title_artist` / `alias_unique_title` として区別する。
  - M5の期待曲名/song_select参照解決は、M4 `songs.title` に見つからない場合だけ `song_aliases` も参照する。
- `RЁVOLUTIФN` はM4公式canonical化で解決した。
  - M4 `songs`: `RЁVOLUTIФN` / `TЁЯRA` / `grand_prix_play_available=1` / `official_availability_match=alias_title_artist`
  - `song_aliases`: Wiki由来 `RËVOLUTIФN` / `TËЯRA`
  - M5 song_select feature masterではローカルmetadataの `RËVOLUTIФN` ラベルがalias経由で `RЁVOLUTIФN` に解決され、featureとしてaccepted。
- ユーザー追加の `スクリーンショット (258).png` をローカル素材としてcrop/配置し、metadataへ `Inner Spirit -GIGA HiTECH MIX-` のsong_select grid参照を追加した。素材とmetadata実体はGit管理しない。
  - `organized/song_select/song_select_258_sp_difficult_lv11_inner_spirit_giga_hitech_mix_grid.png`
  - `paired_result_file=organized/result/result_054_sp_difficult_lv11_inner_spirit_giga_hitech_mix_score990270.png`
- 追加素材とM4 alias消費後の最新 M5 jacket通常候補結果は、ユーザー確認対象2曲が解消済み。
  - `jacket_feature_master`: `target_count=69` / `accepted=69`
  - `jacket_match_status_counts={"ambiguous": 3, "insufficient_input": 0, "matched": 57, "missing_feature": 0, "not_found": 0}`
  - `identity_signal_status_counts={"composite_resolved_candidate": 3, "jacket_resolved_candidate": 57}`
  - `expected_song_resolution_status_counts={"resolved": 59, "unresolved": 1}`
  - `expected_song_resolution_reason_counts={"title_not_found": 1}`
  - `expected_song_grand_prix_play_available_counts={"True": 59}`
- 解消確認:
  - `Inner Spirit -GIGA HiTECH MIX-`: `jacket_match_status=matched` / `top_distance=0.0501` / `expected_jacket_rank=1` / `jacket_top_margin=0.1699` / `identity_signal_status=jacket_resolved_candidate`
  - `RЁVOLUTIФN`: `jacket_match_status=matched` / `top_distance=0.0321` / `expected_jacket_rank=1` / `jacket_top_margin=0.2194` / `identity_signal_status=jacket_resolved_candidate`
- M4 DBの最新ローカル生成結果は 1282 songs / 9594 charts / song_aliases 39件 / source_snapshots 2件。
  - Wiki source hash は `9102987ea11f208287cd392a867fea4153ed0772f0834ab64f0e734a3f56e535`。
  - 公式収録曲一覧 source hash は `e929391b31f45f48c1dc461e323380d69b9b24db50cd15f1601f1766034e5856`。
  - `grand_prix_play_available_song_count=1181`、`free_play_available_song_count=64`、`official_availability_matched_song_count=1181`。
  - `official_availability_match` 内訳は `title_artist=1143`、`unique_title=36`、`alias_title_artist=2`、`not_found=101`。
- M5の通常候補境界は confirmed-events 60件のまま維持している。duplicate / unconfirmed は `jacket_match_diagnostics.csv` 側の観察対象であり、通常候補や保存候補へ混ぜない。
- 生成DB、PoC出力、OCR画像、`metadata.csv`、`data/`、`logs/`、ローカル素材、ローカルDBはGit管理しない。

## 必読資料

- `AGENTS.md`
- `docs/next-task.md`
- `docs/implementation-roadmap.md`
- `docs/design/03_event_and_save_boundary.md`
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
- `tests/test_vision_poc_result_events.py`

M3評価レポートや画像PoCの境界へ触る場合だけ追加で読む資料:

- `docs/design/00_glossary.md`
- `docs/design/06_regression_guard.md`

## スコープ外

- スクリーンショット画像、`samples/screenshots/metadata.csv`、`data/`、`logs/`、ローカルDBのGit管理
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下のローカル追加画像コミット
- 本番キャプチャAPI、実キャプチャデバイス依存コード、常駐監視ループ、非同期処理
- 個人スコアDB保存、保存可否判定本番仕様、低確信度ログ本番仕様
- OCR方式の大幅刷新、ROI座標定義の大変更
- duplicate key の本格実装差し替え
- 全曲ジャケット画像取得ツールの実装
- grid内の小ジャケットセル検出
- song_select detail画面を必須にすること
- X-Special付き譜面など、現時点でGRAND PRIXプレー対象として扱わない同一ジャケット分岐の実装対応
- M4 Releases配布の実装
- Windows常駐アプリUI
- プロジェクト専用Skill/Subagentの作成
- 生成済み `data/master/ddrgp-master.sqlite` のコミット

M5内でまだ成功扱いにしないもの:

- OCR結果、ジャケットPoC結果、title補助結果から曲ID/譜面IDを保存用に確定すること
- jacket `matched`、title補助 `resolved_candidate`、`identity_signal_*` を本番採用済み照合として扱うこと
- `expected_song_*` 診断列を保存候補化やGP対象外曲復帰の根拠にすること
- duplicate / unconfirmed を含める診断用M5出力を、保存候補や保存可否判定として扱うこと
- 参照不足の曲を、近傍の別曲へ寄せて解消扱いにすること
- `artist` を曲名照合の一意主キーとして扱うこと
- 同一ジャケット候補を画像特徴量だけで無理に一意化すること
- title画像特徴量、title OCR、title line-hashを候補集合外から曲を拾うために使うこと
- スコア/判定数のTesseract離脱を今回の実装に含めること

## 次に必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- M5内で、参照カバレッジを明示し、参照不足時に別曲へ寄せないための診断を実装する。
  - 例: jacket特徴量参照のcoverage summary、候補song_idごとの参照有無、期待song_idの参照不足理由、参照不足代表CSV/Markdown。
  - `missing_feature`、`no_dict_match`、`title_not_found`、`ambiguous` を混同せず、参照不足は参照不足として観察できるようにする。
  - Inner Spiritのように正しいgrid参照を追加すれば解決するケースと、London系のような近傍・同一/類似ジャケット候補を切り分ける。
- 通常候補60件と診断出力を混同しない。
  - 通常候補は `confirmed_result=true` かつ `duplicate=false`。
  - 診断出力は duplicate / unconfirmed を含む観察用で、保存候補ではない。
- M5語彙は維持する。
  - `matched` はPoC信号上の一意候補であり、保存OKではない。
  - `composite_resolved_candidate` は複合根拠で候補1件を示した観測であり、保存OKではない。
  - duplicate / unconfirmed 診断結果は保存候補ではない。
  - 保存OK/NG、低信頼度ログ、人手確認キュー、個人スコアDB書き込みはM7以降で決める。
- 大きなOCR方式刷新やROI座標定義の大変更には進まない。
- スコア/判定数のTesseract離脱や数字テンプレート認識は後続タスクとして扱い、今回の実作業には含めない。

## 検証コマンド

最低限:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
python -m tools.vision_poc --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc --no-rois --no-ocr
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_ocr --no-rois
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --m5-jacket-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_jacket --no-rois
python -m tools.vision_poc --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
git diff --check
```

M4 canonical/alias確認:

```powershell
@'
import sqlite3
with sqlite3.connect("data/master/ddrgp-master.sqlite") as con:
    print(con.execute("select count(*) from song_aliases").fetchone()[0])
    print(con.execute("select title, artist, grand_prix_play_available, official_availability_match from songs where title = 'RЁVOLUTIФN'").fetchall())
    print(con.execute("select s.title, a.alias_title, a.alias_artist, a.alias_type, a.source from song_aliases a join songs s on s.song_id = a.song_id where s.title = 'RЁVOLUTIФN'").fetchall())
    print(con.execute("select official_availability_match, count(*) from songs group by official_availability_match order by count(*) desc").fetchall())
'@ | python -
```

M5 jacket確認:

```powershell
Get-Content data\master_match_poc_jacket\jacket_feature_master_summary.json
Get-Content data\master_match_poc_jacket\jacket_match_summary.json
Get-Content data\master_match_poc_jacket\jacket_match_diagnostics_summary.json
Import-Csv data\master_match_poc_jacket\jacket_match_candidates.csv |
  Where-Object {$_.expected_song_title -match 'Inner Spirit|RЁVOLUTIФN|RËVOLUTIФN'} |
  Select-Object organized_file,expected_song_title,expected_song_id,expected_song_resolution_status,expected_song_resolution_reason,expected_song_grand_prix_play_available,expected_song_official_availability_match,jacket_match_status,failure_reason,expected_jacket_rank,jacket_top_margin,identity_signal_status,identity_signal_source,top_candidates |
  Format-List
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
- `samples/screenshots/cropped/` と `samples/screenshots/organized/` 配下の画像はローカル素材扱いでコミットしない。
- `docs/next-task.md` は引き継ぎ仕様としてコミット対象に含める。
- コード、README、docs、テストに変更がある場合のみ、今回作業分だけをステージしてコミットする。
- `data/master/ddrgp-master.sqlite`、`data/master/master-summary.json`、M5 PoC出力、ROI画像、OCR画像、解析ログはステージしない。
- M5照合境界、正規化方針、候補スコア、ジャケット特徴量方針、title補助方針、`match_status`、`failure_reason`、`identity_signal_*`、`expected_song_*` を変えた場合は、関連する `docs/design/` または `tools/vision_poc/README.md` を同じコミットに含める。
- コミットがある場合は作業ブランチを push する。

## 完了条件

- M4 DBをM5入力として生成・検査できる。
- M4 DBが公式収録曲一覧から `grand_prix_play_available` を付与し、`source_snapshots` にWiki/公式の2件を保持している。
- M4 DBが公式canonicalを `songs.title` / `songs.artist` に保持し、Wiki側表記差を `song_aliases` に保持している。
- M5が `songs.title` に見つからないローカルラベルを `song_aliases` で解決できる。
- `RЁVOLUTIФN` が公式canonicalとして解決され、Wiki側 `RËVOLUTIФN` がaliasとして保持される。
- `Inner Spirit -GIGA HiTECH MIX-` と `RЁVOLUTIФN` が最新ローカル素材で `jacket_resolved_candidate` として確認できる。
- M5の通常入力境界が、confirmed-events由来の保存候補だけを対象にしている。
- M5 jacket診断出力が通常候補CSVとは別に生成され、duplicate / unconfirmed を保存候補外として観察できる。
- `jacket_match_candidates.csv` で expected song / expected song_id / expected song resolution / official availability / expected distance / expected rank / top margin を確認できる。
- M3の `ready` やOCR文字列を、マスタ照合成功として扱っていない。
- M5通常候補は `grand_prix_play_available=1` に限定され、X-SpecialなどGP対象外曲を候補へ戻していない。
- title画像特徴量、title OCR、title line-hashを追加・変更する場合は、jacket ambiguous候補集合内の再順位付けに限定し、保存可能判定と混同していない。
- `identity_signal_*` と `expected_song_*` はM5後続渡し/レビュー用の観測として出力し、保存可能、曲ID/譜面ID確定、`jacket_match_status` 昇格と混同していない。
- 参照不足時は参照不足として明示し、近傍の別曲へ寄せて解消扱いにしない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
