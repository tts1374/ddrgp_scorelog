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

## M10-2 local application storage boundary

M10-2では、DBの責務と実行環境をpathで固定する。Debugで明示されたdevelopment root、またはDebugのcurrent directory／Debug出力directoryから親方向にsource checkout（`databases/`とScore Viewer project）が検出できる場合だけdevelopmentとし、Releaseは常にproduction固定pathを使う。Releaseではrepository rootやapp配置場所の親を探索せず、両環境のDB pathをfallbackしない。

| 責務 | development | production | 初期化・更新責務 |
| --- | --- | --- | --- |
| M4 master DB | `databases/ddrgp-master.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\ddrgp-master.sqlite` | M4とは別のreference data set assetとしてread-only検証・セット更新 |
| M5b jacket reference catalog | `databases/jacket-catalog-release.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\jacket-catalog.sqlite` | M4とは別fileのstrict schema。developmentではbinding済みruntime catalog、productionではreference data setとしてread-only検証・セット更新 |
| 正式個人スコアDB | `databases/score.dev.db` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\score\score.db` | 固定pathのmissing／0 byteだけWPF側の正式schema初期化境界で初期化。既存formal DBは明示saveまたは確認済み個人スコアデータ復元以外で変更しない |
| 評価用DB | `databases/evaluation.db` | 既定pathなし | M10-3評価器だけが明示的に初期化・再実行 |

起動時はDB親directory、`data/`、`logs/`を作成し、M4 master DBとM5b jacket reference catalogのread-only検証に成功した場合だけ、現在の環境の固定score pathがmissingまたは0 byteのときWPF側の `PersonalScoreDbInitializer` が既存の正式schema、metadata、migration契約に従って初期schemaを作成する。既存の非空score DBはこの処理で開かず、後段のread-only検証へ進める。app-owned runtimeの明示saveも同じfile-preparation、adapter、transaction writer境界を使う。Release packageはrepository root、repository内module、外部Python executable、Tesseractをruntime依存にせず、認識資材はapp packageの`RuntimeAssets/`または`DDRGP_SCORE_VIEWER_RUNTIME_DATA`で明示したdata pathから解決する。`data/windows_capture/`、`data/capture_save_workflow/`、`logs/analysis_details/`、`logs/analysis_failures/`は再生成・退避可能なlocal outputであり、formal `plays`の代替ではない。

production起動時はlatest GitHub Releaseのreference data setを確認する。Release APIから同じReleaseにある`reference-set.json`、`ddrgp-master.sqlite`、`jacket-catalog.sqlite`のasset URLを解決し、manifestを先に取得する。manifestの`content_version`が現行と同じ場合はDB assetを取得せずno-opとし、古い場合はdowngradeを拒否する。新しい場合だけ3 assetを`data/`配下の一時directoryへ保存する。通信失敗、asset欠落、download中断、空き容量不足は現行reference data setを変更せず、score DBとsettingsにも触れない。

### 個人スコアデータのバックアップ・復元

データ管理画面の個人スコアデータバックアップは、固定score pathの現行正式schemaをread-onlyで検証した後、`plays`の履歴表示・自己ベスト算出に必要な値だけをUTF-8 BOMなし、LF、末尾改行のJSONへ出力する。形式は`ddrgp.personal-score-data`、現行`formatVersion=2`とし、バックアップのJSONには`plays`の個人プレー値、保存日時、正式ID、重複判定に必要な値、nullableな`ok` / `calories`だけを置く。旧`formatVersion=1`も復元入力として受け付け、両値は未取得の`NULL`として復元する。settings、master/catalog、jacket参照、source capture、解析ログ、診断ログは対象外であり、migration用SQLite backupとは別のファイル形式・保持契約である。

復元は選択ファイルをDBへ接続する前に形式、version、必須値、重複ID、正式schemaの制約に照らして検証する。未対応・破損・不正値はscore DBを変更せず拒否する。確認後の有効復元だけ、固定score pathの正式schemaを検証したSQLite transaction内で既存`plays`を置き換える。置換前の`analysis_logs`は旧playへの参照だけを切り離し、未解決を含むログと既存`source_captures`は保持する。バックアップに含めなかった取得元・解析情報は復元せず、既存schemaの外部キーを満たすための最小内部参照だけを復元したプレーごとにアプリが再構成する。insert件数をtransaction内で確認してからcommitし、失敗時はrollbackする。完了後は既存read-only repositoryで再読込し、履歴・自己ベストへ反映する。設定、master/catalog、既存のformal save workflow、Debug/Release境界は変更しない。

バックアップ作成・復元はデータ管理画面からのみ明示実行し、任意DB pathの読み込み、DB path変更、CSV/export、外部入力、repair、migration、自動実行を追加しない。保存・監視・更新・終了処理中は操作を開始しない。作成先は既存のユーザー選択directoryに限り、途中ファイルを残さず公開する。

### 二つのmaster DBのread-only inspection

M4 master DBは `songs`、`charts`、`song_aliases`、`master_metadata`、`source_snapshots`、metadataの必須値、song/chart件数、source URL/hash、optional source snapshotの整合を既存検査で確認する。M5b jacket reference catalogは別connectionで、`catalog_metadata`、`result_text_features`、`jacket_references`、`reference_candidates`、`reference_review_history`のtable identityとcolumn、`user_version=1`、catalog identity、unique index、foreign keyを確認する。どちらも `Mode=ReadOnly` と `PRAGMA query_only=ON`で開き、検査はファイルを変更しない。

両方の結果を保存開始前にそろえて確認し、どちらかが `missing`、`read不可`、`schema incompatible` の場合は理由をUIへ表示してcapture解析・正式saveを開始しない。capture後のworkflow直前にも同じ検査を再実行する。DBの任意path切替はUIから許可せず、現在の環境の固定pathだけを使用する。過去sessionのstatusや候補値をDBへ昇格しない。

### 評価用DBの初期化、退避、再実行

評価用DBはdevelopment専用で、WPF viewer、正式個人スコアDB、M4 master DB、M5b jacket reference catalogから分離する。M10-3評価器がschemaとinitializerを所有し、M10-2のWPFは評価用DBを開いたり自動初期化したりしない。

再実行は次の順序に固定する。

1. WPFと評価processを停止し、評価用DBへのwriterがないことを確認する。
2. `databases/evaluation.db` が存在する場合、`data/evaluation/backups/evaluation-<UTC timestamp>.db` という新規pathへcopyし、sourceとbackupのpath、file size、SQLite `PRAGMA integrity_check`をread-onlyで確認する。既存backupは上書き・削除しない。
3. backup確認後にだけ、M10-3評価器の明示initializerで`databases/evaluation.db`を初期化する。initializerがない場合は、既存DBを変更せず未実施として終了する。
4. 同じevaluation input、同じM4 master DB、同じM5b jacket reference catalogを明示して評価を再実行し、出力を新しい`data/`配下へ保存する。評価用DBを正式個人スコアDBのpathへ向けない。

この手順は自動backup、cloud backup、正式個人スコアDB migration、master DB取得を追加しない。評価DBのschema変更・initializer実装はM10-3の別契約で固定する。

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
- Debugの明示またはsource checkoutから検出したdevelopment root、またはReleaseの固定production data pathからcapture output rootを解決し、process cwdだけには依存しない。
- Releaseではrepository root探索を行わず、app data pathが解決できない場合はwrite失敗として扱い、通常viewer起動やread-only閲覧を妨げない。
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

通常の `監視開始` で対象window用sessionを開始する場合だけ、対応OS・runtime API・Windowsの同意がそろえばcapture borderless設定を適用する。拒否、非対応、未署名VeloPack packageでのcapability不足、権限取得失敗、API例外では枠ありsessionを継続し、capture outputのpath、manifest、timestamp、解析artifact、正式個人スコアDB、transaction、保存statusは変更しない。Debugのpicker captureはこの同意要求を行わない。

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

app runtimeのRelease logは`%LOCALAPPDATA%/DDRGpScoreViewer/logs/gp-score-log.log`へ出力し、`level_recognition`イベントにLevel画像認識のevent ID、status、認識桁、候補、距離、margin、適用閾値、理由を構造化JSONで記録する。診断値は正式個人スコアDBへ保存せず、ログにも画像を保存しない。

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

正式個人スコアDBのfile preparationは、app-owned runtimeとoffline PoCが同じ正式schema契約を使う。新規DBファイルと0 byte空ファイルだけ正式初期schemaを作成でき、既存の正式DBは変更せずに互換確認だけ行う。M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補、SQLiteとして読めないファイル、ディレクトリは正式DBとして開かず、自動変更しない。WPFの起動時bootstrapは `PersonalScoreDbInitializer` が正式schema・metadata・拒否契約をアプリ側で使い、offline PoCのCLIやmoduleを呼び出さない。どちらの入口もplayのinsertや既定の監視保存を暗黙には開始しない。

正式DBの現行versionは3である。version 1→2の明示migrationは日時順indexを追加し、version 2→3のproduction migrationは`plays`へnullableな`ok` / `calories`だけを追加する。過去playは両値を`NULL`のまま保持し、推測backfillしない。いずれもmigration前backup、transaction内のschema・履歴・metadata・`PRAGMA user_version`更新、現行schemaでの再検証、失敗時restoreを行い、既存の`plays`、`source_captures`、`analysis_logs`、index、duplicate契約を保持する。preview、unknown、identity mismatch、newer unsupported、partial migration stateは自動昇格・修復しない。viewerは起動時に最近プレーを50件、譜面詳細履歴を10件、選択譜面のグラフを最新100件だけ取得する。追加取得は最近プレーが下端到達ごとに50件、譜面詳細が `続きを見る` ごとに10件で、件数・bests・ホーム集計・自己ベスト差分は全履歴queryの結果を使う。詳細画面の履歴DataGridは独立した縦スクロールを持たず、画面外側のscrollだけを使う。

CLI診断は `python -m tools.vision_poc --personal-score-db-diagnostic <path>` で標準出力へ出す。既定のinspect modeは読み取り専用で、`--personal-score-db-diagnostic-mode prepare-write` は新規DBファイルまたは0 byte空ファイルだけ正式初期schemaを作成する。出力はMarkdown既定で、`--personal-score-db-diagnostic-format json` も選べる。`--personal-score-db-diagnostic-output <path>` を指定した場合は、標準出力と同じ診断テキストをファイルへ保存する。出力先は `data/` 配下に限定し、Markdown format は `.md` / `.markdown`、JSON format は `.json` の拡張子だけを許可する。この出力は診断の保存だけであり、playの本番insert、既定の監視保存、既存DB migration、低信頼度ログ本番保存には進まない。

`--personal-score-db-diagnostic-log-output <path>` を指定した場合は、診断1回につき1行のJSONLログを `logs/` 配下へappendする。拡張子は `.jsonl` に限定する。ログレコードは `log_schema_version=1`、`event_type=personal_score_db_diagnostic`、diagnostic mode、format、exit code相当status、対象DB path、任意の diagnostic output path、diagnostic dictを必須keyとして持つ。書き込み前に必須key、mode、format、event type、schema version、`diagnostic.is_compatible` と exit code / status の整合を検査する。これは標準出力や `data/` file outputとは別のDB診断ログ入口であり、本番insert、既定自動保存、既存DB migration、低信頼度ログ本番保存、source capture保存には進まない。`logs/` 外指定や `.jsonl` 以外はDB準備より前に拒否し、prepare-write対象の新規DBを作らない。将来の低信頼度ログ本番仕様や `analysis_logs.log_path` から参照する本番解析ログは、このdiagnostic JSONLとは別ファイルとして扱い、同じJSONLへ `event_type` だけで混在させない。

正式connectionへの最小write境界は `write_personal_score_db_save(connection, save_input)` で扱う。入力検査をDB準備より前に行い、確定済み入力だけを受け付ける。保存成功は `source_captures`、`plays`、`analysis_logs`、保存除外は `source_captures` と `analysis_logs` を同じtransactionでinsertする。ready入力の明示 `duplicate_key` はDB準備後・source insert前に既存 `plays` へ照会し、衝突時はplayをinsertせず、sourceと `skipped/duplicate/duplicate_key_already_saved` のanalysisだけを同じtransactionで記録する。途中失敗時は同じ呼び出しの全rowをrollbackする。

write前の `adapt_personal_score_db_save_input()` はpure functionであり、DB connectionや出力pathを受け取らない。戻り値が `ready` または `excluded` の場合だけ正式 `PersonalScoreDbSaveInput` を持ち、`unresolved` は不足・不正理由だけを返す。adapterの追加によって既定自動保存、実ファイル作成、既存DB migrationは開始しない。

明示ファイル保存は `save_personal_score_db_file(db_path, adapter_input)` で扱う。adapterを最初に実行し、`unresolved` はDBファイルや親ディレクトリの作成・変更前に理由付き結果として返す。`ready` / `excluded` だけ `prepare_personal_score_db_file_for_write(path)` と同じ拒否境界を通り、既存writerへ渡す。新規/0 byte/compatible正式DBだけを許可し、preview / unknown / metadata identity mismatch / manual migration候補 / 非SQLite / ディレクトリは自動修復せず拒否する。duplicate collisionは結果を `excluded` / `written=true` / `play_id=null` とし、新しい一意なsource capture / analysisだけを残す。同一IDの完全再送は冪等化せず、writer途中失敗では同じ呼び出しのsource/play/analysis rowをrollbackする。

この入口は呼び出し元がpathとadapter入力を明示する単発Python APIであり、実ファイルの既定自動保存、常駐監視、既存DB migrationを開始しない。DB診断ファイルやdiagnostic JSONLも自動出力しない。

CLIからは `--personal-score-db-save-input <utf8-json>` と `--personal-score-db-save-database <sqlite>` を必須ペアとして明示した場合だけ、同じAPIを1回呼ぶ。通常M5候補観測の `--m5-jacket-catalog` が混在する場合は、入力JSON読込やDB準備より前に拒否し、無視したまま正式saveへ進めない。JSON外部形式は `input_schema_version=1` とし、`candidate_material`、source/analysis値、object/nullの `formal_play`、object/nullの `exclusion` を分離する。全階層の必須key、未知key、object/null、bool/integer/number/string型はファイル準備前に検査し、boolをintegerとして通さない。`candidate_material` と `timestamp_ms` は由来情報のまま保持し、正式playへコピーしない。

終了コードはtransaction完了した `ready` / `excluded` が0、adapterの `unresolved` が1、入力/DB拒否が2とする。結果JSONはDB path、adapter status、written、任意のplay ID、source capture ID、analysis ID、理由を持つ。duplicate collisionも終了コード0で `adapter_status=excluded`、`reasons=[duplicate_key_already_saved]` として区別する。CLI専用output file、diagnostic JSONL、低信頼度ログは生成せず、通常PoC、timestamped/manifest runner、`--m8-score-db-output` へ接続しない。

`--personal-score-db-save-input-validate <utf8-json>` は保存CLIと同じloaderとadapterだけを各1回実行する単独modeである。DB pathを受け取らず、DBファイル、親ディレクトリ、`data/`、`logs/`、diagnostic outputを作成・変更しない。結果はvalidation schema version、入力path、adapter status、正式save input構築可否、理由だけをJSONで返し、正式値や候補材料を再掲しない。ready/excludedは0、unresolvedは1、不正JSON/schemaまたは他option混在は2とする。DBを開かないため、DB互換性、既存duplicate collision、並行writer、実保存成功は保証しない。

`--personal-score-db-save-input-validate-output <path>` はvalidation inputとの必須ペアで、同じvalidation結果投影をレビューreceiptとして `data/` 配下の新規 `.json` へ1件だけ保存する。UTF-8 BOMなし、LF、固定key順、末尾改行とし、既存ファイルを上書きしない。output path、拡張子、必須ペア、他mode排他は入力読込と出力作成より先に検査する。invalid input schemaを含め、receiptに記録するstatusと終了コードは標準出力/標準エラーのvalidation結果と同じに保つ。receiptは正式値、候補材料、template本文、DB情報を持たず、レビュー承認、DB互換性、duplicate非衝突、並行writer安全性、実保存成功を保証しない。outputを指定しない従来validationは引き続き `data/` を含む出力を作成・変更しない。

`--personal-score-db-save-input-template <path>` は、`data/` 配下の新規 `.json` へ空のschema version 1 review templateを1件だけ生成する単独modeである。既存ファイルを上書きせず、UTF-8 BOMなし、LF、固定key順、末尾改行で書く。出力はtemplate JSON以外のDB、`logs/`、画像、diagnosticを作らず、標準出力も生成path、template schema version、status、理由だけに限定する。`RESULT同定根拠`、`RESULT数値認識根拠`、`RESULT状態認識根拠`、`capture event根拠`がまだ採用されていないmetadata、preview、manifest、画像、DBは入力にせず、候補・相対時刻・duplicate keyを正式値へ転記しない。他optionとの混在、`data/` 外、`.json` 以外、既存出力は作成前に終了コード2で拒否する。

M8の保存予定レコードプレビューでは、まず in-memory SQLite fixtureで `plays` 最小スキーマとrow contractを確認する。実ファイルDBを生成する場合は必ず `data/` 配下に置き、Git管理しない。

M8のscore DB write previewでは、保存予定レコードだけを新規 in-memory SQLite `plays` テーブルへinsertし、`m8_score_db_write_preview.*` としてpreview `schema_version=1`、`schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema`、`created_by_preview=tools.vision_poc.m8_score_db_preview`、insert対象件数、insert後件数、除外件数、代表行を確認する。これは実ファイルDB生成ではなく、ローカルDBファイルは作らない。SQLite側の `preview_metadata` 表はpreview生成物識別用の軽量表であり、正式マイグレーションではない。`schema_contract_scope` と `production_schema_status` は、M8の `plays` が正式個人スコアDB候補列を持つ本番スキーマではなく、preview専用最小スキーマであることを示す読み間違い防止欄です。

M8のscore DB file output previewでは、`--m8-score-db-output data\...\ddrgp-scores.sqlite` を明示した場合だけ、保存予定レコードを指定された新規SQLiteファイルへinsertする。出力先は `data/` 配下に限定し、`data/` 外や既存ファイルへの書き込みは拒否する。実ファイルDBには `PRAGMA user_version=1` と `preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview`、`preview_metadata.schema_contract_scope=preview_minimal_plays`、`preview_metadata.production_schema_status=not_production_schema` を設定し、summary/reportの `schema_version=1`、`schema_contract_scope`、`production_schema_status`、`created_by_preview` に一致させる。summary/reportの `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns` は実DBから読み戻した診断欄で、`database_readback_matches_preview_contract`、`database_readback_mismatch_reasons`、`database_plays_row_count_matches_insert_counts`、`database_plays_row_count_mismatch_reasons`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` はreadback値とpreview識別契約、insert件数、preview最小 `plays` schemaの一致診断として扱い、定数として出すpreview識別欄とは分けて扱う。`m8_score_db_file_output_preview.json` / Markdown はpreview DBへのinsert件数とpreviewスキーマ識別の確認であり、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定として扱わない。生成したDBファイルはローカルDBとしてGit管理しない。

