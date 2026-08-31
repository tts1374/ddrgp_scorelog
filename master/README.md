# マスタDB生成

BEMANIWiki の DanceDanceRevolution GRAND PRIX 全曲リスト／新曲リスト、公式収録曲一覧、DDR WORLD公式楽曲一覧を取得し、配布用SQLiteマスタDBを生成する入口です。公式ページの曲名・アーティストを正本にし、譜面レベルはDDR WORLD公式を優先し、BEMANIWikiを補完情報として扱います。

## Source

譜面情報の取得元は以下です。

```text
https://bemaniwiki.com/index.php?DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88
```

新曲リストにしかない曲のレベル取得元:

```text
https://bemaniwiki.com/?DanceDanceRevolution+GRAND+PRIX/%E6%96%B0%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88
```

公式の曲名・アーティスト取得元:

```text
https://p.eagate.573.jp/game/eacddr/konaddr/info/mlist.html
```

公式の譜面レベル取得元（`filter=7&filtertype=0&playmode=2` を固定）:

```text
https://p.eagate.573.jp/game/ddr/ddrworld/music/index.html?filter=7&filtertype=0&playmode=2
```

DDR WORLD公式楽曲一覧は、#193で固定された単一並列なし・自動retryなし・空ページ終端のsnapshot契約で全ページを取得します。`--ddrworld-input` を指定すると、その契約で完了したsnapshot directoryを入力できます。既定の直接生成では同じページ取得をメモリ上で行い、取得HTMLやsnapshotをリポジトリへ保存しません。

M4のDDR WORLD parserは、snapshot pageの現行`table.table-ui`とlegacy`table#data_tbl`の両方を受理します。legacy形式は`td.difficult`のSP/DP 9セル、現行形式はSP/DPのdifficulty containerを必須とし、両形式とも各セルの空値・非整数・欠落・重複を譜面なしとして黙って通さず、生成を失敗させます。有効な公式levelは1〜19の1〜2桁の数字だけで、譜面なしは公式表記の`-`だけを許可します。

公式のアーティスト欄が空の場合は空のまま保存します。Wiki側のアーティストや版権元名へのフォールバックは行いません。全曲リストと新曲リストの譜面行は同じ曲へ統合し、既存のWiki由来 `song_id` を可能な限り維持します。

### 確認済みCHALLENGE欠落の局所補正

通常の譜面表にCHALLENGEがない一方でプレー可能と確認済みの34曲について、SP/DP計68譜面だけを生成時に局所補正します。レベルと確認元は、2026-07-25取得のDDR WORLD楽曲一覧snapshotで確認した25曲50譜面と、2026-08-09取得のBEMANIWiki楽曲パック一覧で確認した9曲18譜面です。

補正は同じ `song_id + play_style + CHALLENGE` の譜面がない場合だけ追加します。譜面情報の優先順位はDDR WORLD公式、BEMANIWiki、確認済みCHALLENGE補正の順です。公式に同じCHALLENGE譜面があれば公式レベルを採用し、公式にない譜面ではWiki由来または確認済み補正を維持します。確認値と異なるWiki既存譜面がある場合は生成を失敗させます。追加chartの `notes` には確認元URLと取得日を保持し、`master_metadata` の `confirmed_challenge_supplement_json` でchart ID、song ID、曲名、SP/DP、レベル、確認元URL、取得日を譜面単位で追跡します。manifest件数とhashは `confirmed_challenge_chart_count` / `confirmed_challenge_supplement_hash` に記録し、`master.inspect` で実chart、notes、manifest、hashの整合を検査します。

対象ページには、`分類 / 曲名 / アーティスト / 出典 / BPM / MV/St / SINGLE / DOUBLE` の2段ヘッダを持つ楽曲リスト表が複数あります。パーサはこの表だけを対象にし、セル結合されたバージョン見出しと譜面レベル列を展開します。

譜面レベルは raw 表記を `raw_level` に保持しつつ、整数 `level` は最初に現れる数字列から取得します。これにより `10(旧9)`、`10;`、`[SA] 12` のような注記付き表記で数字を連結しません。`[SA]` などショックアローを示す表記は `shock_arrow` に反映します。

