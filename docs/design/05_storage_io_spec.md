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

正式個人スコアDBのファイル準備境界は `prepare_personal_score_db_file_for_write(path)` で扱う。新規DBファイルと0 byte空ファイルだけ正式初期schemaを作成でき、既存の正式DBは変更せずに互換確認だけ行う。M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補、SQLiteとして読めないファイル、ディレクトリは正式DBとして開かず、自動変更しない。この入口は本番insertや既定自動保存ではなく、正式DBファイルを開いてよいかを説明する前段である。検査済み結果は `personal_score_db_schema_inspection_diagnostic()` / `format_personal_score_db_schema_diagnostic_markdown()` / `personal_score_db_file_preparation_diagnostic()` で、path、status、拒否理由、必須table、metadata identity、初期化有無を人間が読める診断へ投影できるが、diagnostic生成自体はDBやファイルを追加変更しない。

CLI診断は `python -m tools.vision_poc --personal-score-db-diagnostic <path>` で標準出力へ出す。既定のinspect modeは読み取り専用で、`--personal-score-db-diagnostic-mode prepare-write` は新規DBファイルまたは0 byte空ファイルだけ正式初期schemaを作成する。出力はMarkdown既定で、`--personal-score-db-diagnostic-format json` も選べる。`--personal-score-db-diagnostic-output <path>` を指定した場合は、標準出力と同じ診断テキストをファイルへ保存する。出力先は `data/` 配下に限定し、Markdown format は `.md` / `.markdown`、JSON format は `.json` の拡張子だけを許可する。この出力は診断の保存だけであり、本番insert、既定自動保存、既存DB migration、低信頼度ログ本番保存には進まない。

`--personal-score-db-diagnostic-log-output <path>` を指定した場合は、診断1回につき1行のJSONLログを `logs/` 配下へappendする。拡張子は `.jsonl` に限定する。ログレコードは `log_schema_version=1`、`event_type=personal_score_db_diagnostic`、diagnostic mode、format、exit code相当status、対象DB path、任意の diagnostic output path、diagnostic dictを必須keyとして持つ。書き込み前に必須key、mode、format、event type、schema version、`diagnostic.is_compatible` と exit code / status の整合を検査する。これは標準出力や `data/` file outputとは別のDB診断ログ入口であり、本番insert、既定自動保存、既存DB migration、低信頼度ログ本番保存、source capture保存には進まない。`logs/` 外指定や `.jsonl` 以外はDB準備より前に拒否し、prepare-write対象の新規DBを作らない。将来の低信頼度ログ本番仕様や `analysis_logs.log_path` から参照する本番解析ログは、このdiagnostic JSONLとは別ファイルとして扱い、同じJSONLへ `event_type` だけで混在させない。

正式connectionへの最小write境界は `write_personal_score_db_save(connection, save_input)` で扱う。入力検査をDB準備より前に行い、確定済み入力だけを受け付ける。保存成功は `source_captures`、`plays`、`analysis_logs`、保存除外は `source_captures` と `analysis_logs` を同じtransactionでinsertする。途中失敗時は同じ呼び出しの全rowをrollbackする。

write前の `adapt_personal_score_db_save_input()` はpure functionであり、DB connectionや出力pathを受け取らない。戻り値が `ready` または `excluded` の場合だけ正式 `PersonalScoreDbSaveInput` を持ち、`unresolved` は不足・不正理由だけを返す。adapterの追加によって既定自動保存、実ファイル作成、既存DB migrationは開始しない。

明示ファイル保存は `save_personal_score_db_file(db_path, adapter_input)` で扱う。adapterを最初に実行し、`unresolved` はDBファイルや親ディレクトリの作成・変更前に理由付き結果として返す。`ready` / `excluded` だけ `prepare_personal_score_db_file_for_write(path)` と同じ拒否境界を通り、既存writerへ渡す。新規/0 byte/compatible正式DBだけを許可し、preview / unknown / metadata identity mismatch / manual migration候補 / 非SQLite / ディレクトリは自動修復せず拒否する。writer途中失敗では同じ呼び出しのsource/play/analysis rowをrollbackする。

この入口は呼び出し元がpathとadapter入力を明示する単発Python APIであり、実ファイルの既定自動保存、常駐監視、runner/CLI保存、既存DB migrationを開始しない。DB診断ファイルやdiagnostic JSONLも自動出力しない。

M8の保存予定レコードプレビューでは、まず in-memory SQLite fixtureで `plays` 最小スキーマとrow contractを確認する。実ファイルDBを生成する場合は必ず `data/` 配下に置き、Git管理しない。

M8のscore DB write previewでは、保存予定レコードだけを新規 in-memory SQLite `plays` テーブルへinsertし、`m8_score_db_write_preview.*` としてpreview `schema_version=1`、`schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema`、`created_by_preview=tools.vision_poc.m8_score_db_preview`、insert対象件数、insert後件数、除外件数、代表行を確認する。これは実ファイルDB生成ではなく、ローカルDBファイルは作らない。SQLite側の `preview_metadata` 表はpreview生成物識別用の軽量表であり、正式マイグレーションではない。`schema_contract_scope` と `production_schema_status` は、M8の `plays` が正式個人スコアDB候補列を持つ本番スキーマではなく、preview専用最小スキーマであることを示す読み間違い防止欄です。

M8のscore DB file output previewでは、`--m8-score-db-output data\...\ddrgp-scores.sqlite` を明示した場合だけ、保存予定レコードを指定された新規SQLiteファイルへinsertする。出力先は `data/` 配下に限定し、`data/` 外や既存ファイルへの書き込みは拒否する。実ファイルDBには `PRAGMA user_version=1` と `preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview`、`preview_metadata.schema_contract_scope=preview_minimal_plays`、`preview_metadata.production_schema_status=not_production_schema` を設定し、summary/reportの `schema_version=1`、`schema_contract_scope`、`production_schema_status`、`created_by_preview` に一致させる。summary/reportの `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns` は実DBから読み戻した診断欄で、`database_readback_matches_preview_contract`、`database_readback_mismatch_reasons`、`database_plays_row_count_matches_insert_counts`、`database_plays_row_count_mismatch_reasons`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` はreadback値とpreview識別契約、insert件数、preview最小 `plays` schemaの一致診断として扱い、定数として出すpreview識別欄とは分けて扱う。`m8_score_db_file_output_preview.json` / Markdown はpreview DBへのinsert件数とpreviewスキーマ識別の確認であり、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱わない。生成したDBファイルはローカルDBとしてGit管理しない。

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