開発中に生成したDB、取得元HTML snapshot、解析ログはGit管理しない。配布用マスタDBはGitHub Releases成果物として扱う。

## M5b jacket catalog

通常runtimeのread-only identity loaderは、catalog rowの`master_version`がcurrent値と異なっていても、`song_id`・canonical title・canonical artistがcurrent GP masterと完全一致するconfirmed jacket referenceをcurrent-master-compatibleとして利用する。masterとの不一致、orphan、未確認、旧extractor、不正persisted featureは除外し、catalog rowは変更しない。coverageのcurrent-only表示やcollectorのcurrent ingest契約とは別の、保存入口での互換性検証である。

ローカルjacket catalogはdevelopmentでは `databases/jacket-catalog-release.sqlite`、productionでは `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\jacket-catalog.sqlite` をruntimeの既定pathとする。collectorが更新する未binding source `databases/jacket-catalog.sqlite`は、明示`bind-master`の入力としてだけ扱う。M4 masterはそれぞれ `databases/ddrgp-master.sqlite`、`%LOCALAPPDATA%\DDRGpScoreViewer\data\master\ddrgp-master.sqlite` で、catalogとは別fileとして扱う。初回リリース向けcurrent schemaのversionは1で、専用identity、`PRAGMA user_version=1`、metadata schema version 1、exact tables/columns/constraints/index/foreign keyをstrictに検査する。runtimeはcurrent schemaとexact一致しない旧catalog、非catalog SQLite、破損catalog、正式個人スコアDB、M8 preview DB、M4 master DBを読み取り専用検査でunsupportedとして拒否し、自動作成・修復・migrationを行わない。既存の明示migration CLIがある場合も、WPF起動・master操作・正式save・評価DB準備から暗黙起動しない。

