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

- M5 jacket match に、通常保存候補とは別の診断出力を追加した。
  - 通常の `jacket_match_candidates.csv` / summary / report は維持し、対象はこれまで通り `confirmed_result=true` かつ `duplicate=false` の60件。
  - 新規に `jacket_match_diagnostics.csv`、`jacket_match_diagnostics_summary.json`、`jacket_match_diagnostics.md` を出力する。
  - 診断出力は metadata上のresult行、未確定result、duplicateを含む別ファイルで、保存候補用CSVへ混ぜない。
  - 診断行には `diagnostic_scope`、`m5_target_boundary_reason`、`event_type`、`confirmed_result`、`duplicate`、`duplicate_key`、`timestamp_ms`、`confirmation_mode` を付けた。
  - `m5_target_boundary_reason` は `save_candidate` / `unconfirmed` / `duplicate` / `metadata_result_not_candidate` を使う。
  - 診断行の曲名と `play_style` / `difficulty` / `level` はローカルmetadata期待値を `metadata-expected-diagnostic` として使う。これはM5同定能力とイベント境界の観察用で、保存候補への昇格やDB保存可能判定ではない。
- `tools/vision_poc/README.md` と `docs/design/09_master_match_poc.md` に、通常候補と診断候補を混ぜない読み方を追記した。
- M5 jacket Markdownレポートに代表行セクションを追加した。
  - `jacket_match_report.md` に `Identity Signal Representatives` を追加し、通常候補60件の `jacket_resolved_candidate` / `composite_resolved_candidate` / `unresolved_*` を代表行つきで確認できる。
  - `jacket_match_diagnostics.md` に `Boundary Representatives` を追加し、`save_candidate` / `unconfirmed` / `duplicate` を同じ診断レポートで観察できる。表示順は `save_candidate`、`unconfirmed`、`duplicate`、`metadata_result_not_candidate` を優先する。
  - 代表行はMarkdown上の観察補助であり、通常候補CSVの境界、`jacket_match_status`、保存OK/NG、DB保存可否は変えない。
- テストを追加した。
  - `tests/test_vision_poc_result_events.py`: M5 jacket診断入力行が未確定result、保存候補、duplicateを含み、metadata期待値を診断入力として渡すこと。
  - `tests/test_master_match.py`: 診断出力 writer が境界context、summary、別CSV/Markdownを生成すること。
  - `tests/test_master_match.py`: 通常候補レポートの `Identity Signal Representatives` と診断レポートの `Boundary Representatives` / `Identity Signal Representatives` を固定すること。
- 2026-07-06の今回検証では、M4 DBは 1282 songs / 9594 charts / source_snapshots 2件。
  - Wiki source hash は `2518be81691192ffd76c89d36f96c513bd5e5b01f88295ddc7e8b08654b8953e`。
  - 公式収録曲一覧 source hash は `2509cd8bb140aa2a587d6574ef7d900cec987394dbdd042e0ee149c1f4bbaee5`。
  - `grand_prix_play_available_song_count=1180`、`free_play_available_song_count=64`、`official_availability_matched_song_count=1180`。
- 2026-07-06の今回検証では、M5 jacket feature master は `target_count=68` / `accepted=68`。
- 今回の M5 jacket通常候補結果は60件で、従来境界を維持している。
  - `jacket_match_status_counts={"ambiguous": 4, "insufficient_input": 0, "matched": 55, "missing_feature": 0, "not_found": 1}`
  - `identity_signal_status_counts={"composite_resolved_candidate": 3, "jacket_resolved_candidate": 55, "unresolved_ambiguous": 1, "unresolved_not_found": 1}`
  - `identity_signal_source_counts={"jacket_feature": 55, "title_linehash_dict": 3}`
- 通常候補の未解決代表は以下。
  - `Inner Spirit -GIGA HiTECH MIX-`: `organized/result/result_054_sp_difficult_lv11_inner_spirit_giga_hitech_mix_score990270.png`、`identity_signal_status=unresolved_ambiguous`、`jacket_match_status=ambiguous`、`title_linehash_dict_status=no_dict_match`、`title_linehash_dict_top_title=London EVOLVED ver.A`。
  - `RЁVOLUTIФN`: `organized/result/result_085_sp_expert_lv17_revolution_score882780.png`、`identity_signal_status=unresolved_not_found`、`jacket_match_status=not_found`、`failure_reason=above_distance_threshold`、`expected_song_id` は空。
