# M4 マスタDB生成設計

M4では、BEMANIWiki 由来の楽曲・譜面情報、公式収録曲一覧由来のプレー可否、DDR WORLD公式楽曲一覧由来の譜面レベルを、M5のマスタ照合PoCが参照できるSQLite DBへ変換する。ここでは本番配布や照合ロジックへ進みすぎず、HTML入力、解析境界、DBスキーマ、生成物の扱いを固定する。

## 目的

- 公式収録曲一覧HTMLから `songs.title` / `songs.artist` を取得する。
- BEMANIWiki の全曲リスト／新曲リストHTMLから `charts` と補助情報を生成する。
- 公式収録曲一覧HTMLから `free_play_available` / `grand_prix_play_available` を付与する。
- DDR WORLD公式楽曲一覧の全ページからSP/DP・難易度・レベルを取得し、GP対象曲の譜面へ優先統合する。
- マスタDBと個人スコアDBを分離する。
- 取得元HTMLのhashとsnapshotを残し、表構造変化を検出しやすくする。
- M5の曲名正規化、ファジーマッチ、候補絞り込みが参照できる安定した初期スキーマを作る。

## 入力

譜面マスタ取得元URL:

```text
https://bemaniwiki.com/index.php?DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88
```

新曲リスト取得元URL:

```text
https://bemaniwiki.com/?DanceDanceRevolution+GRAND+PRIX/%E6%96%B0%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88
```

2026-07-04時点の対象表は、以下の2段ヘッダを持つ。

```text
分類 / 曲名 / アーティスト / 出典 / BPM / MV/St / SINGLE / DOUBLE
Be / Ba / Di / Ex / Ch / Ba / Di / Ex / Ch
```

パーサはこのヘッダを持つ表だけを楽曲リストとして扱う。セル結合されたバージョン見出しは `songs.version` / `songs.category` に入れる。レベルが `-` または空の譜面は未存在として `charts` に作らない。

注記付きレベルは raw 表記を `charts.raw_level` に保持し、整数 `charts.level` は最初に現れる数字列から取得する。`10(旧9)` や `10;` は `10`、`[SA] 12` は `12` として扱い、数字を連結しない。`[SA]`、`SA`、`Shock`、`ショック` を含む表記は `charts.shock_arrow=true` とする。

曲名やartistなどの表セルに含まれる脚注リンクは、リンク本文が `*2` のような `*` + 数字だけの場合に限り、マスタ本文から除外する。これはBEMANIWikiの脚注番号を曲名へ混入させないための処理で、`neko*neko` のように曲名本文へ含まれるアスタリスクは残す。

プレー可否取得元URL:

```text
https://p.eagate.573.jp/game/eacddr/konaddr/info/mlist.html
```

公式収録曲一覧では、`タイトル` / `アーティスト` / `フリープレー` / `グランプリプレー` を持つ表だけをプレー可否ソースとして扱う。`グランプリプレー` 列に `〇` がある曲を `songs.grand_prix_play_available=true` とする。アーケードプレーのみの表は、GP対象曲判定には使わない。

DDR WORLD公式譜面取得元URL:

```text
https://p.eagate.573.jp/game/ddr/ddrworld/music/index.html?filter=7&filtertype=0&playmode=2
```

この固定queryの公式楽曲一覧を、#193で定めた単一並列なし・自動retryなし・空ページ終端・最大100ページのsnapshot契約で全ページ取得する。各ページのSP/DP、難易度、レベルを `song + play_style + difficulty` 単位で読む。完了済みsnapshot directoryはCLIの `--ddrworld-input` で再利用でき、既定の直接生成は同じページ取得をメモリ上で行う。

公式収録曲一覧の `タイトル` / `アーティスト` は曲情報の正本として `songs.title` / `songs.artist` に保存する。公式アーティストが空の場合も空を保持し、Wiki側の版権元名へフォールバックしない。全曲リストと新曲リストは曲名・アーティストの正規化一致で統合し、新曲リストだけに存在する曲も `songs` / `charts` へ追加する。新曲リストのHTMLは `source_snapshots` と `new_song_source_url` / `new_song_source_hash` で追跡する。