current referenceはmanual review revision/historyと、`jacket_feature_version/hash`、`title_line_feature_version/hash`、`composite_identity_version/hash`を全nullまたは全非nullの1組として保持する。これに加えてM7 result-text featureのtitle/artist payloadを`result_text_features`へ、field、収集時のmaster version、canonical title/artist snapshot、source label、payload hashと共に保存する。master更新やcatalog bindingでは旧master rowを削除・上書きせず、元の`master_version`を履歴として保持する。通常observation ingestは完全な非null組を必須とし、既知version、lower SHA-256、UTF-8 NUL区切りcanonical hashを検査する。`(composite_identity_version, composite_identity_hash)`はcatalog全体で一意とし、read-only identity集合には`unresolved`、review待ち、確定、再割当、`reopen`、`rejected`をすべて含める。

app-owned runtimeのM7 result-text feature loaderは、このcurrent schema version 1 catalogをread-onlyで参照する。`feature_version=m7-result-text-image-v1`、`roi_version=m7-result-title-artist-roi-v1`、payloadのflat shape `[1536]` / `[640]`、vector encoding、payloadのcanonical SHA-256、feature ID、current masterとのcanonical title/artist完全一致を検査する。rowの`master_version`が旧値でもcanonical identityがcurrent GP masterと一致する場合はhistorical master-compatible featureとして再利用し、旧versionという理由だけで除外しない。不正・欠落・旧versionのfeature形式・hash不一致・master identity driftのrowは正式同定の候補から除外する。nested shape `[96,16]` / `[40,16]` はリリース前の現行形式外として読み込み対象外とし、jacketが一意ならこのloaderを呼ばず、jacketがambiguousの場合だけchart候補集合との共通部分でtitleを先に比較する。通常featureがambiguousのときだけ`title_linehash_rows`で再順位付けし、比較距離は正規化距離`0.35`以下を採用条件とし、これを超える最良候補はmarginに関係なく除外する。titleが欠落またはambiguousなら同じ候補集合でartistを比較し、一意に解消した場合だけ既存のformal evidence bridgeへ渡す。候補集合外のsongを検索せず、解消不能なら`unresolved`として正式DB保存を拒否する。このruntime補助比較は`result_text_features`のschema、migration、catalog writer、既存jacket判定、duplicate、transaction、正式DB insert境界を変更しない。

