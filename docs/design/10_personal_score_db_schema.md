# M8 正式個人スコアDBスキーマ設計

M8 preview完了後の正式 `ddrgp-scores.sqlite` 初期スキーマ、migration境界、正式保存入力、transaction write境界、明示単発保存、analysis詳細JSONのpure contractを固定する。実ファイルへの既定自動保存、duplicate key生成、analysis artifact自動生成はまだ実装しない。

## 目的

- M8 preview最小 `plays` と正式個人スコアDB `plays` を別物として扱う。
- 1プレー1レコードの正式履歴テーブル、保存スキップ/解析ログ、DB metadata、migration metadata、source capture reference の責務を分ける。
- `PRAGMA user_version` だけで正式DB判定をせず、metadata table と必須tableを合わせて互換チェックする。
- M8 preview DB、未知スキーマ、既存の壊れたDBを正式DBとして開かない。

## 実装済みの小さな契約

正式スキーマ候補のコード側契約は `tools/vision_poc/personal_score_db_schema.py` に置く。

- `PERSONAL_SCORE_DB_SCHEMA_VERSION = 1`
- `score_db_metadata`
- `schema_migrations`
- `source_captures`
- `plays`
- `analysis_logs`
- `create_personal_score_db_schema()`
- `personal_score_db_compatibility_errors()`
- `initialize_personal_score_db_if_empty()`
- `prepare_personal_score_db_for_write()`
- `prepare_personal_score_db_file_for_write(path)`
- `personal_score_db_schema_inspection_diagnostic()`
- `format_personal_score_db_schema_diagnostic_markdown()`
- `personal_score_db_file_preparation_diagnostic()`

正式保存入力とconnection単位のtransaction writerは `tools/vision_poc/personal_score_db_save.py` に置く。

- `PersonalScoreDbSourceCaptureInput`
- `PersonalScoreDbPlayInput`
- `PersonalScoreDbAnalysisInput`
- `PersonalScoreDbSaveInput`
- `personal_score_db_save_input_errors()`
- `validate_personal_score_db_save_input()`
- `write_personal_score_db_save()`

schema moduleは正式DB識別と準備、save moduleは確定済み入力の検査とtransaction writeを担当する。save moduleはCLIや既定自動保存ではなく、in-memory SQLiteを含む明示connection向けの最小縦断入口である。

preview候補材料と正式値の間のpure adapterは `tools/vision_poc/personal_score_db_save_adapter.py` に置く。

- `PersonalScoreDbFormalPlayValues`
- `PersonalScoreDbSaveExclusion`
- `PersonalScoreDbSaveAdapterInput`
- `PersonalScoreDbSaveAdapterResult`
- `adapt_personal_score_db_save_input()`

adapterは `candidate_material` を正式値の由来として自動採用しない。正式play値は別入力で明示し、結果を `ready` / `unresolved` / `excluded` に分ける。`ready` だけplayつき正式入力、`excluded` はplayなし正式analysis入力を返し、`unresolved` は正式入力を返さない。

明示path単位の保存APIは `tools/vision_poc/personal_score_db_file_save.py` に置く。

- `PersonalScoreDbFileSaveResult`
- `save_personal_score_db_file(db_path, adapter_input)`

このAPIはadapterをDB準備より先に評価する。`unresolved` は理由付き `written=false` としてDBファイルや親ディレクトリを作らず返す。`ready` / `excluded` だけがファイル準備とtransaction writerへ進む。結果はDB path、adapter status、理由、write完了有無、source capture ID、analysis ID、任意のplay IDを持つ。`written=true` はsource/analysisを含むtransactionが完了したことを表し、正式play保存の有無は `play_id` で区別する。

## 正式保存入力契約

`PersonalScoreDbSaveInput` はM8 preview payloadを直接受け取らない。M5/M7a由来の候補材料を上流で確認し、正式値へ確定した後だけ生成する。

保存成功入力では以下を必須にする。

- timezone付きISO 8601の `played_at` / `captured_at`
- 空でない `master_version`、`song_id`、`chart_id`
- 範囲検査済みのscore/判定数/EX SCORE
- 空でない `rank` / `clear_type`
- 同じsource captureを指す `capture_hash` / `source_capture_id`
- PoCの `score:` / `file:` 形式ではない正式 `duplicate_key`
- 0.0から1.0の `analysis_confidence`
- 一致する `app_version`
- `analysis_status=saved`、`save_boundary_status=save_ready`、`event_type=confirmed`、`confirmed_result=true`、`duplicate=false`