- 今回の M5 jacket診断結果は118件。
  - `event_type_counts={"confirmed": 60, "duplicate": 37, "none": 21}`
  - `m5_target_boundary_reason_counts={"duplicate": 37, "save_candidate": 60, "unconfirmed": 21}`
  - `jacket_match_status_counts={"ambiguous": 40, "insufficient_input": 12, "matched": 64, "missing_feature": 1, "not_found": 1}`
  - `identity_signal_status_counts={"composite_resolved_candidate": 38, "jacket_resolved_candidate": 64, "unresolved_ambiguous": 2, "unresolved_insufficient_input": 12, "unresolved_missing_feature": 1, "unresolved_not_found": 1}`
  - `identity_signal_source_counts={"jacket_feature": 64, "title_linehash_dict": 38}`
- 診断CSVで New York EVOLVED の代表行を別物として観察できる。
  - `result_228` Type A は `m5_target_boundary_reason=unconfirmed` / `event_type=none`。
  - `result_231` Type B は `m5_target_boundary_reason=duplicate` / `event_type=duplicate`。
  - `result_234` Type C は `m5_target_boundary_reason=duplicate` / `event_type=duplicate`。
  - 3件とも `jacket_match_status=ambiguous` だが、`identity_signal_source=title_linehash_dict` で期待Typeの `composite_resolved_candidate` を観察できた。これは保存候補化ではない。
- osaka EVOLVED、tokyoEVOLVED、London EVOLVED のゼロ点連続素材も診断CSVで観察できる。
  - osaka / tokyo は duplicate側でも多くが `title_linehash_dict` で期待Typeへ寄っている。
  - London ver.B は代表として `result_254` が期待 `ver.B` に対して `identity_signal_title=London EVOLVED ver.A` になり、`result_255` は `title_linehash_dict_status=no_dict_match` / `unresolved_ambiguous`。次の観察対象にする。
- OCRなしM5曲名照合は confirmed-events 60件、`insufficient_input=60` / `ocr_not_run=60`。
- OCRありM5曲名照合は confirmed-events 60件、`matched=19`、`not_found=39`、`insufficient_input=2`。
- M3 song/artist OCR入口失敗代表は `failure_count=24`、`affected_candidate_count=22`、`song_title empty_ocr=2`、`artist empty_ocr=22`。
- `python -m tools.vision_poc --no-ocr` は220/220全正解、`transition_countup` shape candidates は3件。
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
- `samples/screenshots/cropped/` と `samples/screenshots/organized/chart_field_templates/` の画像コミット
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
- 未解決の曖昧一致や低確信度をDB保存可能として扱うこと
- duplicate / unconfirmed を含める診断用M5出力を、保存候補や保存可否判定として扱うこと
- 0点リザルトの診断結果を、保存候補や保存可否判定として扱うこと
- `grand_prix_play_available=0` の曲を通常M5候補へ戻すこと
- `artist` を曲名照合の一意主キーとして扱うこと
- 同一ジャケット候補を画像特徴量だけで無理に一意化すること
- title画像特徴量、title OCR、title line-hashを候補集合外から曲を拾うために使うこと
- title line-hash成功をDB保存可能、M7/M8保存判定、実保存処理として扱うこと
- スコア/判定数のTesseract離脱を今回の実装に含めること

## 次に必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- まず今回追加した `jacket_match_report.md` の `Identity Signal Representatives` と `jacket_match_diagnostics.md` の `Boundary Representatives` を入口に、通常候補60件と診断118件を混同せずに読む。
  - New York EVOLVED の Type A/B/C が診断CSVで別行として観察できる状態は維持する。
  - London EVOLVED ver.B の `result_254` / `result_255` は、line-hash参照やmetadataラベルの目視確認対象として扱う。保存候補化やしきい値変更へ直結しない。
- 次に、通常候補側で残る `Inner Spirit -GIGA HiTECH MIX-` の `unresolved_ambiguous` と、`RЁVOLUTIФN` の `unresolved_not_found` を観察する。
  - `jacket_match_candidates.csv` の該当行、M4 `songs.official_availability_match`、ローカルmetadata期待値、result title ROIを突き合わせる。
  - 公式GP可否フィルタは維持し、GP対象外曲をM5候補へ戻さない。
  - 表記差を直す場合はM4側のalias/official availability突合補助として扱い、M5保存判定へ直結しない。
- そのうえで、観察結果に応じて `identity_signal_*` をM5の後続渡し出力としてさらに扱いやすくする実装を進める。
  - 例: unresolved専用の代表表、expected song未解決理由、M4 official availability突合状態、line-hash参照元の表示など。ただし保存判定や候補昇格へは進めない。
  - 通常候補と duplicate / unconfirmed 診断候補は混ぜず、診断側の観測は保存候補外として明示する。
  - `composite_resolved_candidate` は複合根拠で曲候補を1件示した観測であり、DB保存可能や `jacket_match_status=matched` への昇格ではない。
  - `title_linehash_distance_status` は参考列のまま維持し、`identity_signal_source` や主判断へ戻さない。
  - title補助参照は引き続き result素材のみ。song_select 側タイトル表示ROIは使わない。
  - jacketで `ambiguous` になったsong_id集合内だけを対象にし、候補集合外から曲を拾わない。