current `ingest`は非空observation ID、artifact image bytes/hash、空title/artist、`unresolved`、session開始時のmaster version/source hash、catalog identity/schema/created-at、current extractor、完全なcomposite identityをcatalog変更前に検査する。同一observation ID・同一payloadは冪等、異payloadは拒否する。異なるobservation IDでも同じcomposite identityなら、review statusに関係なくtransaction内で既存reference receiptへ収束させ、2件目を作らない。新規rowはsong未割当、revision 0、manual provenance/history/candidateなしとする。

collectorの手動保存と明示opt-in自動保存は、artifact publish前にcurrent checkpointとcurrent catalogのcomposite identity集合を照合する。identity集合はcatalog identity/schema/created-atと同じread-only接続で検査し、`rejected`を含む全review状態を対象にする。checkpoint既存identityは新規観測を作らず既存receipt/retryへ留め、catalogだけにあるidentityはartifact/checkpointを作らない。自動保存はsession単位・既定OFFで、fresh/resume/stop時にOFFへ戻し、端末設定へ永続化しない。1 identityにつき自動試行は1回とし、失敗後は明示保存またはcatalog retryを使う。照合後の並行投入はcatalogの一意制約と冪等ingestで既存referenceへ収束させる。

projectionとmanual review、coverage、M5 feature loader、title/artist evaluationはcurrent catalogだけを受け入れる。projectionはversion 5でcurrent/stored state、revision、candidate、manual provenance、append-only historyに加え、artifact/checkpoint照合済みのsource image pathとversion付きunresolved candidate evaluationを返し、旧migration/capability fieldを持たない。通常画面のcoverage/review表示は、保存済みのconfirmed statusを後発のmaster・extractor差分だけで未確認へ戻さない。candidate evaluationとCSV/JSON/Markdown reportはread-onlyで、exact/alias一意、曖昧、候補なし、低confidence、OCR失敗、artifact/master/catalog/extractor/identity不整合、review済み対象外を区別する。manual mutationはexpected revision/status/songをpreconditionにし、同一action ID・同一payloadだけを冪等成功とし、current row/historyを同じtransactionで更新する。candidate、expected song、OCR rawを確定songへ昇格しない。