duplicate、低信頼度、error、その他skipでは `play=None` とし、`source_captures` と `analysis_logs` だけを同じtransactionで記録する。非保存analysisには `skip_reason` を必須にし、duplicateは `analysis_status=skipped`、`save_boundary_status=duplicate` とする。これらを成功した `plays` rowへ丸めない。

`write_personal_score_db_save()` は呼び出し元connectionにactive transactionがないことを要求し、正式DBを準備した後、`source_captures`、任意の `plays`、`analysis_logs` を1 transactionでinsertする。playつき入力ではsource insert直前に明示 `duplicate_key` を既存 `plays` へ照会し、衝突時はplayを作らず、analysisを `analysis_status=skipped`、`save_boundary_status=duplicate`、`skip_reason=duplicate_key_already_saved`、`duplicate=true` へ変換してsourceと同じtransactionで記録する。途中のUNIQUE/FK/CHECK違反では、同じ呼び出し内のsource captureとanalysisもrollbackする。

collision時の `capture_id` / `analysis_id` は新しい一意値を要求する。同一IDの完全再送は冪等成功へ丸めない。preflightとinsertの間に別connectionが書く並行writer raceは現フェーズでは制御せず、既存 `plays.duplicate_key` UNIQUE制約による拒否とrollbackを維持する。

## M8 previewとの境界

M8 preview最小 `plays` は以下の用途に限定する。

- `m8_planned_play_records.*` のrow contractをSQLiteへinsertできるか確認する。
- in-memory write previewと明示file output previewの内部整合を確認する。
- `schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema` でpreview専用であることを示す。

正式個人スコアDBの `plays` は別物であり、以下を直接持たない。

- `source_organized_file`
- `source_confirmation_mode`
- `analysis_payload_status`
- `identity_signal_source`
- `m5_identity_signal_status`
- `m5_jacket_match_status`
- M7aの `recognized_digits` / `expected_value` / `match`
- OCR raw / normalized

これらは保存判定前の候補観測、review材料、または解析ログ側の材料であり、正式 `plays` の保存値確定列として扱わない。

## 正式 `plays`

正式 `plays` は1プレー1レコードの履歴を持つ。初期候補列は以下。

- `play_id`: 本番保存時に生成する安定ID。
- `played_at`: リザルト確定時刻。timestampなしPoCの `played_at_ms=0` をそのまま正式値にしない。
- `master_version`: 保存時に参照したマスタDB version。
- `song_id` / `chart_id`: 保存判定後のID。M5 `identity_signal_*` をそのまま確定ID扱いしない。
- `score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score`: 保存判定後の数値。
- `rank`、`clear_type`: 空文字を正式入力として許可しない。未取得時は保存成功へ進めず、上流の未解決/低信頼度として扱う。
- `capture_hash`: 元キャプチャ参照と重複防止用のhash。
- `source_capture_id`: `source_captures` への参照。
- `duplicate_key`: 本番重複判定用key。現行PoCのscore由来簡易keyとは別物にする。
- `analysis_confidence`: 保存判定後の総合信頼度。
- `app_version`
- `created_at`

`plays` は自己ベストではなく全履歴を保持する。自己ベスト集計や表示用viewは後続で追加する。

## 解析ログと保存スキップ

`analysis_logs` は保存成功、保存スキップ、低信頼度、例外を追跡するためのtable候補である。

`analysis_logs` に置くもの:

- `analysis_status`: `saved` / `skipped` / `low_confidence` / `error`
- `save_boundary_status`
- `skip_reason`
- `event_type`
- `confirmed_result`
- `duplicate`
- `confirmation_mode`
- `timestamp_ms`
- `candidate_duration_ms`
- `identity_signal_status`
- `digit_review_status`
- `analysis_confidence`
- `analysis_summary_json`
- `log_path`

`analysis_logs` は保存判定の説明と再調査の入口であり、正式保存値を二重管理する場所ではない。OCR/M5/M7a由来の材料はversion 1詳細JSONの `candidate_material` でkind、status、短いsummaryとしてだけ残し、正式play値へ投影しない。