曲名やartistなどの表セルにある脚注リンクは、リンク本文が `*2` のような脚注番号だけの場合にマスタ本文から除外します。曲名本文に含まれる `neko*neko` のようなアスタリスクは残します。

公式収録曲一覧の `グランプリプレー` 列に `〇` がある曲だけを、通常のM5候補として扱います。マスタDBには対象外曲も保持しますが、`songs.grand_prix_play_available` で候補から除外できます。公式リストとWiki譜面表の突合状態は `official_availability_match` に残します。

公式の `グランプリプレー` 列に `〇` がある行がWiki側に存在しない場合も、公式の曲名・アーティスト・プレー可否だけで `songs` に追加します。この場合は `official_availability_match=official_only` とし、Wikiから取得できる譜面行がないため `charts` は作成しません。

公式リストへ突合できた曲は、曲名/アーティスト名を公式表記へ寄せます。Wiki側に `RËVOLUTIФN` / `TËЯRA`、公式側に `RЁVOLUTIФN` / `TЁЯRA` のような表記差がある場合は、alias正規化で `alias_title_artist` として突合し、公式表記を `songs.title` / `songs.artist` に保存します。差分のあるWiki表記は `song_aliases` に `wiki_source` として保存し、ローカルmetadataや旧表記からの解決補助に使えます。

### DDR WORLD公式譜面の統合

譜面の一意キーは `song_id + play_style + difficulty` です。まず公式収録曲一覧の `grand_prix_play_available=true` でGP対象曲の境界を確定し、その境界内でDDR WORLD公式のSP/DP・難易度・レベルを統合します。Wikiに同じ譜面があれば公式レベルへ置き換え、公式にだけある譜面はGP対象曲に限って追加します。DDR WORLDにだけ存在する曲は、公式楽曲一覧だけを根拠にGP対象曲へ昇格しません。

対応付けは、曲名+アーティストの一意一致、既存aliasの一意一致、曲名の一意一致の順で行います。reportの各行は、`official_override`、`official_only`、`wiki_only`、`supplement_only`、`excluded_non_gp`、`world_only_outside_gp`、`unmatchable_gp_candidate`、`ambiguous_gp_candidate`のいずれか1つへ分類します。

DDR WORLDに存在しても既存のプレー可否判定元とBEMANIWikiのどちらからもGP対象と確認できない譜面は、`world_only_outside_gp`としてmasterへ追加しません。既知の曲へ一意対応でき、既存判定でGP対象外の譜面は`excluded_non_gp`として公式値を統合しません。どちらも理由付きの正常な除外です。

GP対象候補の対応候補が0件なら`unmatchable_gp_candidate`、複数件なら`ambiguous_gp_candidate`として推測で統合しません。この2 statusは0件必須で、1件以上あるDBは`master.inspect`が検証を失敗させます。`level_changed`と`level_unchanged`は`official_override`の内訳としてreportに保存します。

## Usage

ローカルHTML snapshotから生成:

```powershell
python -X utf8 -m master --input data\master\source.html --output data\master\ddrgp-master.sqlite
```

現在の取得元URLから直接取得して生成:

```powershell
python -X utf8 -m master --output data\master\ddrgp-master.sqlite
```

ローカル公式収録曲一覧snapshotを使う場合:

```powershell
python -X utf8 -m master --input data\master\source.html --official-input data\master\official-mlist.html --output data\master\ddrgp-master.sqlite
```

新曲リストのsnapshotもローカル入力にする場合:

```powershell
python -X utf8 -m master --input data\master\source.html --new-song-input data\master\new-song-list.html --official-input data\master\official-mlist.html --output data\master\ddrgp-master.sqlite
```

完了済みDDR WORLD snapshotを明示入力する場合:

```powershell
python -X utf8 -m master --input data\master\source.html --new-song-input data\master\new-song-list.html --official-input data\master\official-mlist.html --ddrworld-input data\ddrworld_music_snapshot\<snapshot-id> --output data\master\ddrgp-master.sqlite
```