title/artist OCR診断は同じstrict projection検証済みsourceだけを読み、profile別raw/status/confidence/candidate結果と代表contact sheetを`data/`配下へatomic生成する。Tesseract installed language不足は`m5c-title-artist-ocr-diagnostics-report-v1`の`ocr_unavailable` / `tesseract_language_unavailable_v1:<lang>`へ固定し、別languageへfallbackしない。診断前後でmaster/catalog hashとmanifest/source/crop/checkpoint fingerprintを照合し、変化時はreportをpublishしない。診断はcatalog writer、manual review transaction、artifact/checkpoint writerを呼ばず、schema、revision、history、source/cropを変更しない。

#58のmanual review XLSX exportはproduction auto-registration planとは別のread-only projection exportとする。current projectionの`needs_review` / `unresolved`だけを対象に、既存のtitle/artist ROI定義で切り出した画像をXLSX package内へ埋め込む。`Manual Review`、current Master全曲の`Master Songs`、schema/export/catalog/master version・export日時・対象件数だけの`Metadata`を持ち、`status`は`unreviewed` / `confirmed` / `rejected` / `hold`の選択式とする。catalog、Master、下書き、source画像は変更しない。出力先は呼び出し側が明示指定した`.xlsx`とし、WPFでは標準保存ダイアログの確認後に既存fileを置き換える。画像を検証できない対象があれば全体を拒否する。

#59のmanual review XLSX importは、export形式の`Manual Review`、`Master Songs`、`Metadata`をread-onlyで全件検証し、`observation_id`、`status`、`truth_song_id`、`notes`だけを既存のレビュー下書き保存経路へ一括保存する。schema version、必須sheet/column、current projection上のobservation ID、status/song制約、Metadata対象件数を検査し、catalog/Masterのversion差だけでは拒否しない。1行でも不正なら行番号・observation ID・理由を返して、メモリ上・保存済み下書きを変更しない。検証成功後もcatalog、review transaction、history、確定状態を変更せず、projectionへ下書きを再適用する。レビュー済み対象、ROI画像、ODSはimportしない。

coverageは `data/` 配下の明示directoryへ `jacket_catalog_song_coverage.csv`、`jacket_catalog_coverage_summary.json`、`jacket_catalog_coverage.md` を生成する。確定songがないreferenceでもGP対象candidateは `needs_review` として数え、候補のない観測だけを未割当集計へ残す。current master/GP/current extractorを満たす `auto_confirmed` / `manual_confirmed` referenceだけをM5 matcherへ供給し、`rejected`、orphan、旧extractor、不正persisted featureを除外する。

照合に必要な`result_text_features`を含むjacket catalogはrelease時に配布する照合参照DBとし、collectorのcapture、observation artifact/checkpoint、source/crop画像、review用coverageやJSON/CSV診断出力はローカル運用物としてGit、CI artifact、通常analysis logへ含めない。既存local DB/artifact/checkpoint/source/crop画像を削除、上書き、in-place repairしない。artifact manifest/checkpoint v1/v2、resume/retry状態機械はcatalog schema version再採番と独立して維持する。

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