`analysis_logs.log_path` は空文字、またはリポジトリroot基準の `logs/analysis_details/**/*.json` だけを参照する。version 1詳細JSONは1 analysisにつき1 objectで、schema/generator、analysis/source ID、status、event、review、investigation、任意の失敗画像参照、retentionを持つ。明示API/CLIは検査済みpayloadを同namespaceの新規pathへatomic生成できるが、DB insertやsave連鎖は行わない。正式play値、receipt key、DB diagnostic payloadを持たず、DB diagnostic JSONLも `log_path` に記録しない。

失敗画像は詳細JSONの `failure_image_path` から `logs/analysis_failures/**/*.{png,jpg,jpeg,webp}` を参照する。元フレームの `source_captures.source_path` と相互代用しない。相対pathはrepository rootを基準にPOSIX形式で解決し、絶対path、traversal、backslash、namespace外を拒否する。`short` は7日、`standard` は30日、`indefinite` は期限なしで、UTC `basis_at` から `expires_at` を計算する。retention metadataは削除を実行せず、明示入口もfailure imageを生成・copyしない。

## Source Capture Reference

`source_captures` は、保存またはスキップ判断の元になったフレーム参照を保持する。

候補列:

- `capture_id`
- `capture_hash`
- `captured_at`
- `source_kind`: schema互換用語彙は `manifest` / `timestamped` / `capture` / `manual` / `unknown`。正式writer入力では由来不明の `unknown` を拒否する。
- `source_path`
- `manifest_image_path`
- `frame_index`
- `created_at`

画像そのものはGit管理しない。正式アプリではローカルアプリデータ配下またはログディレクトリに置き、DBにはhashと参照だけを残す方針にする。

`source_captures` はフレームやキャプチャの参照を保持するtableであり、解析ログ本文、DB診断ログ、低信頼度ログ本文を持たない。`plays.source_capture_id` と `analysis_logs.source_capture_id` は同じ capture reference を指せるが、`source_path` / `manifest_image_path` は入力フレーム参照であり、`analysis_logs.log_path` や diagnostic JSONL のパスとは別物として扱う。

## Metadata と Migration

正式DB判定は以下の全てを見る。

- `PRAGMA user_version`
- `score_db_metadata.schema_name=personal_score_db`
- `score_db_metadata.schema_contract_scope=production_personal_score_db`
- `score_db_metadata.production_schema_status=production_schema`
- 必須tableの存在
- `schema_migrations` の適用履歴

`PRAGMA user_version=1` だけでは正式DB扱いしない。M8 preview DBも `user_version=1` を使うため、`preview_metadata` があるDB、`score_db_metadata` がないDB、`production_schema_status=not_production_schema` のDBは正式DBとして拒否する。

初期migrationは `001_initial_personal_score_db_schema` とし、以後の変更は次の原則に従う。

- 既存列の意味を静かに変えない。
- 破壊的変更は自動実行せず、拒否または明示migrationにする。
- unknown schema、preview schema、metadata欠損DBは本番保存前に拒否する。
- migration実行前に必ずbackup方針を決める。

## 互換チェックと拒否理由語彙

正式DBへ本番insertする前に、`inspect_personal_score_db_schema()` で既存DBまたは新規接続の状態を検査する。検査結果は以下を返す。

- `user_version`
- 既存table一覧
- `score_db_metadata`
- 欠落している必須table
- `personal_score_db_compatibility_errors()` と同じ拒否理由
- `migration_plan_status`
- `migration_plan_reason`

`assert_personal_score_db_compatible()` は同じ検査を行い、互換エラーがあれば `ValueError` で止める。これは正式DBとして開いてよいかの入口であり、migration実行や本番insertはまだ行わない。

`personal_score_db_schema_inspection_diagnostic()` は、検査済みの `PersonalScoreDbSchemaInspection` をJSON風のdictへ変換する表示用の投影である。対象path、期待schema version、実 `PRAGMA user_version`、互換可否、`migration_plan_status`、`migration_plan_reason`、拒否理由、必須tableの present/missing、metadata identity の expected/actual/status をまとめる。これはDBを再検査したり変更したりせず、CLIやログで人間が読める形にするための境界である。

`format_personal_score_db_schema_diagnostic_markdown()` は同じdiagnostic dictをMarkdown文字列へ整形する。Markdownには `compatible`、`migration_plan_status`、`migration_plan_reason`、`user_version`、`compatibility_errors`、必須table、metadata identity table を出す。`manual_migration_required` は backup方針と明示確認が必要な状態として表示し、自動migrationや欠落table作成の指示にはしない。