生成DBを検査して、artifact用summaryを出力:

```powershell
python -X utf8 -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json --merge-report data\master\ddrworld-merge-report.json
```

生成DB、取得元snapshot、解析ログはGit管理しません。ローカル生成物は原則 `data/` 配下に置きます。

## GitHub Actions

`.github/workflows/build-master-db.yml` で、手動実行と週次定期実行のマスタDB生成を行います。

workflowでは、ネットワークに依存しないfixtureテストを通した後、Wiki、公式収録曲一覧、DDR WORLD公式楽曲一覧の実HTMLから `data/master/ddrgp-master.sqlite` を生成し、`python -X utf8 -m master.inspect` で `master_metadata` とテーブル件数の整合、source snapshot件数とhashを検査します。生成DB、`master-summary.json`、DDR WORLD譜面差分reportは `ddrgp-master-<run_number>` artifact としてアップロードし、リポジトリにはコミットしません。

`master.inspect` は、必須metadataキー、`songs` / `charts` の実件数、確認済みCHALLENGE補正manifestとchartの対応、`source_snapshots` がWikiのみなら1件、公式込みなら2件、新曲リスト込みなら3件、DDR WORLD公式譜面込みなら4件であること、各source hashとsource URLがmetadataとsnapshotで一致すること、chart ID重複・曲+style+difficulty重複・外部キー違反がないこと、DDR WORLD差分reportの全行がstatus contractへ一意に分類されて件数と一致すること、最終レベルが一致すること、Stop対象statusが0件であることを検査します。`master-summary.json` にはテーブル件数、補正chart件数とhash、snapshot件数、各source hash、snapshot側のsource URL、parser version、公式プレー可否の突合件数、DDR WORLD差分件数を出力し、artifact単体でも生成元を確認できるようにします。

Releases配布はまだ未実装です。まずはartifactで生成結果と取得元構造変化の検出を確認し、安定後にReleases配布を別フェーズで追加します。

## Tables

- `songs`: 楽曲単位。曲名、アーティスト、分類バージョン、出典、BPM、MV/St、分類記号、公式フリープレー可否、公式グランプリプレー可否を保持する。
- `charts`: 譜面単位。`song_id`、`play_style`、`difficulty`、`level`、元レベル表記、限定/削除候補フラグを保持する。
- `song_aliases`: 公式表記へ寄せた際のWiki由来表記差を保持する。
- `master_metadata`: `master_version`、Wiki全曲リスト／新曲リスト／公式リスト／DDR WORLD公式楽曲一覧のsource URL・hash、DDR WORLD snapshot ID・取得ページ数・曲数・譜面数・差分report、確認済みCHALLENGE補正manifest・hash・件数、`generated_at`、`generator_version`、件数を保持する。
- `source_snapshots`: 取得元URL、取得時刻、HTMLまたはDDR WORLD全ページ連結本文のhash、parser version、本文を保持する。

自動生成時の `master_version` は、存在する入力snapshotのhashを `primary` → `new-song` → `official` → `ddrworld` の固定順序と種別ラベルで並べ、確認済みCHALLENGE補正manifestのhashを続けて計算する。CLIで `--master-version` を指定した場合は、その明示値を使用する。

## Current Boundaries

- マスタDB生成と公式canonical／プレー可否付与までを扱い、ファジーマッチ、候補スコア、一意照合は別責務に残します。
- GitHub Actions による手動・週次artifact生成入口は追加済みです。Releases配布は未実装です。
- BEMANIWikiとDDR WORLD公式楽曲一覧の表構造は変わり得るため、本番取得前にfixtureと実HTMLの両方で件数・ヘッダ検出を確認します。
- 脚注リンクは曲名本文に混ぜず、本文としてのアスタリスクは残します。
- `song_id` と `chart_id` は現時点ではHTML由来テキストから作る安定hashです。将来、配布互換性が必要になった段階でID互換方針を別途固定します。
- 同じ曲名・同じアーティストは同じ `song_id` として扱います。同一 `chart_id` の譜面行が食い違う場合は、静かな上書きではなく生成失敗として扱います。