公式の `グランプリプレー` 列に `〇` がある行がWiki譜面マスタに存在しない場合も、公式の曲名・アーティスト・プレー可否だけで `songs` に追加する。この公式のみの行は `official_availability_match=official_only` とし、Wiki由来の分類・BPM・譜面レベルがないため、それらの補助項目は空、`charts` は未作成とする。

公式リストとWiki譜面マスタの突合は、まず曲名+artistの正規化一致で行い、artistが空または表記差がある場合は曲名が公式リスト内で一意な場合だけ曲名一致で補完する。省略記号などの表記差も正規化して照合する。`Ё` / `Ë` のような装飾記号差や一部のキリル/ラテン混在差はalias正規化でも照合し、`alias_title_artist` / `alias_unique_title` として区別する。公式に突合できた曲は `songs.title` / `songs.artist` を公式表記へ寄せ、Wiki由来表記差は `song_aliases` に `wiki_source` として保存する。Wiki側にないGP対象曲は `official_only` として残す。突合結果は `official_availability_match` に残す。公式リストにない曲や曖昧な曲は `grand_prix_play_available=false` のままにし、M5の通常候補から除外する。

通常の譜面表にCHALLENGEがない一方でプレー可能と確認済みの34曲は、2026-07-25取得のDDR WORLD楽曲一覧snapshotで確認した25曲50譜面と、2026-08-09取得のBEMANIWiki楽曲パック一覧で確認した9曲18譜面の固定一覧から、SP/DP計68譜面だけを局所補正する。優先順位はDDR WORLD公式、BEMANIWiki、確認済みCHALLENGE補正の順とし、同じ `song_id + play_style + CHALLENGE` がない場合だけ補正を追加する。DDR WORLD公式に同じ譜面がある場合は公式レベルを採用し、公式にない譜面ではWiki由来または確認済み補正を維持する。確認値と異なるWiki既存譜面がある場合は生成を失敗させる。追加chartの `notes` と `master_metadata` の補正manifestに、確認元URLと取得日を譜面単位で保持する。

DDR WORLD公式との曲対応付けは、曲名+アーティストの正規化一致、既存aliasの一意一致、曲名の一意一致の順で行う。公式収録曲一覧でGP対象と確定した曲だけを統合対象とし、DDR WORLDにだけ存在する曲を公式情報だけで `grand_prix_play_available` に昇格させない。公式のみの譜面は追加し、公式にないGP専用譜面はWiki由来情報を維持する。確認済みCHALLENGE補正だけが残る譜面はWikiのみと混同しない。

差分reportの各行は、`official_override`、`official_only`、`wiki_only`、`supplement_only`、`excluded_non_gp`、`world_only_outside_gp`、`unmatchable_gp_candidate`、`ambiguous_gp_candidate`のいずれか1つへ分類する。DDR WORLDに存在しても既存のプレー可否判定元とBEMANIWikiのどちらからもGP対象と確認できない曲・譜面は`world_only_outside_gp`として正常に除外する。既知の曲へ一意対応でき、既存判定でGP対象外のものは`excluded_non_gp`として公式譜面を統合しない。

GP対象候補について対応候補が0件なら`unmatchable_gp_candidate`、複数件なら`ambiguous_gp_candidate`とし、推測で統合しない。この2 statusはStop条件であり、`master.inspect`はどちらかが1件以上なら検証を失敗させる。`level_changed`と`level_unchanged`は`official_override`の集計値として扱い、両者の合計を`official_override`件数と一致させる。status別件数の合計はreport行数と一致させ、除外またはStop対象の各行に理由を残す。

## 出力

ローカル生成先の既定:

```text
data/master/ddrgp-master.sqlite
```

生成DBはGit管理しない。将来の配布用DBは GitHub Releases 成果物として扱う。

CI生成では `.github/workflows/build-master-db.yml` を使う。workflowは手動実行と週次定期実行を持ち、fixtureテスト、Wiki・公式収録曲一覧・DDR WORLD公式楽曲一覧の実HTMLからのSQLite生成、`python -X utf8 -m master.inspect` による必須metadataキー検査、`master_metadata` と実テーブル件数の整合検査、`source_snapshots` 件数検査、source hash / source URLの整合検査、chart ID重複・chart identity重複・外部キー違反の検査を行う。生成DB、`master-summary.json`、DDR WORLD差分reportは `ddrgp-master-<run_number>` artifactとして保存し、Git管理対象にはしない。`master-summary.json` にはテーブル件数、snapshot件数、Wiki/公式source URL、parser version、公式プレー可否の突合件数、DDR WORLD差分件数を出力する。