`personal_score_db_file_preparation_diagnostic()` は `PersonalScoreDbFilePreparationResult` のsummaryを同じdiagnostic dictへ重ねる。`existed_before`、`size_before`、`initialized`、初期/最終 `migration_plan_status` を表示できるようにするが、これもファイル準備済み結果の説明であり、本番insertや追加migrationを行わない。

CLI表示入口は `python -m tools.vision_poc --personal-score-db-diagnostic <path>` に置く。既定の `inspect` mode は既存DBを読み取り専用で検査し、Markdownまたは `--personal-score-db-diagnostic-format json` のJSON風dictを標準出力へ出す。存在しないpath、非SQLiteファイル、ディレクトリは正式DBとして開かず、診断上の拒否理由として表示する。

`--personal-score-db-diagnostic-mode prepare-write` は `prepare_personal_score_db_file_for_write(path)` と同じファイル準備境界をCLIから確認するための入口である。新規DBファイルまたは0 byte空ファイルだけ正式初期schemaへ初期化し、`file_preparation` summaryを表示する。既存compatible DBは変更しない。M8 preview DB、unknown DB、metadata identity mismatch、`manual_migration_required` 候補、非SQLiteファイル、ディレクトリは拒否診断を出し、自動修復しない。このCLI入口も本番insert、既定自動保存、既存DB migration、低信頼度ログ本番保存には進まない。

`--personal-score-db-diagnostic-output <path>` は、標準出力と同じ診断をファイルへ残す軽い生成物入口である。出力先は `data/` 配下だけを許可し、format と拡張子の不一致を拒否する。Markdown は `.md` / `.markdown`、JSON は `.json` だけを許可する。`prepare-write` modeで新規DBを初期化する場合も、診断ファイルはDB pathとは独立に明示指定された `data/` 配下へだけ保存する。この入口は診断結果の保存であり、解析ログ本番保存、本番insert、自動migrationには進まない。

`--personal-score-db-diagnostic-log-output <path>` は、同じdiagnostic dictを `logs/` 配下のJSONLへappendするDB診断ログ入口である。1回のCLI実行につき1行だけ追加し、`log_schema_version=1`、`event_type=personal_score_db_diagnostic`、mode、format、exit code相当status、対象DB path、diagnostic output path、diagnostic dictを記録する。これらのkeyは必須で、書き込み前に schema version、event type、mode、format、status、exit code、`diagnostic.is_compatible` との整合を検査する。log output先は `.jsonl` に限定し、`logs/` 外指定や拡張子不一致は `prepare-write` のDB作成・初期化より前に拒否する。log outputはDB診断を記録するだけで、`analysis_logs.log_path` が将来参照する本番解析ログではなく、本番insert、既定自動保存、既存DB migration、低信頼度ログ本番保存、source capture保存には進まない。

互換チェックの主な拒否理由は以下。

- `schema_version_mismatch`: `PRAGMA user_version` が正式schema versionと一致しない。
- `m8_preview_database_not_supported`: `preview_metadata` を持つM8 preview DB。正式DBとしては拒否する。
- `unknown_database_not_supported`: tableはあるが `score_db_metadata` がなく、正式DBともpreview DBとも識別できない。
- `missing_table:<table>`: 正式DB必須tableが欠落している。
- `score_db_metadata_missing`: `score_db_metadata` がない。
- `score_db_metadata.<key>_missing`: 必須metadata keyがない。
- `score_db_metadata.<key>_mismatch`: 必須metadata valueが期待値と違う。

`migration_plan_status` は現時点では自動migrationではなく、次の扱い候補を示すだけにする。

- `compatible`: そのまま正式DBとして扱える。
- `initialize_empty_database`: user tableがない空DB。初期化候補だが、既存DBの自動migrationではない。
- `manual_migration_required`: 正式metadataで識別できるが、versionや必須tableが合わない。backup方針と明示確認を決めるまで自動変更しない。
- `reject_m8_preview_database`: M8 preview DB。正式DBへ自動昇格しない。
- `reject_unknown_database`: metadata欠損DB、metadata identity mismatch、未知schema。正式DBとして開かない。

metadata identity は `created_by`、`schema_name`、`schema_contract_scope`、`production_schema_status` を見る。これらが一致しないDBは、`user_version=1` や似たtable名があっても正式DBとして扱わない。`schema_version` だけの不一致は、正式metadata identityが揃っている場合に限り `manual_migration_required` の候補として読む。

