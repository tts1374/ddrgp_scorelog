# ストレージI/O仕様

ローカル素材、PoC出力、manifest、ログ、DB、将来の本番保存物の置き場とGit管理方針を定義する。AGENTS.md のプロジェクトルールを設計資料として補強する。

## 基本方針

- スクリーンショット画像はGit管理しない。
- `samples/screenshots/metadata.csv` はGit管理しない。
- PoC出力は原則 `data/` 配下へ出す。
- 解析ログは原則 `logs/` 配下へ出す。
- ローカルDBはGit管理しない。
- 既存のローカル素材や生成物を削除・移動するときは、目的と対象を明確にする。

## Git管理するもの

- Python PoCコード
- テストコード
- README
- `docs/`
- サンプル用の空READMEや例示CSV
- 設計資料
- CI設定
- 将来のアプリコード

## Git管理しないもの

- `samples/screenshots/organized/`
- `samples/screenshots/metadata.csv`
- `data/`
- `logs/`
- ローカルDB
- 実キャプチャ画像
- 失敗時キャプチャ画像
- OCR前処理画像
- PoC解析ログ

## 入力素材

### スクリーンショット

配置:

```text
samples/screenshots/organized/
```

用途:

- 分類評価
- ROI確認
- OCR前処理確認
- regression fixture

Git管理しない。

### metadata

配置:

```text
samples/screenshots/metadata.csv
```

用途:

- `organized_file`
- `screen_type`
- score expected values
- judgment expected values

Git管理しない。列定義はREADMEや設計資料で管理する。

### metadata example

配置:

```text
samples/metadata.example.csv
```

用途:

- 入力列の例示。

Git管理してよい。

## PoC出力

### metadata mode

既定:

```text
data/vision_poc/
```

主な出力:

- `results.csv`
- `summary.json`
- `misclassifications.md`
- `result_events.csv`
- `result_events_summary.json`
- `score_ocr.csv`
- `score_ocr_summary.json`
- `ocr_roi_report.md`
- `ocr_expected_coverage.md`
- `ocr_expected_template.csv`
- `rois/`
- `ocr/`

### timestamped mode

既定:

```text
data/vision_poc_timestamped/
```

追加出力:

- `frame_manifest.csv`

### manifest mode

既定:

```text
data/vision_poc_manifest/
```

用途:

- timestamped または dry-run の manifest 再読込結果。

### dry-run capture provider

既定:

```text
data/vision_poc_capture_dry_run/
```

主な出力:

- `frames/`
- `frame_manifest.csv`

制約:

- `--capture-dry-run-output` は `data/` 配下に限定する。

## manifest

manifest はフレーム列を再実行可能にするCSV。

最小列:

- `image_path`
- `timestamp_ms`

任意列:

- `screen_type`
- expected columns
- 補助列

Git管理しない。ただし仕様と例はdocsに書く。

## ログ

PoCログ:

```text
logs/
```

本番アプリログ候補:

```text
%LOCALAPPDATA%/ddrgp_scorelog/logs/
```

本番失敗画像候補:

```text
%LOCALAPPDATA%/ddrgp_scorelog/failed-captures/
```

最終パスは未決。

## ローカルDB

マスタDB候補:

```text
ddrgp-master.sqlite
```

M4初期実装のローカル生成先:

```text
data/master/ddrgp-master.sqlite
```

個人スコアDB候補:

```text
ddrgp-scores.sqlite
```

M8の保存予定レコードプレビューでは、まず in-memory SQLite fixtureで `plays` 最小スキーマとrow contractを確認する。実ファイルDBを生成する場合は必ず `data/` 配下に置き、Git管理しない。

M8のscore DB write previewでは、保存予定レコードだけを新規 in-memory SQLite `plays` テーブルへinsertし、`m8_score_db_write_preview.*` としてpreview `schema_version=1`、`created_by_preview=tools.vision_poc.m8_score_db_preview`、insert対象件数、insert後件数、除外件数、代表行を確認する。これは実ファイルDB生成ではなく、ローカルDBファイルは作らない。SQLite側の `preview_metadata` 表はpreview生成物識別用の軽量表であり、正式マイグレーションではない。

M8のscore DB file output previewでは、`--m8-score-db-output data\...\ddrgp-scores.sqlite` を明示した場合だけ、保存予定レコードを指定された新規SQLiteファイルへinsertする。出力先は `data/` 配下に限定し、`data/` 外や既存ファイルへの書き込みは拒否する。実ファイルDBには `PRAGMA user_version=1` と `preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview` を設定し、summary/reportの `schema_version=1` と `created_by_preview` に一致させる。summary/reportの `database_schema_version`、`database_preview_metadata`、`database_plays_row_count` は実DBから読み戻した診断欄で、`database_readback_matches_preview_contract`、`database_readback_mismatch_reasons`、`database_plays_row_count_matches_insert_counts`、`database_plays_row_count_mismatch_reasons` はreadback値とpreview識別契約またはinsert件数の一致診断として扱い、定数として出すpreview識別欄とは分けて扱う。`m8_score_db_file_output_preview.json` / Markdown はpreview DBへのinsert件数とpreviewスキーマ識別の確認であり、本番DB保存成功、曲ID/譜面ID確定、保存値確定として扱わない。生成したDBファイルはローカルDBとしてGit管理しない。

開発中に生成したDB、取得元HTML snapshot、解析ログはGit管理しない。配布用マスタDBはGitHub Releases成果物として扱う。

## 削除・移動のルール

削除または移動前に確認すること:

- 対象がローカル素材か生成物か。
- 再生成可能か。
- metadata と画像の対応が壊れないか。
- `data/` や `logs/` の掃除で十分か。

原則:

- コード変更のついでにローカル素材を削除しない。
- PoC出力の削除は目的を明確にして行う。
- Git管理外ファイルはコミット対象にしない。

## 今後決めること

- 本番アプリの正式なローカルデータ保存先
- 失敗画像の保存期間と掃除方法
- ログローテーション
- DBバックアップ方針
- manifest dry-run 出力を本番でも残す期間