Releases配布は、artifactで生成結果と取得元構造変化検出を確認できる状態が安定してから追加する。

## 初期スキーマ

### `songs`

- `song_id`: HTML由来テキストから作る安定hash。
- `title`
- `artist`
- `version`: セル結合の分類見出し。
- `source_version`: 表の `出典` 列。
- `bpm`
- `category`
- `movie_stage`: 表の `MV/St` 列。
- `availability`: 表の `分類` 列。
- `free_play_available`: 公式収録曲一覧の `フリープレー` 列が `〇` か。
- `grand_prix_play_available`: 公式収録曲一覧の `グランプリプレー` 列が `〇` か。
- `official_availability_match`: 公式収録曲一覧との突合状態。`title_artist` / `unique_title` / `alias_title_artist` / `alias_unique_title` / `official_only` / `ambiguous_title` / `ambiguous_alias_title_artist` / `ambiguous_alias_title` / `not_found` / `not_checked`。
- `notes`
- `created_at`
- `updated_at`

### `song_aliases`

- `alias_id`
- `song_id`
- `alias_title`
- `alias_artist`
- `alias_type`: 現時点では `wiki_source`。
- `source`: 現時点では `bemaniwiki`。

公式canonicalへ寄せた際にWiki側の曲名/artist表記が異なる場合だけ保存する。M5などの消費側は、通常は `songs.title` / `songs.artist` を公式canonicalとして読み、ローカルmetadataや旧表記がWiki側表記を持つ場合だけ `song_aliases` を解決補助として使う。

### `charts`

- `chart_id`: `song_id + play_style + difficulty` 由来の安定hash。
- `song_id`
- `play_style`: `SINGLE` または `DOUBLE`。
- `difficulty`: `BEGINNER`、`BASIC`、`DIFFICULT`、`EXPERT`、`CHALLENGE`。
- `level`: 1から19の整数。
- `raw_level`: 注記を含む元レベル表記。
- `shock_arrow`: 元レベル表記にショックアローらしい記号があるか。
- `is_removed`: `分類` 列から削除候補として読めるか。
- `is_limited`: `分類` 列が空でないか。
- `notes`: 初期実装では `分類` 列の内容。

DDR WORLD公式で上書き・追加したchartは、`notes` に公式source URL、取得時刻、ページ位置を追記する。確認済みCHALLENGE補正で追加したchartは、`notes` に元の `分類` 列があれば保持したうえで、確認元URLと取得日を追記する。

同じ曲名・同じアーティストは同じ `song_id` として扱う。同一 `chart_id` の譜面行が複数回出て、保持値が食い違う場合は、HTML構造または入力解釈の変化として生成を失敗させる。

### `master_metadata`

- `master_version`
- `source_url`
- `generated_at`
- `generator_version`
- `source_hash`
- `official_source_url`
- `official_source_hash`
- `confirmed_challenge_chart_count`
- `confirmed_challenge_supplement_hash`
- `confirmed_challenge_supplement_json`: 補正したchart ID、song ID、曲名、SP/DP、レベル、確認元URL、取得日の正規化JSON。
- `ddrworld_source_url` / `ddrworld_source_hash`: DDR WORLD公式全ページsnapshotのURLとsnapshot hash。
- `ddrworld_snapshot_id` / `ddrworld_fetched_at` / `ddrworld_parser_version` / `ddrworld_collector_version`
- `ddrworld_page_count` / `ddrworld_song_count` / `ddrworld_chart_count`
- `ddrworld_merge_report_hash` / `ddrworld_merge_report_json`: 譜面単位の公式優先統合結果、理由、件数、source metadata。
- `song_count`
- `chart_count`
- `free_play_available_song_count`
- `grand_prix_play_available_song_count`
- `official_availability_matched_song_count`
- `song_alias_count`

### `source_snapshots`

- `snapshot_id`
- `source_url`
- `fetched_at`
- `content_hash`
- `parser_version`
- `html_content`