- 最後にM5の語彙を固定する。
  - `matched` はPoC信号上の一意候補であり、保存OKではない。
  - `composite_resolved_candidate` は複合根拠で候補1件を示した観測であり、保存OKではない。
  - duplicate / unconfirmed 診断結果は保存候補ではない。
  - 0点リザルトはM5診断対象にはできるが、保存候補ではない優先案として扱う。
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
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --m5-jacket-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_jacket_ingested --no-rois
Get-Content data\master_match_poc\master_match_summary.json
Get-Content data\master_match_poc_ocr\master_match_summary.json
Get-Content data\master_match_poc_jacket\jacket_feature_master_summary.json
Get-Content data\master_match_poc_jacket\jacket_match_summary.json
Get-Content data\master_match_poc_jacket\jacket_match_diagnostics_summary.json
Get-Content data\master_match_poc_ocr\m3_song_artist_ocr_entry_failures_summary.json
Select-String -Path data\master_match_poc_jacket\jacket_match_report.md -Pattern "Identity Signal Representatives" -Context 0,8
Select-String -Path data\master_match_poc_jacket\jacket_match_diagnostics.md -Pattern "Boundary Representatives|Identity Signal Representatives" -Context 0,12
python -m tools.vision_poc --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
```

診断出力の代表確認:

```powershell
Import-Csv data\master_match_poc_jacket\jacket_match_diagnostics.csv |
  Where-Object {$_.expected_song_title -match 'New York|tokyo|London|osaka'} |
  Select-Object organized_file,m5_target_boundary_reason,event_type,confirmed_result,duplicate,expected_song_title,jacket_match_status,identity_signal_status,identity_signal_source,identity_signal_title,title_linehash_dict_status,title_linehash_dict_top_title |
  Format-List
```

通常候補の未解決確認:

```powershell
Import-Csv data\master_match_poc_jacket\jacket_match_candidates.csv |
  Where-Object {$_.identity_signal_status -like 'unresolved_*'} |
  Select-Object organized_file,expected_song_title,expected_song_id,jacket_match_status,failure_reason,expected_jacket_rank,jacket_top_margin,identity_signal_status,identity_signal_source,title_linehash_dict_status,title_linehash_dict_top_title,title_linehash_rerank_reason,top_candidates |
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
    print(con.execute("select count(*) from songs where grand_prix_play_available = 1").fetchone()[0])
    print(con.execute("select official_availability_match, count(*) from songs group by official_availability_match order by count(*) desc").fetchall())
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
- M4 DBが公式収録曲一覧から `grand_prix_play_available` を付与し、`source_snapshots` にWiki/公式の2件を保持している。
- M5の通常入力境界が、confirmed-events由来の保存候補だけを対象にしている。
- M5 jacket診断出力が通常候補CSVとは別に生成され、duplicate / unconfirmed を保存候補外として観察できる。
- New York EVOLVED Type A/B/C、tokyoEVOLVED、London EVOLVED、osaka EVOLVED などの同一・類似ジャケット分岐を診断CSVで確認できる。
- M3の `ready` やOCR文字列を、マスタ照合成功として扱っていない。
- M5通常候補は `grand_prix_play_available=1` に限定され、X-SpecialなどGP対象外曲を候補へ戻していない。
- `jacket_match_candidates.csv` で expected song / expected song_id / expected distance / expected rank / top margin を確認できる。
- title画像特徴量、title OCR、title line-hashを追加・変更する場合は、jacket ambiguous候補集合内の再順位付けに限定し、保存可能判定と混同していない。
- title line-hashは result素材参照だけを使い、候補集合外から曲を拾わない。
- `identity_signal_*` はM5後続渡し候補観測として出力し、保存可能、曲ID/譜面ID確定、`jacket_match_status` 昇格と混同していない。
- `identity_signal_source` は `title_linehash_dict` を主なtitle補助とし、`title_linehash_distance_status` を主判断へ戻していない。
- M5 fixtureテストがネットワーク、画像、`metadata.csv` に依存せず通る。
- 画像PoCやM3境界を触った場合は、`python -m tools.vision_poc --no-ocr` が全正解。
- 画像PoCやM3境界を触った場合は、`transition_countup_*` と confirmed-events 境界が維持されている。
- 生成DB、テンプレート素材、PoC出力、`metadata.csv` 実体や画像をコミットしていない。
- 検証コマンドが通っている。
- コミット/Pushする場合は、Git管理対象外ファイルを含めていない。