## 初期化とオープン前段

`initialize_personal_score_db_if_empty()` は、検査結果が `initialize_empty_database` の場合だけ `create_personal_score_db_schema()` を実行する。初期化後は再検査し、正式metadata、`PRAGMA user_version`、必須tableがそろった `compatible` 状態として返す。

以下は自動変更しない。

- `compatible`: 既存の正式DBとして扱い、schema再作成やmetadata上書きはしない。
- `reject_m8_preview_database`: M8 preview DBを正式DBへ自動昇格しない。
- `reject_unknown_database`: metadata欠損DBやidentity mismatch DBを正式DBへ寄せない。
- `manual_migration_required`: backup方針と明示確認を決めるまで、欠落tableの作成や `user_version` 修正をしない。

`prepare_personal_score_db_for_write()` は、正式writerのオープン前段である。空DBなら初期化してから互換性を確認し、`compatible` なら検査結果を返す。互換エラーが残るDBは `migration_plan_status` と拒否理由を含む `ValueError` で止める。この関数単体はinsertを行わず、`write_personal_score_db_save()` が検査済みconnectionへtransaction writeする。

`prepare_personal_score_db_file_for_write(path)` は、正式DBファイルをパス単位で検査する前段である。新規ファイル、または既存の0 byte空ファイルだけSQLiteとして開いた後に `initialize_empty_database` へ進め、正式初期schemaを作成できる。既存の compatible DB はそのまま通し、schema再作成やmetadata上書きはしない。既存のM8 preview DB、unknown DB、metadata identity mismatch、`manual_migration_required` 候補、SQLiteとして読めないファイル、ディレクトリは拒否し、自動変更しない。戻り値には、対象path、既存ファイルだったか、既存サイズ、初期化結果、最終inspectionを含める。

`save_personal_score_db_file()` はこのファイル境界と既存writerを合成し、明示された新規/0 byte/compatible正式DBへ1件だけ記録する。readyはsource/play/analysis、excludedはsource/analysisだけを保存する。DB保存直前duplicate collisionは `adapter_status=excluded`、`written=true`、`play_id=null`、理由 `duplicate_key_already_saved` として返す。M8 preview DB、unknown DB、metadata identity mismatch、`manual_migration_required`、非SQLite、ディレクトリは同じ拒否理由で止め、自動修復しない。writer失敗時は同じ呼び出しのrowをrollbackする。

この明示ファイル保存はM8 preview の `--m8-score-db-output` とは別物として扱う。`--personal-score-db-save-input` と `--personal-score-db-save-database` の必須ペアを指定した単発CLIだけが1回呼べる。通常PoC、timestamped/manifest runner、既定自動保存、既存DB migration、DB診断の自動ファイル出力には進まない。

CLI入力はUTF-8 JSONの `input_schema_version=1` objectとする。候補材料、source/analysis値、任意の `formal_play`、任意の `exclusion` を別構造にし、全階層の必須/未知keyと型をadapter前に検査する。M5 `identity_signal_*`、M7a `recognized_digits`、`played_at_ms` / `timestamp_ms` は正式playへ暗黙コピーしない。不正入力は終了コード2、adapterの `unresolved` は終了コード1でDB準備前に止め、transaction完了した `ready` / `excluded` だけ終了コード0とする。

`--personal-score-db-save-input-validate` は同じJSON契約をDB保存前に検査する単独入口である。strict loaderとadapterだけを各1回実行し、`validation_result_schema_version=1`、入力path、`adapter_status`、`save_input_constructed`、理由をJSONで返す。ready/excludedは0、unresolvedは1、不正JSON/schemaまたはoption混在は2とする。DB pathを受け取らず、DB準備、duplicate preflight、insert、diagnostic/output/log生成を行わないため、readyはDB互換性、既存duplicate非衝突、並行writer安全性、実保存成功を保証しない。

`--personal-score-db-save-input-validate-output <data/...json>` はvalidation inputとの必須ペアで、同じvalidation投影だけを新規receiptへ保存する。receiptはschema version 1の5 keyだけを固定順で持ち、正式値、候補材料、template本文、DB情報を持たない。path/拡張子/既存ファイル/option排他をinput loadより先に拒否し、receiptの有無でready/excluded/unresolved/invalidや終了コードを変えない。これはレビュー結果の記録であって、レビュー承認、DB互換性、DB内duplicate非衝突、並行writer安全性、実保存成功の証明ではない。