collectorの明示的な`収集を終了`は、完成済みDDR WORLD snapshotをread-only入力として同じ保存境界へ接続する
live collection-end入口です。`data/ddrworld_music_snapshot/<snapshot-id>/` の`songs.jsonl`と32x32公式jacket画像を
current masterへ対応付けて公式feature masterを作り、`pending`のcatalog retryが完了した後、収集したjacket観測へ
既存のdistance threshold / ambiguity gateを適用し、一意なjacket top-1だけを`jacket_gate` auto-confirm targetとして
組み立てます。jacketで解決しない行は、current
projectionの`exact_unique` / `alias_unique`だけを既存#53の`ocr_title_artist_pair` auto-confirm targetとして組み立て、
既存writerの1 transactionへ渡し、完了後にprojectionを再読込します。ODSの再構築、jacket top3 routeや
OCR方式の変更は行いません。曖昧、候補なし、低confidence、評価失敗/不能、GP対象外、既存manual/rejected/revision
stateはjacket単独では自動確定せず、既存manual review境界に残します。これはmatching policy、threshold、OCR、
catalog schema、manual historyを変更せず、unsafe stop、通常のcatalog retry、manual review、coverageからは起動しません。

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

- 失敗画像の保存期間と掃除方法
- ログローテーション
- 評価用DBのschemaとinitializer（M10-3）
- 手動backupの保持期間
- manifest dry-run 出力を本番でも残す期間

## Analysis artifact path contract

`tools.vision_poc.personal_score_db_analysis_artifacts` はversion 1のpure contractに加え、検査済みpayloadを明示された新規pathへ1件だけ生成する `write_analysis_detail_file()` を提供する。`analysis_logs.log_path` は空文字、またはリポジトリroot基準のPOSIX相対path `logs/analysis_details/**/*.json` とする。絶対path、`..`、backslash、`logs/` 外、別拡張子、既存outputをdirectory作成より前に拒否する。出力はUTF-8 BOMなし、LF、sort済みkey、末尾改行とし、同一directoryの完成済み一時ファイルをatomicに公開して部分JSONを残さない。

CLIは `--personal-score-db-analysis-detail-input <json>` と `--personal-score-db-analysis-detail-output <logs/analysis_details/...json>` の必須ペアだけで実行する。save、diagnostic、validation、template、receipt、通常PoC optionとの混在を副作用前に終了コード2で拒否し、成功は `status=created` / 終了コード0とする。DB、`data/`、failure image、通常PoC生成物は作成・変更せず、save workflowへ自動連鎖しない。

任意の失敗画像は詳細JSON内の `failure_image_path` で `logs/analysis_failures/**/*.{png,jpg,jpeg,webp}` を参照する。これは `log_path`、元フレーム用 `source_captures.source_path`、`data/` 配下のvalidation receipt、`logs/` 配下のDB diagnostic JSONLと相互代用しない。

retention classは `short=7日`、`standard=30日`、`indefinite=期限なし` とする。UTCの `basis_at` から `expires_at` を決定的に計算し、期限なしだけnullにする。同じ詳細JSONとそこから参照する失敗画像へ同じretention metadataを適用する。この契約は将来cleanupの判断材料だけであり、ファイル作成、削除、scheduler、起動時掃除を行わない。

## Capture save workflow output

capture-onlyのcontinuous capture原本は `data/windows_capture/session-*/` に保持し、解析生成物は別の一意directory `data/capture_save_workflow/<session>-<id>/` に出力する。画像原本やmanifestを解析出力へ移動・上書きしない。通常のlive監視はsession原本を作らず、安定候補のPNG、1行manifest、Vision PoCのCSV/JSONをOS一時directoryへ置き、candidate workflow終了後に全て削除する。live candidateの正式DB source captureにはhashと論理 `live-memory://...` sourceだけを記録し、`manifest_image_path` は空にする。出力directoryは正式DB、`source_captures` 本文、`analysis_logs.log_path`、DB diagnostic logの代用にしない。

正式DB、M4 master DB、M5b jacket reference catalogは、development / productionごとの既定pathを起動時に設定し、通常の監視・単発保存でpickerを開かない。DBの任意path切替は行わず、path保存も現在の環境の既定pathだけに限定する。capture-only操作はDBを開かず、正式workflowへ進むconfirmed eventも既存file-save境界だけが新規/0 byte/compatible DBを準備する。`saved` transactionの後だけviewerが3つのDBをread-onlyで開き直し、正常確認できた既定pathだけを次回起動用のローカル設定へ保存する。起動時監視、保存できない結果の通知、既定プレイスタイル、起動時画面は正式DBやpath設定と分離した`user-settings.json`へ保存し、欠落・読込不能時は4項目すべてを初期値へ戻す。

WPFの起動時、単発保存・連続取得の保存開始時は、現在の環境の固定pathにあるM4 master DBとM5b jacket reference catalogを別々にread-onlyで再検査する。pathの存在・SQLite読込可否・M4の必須table/metadata/count/source snapshot、M5bのtable/column/metadata/schema/index/foreign key整合を分けて `missing`、`read不可`、`schema incompatible`、`compatible` と表示し、前3状態ではcapture解析や正式保存を開始しない。保存するのはscore DB/M4 master/M5b catalogの既定pathと環境タグだけで、capture、候補、skip、拒否、失敗、workflow結果のcheckpointは持たない。

## WPF monitoringとtask tray lifecycle

