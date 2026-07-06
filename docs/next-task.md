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
- jacketで `ambiguous` になった候補集合内だけを対象にする title画像特徴量PoC、title OCR suffix診断、title line-hash診断を追加済み。
- result `song_title` ROI の title line-hash rerank PoC を実装済み。
  - `song_title` ROIの曲名行だけを固定サイズへ寄せ、白文字色域を固定しきい値で二値化し、行ごとのbit列をhex化する。
  - 参照はローカルmetadata期待曲名を M4 `songs.title` へ一意解決できた result素材のみ。
  - 同じ `organized_file` の参照は比較から除外する。
  - jacketで `ambiguous` になったsong_id集合内だけで比較し、候補集合外から曲を拾わない。
  - inf-notebook 風の行hexキー辞書 `title_linehash_dict_status` を主観測にする。
  - 完全一致型 `title_linehash_exact_status` と、候補参照間で差が出るbitを重く見る距離比較型 `title_linehash_distance_status` は参考列として残す。
  - `title_linehash_*_status=resolved_candidate` はPoC観測語彙であり、保存可能、曲ID/譜面ID確定、`jacket_match_status=matched` への昇格を意味しない。
- 2026-07-05の追加ローカル素材取り込み後、metadata は220行、classification は220/220全正解。`transition_countup` shape candidates は3件。
- 追加ローカル素材として `samples/screenshots` の `スクリーンショット (216).png` 〜 `スクリーンショット (257).png` を crop 済み。raw画像は残し、`samples/screenshots/cropped/` と `samples/screenshots/organized/` に配置し、`samples/screenshots/metadata.csv` へ追記済み。これら画像と metadata 実体はGit管理しない。
  - osaka result は 216〜218 TYPE1、219〜221 TYPE2、222〜224 TYPE3。
  - New York / tokyo / London EVOLVED の同一・類似ジャケット grid/result も追加済み。
- 2026-07-06の M4 DB生成は 1282 songs / 9594 charts。source hash は `f31dac01e0d54f9f984360a56206b2b3b56c0ab26ab708089cb16d92a6704cc2`。
- OCRなしM5曲名照合は confirmed-events 60件、`insufficient_input=60` / `ocr_not_run=60`。
- OCRありM5曲名照合は confirmed-events 60件、`matched=19`、`not_found=39`、`insufficient_input=2`。
- M3 song/artist OCR入口失敗代表は `failure_count=24`、`affected_candidate_count=22`、`song_title empty_ocr=2`、`artist empty_ocr=22`。
- 今回の `--m5-jacket-match` 結果は confirmed-events 60件、`jacket_feature_master accepted=68`、`matched=57`、`ambiguous=3`、`not_found=0`、`missing_feature=0`。
- 残り `ambiguous=3` は引き続き `osaka EVOLVED -毎度、おおきに！- (TYPE1/2/3)` の同一ジャケット3件。`jacket_top_margin=0.0000` で、画像特徴量だけでは一意化しない。
- 追加 result の `result_228` 以降は通常の `jacket_match_candidates.csv` には出ない。
  - M5 jacket match の対象境界は `confirmed_result=true` かつ `duplicate=false`。
  - `result_228` 以降はゼロ点リザルト連続素材で、`score:000000` の duplicate か、confirmed-events 境界外として扱われる。
  - したがって「ambiguous にならない」のではなく、保存候補評価対象に入っていない。追加 result は主に title line-hash 参照素材、追加 grid は jacket feature master 参照素材として効く。
- title画像特徴量PoCの今回結果は `title_rerank_status_counts={"ambiguous_candidate": 3, "not_run": 57}`。
- title OCR suffix診断の今回結果は `title_ocr_rerank_status_counts={"no_suffix": 3, "not_run": 57}`。
- title line-hash辞書化後の今回結果は `title_linehash_dict_status_counts={"not_run": 57, "resolved_candidate": 3}`、`title_linehash_exact_status_counts={"no_exact_match": 3, "not_run": 57}`、`title_linehash_distance_status_counts={"not_run": 57, "resolved_candidate": 3}`。
  - osaka 3件の `title_linehash_dict_top_title` は期待TYPEと一致した。
  - 辞書の行一致数は TYPE1 `13`、TYPE2 `14`、TYPE3 `16`。
  - 追加素材のラベル誤りがあると辞書結果も容易に崩れるため、同一ジャケット分岐の取り込み時は resultタイトルROIを目視して TYPE suffix を確認する。
  - `title_linehash_distance_status` は今回3件とも `resolved_candidate` だが、参考列として残すだけにし、今後も距離比較を本命にしない。
- title line-hash辞書結果をM5候補観測として後続へ渡すため、`jacket_match_candidates.csv` に `identity_signal_*` 列を追加済み。
  - `jacket_match_status` はjacket特徴量単体の観測として維持する。title補助で候補が1件に見えても `ambiguous` から `matched` へ昇格しない。
  - `identity_signal_status=jacket_resolved_candidate` はjacket単体のPoC一意候補。
  - `identity_signal_status=composite_resolved_candidate` は、jacket候補集合にtitle補助を合わせると候補集合内で1件を示した状態。
  - `identity_signal_source` の優先順は `jacket_feature`、`title_linehash_dict`、`title_ocr_suffix`、`title_image_feature`。
  - `title_linehash_exact_status` / `title_linehash_distance_status` は参考列で、`identity_signal_source` には使わない。
  - 今回の jacket summary は `identity_signal_status_counts={"composite_resolved_candidate": 3, "jacket_resolved_candidate": 57}`、`identity_signal_source_counts={"jacket_feature": 57, "title_linehash_dict": 3}`。
