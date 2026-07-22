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

### WPF single-frame capture

既定:

```text
data/windows_capture/capture-<UTC>-<unique>/
```

主な出力:

- `frame.png`
- `frame_manifest.csv`
- `capture_metadata.json`

制約:

- output rootは `data/` の子directoryに限定する。
- current directoryまたはapp配置場所からrepository rootをcapture操作時に探索し、process cwdに関係なくrepository root直下の `data/windows_capture/` を使う。
- repository root探索失敗はwrite失敗として扱い、通常viewer起動やread-only閲覧を妨げない。
- captureごとに一意な新規directoryを使い、既存ファイルや既存capture directoryを上書きしない。
- 3ファイルは同一filesystem上のstaging directoryへ書き、directory rename後だけ完成出力として扱う。
- cancel、capture失敗、write失敗ではstagingを削除し、空画像、部分manifest、temp directoryを完成出力へ残さない。
- capture画像とmetadataはGit管理しない。
- capture出力を分類、OCR、正式save input、DBへ自動接続しない。

### WPF continuous capture session

既定:

```text
data/windows_capture/session-<UTC>-<unique>/
```

主な出力:

- `frames/frame-*.png`
- `frame_manifest.csv`
- `capture_session_metadata.json`

制約:

- session開始時は `data/` 直下の一意staging directoryだけを作り、明示停止かつ1frame以上の場合だけ最終directoryへrenameする。
- frameは連番で保存し、manifestの `image_path` はdirectory相対、`timestamp_ms` はstrictly increasingとする。
- 0frame、cancel、target closed、resize、device lost、write失敗はstagingを削除し、完成sessionとして公開しない。
- 既存capture/session directoryを上書きせず、画像、metadata、manifest実出力をGit管理しない。
- session出力を分類、OCR、identity、confirmed event、正式save input、DBへ自動接続しない。

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

正式connectionへの最小write境界は `write_personal_score_db_save(connection, save_input)` で扱う。入力検査をDB準備より前に行い、確定済み入力だけを受け付ける。保存成功は `source_captures`、`plays`、`analysis_logs`、保存除外は `source_captures` と `analysis_logs` を同じtransactionでinsertする。ready入力の明示 `duplicate_key` はDB準備後・source insert前に既存 `plays` へ照会し、衝突時はplayをinsertせず、sourceと `skipped/duplicate/duplicate_key_already_saved` のanalysisだけを同じtransactionで記録する。途中失敗時は同じ呼び出しの全rowをrollbackする。

write前の `adapt_personal_score_db_save_input()` はpure functionであり、DB connectionや出力pathを受け取らない。戻り値が `ready` または `excluded` の場合だけ正式 `PersonalScoreDbSaveInput` を持ち、`unresolved` は不足・不正理由だけを返す。adapterの追加によって既定自動保存、実ファイル作成、既存DB migrationは開始しない。

明示ファイル保存は `save_personal_score_db_file(db_path, adapter_input)` で扱う。adapterを最初に実行し、`unresolved` はDBファイルや親ディレクトリの作成・変更前に理由付き結果として返す。`ready` / `excluded` だけ `prepare_personal_score_db_file_for_write(path)` と同じ拒否境界を通り、既存writerへ渡す。新規/0 byte/compatible正式DBだけを許可し、preview / unknown / metadata identity mismatch / manual migration候補 / 非SQLite / ディレクトリは自動修復せず拒否する。duplicate collisionは結果を `excluded` / `written=true` / `play_id=null` とし、新しい一意なsource capture / analysisだけを残す。同一IDの完全再送は冪等化せず、writer途中失敗では同じ呼び出しのsource/play/analysis rowをrollbackする。

この入口は呼び出し元がpathとadapter入力を明示する単発Python APIであり、実ファイルの既定自動保存、常駐監視、既存DB migrationを開始しない。DB診断ファイルやdiagnostic JSONLも自動出力しない。

CLIからは `--personal-score-db-save-input <utf8-json>` と `--personal-score-db-save-database <sqlite>` を必須ペアとして明示した場合だけ、同じAPIを1回呼ぶ。通常M5候補観測の `--m5-jacket-catalog` が混在する場合は、入力JSON読込やDB準備より前に拒否し、無視したまま正式saveへ進めない。JSON外部形式は `input_schema_version=1` とし、`candidate_material`、source/analysis値、object/nullの `formal_play`、object/nullの `exclusion` を分離する。全階層の必須key、未知key、object/null、bool/integer/number/string型はファイル準備前に検査し、boolをintegerとして通さない。`candidate_material` と `timestamp_ms` は由来情報のまま保持し、正式playへコピーしない。