`--personal-score-db-save-input-template <data/...json>` は、この同じ外部入力schemaを人がレビューするための空templateを新規作成する単独入口である。`candidate_material={}`、全fieldを明示した空文字/nullの `formal_play`、`exclusion=null` を固定し、現行strict loaderで読み戻せる一方、未編集状態はadapterで `unresolved` にする。候補値や正式値を自動生成せず、template生成からvalidation、DB検査、duplicate preflight、insertへ自動連鎖しない。

## 未決事項

- `play_id` と `duplicate_key` の本格生成方式。
- OCR raw、normalized、M5候補観測、M7a候補値の詳細ログ粒度。
- analysis artifactとsaveの接続設計は確定済み。現行CLIは独立のまま維持し、後続の明示orchestration入口がpath/ID/status一致、artifact先行publish、DB失敗時のartifact保持と同一payload再利用を担当する。
- マスタDBの互換方針が固まった後の `master_version` と `song_id` / `chart_id` の扱い。

## 回帰ガード

- `tests/test_personal_score_db_schema.py` は正式schema contractを作成し、必須tableとmetadataを確認する。
- 同テストは M8 preview DB を正式個人スコアDBとして拒否する。
- 同テストは空DB、未知DB、metadata identity mismatch、必須table欠落、`user_version` mismatch の検査結果と `migration_plan_status` を固定する。
- 同テストは空DBだけ初期schemaを作成し、M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補を自動変更しないことを固定する。
- 同テストはファイルパス境界として、新規DBファイルと0 byte空ファイルだけ正式schemaへ初期化でき、compatible DBは変更せず、M8 preview DB、unknown DB、metadata identity mismatch、manual migration候補、非SQLiteファイル、ディレクトリを自動変更しないことを固定する。
- 同テストは compatible、空DB、M8 preview DB、unknown DB、manual migration候補のdiagnostic dict / Markdown表示を固定し、拒否理由、必須table欠落、metadata identity、path情報、ファイル準備summaryを人間が読める形に保つ。
- 同テストは preview列、M7a raw候補、OCR raw/normalized が正式 `plays` に混入しないことを確認する。
- 同テストは `source_captures` がフレーム参照列だけを持ち、`analysis_logs.log_path` や diagnostic JSONL と混同しないことを確認する。
- `tests/test_personal_score_db_analysis_artifacts.py` はversion 1 strict contract、安全なoutput path、既存ファイル保護、決定的UTF-8/LF出力、atomic publish失敗時の清掃、CLI排他、failure image非生成を固定する。
- 後続orchestration fixtureはready/excluded/duplicate/error、artifact任意/必須、共有値不一致、file失敗、DB rollback、同一artifact再利用とconflictを行列で固定する。正式schemaと既存writer transactionは変更しない。
- `tests/test_personal_score_db_save.py` は正式保存入力の必須値、timezone、正式duplicate key、source/play/analysisの参照整合を固定する。
- 同テストは正常保存で3tableへ1 transactionでinsertし、duplicate/低信頼度では `plays` を0件のままsource captureとanalysisだけを記録する。
- 同テストは既存正式playのduplicate key衝突を保存直前に検出し、2件目のplayを作らず固定語彙のsource/analysisだけを記録する。完全同一ID再送はUNIQUE拒否し、部分rowを残さない。
- 同テストは入力不整合をschema作成前に拒否し、play insert失敗時に同じ呼び出しのsource captureとanalysisをrollbackする。
- `tests/test_personal_score_db_save_adapter.py` は候補ID/数字/相対時刻を正式値へ昇格しないこと、正式値不足を `unresolved` に保つこと、duplicate/低信頼度をplayなしの `excluded` にすることを固定する。
- `tests/test_personal_score_db_cli_save.py` は保存前validationのready/excluded/unresolved/invalid、option排他、従来modeのDB/`data`/`logs`非生成、receiptの新規 `data/*.json` 限定と固定encoding/key順、正式値非再掲を固定する。
- `tests/test_personal_score_db_file_save.py` は新規/0 byte/compatible正式DBへのready保存、excludedのplayなし保存、DB duplicate collisionの `excluded/written/play_id` 結果、unresolvedの無変更拒否、preview/unknown/identity mismatch/manual migration/non-SQLite/directory拒否、writer失敗時rollbackを固定する。