- `matched`、jacket `matched`、title画像 `resolved_candidate`、title OCR `resolved_candidate`、title line-hash `resolved_candidate`、`identity_signal_*` はPoC上の観測語彙で、DB保存可能、本番採用済み照合、曲ID/譜面ID確定を意味しない。
- 今回コード検証では `python -m ruff check master tools\vision_poc pyproject.toml tests`、`python -m compileall master tools\vision_poc`、`python -m pytest tests` が通過し、pytest は120 passed。M5生成系と `python -m tools.vision_poc --no-ocr` も通過し、classification は220/220全正解。
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
- ファジーマッチ結果、jacket `matched`、title画像 `resolved_candidate`、title OCR `resolved_candidate`、title line-hash `resolved_candidate` を本番採用済み照合として扱うこと
- 未解決の曖昧一致や低確信度をDB保存可能として扱うこと
- `artist` を曲名照合の一意主キーとして扱うこと
- 同一ジャケット候補を画像特徴量だけで無理に一意化すること
- title画像特徴量、title OCR、title line-hashを候補集合外から曲を拾うために使うこと
- title line-hash成功をDB保存可能、M7/M8保存判定、実保存処理として扱うこと
- `identity_signal_status=composite_resolved_candidate` や `identity_signal_source=title_linehash_dict` を保存可能、曲ID/譜面ID確定、本番採用済み照合として扱うこと
- スコア/判定数のTesseract離脱を今回の実装に含めること

## 次に必ず進める実作業

- `docs/next-task.md` の更新だけ、または確認結果の記録だけで完了扱いにしない。
- osaka TYPE1/2/3 は、jacket特徴量、result title画像特徴量、現行M3 title OCR入口では安定一意化できていないが、title line-hash辞書では候補集合内の `resolved_candidate` になり、`identity_signal_source=title_linehash_dict` として複合根拠の曲同定候補観測へ整理済み。
- 次は `identity_signal_*` をM5の後続渡し出力としてさらに扱いやすくする実装を進める。
  - 例: `jacket_match_report.md` または新規M5レポートで、`jacket_resolved_candidate`、`composite_resolved_candidate`、`unresolved_*` を保存判定前の観測カテゴリとして代表行つきで確認できるようにする。
  - `identity_signal_status` は保存判定ではなく、M7以降へ渡す候補観測として読む。
  - `composite_resolved_candidate` はjacket単体より低いという意味ではなく、複合根拠で曲候補を1件示した観測として扱う。ただしDB保存可能や `jacket_match_status=matched` へ直結しない。
  - `title_linehash_distance_status` は参考列として扱い、`identity_signal_source` や主判断へ戻さない。
  - 参照は引き続き result素材のみ。song_select 側タイトル表示ROIは使わない。
  - jacketで `ambiguous` になったsong_id集合内だけを対象にし、候補集合外から曲を拾わない。
- `jacket_match_candidates.csv` の `identity_signal_*`、`expected_jacket_distance` / `expected_jacket_rank` / `jacket_top_margin`、`title_top_candidates`、`title_ocr_text`、`title_ocr_rerank_status`、`title_linehash_*` を見て、しきい値問題か特徴量/OCR/line-hash表現問題か、またはM5後続渡し語彙の整理問題かを分ける。
- New York / tokyo / London EVOLVED の同一・類似ジャケット分岐を result単体で観察したい場合は、通常のM5保存候補境界を変えずに、別の診断出力として duplicate / unconfirmed を含む観察モードを検討する。保存候補用 `jacket_match_candidates.csv` に混ぜない。
- 大きなOCR方式刷新やROI座標定義の大変更には進まない。
- スコア/判定数のTesseract離脱や数字テンプレート認識は後続タスクとして扱い、今回の実作業には含めない。
- `docs/next-task.md` の更新は、実作業と検証が終わった後の引き継ぎ更新として行う。

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
Get-Content data\master_match_poc_ocr\m3_song_artist_ocr_entry_failures_summary.json
python -m tools.vision_poc --no-ocr
python -m ruff check master tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
```

title補助の観測確認:

```powershell
Import-Csv data\master_match_poc_jacket\jacket_match_candidates.csv |
  Where-Object {$_.jacket_match_status -eq 'ambiguous'} |
  Select-Object organized_file,expected_song_title,expected_jacket_rank,jacket_top_margin,identity_signal_status,identity_signal_source,identity_signal_title,identity_signal_reason,title_rerank_status,title_top_title,title_top_distance,title_ocr_text,title_ocr_suffix,title_ocr_rerank_status,title_ocr_top_title,title_linehash_dict_status,title_linehash_dict_top_title,title_linehash_dict_top_row_matches,title_linehash_exact_status,title_linehash_distance_status,title_linehash_top_title,title_linehash_top_distance,title_linehash_diff_bit_count,title_linehash_rerank_reason |
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
- result confirmed-events のジャケットROIを、chart-fieldで絞った候補song_idのfeatureだけと比較できる。
- jacket matchの `matched` / `ambiguous` / `not_found` / `insufficient_input` / `missing_feature` の意味が保存可否と混同されていない。
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
