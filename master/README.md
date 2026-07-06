# マスタDB生成

BEMANIWiki の DanceDanceRevolution GRAND PRIX 全曲リストと公式収録曲一覧を取得し、配布用SQLiteマスタDBを生成するためのM4入口です。現時点では、ネットワークに依存しないfixtureテストでHTMLテーブル解析、公式プレー可否突合、SQLiteスキーマを固定する初期実装です。

## Source

譜面情報の取得元は以下です。

```text
https://bemaniwiki.com/index.php?DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88
```

対象ページには、`分類 / 曲名 / アーティスト / 出典 / BPM / MV/St / SINGLE / DOUBLE` の2段ヘッダを持つ楽曲リスト表が複数あります。パーサはこの表だけを対象にし、セル結合されたバージョン見出しと譜面レベル列を展開します。

譜面レベルは raw 表記を `raw_level` に保持しつつ、整数 `level` は最初に現れる数字列から取得します。これにより `10(旧9)`、`10;`、`[SA] 12` のような注記付き表記で数字を連結しません。`[SA]` などショックアローを示す表記は `shock_arrow` に反映します。

曲名やartistなどの表セルにある脚注リンクは、リンク本文が `*2` のような脚注番号だけの場合にマスタ本文から除外します。曲名本文に含まれる `neko*neko` のようなアスタリスクは残します。

グランプリプレー可否の取得元は以下です。

```text
https://p.eagate.573.jp/game/eacddr/konaddr/info/mlist.html
```

公式収録曲一覧の `グランプリプレー` 列に `〇` がある曲だけを、通常のM5候補として扱います。マスタDBには対象外曲も保持しますが、`songs.grand_prix_play_available` で候補から除外できます。公式リストとWiki譜面表の突合状態は `official_availability_match` に残します。

## Usage

ローカルHTML snapshotから生成:

```powershell
python -m master --input data\master\source.html --output data\master\ddrgp-master.sqlite
```

現在の取得元URLから直接取得して生成:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
```

ローカル公式収録曲一覧snapshotを使う場合:

```powershell
python -m master --input data\master\source.html --official-input data\master\official-mlist.html --output data\master\ddrgp-master.sqlite
```

生成DBを検査して、artifact用summaryを出力:

```powershell
python -m master.inspect data\master\ddrgp-master.sqlite --summary data\master\master-summary.json
```

生成DB、取得元snapshot、解析ログはGit管理しません。ローカル生成物は原則 `data/` 配下に置きます。

## GitHub Actions

`.github/workflows/build-master-db.yml` で、手動実行と週次定期実行のマスタDB生成を行います。

workflowでは、ネットワークに依存しないfixtureテストを通した後、Wikiと公式の実HTMLから `data/master/ddrgp-master.sqlite` を生成し、`python -m master.inspect` で `master_metadata` とテーブル件数の整合、source snapshot件数とhashを検査します。生成DBと `master-summary.json` は `ddrgp-master-<run_number>` artifact としてアップロードし、リポジトリにはコミットしません。

`master.inspect` は、必須metadataキー、`songs` / `charts` の実件数、`source_snapshots` がWikiのみなら1件、公式プレー可否込みなら2件であること、Wiki/公式のsource hashとsource URLがmetadataとsnapshotで一致することを検査します。`master-summary.json` にはテーブル件数、snapshot件数、source hash、snapshot側のsource URL、parser version、公式プレー可否の突合件数を出力し、artifact単体でも生成元を確認できるようにします。

Releases配布はまだ未実装です。まずはartifactで生成結果と取得元構造変化の検出を確認し、安定後にReleases配布を別フェーズで追加します。

## Tables

- `songs`: 楽曲単位。曲名、アーティスト、分類バージョン、出典、BPM、MV/St、分類記号、公式フリープレー可否、公式グランプリプレー可否を保持する。
- `charts`: 譜面単位。`song_id`、`play_style`、`difficulty`、`level`、元レベル表記、限定/削除候補フラグを保持する。
- `master_metadata`: `master_version`、`source_url`、`generated_at`、`generator_version`、`source_hash`、件数を保持する。
- `source_snapshots`: 取得元URL、取得時刻、HTML hash、parser version、HTML本文を保持する。

## Current Boundaries

- M4ではマスタDB生成と公式プレー可否付与までを扱い、曲名正規化、ファジーマッチ、候補スコア、一意照合はM5へ残します。
- GitHub Actions による手動・週次artifact生成入口は追加済みです。Releases配布は未実装です。
- BEMANIWiki の表構造は変わり得るため、本番取得前にfixtureと実HTMLの両方で件数・ヘッダ検出を確認します。
- 脚注リンクは曲名本文に混ぜず、本文としてのアスタリスクは残します。
- `song_id` と `chart_id` は現時点ではHTML由来テキストから作る安定hashです。将来、配布互換性が必要になった段階でID互換方針を別途固定します。
- 同じ曲名・同じアーティストは同じ `song_id` として扱います。同一 `chart_id` の譜面行が食い違う場合は、静かな上書きではなく生成失敗として扱います。