終了コードはtransaction完了した `ready` / `excluded` が0、adapterの `unresolved` が1、入力/DB拒否が2とする。結果JSONはDB path、adapter status、written、任意のplay ID、source capture ID、analysis ID、理由を持つ。duplicate collisionも終了コード0で `adapter_status=excluded`、`reasons=[duplicate_key_already_saved]` として区別する。CLI専用output file、diagnostic JSONL、低信頼度ログは生成せず、通常PoC、timestamped/manifest runner、`--m8-score-db-output` へ接続しない。

`--personal-score-db-save-input-validate <utf8-json>` は保存CLIと同じloaderとadapterだけを各1回実行する単独modeである。DB pathを受け取らず、DBファイル、親ディレクトリ、`data/`、`logs/`、diagnostic outputを作成・変更しない。結果はvalidation schema version、入力path、adapter status、正式save input構築可否、理由だけをJSONで返し、正式値や候補材料を再掲しない。ready/excludedは0、unresolvedは1、不正JSON/schemaまたは他option混在は2とする。DBを開かないため、DB互換性、既存duplicate collision、並行writer、実保存成功は保証しない。

`--personal-score-db-save-input-validate-output <path>` はvalidation inputとの必須ペアで、同じvalidation結果投影をレビューreceiptとして `data/` 配下の新規 `.json` へ1件だけ保存する。UTF-8 BOMなし、LF、固定key順、末尾改行とし、既存ファイルを上書きしない。output path、拡張子、必須ペア、他mode排他は入力読込と出力作成より先に検査する。invalid input schemaを含め、receiptに記録するstatusと終了コードは標準出力/標準エラーのvalidation結果と同じに保つ。receiptは正式値、候補材料、template本文、DB情報を持たず、レビュー承認、DB互換性、duplicate非衝突、並行writer安全性、実保存成功を保証しない。outputを指定しない従来validationは引き続き `data/` を含む出力を作成・変更しない。

`--personal-score-db-save-input-template <path>` は、`data/` 配下の新規 `.json` へ空のschema version 1 review templateを1件だけ生成する単独modeである。既存ファイルを上書きせず、UTF-8 BOMなし、LF、固定key順、末尾改行で書く。出力はtemplate JSON以外のDB、`logs/`、画像、diagnosticを作らず、標準出力も生成path、template schema version、status、理由だけに限定する。metadata、M5/M7a、M8 preview、manifest、画像、DBは入力にせず、候補・相対時刻・duplicate keyを正式値へ転記しない。他optionとの混在、`data/` 外、`.json` 以外、既存出力は作成前に終了コード2で拒否する。

M8の保存予定レコードプレビューでは、まず in-memory SQLite fixtureで `plays` 最小スキーマとrow contractを確認する。実ファイルDBを生成する場合は必ず `data/` 配下に置き、Git管理しない。

M8のscore DB write previewでは、保存予定レコードだけを新規 in-memory SQLite `plays` テーブルへinsertし、`m8_score_db_write_preview.*` としてpreview `schema_version=1`、`schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema`、`created_by_preview=tools.vision_poc.m8_score_db_preview`、insert対象件数、insert後件数、除外件数、代表行を確認する。これは実ファイルDB生成ではなく、ローカルDBファイルは作らない。SQLite側の `preview_metadata` 表はpreview生成物識別用の軽量表であり、正式マイグレーションではない。`schema_contract_scope` と `production_schema_status` は、M8の `plays` が正式個人スコアDB候補列を持つ本番スキーマではなく、preview専用最小スキーマであることを示す読み間違い防止欄です。

