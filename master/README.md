# マスタDB生成

BEMANIWiki の DanceDanceRevolution GRAND PRIX 全曲リストを取得し、配布用SQLiteマスタDBを生成するためのM4入口です。現時点では、ネットワークに依存しないfixtureテストでHTMLテーブル解析とSQLiteスキーマを固定する初期実装です。

## Source

2026-07-04時点で確認した取得元は以下です。

```text
https://bemaniwiki.com/index.php?DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88
```

対象ページには、`分類 / 曲名 / アーティスト / 出典 / BPM / MV/St / SINGLE / DOUBLE` の2段ヘッダを持つ楽曲リスト表が複数あります。パーサはこの表だけを対象にし、セル結合されたバージョン見出しと譜面レベル列を展開します。

譜面レベルは raw 表記を `raw_level` に保持しつつ、整数 `level` は最初に現れる数字列から取得します。これにより `10(旧9)`、`10;`、`[SA] 12` のような注記付き表記で数字を連結しません。`[SA]` などショックアローを示す表記は `shock_arrow` に反映します。

## Usage

ローカルHTML snapshotから生成:

```powershell
python -m master --input data\master\source.html --output data\master\ddrgp-master.sqlite
```

現在の取得元URLから直接取得して生成:

```powershell
python -m master --output data\master\ddrgp-master.sqlite
```

生成DB、取得元snapshot、解析ログはGit管理しません。ローカル生成物は原則 `data/` 配下に置きます。

## Tables

- `songs`: 楽曲単位。曲名、アーティスト、分類バージョン、出典、BPM、MV/St、分類記号を保持する。
- `charts`: 譜面単位。`song_id`、`play_style`、`difficulty`、`level`、元レベル表記、限定/削除候補フラグを保持する。
- `master_metadata`: `master_version`、`source_url`、`generated_at`、`generator_version`、`source_hash`、件数を保持する。
- `source_snapshots`: 取得元URL、取得時刻、HTML hash、parser version、HTML本文を保持する。

## Current Boundaries

- M4ではマスタDB生成までを扱い、曲名正規化、ファジーマッチ、候補スコア、一意照合はM5へ残します。
- GitHub Actions、Releases配布、定期実行は未実装です。
- BEMANIWiki の表構造は変わり得るため、本番取得前にfixtureと実HTMLの両方で件数・ヘッダ検出を確認します。
- `song_id` と `chart_id` は現時点ではHTML由来テキストから作る安定hashです。将来、配布互換性が必要になった段階でID互換方針を別途固定します。
- 同じ曲名・同じアーティストは同じ `song_id` として扱います。同一 `chart_id` の譜面行が食い違う場合は、静かな上書きではなく生成失敗として扱います。