「アプリ起動時に監視を開始」がON（初期値）の場合だけ、監視は起動後に自動探索を開始し、1秒間隔で`process=ddr-konaste`かつclient `1280x720`のtop-level windowを確認する。対象を2回連続で検出したときだけWPFまたはtask trayの明示開始と同じ監視workerへ接続し、0件・複数件・単発の探索失敗では開始せず待機する。OFFでもWPFまたはtask trayの明示開始は利用できる。正式DBと2種類のmaster DBは現在の環境の固定pathから取得し、対象windowを推測で選択しない。window消失は2回連続で確認して安全停止し、対象windowが一度消失してから再出現したときだけ自動復帰する。手動停止は同一app session中の自動再開を抑止する。window title、幅、高さは検出・選択済み対象の表示に使い、任意window選択や自動focusは行わない。監視surfaceはcapture progressのframe数、開始UTC、最新frame UTCと、capture-save結果の `saved`、`duplicate`、`excluded`、`unresolved`、`analysis_failed`、`db_rejected`、`workflow_failed` を投影する。これは結果を再開する新しい永続化形式ではなく、終了後に破棄可能なprocess内状態である。

通常のmain window closeと最小化はwindowを非表示にし、capture sessionとworkflowのownerをApp/ViewModelに残す。task trayは開始、停止、window表示、明示終了を提供する。WPFとtrayの開始要求は1つのoperation gateで直列化し、capture-onlyを含むpicker中の再Startを同じTaskへ合流させる。各capture sessionに世代を付け、停止・対象window終了・capture失敗・workflow失敗・終了後の古いprogress callbackは状態を再開・上書きしない。明示終了はpending pickerをcancel状態にしてその終端と停止の冪等操作、in-flight capture/workflow完了を待ってから、NotifyIcon、context menu、window、processをこの順で終了する。stop自体が例外になった場合も理由を通知してtrayをdisposeし、process終了でOS resourceを残さない。stop、target closed、resize、device lost、capture/write失敗で既存capture resourceを一度だけ解放し、tray resourceはアプリ終了時に一度だけdisposeする。アプリ本体の更新適用時もこの完全終了経路を使い、tray格納やwindow hideだけでVeloPack updaterへ制御を返さない。

通知はtransaction済みsavedが1件以上ある完了、target closed、resize、device lost、capture失敗、workflow失敗に加え、capture event単位の`unresolved`／ambiguous結果を対象とする。「保存できない結果を通知」がON（初期値）の場合、自動保存できない結果は`自動保存できないプレーが発生しました。正式DBには保存されていません。`を基底文としてWPF/trayへ非ブロッキング表示し、理由とcapture event参照だけを補足する。WPF画面内バナーは表示開始から3秒後に自動で非表示にし、表示中に別の通知対象eventが来た場合は最新内容へ更新して表示期限を3秒後へ延長する。OFFではWPF/tray表示だけを抑止し、診断記録と監視結果の集計は維持する。確定していない曲名、日時、スコアを通知の正式値として表示せず、同じevent IDのframe反復は1回にまとめる。通知はformal DBのwriteやcandidate rescueを開始せず、`source_captures`、`analysis_logs`、`plays`の既存保存責務境界を変更しない。monitoring stateは`starting`、`waiting_for_game`、`monitoring`、`manually_stopped`、`blocked`、`shutting_down`を含み、DB検証失敗、runtime起動失敗、更新処理中、終了処理中は自動開始しない。monitoring state、tray menu enable状態、close-to-tray、明示exitのstop待機はWindows Graphics Captureなしのfixtureで固定する。

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

M9 WPFはapp-owned runtimeのstrict workflowを同一processで1回実行する。このUI adapterはユーザーが選択したworkflow入力と固定DB pathだけを受け、candidate materialをformal値へ補完しない。`saved` かつtransaction完了済みplayだけ同じread-only repositoryで再openし、通常の閲覧操作はwrite workflowを起動しない。offline PoCのCLI/module、repository root、Python executable、Tesseractはこのruntimeから呼び出さない。

### 後続実装のfixture行列とacceptance criteria

- readyのartifactなし/あり、低信頼度とerrorのartifact必須、その他skipの任意、DB duplicate collisionの各分岐を固定する。
- unsafe path、入力不正、共有ID/status不一致、`log_path` 不一致をfile/DB副作用前に拒否する。
- DB非互換ではartifactを作らない。早期duplicateは停止せず既存writerへ渡し、transaction内再検査でもplayを作らずsource/analysisを記録する。raceでduplicateになった場合も生成済みartifactを保持する。
- artifact write失敗ではDBを呼ばず、DB失敗ではrowをrollbackしてartifactを残す。
- 同一payloadの既存artifactだけ再利用し、不一致fileを上書き・削除しない。
- loader、adapter、artifact writer、file saveの呼出回数を固定し、現行CLIのstatusと終了コードを変えない。
- 正式値、candidate material、analysis detail、receipt、DB diagnostic、failure image、source captureの責務分離をfixtureで検証する。

このfixture列は通常PoC、常駐監視、migration、個人スコアデータのバックアップ・復元、cleanup、並行writer制御、failure image生成へ暗黙接続しない。各操作はそれぞれの明示契約と既存の保存境界を使う。
## Migration backup and explicit execution boundary

PoCのmigration status / dry-run / explicit backup CLIは通常save、analysis artifact orchestration、diagnosticからmigrationを暗黙実行しない。Release appの固定production score pathだけは、起動時に現在schemaをread-only検査し、source versionからcurrent versionまでの連続したconverter pathがすべて明示登録されている場合だけmigrationを実行できる。converterなし旧version、newer version、preview、unknown、identity/history不一致は変更せず拒否する。