M8のscore DB file output previewでは、`--m8-score-db-output data\...\ddrgp-scores.sqlite` を明示した場合だけ、保存予定レコードを指定された新規SQLiteファイルへinsertする。出力先は `data/` 配下に限定し、`data/` 外や既存ファイルへの書き込みは拒否する。実ファイルDBには `PRAGMA user_version=1` と `preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview`、`preview_metadata.schema_contract_scope=preview_minimal_plays`、`preview_metadata.production_schema_status=not_production_schema` を設定し、summary/reportの `schema_version=1`、`schema_contract_scope`、`production_schema_status`、`created_by_preview` に一致させる。summary/reportの `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns` は実DBから読み戻した診断欄で、`database_readback_matches_preview_contract`、`database_readback_mismatch_reasons`、`database_plays_row_count_matches_insert_counts`、`database_plays_row_count_mismatch_reasons`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` はreadback値とpreview識別契約、insert件数、preview最小 `plays` schemaの一致診断として扱い、定数として出すpreview識別欄とは分けて扱う。`m8_score_db_file_output_preview.json` / Markdown はpreview DBへのinsert件数とpreviewスキーマ識別の確認であり、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱わない。生成したDBファイルはローカルDBとしてGit管理しない。

開発中に生成したDB、取得元HTML snapshot、解析ログはGit管理しない。配布用マスタDBはGitHub Releases成果物として扱う。

## M5b jacket catalog

ローカルjacket catalogは repository root直下の `databases/` 配下の固定SQLite pathへ新規作成する。masterは `databases/ddrgp-master.sqlite`、catalogは `databases/jacket-catalog.sqlite` を正本とする。初回リリース向けcurrent schemaのversionは1で、専用identity、`PRAGMA user_version=1`、metadata schema version 1、exact tables/columns/constraints/index/foreign keyをstrictに検査する。current schemaとexact一致しない旧catalog、非catalog SQLite、破損catalog、正式個人スコアDB、M8 preview DB、M4 master DBは読み取り専用検査でunsupportedとして拒否し、作成、修復、migrationを行わない。

current referenceはmanual review revision/historyと、`jacket_feature_version/hash`、`title_line_feature_version/hash`、`composite_identity_version/hash`を全nullまたは全非nullの1組として保持する。通常observation ingestは完全な非null組を必須とし、既知version、lower SHA-256、UTF-8 NUL区切りcanonical hashを検査する。`(composite_identity_version, composite_identity_hash)`はcatalog全体で一意とし、read-only identity集合には`unresolved`、review待ち、確定、再割当、`reopen`、`rejected`をすべて含める。

current `ingest`は非空observation ID、artifact image bytes/hash、空title/artist、`unresolved`、session開始時のmaster version/source hash、catalog identity/schema/created-at、current extractor、完全なcomposite identityをcatalog変更前に検査する。同一observation ID・同一payloadは冪等、異payloadは拒否する。異なるobservation IDでも同じcomposite identityなら、review statusに関係なくtransaction内で既存reference receiptへ収束させ、2件目を作らない。新規rowはsong未割当、revision 0、manual provenance/history/candidateなしとする。

collectorの手動保存と明示opt-in自動保存は、artifact publish前にcurrent checkpointとcurrent catalogのcomposite identity集合を照合する。identity集合はcatalog identity/schema/created-atと同じread-only接続で検査し、`rejected`を含む全review状態を対象にする。checkpoint既存identityは新規観測を作らず既存receipt/retryへ留め、catalogだけにあるidentityはartifact/checkpointを作らない。自動保存はsession単位・既定OFFで、fresh/resume/stop時にOFFへ戻し、端末設定へ永続化しない。1 identityにつき自動試行は1回とし、失敗後は明示保存またはcatalog retryを使う。照合後の並行投入はcatalogの一意制約と冪等ingestで既存referenceへ収束させる。

projectionとmanual review、coverage、M5 feature loader、title/artist evaluationはcurrent catalogだけを受け入れる。projectionはversion 4でcurrent/stored state、revision、candidate、manual provenance、append-only historyに加え、artifact/checkpoint照合済みのversion付きunresolved candidate evaluationを返し、旧migration/capability fieldを持たない。candidate evaluationとCSV/JSON/Markdown reportはread-onlyで、exact/alias一意、曖昧、候補なし、低confidence、OCR失敗、artifact/master/catalog/extractor/identity不整合、review済み対象外を区別する。manual mutationはexpected revision/status/songをpreconditionにし、同一action ID・同一payloadだけを冪等成功とし、current row/historyを同じtransactionで更新する。candidate、expected song、OCR rawを確定songへ昇格しない。

title/artist OCR診断は同じstrict projection検証済みsourceだけを読み、profile別raw/status/confidence/candidate結果と代表contact sheetを`data/`配下へatomic生成する。Tesseract installed language不足は`m5c-title-artist-ocr-diagnostics-report-v1`の`ocr_unavailable` / `tesseract_language_unavailable_v1:<lang>`へ固定し、別languageへfallbackしない。診断前後でmaster/catalog hashとmanifest/source/crop/checkpoint fingerprintを照合し、変化時はreportをpublishしない。診断はcatalog writer、manual review transaction、artifact/checkpoint writerを呼ばず、schema、revision、history、source/cropを変更しない。

coverageは `data/` 配下の明示directoryへ `jacket_catalog_song_coverage.csv`、`jacket_catalog_coverage_summary.json`、`jacket_catalog_coverage.md` を生成する。確定songがないreferenceでもGP対象candidateは `needs_review` として数え、候補のない観測だけを未割当集計へ残す。current master/GP/current extractorを満たす `auto_confirmed` / `manual_confirmed` referenceだけをM5 matcherへ供給し、`rejected`、orphan、旧extractor、不正persisted featureを除外する。

catalog、observation artifact/checkpoint、source/crop画像、特徴量、review結果、coverageはローカル運用物とし、Git、CI artifact、Release、通常analysis logへ含めない。既存local DB/artifact/checkpoint/source/crop画像を削除、上書き、in-place repairしない。artifact manifest/checkpoint v1/v2、resume/retry状態機械はcatalog schema version再採番と独立して維持する。

PR #53 policyのproduction auto-confirmは、`data/`配下へ新規preflight planを出す既定dry-runと、
同じplanを明示するapplyを分離する。applyはcurrent schema version 1の既存rowだけを対象にし、
`auto_confirmed`、Master由来song/title/artist、confirmation source、version付きevidence JSONを既存列へ
保存する。schema追加やmigrationは行わず、manual historyをauto actionに流用しない。全対象は1つの
`BEGIN IMMEDIATE` transactionで処理し、state/revision/input drift、既存確定との競合、途中例外では
全件rollbackする。同一planのexact evidence再投入はno-opとする。

manual残件ODSは同じdry-run planから`data/`配下の新規`.ods`へatomic publishする。catalog、Master、
manual review stateを変更せず、同じplanからの再exportはbyte-identical、既存fileは上書きしない。
capture mismatchはexport対象外とし、生成ODSとplanはGit管理しない。ODS importは別の明示transaction
境界として後続PRへ分ける。

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

## Analysis artifact path contract

`tools.vision_poc.personal_score_db_analysis_artifacts` はversion 1のpure contractに加え、検査済みpayloadを明示された新規pathへ1件だけ生成する `write_analysis_detail_file()` を提供する。`analysis_logs.log_path` は空文字、またはリポジトリroot基準のPOSIX相対path `logs/analysis_details/**/*.json` とする。絶対path、`..`、backslash、`logs/` 外、別拡張子、既存outputをdirectory作成より前に拒否する。出力はUTF-8 BOMなし、LF、sort済みkey、末尾改行とし、同一directoryの完成済み一時ファイルをatomicに公開して部分JSONを残さない。

CLIは `--personal-score-db-analysis-detail-input <json>` と `--personal-score-db-analysis-detail-output <logs/analysis_details/...json>` の必須ペアだけで実行する。save、diagnostic、validation、template、receipt、通常PoC optionとの混在を副作用前に終了コード2で拒否し、成功は `status=created` / 終了コード0とする。DB、`data/`、failure image、通常PoC生成物は作成・変更せず、save workflowへ自動連鎖しない。

任意の失敗画像は詳細JSON内の `failure_image_path` で `logs/analysis_failures/**/*.{png,jpg,jpeg,webp}` を参照する。これは `log_path`、元フレーム用 `source_captures.source_path`、`data/` 配下のvalidation receipt、`logs/` 配下のDB diagnostic JSONLと相互代用しない。

retention classは `short=7日`、`standard=30日`、`indefinite=期限なし` とする。UTCの `basis_at` から `expires_at` を決定的に計算し、期限なしだけnullにする。同じ詳細JSONとそこから参照する失敗画像へ同じretention metadataを適用する。この契約は将来cleanupの判断材料だけであり、ファイル作成、削除、scheduler、起動時掃除を行わない。

## Capture save workflow output

continuous capture原本は `data/windows_capture/session-*/` に保持し、解析生成物は別の一意directory `data/capture_save_workflow/<session>-<id>/` に出力する。画像原本やmanifestを解析出力へ移動・上書きしない。出力directoryは既存Vision PoCのCSV/JSON/ROI artifactであり、正式DB、`source_captures` 本文、`analysis_logs.log_path`、DB diagnostic logの代用にしない。

正式DB pathとmaster DB pathはWPFの `連続取得・保存` ごとに明示し、capture-only操作には既定DB pathを導入しない。unconfirmed/rejectedと自動formal `unresolved` はDBを開かず、正式workflowへ進むconfirmed eventも既存file-save境界だけが新規/0 byte/compatible DBを準備する。`saved` transactionの後だけviewerが同じ正式DBをread-onlyで開き直す。

## Analysis artifactと正式saveの接続契約

現行のartifact CLIとsave CLIは独立操作のまま維持する。production接続は既存CLIの暗黙連鎖ではなく、`personal_score_db_workflow` の単発明示orchestration入口が担当する。入口はversion 1 workflow入力を受け、artifact payloadとstrict save inputを別objectのままloaderへ渡し、候補材料、analysis detail、正式play値を相互投影しない。

### 適用範囲

| adapter / DB結果 | play | artifact | `analysis_logs.log_path` |
|---|---:|---|---|
| `ready`、duplicate非衝突 | あり | 任意 | 生成時はartifact output path、未生成時は空文字 |
| 明示された低信頼度またはerrorの`excluded` | なし | 必須 | artifact output path |
| その他skipの`excluded` | なし | 任意 | 生成時はartifact output path、未生成時は空文字 |
| DB duplicate collision | なし | 任意 | 事前に生成済みならそのpath、なければ空文字 |
| `unresolved`またはinvalid input | なし | 生成しない | DB writeなし |

上流の保存候補は引き続き `confirmed_result=true` かつ `duplicate=false` である。表のDB duplicate collisionは、その境界通過後に正式 `duplicate_key` が既存playと衝突した場合だけを指す。artifact必須は「DBへplayを作る」条件ではなく、低信頼度/errorを再調査可能にする条件である。

### 順序と整合責任

順序候補のうち、save後にartifactを生成する方式はDBが存在しないfileを参照し得るため不採用、artifactを全検査より前に生成する方式はinvalid/非互換DBでも不要fileを残すため不採用とする。採用順序は次のとおり。

1. workflow optionとartifact output pathを副作用前に検査する。
2. artifact payloadとstrict save inputを独立にload/validateし、adapterを1回評価する。
3. artifact要否、共有する `analysis_id` / `source_capture_id` / 保存境界status、save inputの `analysis.log_path` と指定output pathの一致を検査する。正式play値、candidate material、analysis detail間の補完はしない。
4. 正式DBの存在種別とschema互換性を検査し、readyなら既存playに対するduplicate preflightを行う。このpreflightは利用者向けの予告分類であり、衝突時も処理を止めず、transaction内の既存preflightでsource/analysisを記録する。transaction内preflightとUNIQUE制約を置き換えない。
5. artifactが必要または明示されていれば、新規fileをatomic生成する。再試行時に既存fileがある場合は、UTF-8 JSONをstrict loadし、正規化したversion 1 payloadが今回入力と完全一致するときだけ `artifact_status=reused` とする。不一致、非JSON、unsafe pathは拒否し、上書き・削除しない。
6. artifactが生成済みまたは再利用済み、あるいは表で任意かつ未指定の場合だけ、既存の正式file saveを1回呼ぶ。writerはduplicateを再検査し、source、任意play、analysisを1 transactionでcommitする。

orchestration入口がartifact output pathと `analysis_logs.log_path` の一致を保証する。artifact writerはpath安全性とfile内容、DB writerは渡された `log_path` のschema制約とtransactionだけを担当し、どちらも相手の副作用を暗黙実行しない。

### Partial success、再実行、status

| 到達状態 | workflow status | 終了コード | 永続状態 | 再試行 |
|---|---|---:|---|---|
| 入力、path、共有値、adapterが不正 | `invalid` / `unresolved` | 2 / 1 | file/DBとも変更なし | 入力を修正 |
| DB非互換、artifact未生成 | `db_rejected` | 2 | file/DBとも変更なし | DBを選び直す |
| artifact生成失敗 | `artifact_failed` | 2 | DB未実行。完成artifactなし | 原因を除去して同じ入力で再試行 |
| artifact成功後にDB失敗 | `artifact_created_db_failed` | 2 | artifactは残り、今回のDB rowはrollback。新規/0 byte DBは初期schema準備済みの場合あり | fileを削除せず、同一payloadを`reused`としてDB段階を再試行 |
| 既存artifactが入力と不一致 | `artifact_conflict` | 2 | 既存file/DBとも変更なし | 新しいpathを選ぶか入力を正す |
| transaction完了（早期またはtransaction内duplicateを含む） | `saved` / `excluded` / `duplicate` | 0 | DB row群は原子的。duplicateはsource/analysisだけ、artifactは指定時だけ存在 | 完了。`play_id=null`を成功playと読まない |

終了結果は `workflow_status`、`artifact_status=not_requested|created|reused|failed|conflict`、`adapter_status`、`db_status`、既存save resultと同じID、理由、artifact path、DB pathを返す。正式play値、candidate material、analysis detail本文は結果へ再掲しない。利用者は終了コードだけでなく、`workflow_status`、`artifact_status`、`written`、`play_id`、artifact file、正式DB diagnosticを確認する。自動補償、artifact削除、既存file上書き、DB自動修復は行わない。

M9 WPFは `personal_score_db_workflow_app` を別processで起動する。このUI adapterはユーザーが選択したworkflow入力とDB pathだけを受け、入力内の `save_input.log_path` を既存orchestrationのartifact outputへ渡す。C#側にJSON save loader、DB writer、artifact writerを持たない。`saved` かつtransaction完了済みplayだけ同じread-only repositoryで再openし、通常の閲覧操作は引き続きwrite processを起動しない。

### 後続実装のfixture行列とacceptance criteria

- readyのartifactなし/あり、低信頼度とerrorのartifact必須、その他skipの任意、DB duplicate collisionの各分岐を固定する。
- unsafe path、入力不正、共有ID/status不一致、`log_path` 不一致をfile/DB副作用前に拒否する。
- DB非互換ではartifactを作らない。早期duplicateは停止せず既存writerへ渡し、transaction内再検査でもplayを作らずsource/analysisを記録する。raceでduplicateになった場合も生成済みartifactを保持する。
- artifact write失敗ではDBを呼ばず、DB失敗ではrowをrollbackしてartifactを残す。
- 同一payloadの既存artifactだけ再利用し、不一致fileを上書き・削除しない。
- loader、adapter、artifact writer、file saveの呼出回数を固定し、現行CLIのstatusと終了コードを変えない。
- 正式値、candidate material、analysis detail、receipt、DB diagnostic、failure image、source captureの責務分離をfixtureで検証する。

後続実装でも通常PoC、常駐監視、migration、backup、cleanup、並行writer制御、failure image生成へは接続しない。
## Migration backup and explicit execution boundary

正式個人スコアDB migrationは通常save、analysis artifact orchestration、diagnostic、DB openから暗黙実行しない。将来の専用CLI/APIだけが、source DB、target version、新規backup path、明示確認をすべて受け取って1回実行できる。dry-runとstatusはread-onlyで、DB、backup、`data/`、`logs/`を作成・変更しない。

backup pathはsourceと別pathで、既存file・directory・symlink相当の競合がなく、許可されたローカルbackup namespace内の新規fileに限定する。exclusive createで作成し、SQLite整合copy、flush、再open、identity/version/history/整合性検査、sourceとの対応確認が完了するまでsource DBを変更しない。既存backupやsourceを上書き・削除せず、backup作成途中の失敗はsource無変更として終了する。

実行順序は `inspect source read-only` → identity/history/path preflight → backup exclusive create/flush/verify → `BEGIN IMMEDIATE` → schema steps → migration履歴 → metadata version → `PRAGMA user_version` → transaction内検証 → commit → read-only再検査とする。backup検証前のsource writeは禁止する。commit前のtransaction失敗はrollbackする。commit失敗またはcommit後検証失敗はsource状態を推測せず `manual_recovery_required` とし、検証済みbackupを上書きせず保持する。

pure contractのstatus/終了コードは、`current` / `dry_run_ready` / `ready` / `completed` が0、`confirmation_required` が1、入力・互換性・path・partial state拒否が2、backup I/Oまたはmigration実行失敗が3である。`manual_recovery_required` はsourceが変更済みまたは変更有無を確定できない状態として扱い、検証済みbackupを使う人手復旧を促す。再実行時、既にtargetなら `current`、同じbackup pathが存在すればconflict、partial stateならmanual recovery拒否とし、暗黙の再開・repair・backup再利用をしない。

status/dry-run専用CLIはDB path、target version、明示backup pathの必須組だけを受ける。SQLiteはURI `mode=ro` で開き、backup pathは存在・source同一性・親directoryだけを観測する。JSON/Markdownは同じprojectionを使い、終了コードはpure contractの値を返す。DB、backup、`data/`、`logs/`を作成・変更せず、save/orchestration/diagnosticや通常PoCと混在させない。