DDR WORLD公式snapshotは、各ページHTMLを検証済みの順序で連結した本文を1件のsource snapshotとして保存する。`ddrworld_source_hash` はsnapshot metadataと一致し、`ddrworld_snapshot_id` と差分reportのsource metadataで元snapshotを特定する。

## 構造変化検出

以下の場合は生成を失敗させる。

- 楽曲リストの2段ヘッダを持つ表が見つからない。
- `songs` または `charts` が0件になる。
- 同一 `chart_id` の譜面行が食い違う。
- 確認済みCHALLENGE補正の既存レベルが確認値と食い違う。
- SQLite制約に反するレベルや譜面種別が出る。
- CI生成後の `master_metadata` 件数と実テーブル件数が一致しない。
- CI生成後の `source_snapshots` がWikiのみなら1件、公式プレー可否込みなら2件、新曲リスト込みなら3件、DDR WORLD公式譜面込みなら4件ではない。
- CI生成後の `master_metadata.source_hash` と `source_snapshots.content_hash` が一致しない。
- CI生成後の `master_metadata.source_url` と `source_snapshots.source_url` が一致しない。
- CI生成後の `master_metadata.official_source_hash` と公式 `source_snapshots.content_hash` が一致しない。
- CI生成後の `master_metadata.official_source_url` と公式 `source_snapshots.source_url` が一致しない。
- CI生成後の `master_metadata` に必須キーがない、または必須値が空。
- CI生成後の確認済みCHALLENGE補正manifestの件数、hash、chart値、notesの確認元・取得日が一致しない。
- DDR WORLD snapshotが完了済みでない、全ページ終端が確認できない、source metadataまたはページhashが一致しない。
- DDR WORLD差分reportのsource metadata、件数、chart ID、最終レベルが実DBと一致しない。
- DDR WORLD差分reportの行がstatus contractへ一意に分類されない、status別件数が行数と一致しない、または除外理由がない。
- `unmatchable_gp_candidate`または`ambiguous_gp_candidate`が1件以上ある。
- chart ID重複、`song_id + play_style + difficulty` 重複、外部キー違反がある。

fixtureテストでは、セル結合、注記付きレベル、脚注リンク除去、曲名本文のアスタリスク保持、削除/限定/パック記号、SP/DP片方のみ、CHALLENGEなし、同名曲・同アーティスト、複数バージョン表を扱う。実HTMLの件数確認はネットワークに依存するため、通常テストには含めない。

## M5へ渡すもの

M4で渡してよいもの:

- 曲名、artist、BPM、出典、分類記号。
- 公式収録曲一覧由来の `free_play_available` / `grand_prix_play_available`。
- `song_id` と `chart_id`。
- SP/DP、難易度、レベルの譜面一覧。
- source hash と generator version。

M4ではまだ扱わないもの:

- OCR曲名の正規化。
- ファジーマッチ。
- 候補一覧と照合スコア。
- OCR結果から曲ID/譜面IDを一意に決める処理。
- 個人スコアDB保存。

## M5c developer collectorからの更新契約

M5c-1のdeveloper-only collectorはparser、schema、writerを再実装せず、`python -X utf8 -m master` と `python -X utf8 -m master.inspect` をprocess境界で再利用する。既存targetが非空ならnetwork/build前にinspectionし、M4互換でないfileまたはdirectoryを拒否する。0 byte fileは明示placeholderとして扱い、成功時だけ置換できる。

buildはOS temporary directoryのstagingへ出力し、staging inspectionがversion、source hash、song/chart/GP件数を返した後にだけtarget親directoryを作る。stagingをtarget directory内のpublish fileへcopyし、同一directory内のatomic renameでtargetへ公開する。build、inspection、cancel、publishの失敗時は既存targetまたは0 byte targetを維持し、新規target、temporary staging/summary/publish file、新規に作った空parentを残さない。部分DBはdiagnosticとして保持しない。

この入口は開発者の明示操作専用であり、通常viewer起動、Release、CI artifact生成を変更しない。実network成功は通常testのmerge条件にせず、fake process/publisherで成功・各段階失敗・取消・既存target・0 byte target・publish失敗を固定する。