Release appのmigration backupはsourceと別pathの`data/score/migration-backup/score.db.bak`へ作り、migration単位で最新1件だけを保持する。次のmigrationでは新しいpending copyの作成に成功するまで前回backupを維持し、migration成功後に最新backupへ置換する。source変更前のcopyに失敗した場合はsource無変更で終了する。

Release appの実行順序は `inspect source read-only` → backup copy → transaction開始 → converter schema steps → migration履歴・metadata・`PRAGMA user_version` → commit → current schemaで再open → 基本read/write transactionのrollback確認とする。commit前失敗はtransaction rollbackし、commitまたは再検証失敗はpending backupからsourceを復元する。復元にも失敗した場合だけmanual restoreを要求し、解析・正式保存を開始しない。

pure contractのstatus/終了コードは、`current` / `dry_run_ready` / `ready` / `completed` が0、`confirmation_required` が1、入力・互換性・path・partial state拒否が2、backup I/Oまたはmigration実行失敗が3である。`manual_recovery_required` はsourceが変更済みまたは変更有無を確定できない状態として扱い、検証済みbackupを使う人手復旧を促す。再実行時、既にtargetなら `current`、同じbackup pathが存在すればconflict、partial stateならmanual recovery拒否とし、暗黙の再開・repair・backup再利用をしない。

status/dry-run専用CLIは従来どおりDB path、target version、明示backup pathの必須組だけを受け、Release appの固定path起動migrationとは別入口を維持する。

## #116 application package update boundary

アプリ本体の更新はVeloPack 1.2.0の`GithubSource`と`UpdateManager`だけを使い、`https://github.com/tts1374/ddrgp_scorelog` のstable GitHub Releaseを既定のWindows channelで確認する。アプリ側でchannel、Release API、package展開、rollback、background serviceを独自実装しない。release feedはVeloPackが解決する`releases.win.json`とfull `.nupkg`を基本とし、reference data setの`reference-set.json`、master DB、catalog DBはこの更新対象に含めない。

main windowを表示した後に`CheckForUpdatesAsync`を非同期実行し、確認失敗や通信停止でmain window表示を遅延させない。reference data setの起動時更新がある場合は、その完了後にアプリ本体のdownload・適用を開始し、両更新のinstall root・永続data root操作を並行させない。更新確認には30秒の有限timeoutを設け、package downloadは30分の全体上限とVeloPack downloaderの1要求5分上限を使い、downloadにはVeloPackへCancellationTokenも渡す。進捗中のfull packageを確認timeoutで打ち切らない。stable版の利用可能な更新は自動でdownload・適用し、確認、download、適用準備の失敗またはofflineでは現行app binaryで通常利用を続ける。起動時のVeloPack自動適用を有効にするが、version選択、channel選択、background serviceは提供しない。

適用は既存の明示終了経路の準備段階でpending picker、continuous capture、解析・保存workflow、monitoring worker、Windows Graphics Capture runtime、DB/fileのopen handleを停止・完了させてから、download済みのfull/delta updateをVeloPackへ渡す`WaitExitThenApplyUpdates`を起動する。updater起動後はNotifyIconとcontext menuをdisposeしてprocessを終了し、終了callbackが失敗しても最終終了要求を行って通常利用へ戻らない。準備段階で失敗した場合はupdaterを起動せず、現行app binaryを保持する。適用失敗、確認失敗、offline、未インストール起動では現行app binaryを保持し、再試行前に独自rollbackや自動repairを行わない。

VeloPackのinstall rootと永続data rootを分離するため、`%LOCALAPPDATA%/DDRGpScoreViewer/`配下の正式score DB、settings、reference DB、Release logはapp package updateの書換対象外とする。更新確認、download、適用準備の状態はprocess内UIとRelease logへ投影するだけで、新しい更新checkpointや更新用DBを永続化しない。

## M10-4 / #117 reference data set package and update boundary

VeloPack packageはapp binary/runtimeと`ReferenceData/`を所有し、永続dataはinstall root外の`%LOCALAPPDATA%/DDRGpScoreViewer/`に置く。GitHub Releaseにも、同じReleaseの次の3 assetを同じ名前で公開する。

| asset | 契約 |
|---|---|
| `reference-set.json` | `content_version`、`master_schema_version`、`catalog_schema_version`、`master_content_version`、`catalog_master_content_version`、`master_sha256`、`catalog_sha256` |
| `ddrgp-master.sqlite` | M4 master DB |
| `jacket-catalog.sqlite` | M5b jacket reference catalog |

manifestの`content_version`はreference data set更新の比較に使う3要素数値versionで、master/catalogのmetadataにあるcontent versionとは別に保持する。`master_schema_version`と`catalog_schema_version`は現在1だけを受理し、manifest内のmaster/catalog対応version、master DB実metadata、catalog DB実metadataの3者が一致することをread-only openで検査する。SHA-256はdownload後の各asset bytesと比較する。

検証済み候補は`data/`配下の一時directoryへ作成する。現行`data/master/`を`data/.reference-previous/`へdirectory単位でrenameし、候補directoryを`data/master/`へrenameする。master/catalogを個別に切り替えないため、新旧fileの混在を作らない。切替後の両DBread-only再openまたは切替中のI/O失敗では`data/.reference-previous/`を`data/master/`へ戻し、候補を削除する。同一versionはno-op、古いversionは拒否し、片方欠落、不整合、checksum不一致、通信失敗、download中断、空き容量不足では現行セットを維持する。

保持上限は`data/master/`の現行と`data/.reference-previous/`の直前1世代だけとし、正常確認後に旧世代を削除する。download stagingは処理完了後に削除する。Release logは`logs/gp-score-log.log`を5MB×3 fileで保持する。正式score DBとsettingsは無期限、migration backupは最新1件、cache/tempは処理完了時または次回起動時までとする。uninstallはinstall rootだけを削除し、この永続dataを削除しない。
