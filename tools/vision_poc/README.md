# Vision PoC

`samples/screenshots/organized/` のローカルスクリーンショットを使い、リザルト候補分類、ROI切り出し、スコア数字OCRの初期前処理を確認するためのPoCです。

## 実行

```powershell
python -m tools.vision_poc
```

`Pillow` / `numpy` がない環境では先に以下を実行します。

```powershell
python -m pip install -e ".[vision]"
```

既定では以下を読み書きします。

- 入力メタデータ: `samples/screenshots/metadata.csv`
- 入力画像: `samples/screenshots/organized/`
- 出力先: `data/vision_poc/`

出力先には `results.csv`、`result_events.csv`、`result_events_summary.json`、`summary.json`、`misclassifications.md`、`m3_metadata_expected_coverage.md`、`m3_metadata_expected_template.csv`、`m3_chart_fields.csv`、`m3_chart_fields_summary.json`、`m3_chart_field_extraction.csv`、`m3_chart_field_extraction_summary.json`、`m3_chart_field_image_feature_extraction.csv`、`m3_chart_field_image_feature_extraction_summary.json`、`m3_chart_field_image_feature_diagnostics.md`、`m3_chart_field_template_extraction.csv`、`m3_chart_field_template_extraction_summary.json`、`m3_chart_field_template_diagnostics.md`、`m3_chart_field_template_holdout_extraction.csv`、`m3_chart_field_template_holdout_extraction_summary.json`、`m3_chart_field_template_holdout_diagnostics.md`、`m3_chart_field_adoption_candidates_summary.json`、`m3_chart_field_adoption_candidates.md`、`m3_save_candidate_summary.csv`、`m3_save_candidate_summary.json`、`m3_save_candidate_summary.md`、`m3_save_candidate_blockers_summary.json`、`m3_save_candidate_blockers_summary.md`、`m3_save_candidate_blocker_resolution_plan.json`、`m3_save_candidate_blocker_resolution_plan.md`、`rois/<画像名>/` 配下の主要ROI画像が生成されます。`--m3-song-artist-ocr` 指定時は `m3_song_artist_ocr_entry_failures_summary.json` と `m3_song_artist_ocr_entry_failures.md` も生成します。`--m5-master-match` 指定時は `master_match_candidates.csv`、`master_match_summary.json`、`master_match_report.md` も生成します。`--m5-jacket-match` 指定時は `jacket_feature_master.csv`、`jacket_feature_master_summary.json`、`jacket_feature_label_template.csv`、`jacket_match_candidates.csv`、`jacket_match_summary.json`、`jacket_match_report.md`、`jacket_match_diagnostics.csv`、`jacket_match_diagnostics_summary.json`、`jacket_match_diagnostics.md`、`jacket_reference_coverage.csv`、`jacket_reference_coverage_summary.json`、`jacket_reference_coverage_missing_representatives.csv`、`jacket_reference_coverage_report.md`、`jacket_reference_diagnostics_coverage.csv`、`jacket_reference_diagnostics_coverage_summary.json`、`jacket_reference_diagnostics_coverage_missing_representatives.csv`、`jacket_reference_diagnostics_coverage_report.md` も生成します。`data/` はGit管理対象外です。`rois/<画像名>/` には分類確認用ROIに加えて、M3入口の目視確認用として `play_style`、`difficulty`、`level`、`rank`、`song_title`、`artist` も出力します。この段階では切り出し足場、ファイル名由来 baseline、ROI画像特徴の軽い比較、ローカルテンプレート素材との最近傍比較を中心にし、M5のマスタ照合もPoC観察に留めます。

`--m3-song-artist-ocr` を指定した場合は、追加で `m3_song_artist_ocr.csv`、`m3_song_artist_ocr_summary.json`、`m3_song_artist_ocr.md`、`m3_song_artist_ocr_entry_failures_summary.json`、`m3_song_artist_ocr_entry_failures.md`、`m3_song_artist_ocr_images/<画像名>/` が生成されます。これはM3-4の曲名/artist OCR入口とM3-9のOCR入口失敗代表整理で、confirmed-events 境界だけを対象にし、マスタ照合、ファジーマッチ、曲名正規化の成功扱いにはしません。

`m3_save_candidate_summary.csv`、`m3_save_candidate_summary.json`、`m3_save_candidate_summary.md` はM3-5の保存候補向け集約レポートです。confirmed-events 1件を1行にし、`song_title`、`artist`、`play_style`、`difficulty`、`level` の状態を `ready` / `missing_reference` / `ocr_unavailable` / `ocr_failed` / `empty_ocr` / `no_expected_value` / `not_adopted` へ寄せます。`--m3-song-artist-ocr` を指定していない場合、`song_title` / `artist` はOCR未実行として `ocr_unavailable` になります。このレポートもDB保存可能、マスタ照合成功、ファジーマッチ成功、曲名正規化成功を意味しません。

`m3_save_candidate_blockers_summary.json` と `m3_save_candidate_blockers_summary.md` はM3-6の保存候補ブロッカー代表整理です。M3-5集約のうち `ready` ではないfieldを、status / failure reasonごとに数件だけ代表化し、`organized_file`、期待値、抽出値、extractor、`roi_path` を出します。対象は confirmed-events だけで、duplicate、`rejected_transition`、未確定候補、non-result は含みません。これはレビュー補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進みません。

`m3_save_candidate_blocker_resolution_plan.json` と `m3_save_candidate_blocker_resolution_plan.md` はM3-7の保存前ブロッカー解消順レビューです。M3-5集約の未ready fieldを、追加すべきローカルテンプレート参照ラベル、OCR未実行、OCR入口の空読み失敗、参照追加後の再確認に分けます。テンプレート画像やOCR画像はローカル素材のままGit管理せず、必要ラベル、代表ROI、判断だけをdocsに残すための補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進みません。

`--m7a-digit-recognition` を指定した場合は、追加で `m7a_digit_recognition.csv`、`m7a_digit_recognition_summary.json`、`m7a_digit_recognition_report.md`、`m7a_tesseract_comparison_review.json`、`m7a_tesseract_comparison_review.md`、`m7a_digit_save_candidate_summary.csv`、`m7a_digit_save_candidate_summary.json`、`m7a_digit_save_candidate_summary.md`、`m7a_digit_save_candidate_review.json`、`m7a_digit_save_candidate_review.md`、`m7_save_readiness_review.csv`、`m7_save_readiness_review.json`、`m7_save_readiness_review.md`、`m7_save_decision_preview.csv`、`m7_save_decision_preview.json`、`m7_save_decision_preview.md`、`m8_save_payload_preview.csv`、`m8_save_payload_preview.json`、`m8_save_payload_preview.md`、`m8_planned_play_records.csv`、`m8_planned_play_records.json`、`m8_planned_play_records.md`、`m8_score_db_write_preview.csv`、`m8_score_db_write_preview.json`、`m8_score_db_write_preview.md` が生成されます。さらに `--m8-score-db-output data\...\ddrgp-scores.sqlite` を明示した場合だけ、`m8_score_db_file_output_preview.json` と `m8_score_db_file_output_preview.md` も生成し、指定した `data/` 配下の新規SQLiteファイルへ保存予定レコードをinsertします。これはM7aのスコア系数字認識PoCで、対象は confirmed-events 境界の `confirmed_result=true` かつ `duplicate=false` だけです。初期対象は既定で `score_digits` だけにし、`--m7a-digit-rois all` で `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` へ広げられます。Tesseractは使わず、ROI内の前景maskを桁分割して、ローカルテンプレートとのbitmap最近傍距離で `recognized` / `ambiguous` / `missing_reference` / `failed_segmentation` / `not_evaluated` を出します。`score_digits` は0から1,000,000までの可変桁表示を前提に、カンマや背景ノイズを大きな数字成分から分けて左から読むため、1桁から7桁までを固定桁数へ寄せません。既存 `score_ocr.csv` / `score_ocr_summary.json` は変更せず、同じ実行でOCR結果がある場合だけ summary の `tesseract_comparison` で正規化済み数字列を比較します。テンプレートは既定で `samples/screenshots/organized/digit_templates/<roi>/<digit>.png`、共有グループ、または `<root>/<digit>.png` を読みますが、画像素材はローカル素材としてGit管理しません。テンプレート画像は数字前景の周囲に背景余白を含めます。

`max_combo` は同じROI内に左側ラベルと下線が入るため、M7aではROI右側の数字領域へ寄せ、数字らしい高さの前景コンポーネントだけを桁候補として分けます。`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` も左側ラベルを数字として拾わないよう、右側数字領域へ寄せて同じcomponent分割で読みます。判定数ROIでは、明るい青背景を数字扱いしないよう高明度かつチャンネル差が大きい成分を除外します。`miss` はさらに白数字向けの明度 + チャンネル差maskで前景を切り、短いマーカーを数字として拾わないよう数字候補の高さを少し絞ります。`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` は3桁だけでなく4桁fixtureでも分割と認識を固定しています。`m7a_digit_recognition_summary.json` のROI別bucketには `segment_count_counts` と `expected_digit_length_counts` があり、テンプレート不足で `missing_reference` の段階でも、分割数が期待値桁数と合っているかを先に確認できます。2026-07-07時点のローカル `max_combo` テンプレート配置後は、`score_digits` と `max_combo` の2 ROIで confirmed-events 60件ずつ、合計120/120 `recognized` / match です。

`m7a_digit_save_candidate_review.json` と `m7a_digit_save_candidate_review.md` は、M7a横持ち集約から `aggregate_status=needs_digit_review` の行だけを入力として、ROI別 `status` / `failure_reason` ごとに代表を出すレビュー補助です。代表には `organized_file`、ROI名、`recognized_digits`、`expected_value`、`status`、`failure_reason`、`match`、`confidence`、`distance`、`segment_count` を含めます。`missing_reference` はテンプレート不足、`ambiguous` は距離や余白不足、`failed_segmentation` は桁分割失敗、`not_evaluated` は期待値不足または未試行として読み分けます。このレポートも保存OK/NG判定やDB保存ではなく、duplicate、`rejected_transition`、未確定候補、non-result は対象外のままです。

`m7a_tesseract_comparison_review.json` と `m7a_tesseract_comparison_review.md` は、同じ実行内の `m7a_digit_results` と既存 `score_ocr_results` だけを比較し、`same_normalized`、`different_normalized`、`tesseract_unavailable`、`m7a_unavailable` ごとに代表を出すTesseract差分レビュー補助です。既存 `m7a_digit_recognition_summary.json` の `tesseract_comparison` counts は維持し、この詳細代表で `different_normalized` の実例や、Tesseract側の正規化数字列が出ていないROI/理由を確認します。代表には `organized_file`、ROI名、M7a `recognized_digits` / `status` / `failure_reason`、Tesseract raw / normalized / status / error、`expected_value`、M7a match、Tesseract match を含めます。保存OK/NG判定、DB保存、OCR方式刷新には進みません。

`m7_save_readiness_review.csv`、`m7_save_readiness_review.json`、`m7_save_readiness_review.md` は、M3保存候補材料、M7a数字材料、任意のM5 jacket候補観測を confirmed-events 1件単位で束ねるM7保存判定前レビューです。`--m5-jacket-match` 実行時は `jacket_match_rows` 由来の `identity_signal_*` と `jacket_match_status` も参照列として取り込みます。`identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` の場合だけM5側材料をレビュー可能な候補観測として扱い、未解決の場合は `blocked_identity_signal` として止めます。M5 identity材料がレビュー可能な場合、曲同定はM5候補観測を主材料として読むため、`song_title` / `artist` OCR不足だけでは `blocked_m3_material` にしません。元のM3集約の未ready項目は `m3_blocking_fields` に残し、M7保存前レビュー上のM3 blockerは `m7_m3_blocking_fields` として別に読みます。M5未実行時は従来どおりM3材料とM7a数字材料だけでレビューし、`song_title` / `artist` 不足もM3 blockerとして扱います。`ready_for_save_review` はレビュー材料が揃った状態であり、保存OK、DB保存成功、曲ID/譜面ID確定を意味しません。duplicate、`rejected_transition`、未確定候補、non-result は対象外のままです。

`m7_save_decision_preview.csv`、`m7_save_decision_preview.json`、`m7_save_decision_preview.md` は、`m7_save_readiness_review_rows` を入力にした保存判定プレビューです。preview status は `preview_save_candidate`、`blocked_readiness`、`needs_identity_review`、`needs_digit_review`、`missing_required_material` だけを使います。`preview_save_candidate` はM8へ渡す候補材料が揃ったプレビュー状態で、保存OK、DB保存成功、曲ID/譜面ID確定ではありません。M5未実行やM5候補観測未解決は `needs_identity_review`、M7a数字レビューは `needs_digit_review`、M7 readiness上のM3 blockerは `blocked_readiness`、必須PoC材料欠落は `missing_required_material` として分けます。`needs_identity_review` の `preview_reason` は `m5_not_run`、`m5_identity_not_reviewable`、`identity_signal_id_missing` を分けます。JSON / Markdownには `preview_save_candidate` の M5 source、jacket status、identity signal status の件数と代表、`needs_identity_review` の理由別代表、`needs_digit_review` のROI別 `recognized_digits` / `expected_value` / `match` / `failure_reason` 代表も出します。CSVに出す `identity_signal_song_id` / `identity_signal_chart_id` とM7aの `recognized_digits` / `expected_value` / `match` は候補観測であり、保存値確定として扱いません。

`m8_save_payload_preview.csv`、`m8_save_payload_preview.json`、`m8_save_payload_preview.md` は、`m7_save_decision_preview_rows` を入力にしたM8 dry-run payload previewです。`preview_status=preview_save_candidate` の行だけをpayload候補として扱い、それ以外は `unsupported_preview_status` としてpayload材料へ昇格せず、summary の `excluded_preview_status_counts` とMarkdown代表で読みます。payload status は `payload_ready`、`missing_identity_candidate`、`missing_digit_value`、`unsupported_preview_status` だけを使います。`payload_ready` は将来DBへ渡すなら使う材料が揃ったdry-run状態で、DB保存可能、保存成功、曲ID/譜面ID確定、保存値確定ではありません。CSVの `identity_signal_song_id` / `identity_signal_chart_id` / `identity_signal_source` はM5候補観測、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` はM7a `*_recognized_digits` 由来の候補値としてだけ読みます。

`m8_planned_play_records.csv`、`m8_planned_play_records.json`、`m8_planned_play_records.md` は、M8 payload previewの `payload_ready` 行だけを個人スコアDB `plays` 相当の最小row contractへ変換するプレビューです。`unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` は保存予定レコードへ変換しません。`song_id` / `chart_id` はM5候補観測、数字列はM7a候補値であり、保存用確定IDや保存値確定ではありません。

`m8_score_db_write_preview.csv`、`m8_score_db_write_preview.json`、`m8_score_db_write_preview.md` は、保存予定レコードだけを新規の in-memory SQLite `plays` テーブルへinsertするdry-runプレビューです。入力は `m8_planned_play_records_rows` に限定し、非ready payloadは上流の planned records で止まります。summary/report には `schema_name=m8_score_db_preview`、`schema_version=1`、`schema_version_source=PRAGMA user_version`、`schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema`、`created_by_preview=tools.vision_poc.m8_score_db_preview` を出します。SQLite側にも軽量な `preview_metadata` 表を作りますが、これはpreview生成物識別だけに使い、正式マイグレーションではありません。preview `plays` の `play_id` と `created_at` はDB側補助列として扱い、insert対象列は `m8_planned_play_records.*` の最小row contractと同じ順序に保ちます。この列順・integer型のテストはpreview最小スキーマの内部整合ガードであり、正式個人スコアDBスキーマ確定ではありません。`inserted_in_memory`、`schema_version`、`schema_contract_scope`、`production_schema_status`、`created_by_preview` はスキーマとinsert境界の確認であり、実ファイルDB生成、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定ではありません。timestampなし入力の `played_at_ms=0` は暫定値のままinsert境界へ渡します。

`--m8-score-db-output data\...\ddrgp-scores.sqlite` は、明示指定された場合だけ実ファイルSQLiteへ書くM8出力プレビューです。出力先は `data/` 配下の新規ファイルに限定し、`data/` 外や既存ファイルへの書き込みは拒否します。入力は `m8_planned_play_records_rows` だけで、非ready payloadやM5未実行で止まった行をここで再判定しません。出力DBの `plays` は既存 in-memory preview と同じ最小列で、`PRAGMA user_version=1` と `preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview`、`preview_metadata.schema_contract_scope=preview_minimal_plays`、`preview_metadata.production_schema_status=not_production_schema` を設定します。`m8_score_db_file_output_preview.json` / Markdown では `schema_version=1`、`schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema`、`created_by_preview`、`inserted_to_file_preview` に加えて、実DBから読み戻した `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns`、preview契約やinsert件数との一致を示す `database_readback_matches_preview_contract`、`database_readback_mismatch_reasons`、`database_plays_row_count_matches_insert_counts`、`database_plays_row_count_mismatch_reasons`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` を出します。`database_readback_mismatch_reasons` の `database_preview_metadata.<key>_missing` は期待key欠落、`database_preview_metadata.<key>_mismatch` はkeyはあるが値が違う状態です。schema readback診断は実ファイルpreview DBの `PRAGMA table_info(plays)` がpreview最小 `plays` と一致するかの確認であり、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定ではありません。

正式個人スコアDBの初期schema contractは `tools.vision_poc.personal_score_db_schema` に分けています。ここでは `score_db_metadata`、`schema_migrations`、`source_captures`、正式 `plays`、`analysis_logs` のtable候補と、M8 preview DBを正式DBとして拒否する互換チェックだけを固定します。これは本番insert入口ではなく、M8 preview最小 `plays` と正式 `ddrgp-scores.sqlite` を混同しないための設計・テスト用足場です。詳細は `docs/design/10_personal_score_db_schema.md` を参照してください。

正式schema contractの検査入口は `inspect_personal_score_db_schema()` と `assert_personal_score_db_compatible()` です。検査では空DBを初期化候補、M8 preview DBを `reject_m8_preview_database`、metadata欠損やidentity不一致を `reject_unknown_database`、正式metadataつきのversion/table不整合を `manual_migration_required` として分けます。`initialize_personal_score_db_if_empty()` と `prepare_personal_score_db_for_write()` は、空DBだけ正式初期schemaを作り、preview / unknown / manual migration候補を自動変更せずに止めるためのオープン前段です。`prepare_personal_score_db_file_for_write(path)` はその前段をファイルパスへ広げ、新規DBファイルまたは0 byte空ファイルだけ初期化し、既存の正式DBは変更せず、preview / unknown / identity mismatch / manual migration候補 / 非SQLiteファイル / ディレクトリを拒否します。`personal_score_db_schema_inspection_diagnostic()` と `format_personal_score_db_schema_diagnostic_markdown()` は検査結果をJSON風dict / Markdownへ投影し、path、`migration_plan_status`、`migration_plan_reason`、拒否理由、必須table欠落、metadata identityを人間が読める形にします。`personal_score_db_file_preparation_diagnostic()` はファイル準備結果の `existed_before`、`size_before`、`initialized`、初期/最終statusを同じ診断へ重ねます。これらは本番insertや自動migrationではなく、既存DBを正式個人スコアDBとして開いてよいかを説明するための足場で、`--m8-score-db-output` のpreview DB出力とは別物です。

正式DB診断はCLIからも確認できます。`python -m tools.vision_poc --personal-score-db-diagnostic path\to\ddrgp-scores.sqlite` は既存DBを読み取り専用で検査し、Markdown診断を標準出力へ出して終了します。`--personal-score-db-diagnostic-mode prepare-write` を付けると、`prepare_personal_score_db_file_for_write(path)` と同じ境界で、新規DBファイルまたは0 byte空ファイルだけ正式初期schemaへ初期化し、file preparation summaryつきの診断を出します。compatible DBは変更せず、M8 preview DB、unknown DB、manual migration候補、非SQLiteファイル、ディレクトリは拒否表示に留めます。`--personal-score-db-diagnostic-format json` は同じ内容をJSON風dictとして出します。`--personal-score-db-diagnostic-output data\...\diagnostic.md` または `.json` を付けると、標準出力と同じ診断を `data/` 配下へ保存します。拡張子は format と一致させ、Markdown は `.md` / `.markdown`、JSON は `.json` に限定します。`--personal-score-db-diagnostic-log-output logs\...\personal-score-db.jsonl` を付けると、診断1回につき1行のDB診断JSONLログを `logs/` 配下へappendします。ログレコードは `log_schema_version`、`event_type=personal_score_db_diagnostic`、mode、format、exit code相当status、対象DB path、diagnostic output path、diagnostic dictを必須keyとして持ち、`diagnostic.is_compatible` と exit code / status の整合も書き込み前に検査します。このファイル出力とログ出力は診断の保存だけであり、本番insert、既定自動保存、既存DB migration、低信頼度ログ本番保存、source capture保存には進みません。将来 `analysis_logs.log_path` から参照する本番解析ログや低信頼度ログは、このdiagnostic JSONLとは別ファイルとして扱います。

テンプレート探索はROI別ディレクトリを最優先し、判定数系の `marvelous`、`perfect`、`great`、`good`、`miss` は共有 `digit_templates/judgment_counts/<digit>.png` も参照します。`max_combo` と `ex_score` は、フォント共通化候補として共有 `digit_templates/combo_ex_score/<digit>.png` も参照します。さらに `ex_score` は、`combo_ex_score` がない環境でも既存 `digit_templates/max_combo/<digit>.png` をfallbackとして参照します。ROI別テンプレートを残したまま共有ディレクトリや `max_combo` fallbackを試せるため、ローカル素材の削除や移動をしなくても共通化可否を検証できます。2026-07-08時点のローカル確認では、`miss` は共有 `judgment_counts` だけだと分割数は期待桁数と一致するものの、60件中58件が `ambiguous` になるため、ROI別 `digit_templates/miss/` テンプレートを使います。同日時点で `ex_score` は右側数字領域へのcomponent分割と `max_combo` fallbackにより、ROI別 `ex_score` テンプレートなしで60/60 `recognized` / matchです。

`--m5-jacket-match` 指定時は、通常の保存候補出力 `jacket_match_candidates.csv` / summary / Markdown に加えて、`jacket_match_diagnostics.csv`、`jacket_match_diagnostics_summary.json`、`jacket_match_diagnostics.md` も生成します。通常候補は引き続き `confirmed_result=true` かつ `duplicate=false` のM3保存候補だけを対象にします。診断出力は別ファイルで、metadata上のresult行、未確定result、duplicateを含め、`m5_target_boundary_reason` で `save_candidate` / `unconfirmed` / `duplicate` などを分けます。診断行の曲名と譜面3項目はローカルmetadata期待値を `metadata-expected-diagnostic` として使う観察用入力であり、保存候補への昇格やDB保存可能判定を意味しません。

同じ `--m5-jacket-match` では、参照カバレッジ診断として `jacket_reference_coverage.csv`、`jacket_reference_coverage_summary.json`、`jacket_reference_coverage_missing_representatives.csv`、`jacket_reference_coverage_report.md` も生成します。これは通常候補について、`play_style` / `difficulty` / `level` で絞った候補song_idごとに、song_select由来のローカルjacket特徴量参照があるかを出す観察用レポートです。duplicate / unconfirmed を含む診断側は、別ファイル `jacket_reference_diagnostics_coverage.csv`、`jacket_reference_diagnostics_coverage_summary.json`、`jacket_reference_diagnostics_coverage_missing_representatives.csv`、`jacket_reference_diagnostics_coverage_report.md` として生成します。`expected_song_reference_status` は `expected_unresolved`、`expected_not_in_chart_candidates`、`expected_missing_feature`、`expected_referenced` を分け、参照不足や期待曲名未解決を近傍の別曲へ寄せた解消扱いにしません。

`jacket_match_report.md` は `identity_signal_status` ごとの代表行を出し、通常候補内の `jacket_resolved_candidate`、`composite_resolved_candidate`、`unresolved_*` を保存判定前の観測カテゴリとして確認できます。`jacket_match_diagnostics.md` は `m5_target_boundary_reason` ごとの代表行も出し、`save_candidate` / `unconfirmed` / `duplicate` を同じ診断レポート内で観察できます。どちらの代表行もMarkdown上の読みやすさのための抜粋であり、保存候補CSVの境界やDB保存可否を変えません。

`jacket_match_candidates.csv` と診断CSVには、ローカルmetadata期待曲名をM4 `songs.title` へ突き合わせた `expected_song_resolution_status`、`expected_song_resolution_reason`、`expected_song_grand_prix_play_available`、`expected_song_official_availability_match` を出します。これは `Inner Spirit -GIGA HiTECH MIX-` や `RЁVOLUTIФN` のような未解決代表で、期待曲ラベル自体がM4へ解決できているか、公式GP対象として解決されているかを見るための診断列です。`jacket_match_report.md` と `jacket_match_diagnostics.md` の `Unresolved Identity Signal Representatives` は、`unresolved_*` だけを抜き出し、期待曲解決状態、GP可否、公式突合状態、line-hash辞書状態を同じ行で確認できるようにします。この表も保存候補化、曲ID確定、`jacket_match_status` 昇格、GP対象外曲の復帰には使いません。

この既定実行は metadata 評価モードです。`samples/screenshots/metadata.csv` の並びをフレーム順として扱いますが、キャプチャ時刻は持たないため、`result_events.csv` の `timestamp_ms` と `candidate_duration_ms` は空欄、`confirmation_mode` は `frames` になります。

入力モードの役割は以下です。

- `metadata`: 分類評価用。`samples/screenshots/metadata.csv` の `organized_file` と `screen_type` を真値として、既存画像の分類精度を確認します。時刻を持たないため、イベント確定は従来どおりフレーム数ベースです。
- `timestamped`: 実キャプチャ前の人工時系列PoC。metadata と同じ画像列へ単調増加する人工 `timestamp_ms` を付け、時刻ベースのリザルト確定を確認します。
- `manifest`: 実フレーム列入力PoC。録画切り出しや将来のキャプチャ処理が供給する `image_path,timestamp_ms` のCSVを読み、実キャプチャに近い入力境界を確認します。

### frame source 境界

PoC内部の薄い入力境界は `FrameInput` です。`FrameInput` は、分類器とイベント確定処理へ渡す1フレーム分の情報として `image_path`、任意の `timestamp_ms`、metadata互換の `row` を持ちます。`row` には `organized_file`、任意の `screen_type`、任意のOCR期待値列、将来のキャプチャ前段が付ける補助列を保持します。

実キャプチャAPIが最初に満たす契約は以下だけです。

- 画像パス、またはフレーム画像へ到達できる参照を1フレームごとに渡す
- `timestamp_ms` はキャプチャ時点のミリ秒値で、入力順に単調増加する
- `screen_type` は任意で、未知の場合は空欄や `unknown` のままでよい
- `score` / `expected_score` / `max_combo` / `expected_<roi_name>` などのOCR期待値列は任意で、あれば `FrameInput.row` に保持する
- 入力順は時系列順にする

`metadata` mode は既存ローカル評価のための時刻なし入力です。`timestamped` mode は同じ metadata 行へ人工 `timestamp_ms` を付け、時刻ベース確定をローカルで再現する互換確認です。`manifest` mode はこの frame source 契約のファイル版で、実キャプチャAPIは同じ契約のリアルタイム版として扱います。つまり、本番キャプチャ導入時に差し替える境界は「CSVを読むか、キャプチャ provider から次フレームを受けるか」だけで、以降の分類、`confirmation_mode=time`、confirmed-events OCR対象絞り込みは同じ意味を維持します。

実キャプチャ相当の時系列PoCは、同じローカル画像列へ人工 `timestamp_ms` を付けて実行できます。

```powershell
python -m tools.vision_poc --sequence-mode timestamped
```

既定の出力先は `data/vision_poc_timestamped/` です。このモードでは `build_result_events(..., timestamps_ms=...)` にキャプチャ時刻相当の値を渡すため、`result_events.csv` の `confirmation_mode` は `time`、`timestamp_ms` と `candidate_duration_ms` はミリ秒値になります。あわせて、同じ人工時系列を manifest 入力で再利用できるように `data/vision_poc_timestamped/frame_manifest.csv` を生成します。

人工 timestamp は既定で `0ms` から `1000ms` 間隔です。必要に応じて以下のように開始時刻や間隔を変えられます。

```powershell
python -m tools.vision_poc --sequence-mode timestamped --timestamp-start-ms 5000 --timestamp-interval-ms 333
```

manifest 実フレーム列モードは、最小列 `image_path,timestamp_ms` を持つCSVを読みます。任意で `screen_type` があれば、`results.csv` と `result_events.csv` にそのまま反映します。`image_path` は既定では manifest ファイルからの相対パスとして解決し、`--frame-root` を指定した場合はそのディレクトリからの相対パスとして解決します。

```powershell
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_timestamped/frame_manifest.csv --frame-root samples/screenshots
```

既定の出力先は `data/vision_poc_manifest/` です。manifest モードでは timestamp 付き入力として扱うため、`result_events.csv` の `confirmation_mode` は `time` になります。`timestamp_ms` はフレーム番号ではなく、キャプチャ時点の単調増加する時刻をミリ秒で表します。空、非整数、負数、非単調増加の timestamp や、存在しない画像パスは行番号付きのエラーにします。

録画切り出しやキャプチャ前段で作った連番画像ディレクトリは、補助入口 `--make-frame-manifest` で manifest 化できます。対象拡張子は `png`、`jpg`、`jpeg`、`webp` で、`--frame-root` 直下の画像をファイル名昇順で読みます。生成CSVの `image_path` は `--frame-root` からの相対パスです。`--screen-type unknown` のように固定 `screen_type` を付けることもできます。

```powershell
python -m tools.vision_poc --make-frame-manifest data/vision_poc_sequences/capture_manifest.csv --frame-root data/captured_frames --fps 30 --screen-type unknown
```

この生成処理で付ける `timestamp_ms` はフレーム番号ではなく、仮のキャプチャ時刻です。`--fps` 指定時は `round(index * 1000 / fps)` を基準にし、丸めで同一timestampが出る場合は直前値 + 1ms に補正して単調増加を保証します。`--fps` が0以下、非数値、無限大、対象画像なし、`--frame-root` 不在、出力先の親ディレクトリ不在はエラーにします。

実キャプチャAPI導入前の dry-run capture provider PoC には `--capture-dry-run` を使います。これは本番キャプチャではなく、既存画像ディレクトリを「capture provider の代替入力」として扱い、ファイル名昇順のフレーム列に単調増加する `timestamp_ms` を付けて `data/` 配下へ保存し、manifest互換CSVを出すだけの入口です。`--make-frame-manifest` は既存画像をその場で参照するCSVだけを作る補助、`--capture-dry-run` は将来の実キャプチャAPIと同じ「フレームを保存してから manifest で再実行できる」境界を確認する補助、という分担です。

```powershell
python -m tools.vision_poc --capture-dry-run --frame-root data/captured_frames --fps 30 --screen-type unknown --capture-dry-run-output data/vision_poc_capture_dry_run
```

`--capture-dry-run-output` は `data/` 配下に限定します。出力は `frames/` と `frame_manifest.csv` で、生成した manifest はそのまま manifest モードで読み直せます。

```powershell
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_capture_dry_run/frame_manifest.csv --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

複数 `screen_type` を混ぜた保存境界シナリオは `--capture-dry-run-scenario` で作れます。入力CSVは既存manifest契約と同じ `image_path,timestamp_ms` を必須列にし、任意で `screen_type`、`expected_score`、`max_combo`、`miss`、`ex_score`、補助列を持てます。`image_path` は `--frame-root` からの相対パス、またはCSVから解決できるパスです。PoCは各行の画像を `data/.../frames/` にコピーし、timestampと任意列を保った manifest互換 `frame_manifest.csv` を出力します。

```csv
image_path,timestamp_ms,screen_type,expected_score,max_combo,capture_note
menu_setup_a.png,0,menu_setup,,,non-result
result_score111111_short_a.png,100,result,111111,5,short-start
result_score111111_short_b.png,500,result,111111,5,short-still-unconfirmed
gameplay_reset.png,700,gameplay,,,reset
result_score123456_a.png,1000,result,123456,10,sustained-start
result_score123456_b.png,1500,result,123456,10,sustained-middle
result_score123456_c.png,2100,result,123456,10,confirmed-save-boundary
result_score123456_d.png,2600,result,123456,10,duplicate-window
transition_countup_score999999.png,3000,transition,999999,,countup-shape
```

```powershell
python -m tools.vision_poc --capture-dry-run-scenario data/vision_poc_sequences/dry_run_sequence.csv --frame-root data/vision_poc_sequences/source_frames --capture-dry-run-output data/vision_poc_sequence_dry_run
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_sequence_dry_run/frame_manifest.csv --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

この入口も本番キャプチャではありません。実デバイス、常駐監視、非同期処理、DB保存、OCR方式刷新には踏み込まず、実キャプチャ前に近い時系列を manifest mode で再現するための補助です。確認点は、short result sequence が未確定のまま残ること、sustained result sequence が `confirmation_mode=time` で確定すること、duplicate window 内の同一キーが `duplicate=true` になること、`transition_countup_*` が `result_shape_candidate=true` でも `event_type=rejected_transition` として保存対象外になることです。confirmed-events の保存境界は引き続き `confirmed_result=true` かつ `duplicate=false` だけです。

実キャプチャAPI導入時は provider の入力元だけを実デバイスや録画前段に差し替え、manifest互換 dry-run 出力はしばらく維持します。これにより、`FrameInput` 契約、`timestamp_ms` の単調増加、manifest expected columns の保持、`confirmation_mode=time`、confirmed-events の保存境界を同じコマンドで再確認できます。

生成した manifest はそのまま manifest モードで処理できます。

```powershell
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_sequences/capture_manifest.csv --frame-root data/captured_frames
```

実キャプチャ導入時は、この manifest 生成処理をリアルタイム供給に置き換え、キャプチャAPIが同等の `image_path,timestamp_ms` 列を渡す方針です。FPSが揺れたりフレームが欠落したりしても、時刻ベースなら `result_candidate=true` が実時間で何ミリ秒継続したかを見られるため、固定フレーム数だけに依存するより保存確定が安定します。

導入前の調整では `result_events_summary.json` を見て、`confirmed_count`、`duplicate_count`、`rejected_transition_count`、`first_confirmed_timestamp_ms`、`confirmation_mode_counts` を確認します。実キャプチャ列で確定が早すぎる、遅すぎる、または同一リザルトの重複が多い場合は、この summary と `result_events.csv` の `candidate_duration_ms` / `reason` を見比べて、保存確定しきい値や duplicate window を調整します。

実キャプチャAPIへ進む前の manifest 入力境界チェックは、まず `timestamped` でローカル metadata 由来の再利用可能な manifest を作り、その manifest を `manifest` で読み直します。`timestamped` は既存ローカル画像列に人工時刻を付けて、期待値列を保持した manifest を生成する確認用です。`manifest` は将来のキャプチャ前段と同じ `image_path,timestamp_ms` CSV契約を読む確認用で、timestamp の同値・逆行、空の `image_path`、存在しない画像パス、`--frame-root` あり/なしの相対パス解決をここで潰します。

```powershell
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_timestamped/frame_manifest.csv --frame-root samples/screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

この2系統では `result_events_summary.json` の `confirmed_count`、`duplicate_count`、`rejected_transition_count` と、`score_ocr_summary.json` の `skipped_duplicate_count` / `skipped_unconfirmed_count` / `skipped_rejected_transition_count` を合わせて見ます。`confirmed-events` は保存直前評価なので、`confirmed_result=true` かつ `duplicate=false` だけがOCR対象です。`result-candidate` は未確定候補やduplicateも含む参考母数で、前処理の副作用確認には使えますが、保存直前評価の成功扱いにはしません。

manifest に metadata 由来の `max_combo`、`miss`、`ex_score` などの期待値列がある場合は、`expected_coverage_by_roi` と `ocr_expected_coverage.md` に評価カバレッジとして反映されます。最小 manifest に期待値列がない場合、`score_digits` はファイル名などから期待値を取れることがありますが、判定数ROIと `ex_score` は `no_expected_values` になり、OCR精度の成功扱いにはしません。`partially_evaluated` は一部だけ期待値がある暫定状態なので、採否判断の前に不足列を埋めて再実行します。

実キャプチャAPI実装時の最初の単位は以下に限定します。

- capture frame provider: 実デバイスや録画前段から1フレームずつ画像参照を受ける薄い入口
- timestamp provider: 各フレームへ単調増加する `timestamp_ms` を付ける入口
- frame persistence location: dry-run や目視確認用フレームは `data/` 配下へ保存する
- manifest互換 dry-run 出力: キャプチャしたフレーム列を `image_path,timestamp_ms` と任意列つきCSVとして再実行できる形にする

この段階では、DB保存、常駐監視ループ、非同期処理、OCR方式刷新、duplicate key の本格差し替え、実キャプチャデバイス依存コードには踏み込みません。confirmed-events の保存境界は引き続き `confirmed_result=true` かつ `duplicate=false` で、`transition_countup_*` は `result_shape_candidate=true` でも `event_type=rejected_transition` として保存対象外です。dry-run capture provider もこの境界を変えず、manifest mode で読み直した結果が `confirmation_mode=time` になることだけを確認します。

実キャプチャAPIへ進む前は、以下をチェックリストとして確認します。

1. timestamped 実行で人工時系列と再利用 manifest を作る: `python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all`
2. manifest 実行で同じ入力境界を読み直す: `python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_timestamped/frame_manifest.csv --frame-root samples/screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all`
3. `result_events_summary.json` で `confirmed_count`、`duplicate_count`、`rejected_transition_count`、`first_confirmed_timestamp_ms`、`confirmation_mode_counts` を見る。`rejected_transition_count` は `transition_countup_*` が `result_shape_candidate=true` でも保存対象外になっていることの確認点です。
4. `result_events.csv` で保存対象が原則 `confirmed_result=true` かつ `duplicate=false` だけになっていることを確認する。`event_type=confirmed` は保存候補、`duplicate` は重複除外、`rejected_transition` は遷移除外、`none` は未確定です。
5. `score_ocr_summary.json` で `ocr_target_mode=confirmed-events`、`skipped_duplicate_count`、`skipped_unconfirmed_count`、`skipped_rejected_transition_count`、ROI別の `match_count` / `mismatch_count` / `empty_ocr_count` / `no_expected_value_count` を見る。`no_expected_values` は成功扱いにせず、`partially_evaluated` は暫定扱いです。
6. `score_ocr_profiles_summary.json` の `best_by_roi` で、`evaluation_status`、`recommendation_is_tentative`、`recommended_profiles` を見る。`no_expected_values` の `reference_profiles` は目視用の参考であり、OCR精度の採用根拠にはしません。
7. `miss` は右側数字寄せ補正、`ex_score` は黒文字膨張補正込みの `low-threshold` 優先を維持できているか、`ocr_roi_report.md` と `ocr_profiles/` の前処理画像で確認します。

OCR前処理も既定で実行されます。対象は `--ocr-target` で切り替えます。既定は `result-candidate` で、従来どおり `result_candidate=true` の画像をOCR対象にします。

```powershell
python -m tools.vision_poc --ocr-target result-candidate
```

実キャプチャ導入前の保存直前OCR相当の評価には `confirmed-events` を使います。このモードでは `result_events.csv` 上で `confirmed_result=true` かつ `duplicate=false` のフレームだけをOCRします。未確定候補、`transition_countup_*` のような `rejected_transition`、duplicate window内の重複確定はOCR対象外です。

```powershell
python -m tools.vision_poc --ocr-target confirmed-events --ocr-rois all
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_timestamped/frame_manifest.csv --frame-root samples/screenshots --ocr-target confirmed-events --ocr-rois all
```

既定では `score_digits` ROIから以下を出力します。

- `data/vision_poc/ocr/<画像名>/score_digits_original.png`
- `data/vision_poc/ocr/<画像名>/score_digits_enlarged.png`
- `data/vision_poc/ocr/<画像名>/score_digits_binary.png`
- `data/vision_poc/score_ocr.csv`
- `data/vision_poc/score_ocr_summary.json`
- `data/vision_poc/ocr_roi_report.md`
- `data/vision_poc/ocr_expected_coverage.md`

`score_ocr.csv` には `roi_name`、`score_ocr_raw`、`score_ocr_normalized`、`expected_score`、`match`、`engine`、`status`、`error`、`original_path`、`enlarged_path`、`binary_path` を出力します。`score_digits` の `expected_score` は metadata の `score` / `expected_score` 列があれば優先し、なければファイル名内の `scoreXXXXXX` から取得します。`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` は metadata に同名列または `expected_<roi_name>` 列がある場合だけ期待値として使います。期待値がないROIは `match` を空欄のままにし、summary の `no_expected_value_count` で集計します。`match` 判定では `0111` と `111`、`00` と `0` のような先頭ゼロだけの差は同値として扱いますが、CSV上の `score_ocr_normalized` と `expected_score` は確認用に元の数字文字列を保持します。

`score_ocr.csv` は行単位の詳細確認用です。個別画像のOCR生文字列、正規化後の数字、期待値、前処理画像パスを見るときに使います。`score_ocr_summary.json` はROI別・失敗理由別の俯瞰用です。トップレベルには `total_ocr_attempts`、`ok_count`、`engine_unavailable_count`、`match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count`、`skipped_duplicate_count`、`skipped_unconfirmed_count`、`skipped_rejected_transition_count`、`ocr_target_mode` を出力します。

`score_ocr_summary.json` の `by_roi` はROI名ごとの集計です。各ROIに `total_ocr_attempts`、`ok_count`、`engine_unavailable_count`、`match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count` が入り、`--ocr-rois all` でどのROIが弱いかを横並びで確認できます。`by_status` は `ok`、`engine_unavailable`、`ocr_failed` などOCR実行ステータスの件数、`failure_reasons` は `engine_unavailable`、`ocr_failed`、`empty_ocr`、`mismatch`、`no_expected_value` の観点別件数です。

`score_ocr_summary.json` の `expected_coverage_by_roi` と `ocr_expected_coverage.md` は、ROIごとに期待値がどれだけ入っているかを確認するためのレポートです。`evaluation_status=evaluated` は全OCR試行に期待値がある状態、`partially_evaluated` は一部だけ期待値がある状態、`no_expected_values` はOCRを試していても精度評価できていない状態です。`no_expected_values` の `status=ok` や空でないOCR文字列は目視参考であり、OCR成功数として採用しません。`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` で `no_expected_value_count` が多い場合は、OCR精度未評価として扱い、先に metadata の期待値列を増やします。

default summary と profile summary は役割を分けて読みます。`score_ocr_summary.json` と legacy `score_ocr.csv` は常に default profile の互換出力で、現行既定処理の弱点を確認するための正本です。`score_ocr_profiles_summary.json` は profile比較用で、ROI別に default と推奨profileの差を読むための補助です。default summary に mismatch が残っても、profile summary 側で `evaluation_status=evaluated`、`recommendation_readiness=adoption_candidate`、`recommended_profiles` が空でない場合だけ、そのprofileを採用候補として読めます。`partially_evaluated` は `recommendation_readiness=tentative`、`no_expected_values` は `reference_only` なので採用根拠にしません。

`ocr_roi_report.md` は `score_ocr_summary.json` と `score_ocr.csv` を人間が読みやすい形に並べたROI別弱点レポートです。ROIごとに `evaluation_status`、`total_ocr_attempts`、`match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count`、`engine_unavailable_count` を表で確認し、代表的な `mismatch` / `empty_ocr` の `organized_file` を見て、対応する `ocr/<画像名>/<roi>_*.png` を目視します。profile比較を有効にした場合は、`default profile counts`、`top recommended profile counts`、`recommended vs default delta` も出るため、default と推奨profileの差を同じROI節で確認できます。`--ocr-target confirmed-events` を指定した場合は、未確定候補やduplicateを除いた対象イベントだけでレポートされます。

OCRエンジンはPATH上の `tesseract` を使います。未導入または利用不可の場合でもPoCは落ちず、前処理画像を保存したうえで `score_ocr.csv` に `engine=none`、`status=engine_unavailable` を残します。

実キャプチャ導入時は、まず `confirmed-events` と `--ocr-rois all` を組み合わせて、保存直前OCR相当の精度と失敗理由をROI別に見ます。ここで `skipped_duplicate_count` が増えすぎる、`skipped_unconfirmed_count` に保存したいフレームが混ざる、`skipped_rejected_transition_count` が想定外、特定ROIの `empty_ocr_count` / `mismatch_count` が多い、または `no_expected_value_count` が多く評価できない場合は、キャプチャAPIやDB保存を作る前にイベント確定しきい値、duplicate window、metadata真値列、ROI前処理を調整します。

現在のローカル metadata で `python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all` と、その manifest 再読込を実行すると、保存直前イベントは4件、duplicateは1件、`transition_countup_*` の rejected transition は3件です。timestamped と manifest ではどちらも `confirmation_mode=time` になり、metadata 由来の期待値列が保持されるため、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` はすべて `evaluated` として読めます。default `score_ocr_summary.json` では `ex_score` が4件中1 match / 3 mismatch ですが、`score_ocr_profiles_summary.json` では `low-threshold` が4件中4 matchで、`recommendation_readiness=adoption_candidate` の単独推奨です。したがって M2 の現時点では、`ex_score` の `low-threshold` は confirmed-events かつ expected coverage が `evaluated` の場合だけ採用候補として読む、という条件で固定します。DB保存、常駐監視、本番キャプチャAPI、OCR方式刷新、ROI座標定義の大変更にはまだ進みません。

confirmed-events の標準評価母数が4件に留まる理由は、metadata の時系列で1000ms以上連続した result candidate だけが time-based confirmation で確定し、duplicate window 内の同一 `duplicate_key` は保存直前OCR対象から外れるためです。ローカル素材で `ex_score` の採用候補をもう少し安定して見る場合は、`--make-m2-expanded-manifest` で `data/` 配下に検証用 manifest を再生成できます。この manifest は、各 result 画像を non-result reset の後に2フレーム連続、かつ各result間を90秒超に離して並べます。本番キャプチャやDB保存ではなく、既存の manifest 契約で confirmed-events の母数だけを増やすローカル評価です。

```powershell
python -m tools.vision_poc --make-m2-expanded-manifest data/vision_poc_m2_expanded_manifest
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_m2_expanded_manifest/frame_manifest.csv --frame-root samples/screenshots --output data/vision_poc_m2_expanded_ex_score --ocr-target confirmed-events --ocr-rois ex_score --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_m2_expanded_manifest/frame_manifest.csv --frame-root samples/screenshots --output data/vision_poc_m2_expanded_all --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

2026-07時点のローカル確認では、この形で confirmed-events を16件に増やしても `confirmation_mode=time`、`evaluation_status=evaluated`、`recommendation_readiness=adoption_candidate` は維持されます。`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` は default profile が16 match / 0 mismatch / 0 emptyで、現時点では追加調整なしで固定できます。`ex_score` は default が4 match / 11 mismatch / 1 emptyに留まる一方、`low-threshold` は16 match / 0 mismatch / 0 emptyの単独推奨です。`score_ocr_summary.json` は default profile の互換出力、`score_ocr_profiles_summary.json` は profile比較と採用候補判断として読み分けます。`no_expected_values` は採用根拠にせず、`partially_evaluated` は暫定扱いのまま期待値列を埋めてから再判断します。

### OCR profile比較

実キャプチャ導入前の前処理調整PoCとして、`--ocr-profile` で複数の軽量profileを比較できます。これはローカル画像に対するROI別弱点分析用であり、本番キャプチャAPI、常駐監視ループ、非同期処理、DB保存ではありません。

```powershell
python -m tools.vision_poc --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

利用できるprofileは以下です。

- `default`: 既定の前処理。従来の `score_ocr.csv` は常にこの設定で出力します。
- `high-contrast`: 明るい白文字に寄せ、`luma_threshold` を上げて `channel_spread` を狭めます。
- `low-threshold`: 暗めの文字に寄せ、`luma_threshold` を下げて `channel_spread` を広げます。
- `tighter-white`: 判定数ROIで周辺の色や枠を拾いすぎる場合の仮説確認用です。より明るく色差の少ない白文字候補だけに絞り、`max_combo`、`marvelous`、`perfect` などの白い細数字で空OCRや誤読が減るかを見ます。
- `no-sharpen`: 細い判定数がシャープ化で欠ける場合の仮説確認用です。シャープ化を外し、余白を少し狭めて、`great`、`good`、`miss` など小さい数字のつぶれ方を比較します。

profile比較を有効にすると、追加で以下を出力します。

- `data/vision_poc/score_ocr_profiles.csv`
- `data/vision_poc/score_ocr_profiles_summary.json`
- `data/vision_poc/ocr_profiles/<profile>/<画像名>/<roi>_*.png`

`score_ocr_profiles_summary.json` は `profiles -> profile -> roi -> counts` の形で、profile別・ROI別の `match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count` などを集計します。`best_by_roi` にはROIごとに `match_count` が最も多いprofileと、`empty_ocr_count` が最も少ないprofileを出します。期待値がないROIは成功/失敗にせず、`no_expected_value_count` として扱います。

`best_by_roi` には `evaluation_status`、`recommendation_basis`、`recommended_profiles`、`reference_profiles`、`recommendation_basis_detail` も出力します。期待値が全行にある `evaluated` ROIでは `match_count` を主指標にし、同点なら `mismatch_count` と `empty_ocr_count` が少ないprofileを推奨候補にします。`partially_evaluated` ROIでは同じ指標を使いますが、`recommendation_is_tentative=true` の暫定候補として扱い、残りの期待値を埋めてから判断します。`no_expected_values` ROIでは `recommended_profiles` は空になり、`reference_profiles` は空OCRが少ない目視確認用の参考候補です。これはOCR精度の成功扱いではありません。

`best_by_roi` には読みやすさ用に `recommendation_readiness`、`default_profile_counts`、`top_recommended_profile`、`top_recommended_profile_counts`、`recommended_vs_default_delta`、`default_is_recommended` も出力します。`recommendation_readiness=adoption_candidate` は `evaluated` かつ推奨profileあり、`tentative` は `partially_evaluated` の暫定推奨、`reference_only` は期待値不足で採用根拠にしない状態です。`recommended_vs_default_delta` は先頭の推奨profileと default の差で、`match_count` が正、`mismatch_count` / `empty_ocr_count` が負なら default より読み取りが改善しています。

まずは以下のように confirmed-events と全OCR ROI、profile比較を組み合わせ、保存直前OCR相当で弱いROIを特定します。

```powershell
python -m tools.vision_poc --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_timestamped/frame_manifest.csv --frame-root samples/screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

profile比較は、期待値があるROIでは精度比較、期待値がないROIでは空OCRや前処理画像の目視確認の補助として使います。この段階では `empty_ocr_count` が多いROIは二値化条件やROI位置、`mismatch_count` が多いROIは余白・拡大率・Tesseract設定、`no_expected_value_count` が多いROIは metadata の真値列不足を疑います。調整後も既存互換の `score_ocr.csv` は `profile` 列を追加せず、比較結果は別ファイルで確認します。

実キャプチャAPIに進む前は、まず以下の3系統で保存直前OCR相当の弱いROIを見つけます。`ocr_expected_coverage.md` で未評価ROIを確認し、必要ならローカルの `samples/screenshots/metadata.csv` に期待値列を追加してから再実行します。

```powershell
python -m tools.vision_poc --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_timestamped/frame_manifest.csv --frame-root samples/screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

2026-07時点のローカル実測では、confirmed-events の重複除外後4イベントに `max_combo` / `marvelous` / `perfect` / `great` / `good` / `miss` / `ex_score` の期待値が入り、`score_digits` を含む全OCR ROIが `evaluated` になります。`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good` は既存profileが横並びで全件matchし、まずは `default` のまま確認できます。`miss` はROI内の左35%をOCR前処理時だけ落として右側数字へ寄せる補正を採用し、confirmed-events では全profileが4/4 matchになります。`ex_score` はROI内数字寄せを試すと既存の `low-threshold` 4/4 match を崩すため採用せず、二値化後の黒文字だけを軽く太らせる補正を `ex_score` 限定で採用しました。confirmed-events 標準4件では `low-threshold` が4/4 matchのまま維持され、defaultの mismatch 代表は `ocr/result_016_sp_basic_lv06_score935730/ex_score_binary.png`、`ocr/result_031_sp_challenge_lv09_score977490_duplicate/ex_score_binary.png`、`ocr/result_047_dp_basic_lv09_score834500_duplicate_01/ex_score_binary.png` です。

拡張confirmed-events manifestで主要数字ROI全体を16件評価すると、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great` は全profileが16/16 match、`good` は `default` / `high-contrast` / `low-threshold` が16/16 match、`miss` は `default` / `high-contrast` / `low-threshold` / `no-sharpen` が16/16 matchです。`ex_score` は default が4 match / 11 mismatch / 1 emptyですが、`low-threshold` は16/16 matchです。標準4件の timestamped と manifest 再読込では `confirmed_count=4`、`duplicate_count=1`、`rejected_transition_count=3`、`skipped_duplicate_count=1`、`skipped_rejected_transition_count=3` が一致し、拡張16件では `confirmed_count=16`、`duplicate_count=0`、`skipped_unconfirmed_count=32` になります。duplicate、rejected transition、未確定候補は引き続き confirmed-events OCR対象外です。

同じローカル metadata で `--ocr-target result-candidate --ocr-rois all --ocr-profile all` を使うと、全16 result 行を参考母数としてprofile比較できます。この読みでは全ROIが `evaluated` で、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great` は全profileが16/16 match、`good` は `default` / `high-contrast` / `low-threshold` が16/16 matchです。`miss` は右側数字寄せ補正後に `default` / `high-contrast` / `low-threshold` / `no-sharpen` が16/16 matchになり、`tighter-white` は13/16 matchに留まるため参考候補から外します。`ex_score` は黒文字膨張補正後に `low-threshold` が16/16 matchになり、補正前に empty だった代表は `ocr/result_028_sp_challenge_lv17_score821420/ex_score_binary.png` です。result-candidate は未確定候補やduplicateも含む参考母数であり、保存直前評価の成功扱いにはしません。採否は confirmed-events を主に見て、result-candidate は副作用確認として読み分けます。

代表失敗の目視分類では、`miss` は `*_original.png` / `*_enlarged.png` で右端の数字が読めても、`*_binary.png` に左側の `Miss` ラベルが強く残ります。mismatch はこのラベル白塊が数字扱いに混ざるケース、empty_ocr は数字が右端に孤立して `--psm 8` の単語認識に乗りにくいケースとして扱い、ROI座標定義は変えずに OCR 前処理画像だけ右側へ寄せます。`ex_score` は `default` だと黄色い `EX SCORE` ラベルを消しすぎて数字境界も細くなり、`5` を `6` / `8` に寄せる mismatch が出ます。`low-threshold` の `result_028_sp_challenge_lv17_score821420` は、`*_original.png` / `*_enlarged.png` では数字自体は読めるものの、`*_binary.png` に `EX SCORE` ラベル輪郭が大きく残り、細い4桁数字との境界が Tesseract に乗らず empty_ocr になっていました。余白追加は confirmed-events を崩し、左ラベル寄せ/マスクも副作用が大きいため採用しません。二値化後に `ex_score` の黒文字だけを軽く太らせる補正は confirmed-events 4/4 を維持し、result-candidate 参考母数で `low-threshold` 16/16 へ改善したため採用します。今回の範囲では新profileは追加せず、実キャプチャ前の判断は `miss` の右側数字寄せ採用済み、`ex_score` は黒文字膨張補正込みの `low-threshold` 優先とします。

同じ評価を manifest モードで読む場合は、timestamped モードで生成した `data/vision_poc_timestamped/frame_manifest.csv` を使います。この生成manifestには metadata の期待値列も保持されるため、manifest モードでも `evaluated` / `partially_evaluated` / `no_expected_values` の読み替えが維持されます。外部ツールが作る最小manifestに期待値列がない場合、`score_digits` 以外は `no_expected_values` になり、OCR精度の成功扱いにはしません。

現在のローカル metadata では全16 result 行の `max_combo` / `marvelous` / `perfect` / `great` / `good` / `miss` / `ex_score` が埋まっており、`ocr_expected_template.csv` はヘッダーのみになります。次に確認する場合は、まず `ocr_expected_coverage.md` で期待値カバレッジを見て、不足があればROI切り出し画像と元スクリーンショットで期待値を追加し、その後にprofile比較とROI別採用候補を確認します。

OCR前処理を省略したい場合は以下を使います。

```powershell
python -m tools.vision_poc --no-ocr
```

M3入口の曲・譜面情報確認では、まず `data/vision_poc/rois/<画像名>/play_style.png`、`difficulty.png`、`level.png`、`rank.png`、`song_title.png`、`artist.png` を目視します。これらは保存直前イベントから曲・譜面情報を評価するための切り出し足場であり、現時点では `--ocr-rois all` のOCR評価対象には含めません。期待値列の充足状況は `m3_metadata_expected_coverage.md` で確認します。このレポートの対象は confirmed-events 境界、つまり `confirmed_result=true` かつ `duplicate=false` だけです。`artist.png` は補助ROIで、長いアーティスト名では左右が切れることがあります。M3入口では座標の大変更に進まず、`song_title.png` 内の2行表示も合わせて目視します。代表確認では、長い曲名、記号入り曲名、日本語曲名、DOUBLE、長い `artist` でも `play_style`、`difficulty`、`level`、`rank`、`song_title` は目視評価に使える一方、`artist` は左右切れがあり補助情報として読む前提を維持します。

M3の成果物は、曲・譜面の照合済みデータではなく、M5のマスタ照合PoCへ渡す観測値と失敗理由です。`song_title` はOCR入口結果、`play_style` / `difficulty` / `level` はPoC抽出候補、`artist` は補助観測値として扱います。空でないOCR文字列や `ready` は、曲ID、譜面ID、マスタ曲名への一意照合、曲名正規化、ファジーマッチ、照合スコア、照合確信度の成功を意味しません。M5で照合した結果、M3で読めているように見えたOCR文字列や譜面候補が違っていたと判明する可能性を前提にします。

2026-07-04時点で、M3はこの境界で完了扱いです。追加の前処理改善は別作業単位、マスタDB生成はM4、曲名正規化とマスタ照合はM5で扱います。

M3-4の曲名/artist OCR入口は、明示的に `--m3-song-artist-ocr` を付けて実行します。

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --ocr-target confirmed-events
```

`m3_song_artist_ocr.csv` は `song_title` / `artist` の confirmed-events 対象だけを出し、`ocr_raw`、`pre_normalized_text`、`expected_value`、`engine`、`status`、`error`、`failure_reason`、`roi_path` を確認できます。`pre_normalized_text` は改行と連続空白だけをレビュー用に畳んだ文字列で、本格的な曲名正規化ではありません。OCRエンジンがない環境では `engine_unavailable` を `failure_reason` に記録し、PoC全体は落としません。`song_title` は主要項目、`artist` は左右切れがある補助項目として読みます。

`m3_song_artist_ocr_entry_failures_summary.json` と Markdown は、M3-9の `song_title` / `artist` OCR入口失敗代表整理です。`engine_unavailable`、`ocr_failed`、`empty_ocr` を入口失敗として集約し、`song_title` を主要項目、`artist` を左右切れがある補助項目として別々に代表化します。`no_expected_value` は期待値整備の問題として分け、入口失敗代表には含めません。このレポートも曲名正規化、ファジーマッチ、マスタ照合、DB保存可否判定の成功/失敗扱いにはしません。

`m3_chart_fields.csv` は、M3で先に扱う有限候補の `play_style`、`difficulty`、`level` だけを対象にした評価足場です。全イベント行を出力し、`chart_field_target=true` になるのは confirmed-events 境界の `confirmed_result=true` かつ `duplicate=false` だけです。duplicate、`rejected_transition`、未確定候補、non-result は `chart_field_target=false` とし、`exclusion_reason` に `duplicate`、`rejected_transition`、`unconfirmed`、`non_result` を出します。各対象行には `expected_play_style`、`expected_difficulty`、`expected_level` と、`rois/<画像名>/<field>.png` への相対パスを出します。これは chart-field 抽出PoCの入力一覧であり、曲名OCR、artist OCR、ランクOCR、テンプレート照合、マスタ照合の成功を意味しません。

`m3_chart_fields_summary.json` は同じ対象境界の集計です。`target_boundary`、`chart_field_target_count`、`excluded_counts`、`fields` を確認し、`play_style` / `difficulty` / `level` の期待値が confirmed-events 対象にそろっているかを見ます。数字OCRの `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md` とは別レポートとして読みます。

`m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` は、同じ confirmed-events 境界で `play_style` / `difficulty` / `level` の抽出値、期待値、match、status、failure_reason を見るための初期評価レポートです。現時点の extractor は `filename-baseline` で、`organized_file` の `sp_basic_lv06` のような命名から `SINGLE`、`BASIC`、`6` を取り出すだけです。これはROI画像特徴、OCR、テンプレート照合、マスタ照合の成功ではありません。対象外イベントは `status=skipped` とし、`failure_reason` で `duplicate`、`rejected_transition`、`unconfirmed`、`non_result` を維持します。対象イベントで期待値がない場合は `no_expected_value`、ファイル名から抽出できない場合は `empty_extraction`、不一致は `mismatch` になります。

`m3_chart_field_image_feature_extraction.csv` と `m3_chart_field_image_feature_extraction_summary.json` は、ROI画像由来の軽い比較baselineです。extractor は `roi-feature-nearest-centroid` で、confirmed-events 対象の `play_style` / `difficulty` / `level` ROIから明度、白/黄/シアン/緑比率、エッジ比率などの特徴を取り、期待値ラベルごとの leave-one-out centroid に最も近い値を `extracted_value` として出します。status 語彙は `match` / `mismatch` / `empty_extraction` / `no_expected_value` / `skipped` に寄せています。これは画像特徴の診断用baselineであり、OCR、テンプレート照合、マスタ照合の成功ではありません。既存の `filename-baseline` は比較用baselineとして維持します。

`m3_chart_field_image_feature_diagnostics.md` は、同じ `roi-feature-nearest-centroid` の mismatch を読むための補助Markdownです。field別の match / mismatch 数、期待値と抽出値の混同表、代表mismatchの `organized_file` とROIパスを出します。`play_style` の単発mismatch確認、`difficulty` の色特徴で分けにくい組み合わせ整理、`level` が単純ROI特徴だけでは弱いことの確認に使い、これもOCR、テンプレート照合、マスタ照合の成功扱いにはしません。

`m3_chart_field_template_extraction.csv` と `m3_chart_field_template_extraction_summary.json` は、追加ローカル素材 `samples/screenshots/organized/chart_field_templates/` と confirmed-events 対象の result ROI を参照する `roi-template-nearest` の小さな比較PoCです。テンプレート画像名と metadata 期待値から `play_style`、`difficulty`、`level` の期待ラベルを読み、各ROI画像との最近傍距離で `extracted_value` を出します。confirmed-events 由来の参照では評価中の同一フレームを除く leave-one-out にします。テンプレート素材はローカル素材扱いでGit管理しません。ディレクトリが存在しない環境でも confirmed-events 由来の参照だけで比較できます。参照がない場合は `status=empty_extraction`、`failure_reason=no_template_references` になります。期待ラベルの参照テンプレートがない mismatch は `failure_reason=missing_expected_template_reference` として、単純な最近傍負けとは分けて読みます。この出力も confirmed-events 境界だけを抽出評価対象にし、OCR、マスタ照合、採用済みテンプレート照合の成功扱いにはしません。必要に応じて `--chart-field-template-root <dir>` で参照先を差し替えられます。

`m3_chart_field_template_holdout_extraction.csv` と `m3_chart_field_template_holdout_extraction_summary.json` は、confirmed-events result ROI を評価専用にし、参照を `chart_field_templates/` だけに限定する `roi-template-holdout` の比較PoCです。`roi-template-nearest` が同分布内 leave-one-out 診断であるのに対し、holdout は追加テンプレート素材だけで `play_style` / `difficulty` / `level` を読めるかを見るための分割レポートです。テンプレート素材がない環境では、confirmed-events 参照で補わず `status=empty_extraction`、`failure_reason=no_template_references` になります。この出力も抽出ロジックの採用成功ではなく、外部検証に近づけるための診断として読みます。

`m3_chart_field_template_diagnostics.md` は、同じ `roi-template-nearest` の mismatch を読むための補助Markdownです。field別の match / mismatch 数、期待値と抽出値の混同表、代表mismatch、`difficulty` の期待値レビュー候補を出します。`difficulty` mismatch は抽出ロジックの失敗だけでなく、ROIの見た目と metadata / ファイル名由来期待値の食い違い候補として、実画像、ROI PNG、metadata、ファイル名を突き合わせて確認します。このレポートも同分布内の leave-one-out 診断であり、OCR、採用済みテンプレート照合、マスタ照合の成功扱いにはしません。

`m3_chart_field_template_holdout_diagnostics.md` は、同じ holdout 比較の mismatch を読む補助Markdownです。読み方は template diagnostics と同じですが、参照元に confirmed-events result ROI を含めない点を特に確認します。

`m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` は、M3-3の `play_style` / `difficulty` / `level` 採用候補レビュー用レポートです。候補根拠は `roi-template-holdout` に寄せ、`adoption_readiness=adoption_candidate` のfieldだけを次段階の採用候補として読みます。`needs_template_references` は追加テンプレート素材が必要な状態で、`missing_expected_template_reference` と `no_template_references` は保存前判断へ渡す語彙では `missing_reference` に寄せます。`mismatch` は参照があるのに外したケースとして `low_confidence`、期待値不足は `no_expected_value`、抽出空は `empty_extraction` として読みます。2026-07-04時点のローカル37テンプレート配置後は、confirmed-events 60件で `play_style`、`difficulty`、`level` がすべて 60/60 match の `adoption_candidate` です。ただし、このレポートも本番採用済みテンプレート照合、OCR、マスタ照合の成功扱いにはしません。

`m3_save_candidate_summary.csv` は confirmed-events ごとに `song_title`、`artist`、`play_style`、`difficulty`、`level` の状態を横並びで出します。`play_style` / `difficulty` / `level` は M3-3 の `adoption_candidate` を `ready` として反映できますが、本番採用済みテンプレート照合ではありません。参照不足がある間の `difficulty` / `level` は、保存前判断向けには `missing_reference` または `not_adopted` として読みます。ローカル37テンプレート配置後のM3-5集約では、chart-field 3項目は60件すべて `ready` になり、残りの未readyは `song_title` / `artist` のOCR入口代表失敗として整理します。`song_title` / `artist` は M3-4 OCR入口の `engine_unavailable` / `ocr_failed` / `empty_ocr` / `no_expected_value` を集約し、`pre_normalized_text` を曲名正規化やマスタ照合の成功扱いにしません。

`m3_save_candidate_blockers_summary.json` と Markdown は、M3-5集約内の未ready fieldを status / failure reason ごとに代表化します。代表には `organized_file`、期待値、抽出値、extractor、`roi_path` を出します。`song_title` / `artist` の empty OCR、`difficulty` / `level` の missing reference などを数件だけ見るためのレビュー補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化の成功/失敗扱いにはしません。

`m3_save_candidate_blocker_resolution_plan.json` と Markdown は、M3-7の保存前ブロッカー解消順を整理します。`difficulty` / `level` の `missing_reference` は追加すべきローカルテンプレート参照ラベルとして、`field_needs_template_references` は参照追加後の再確認として、`song_title` / `artist` の `ocr_not_run` や `empty_ocr` はOCR入口の次手として読みます。ローカル37テンプレート配置後に `play_style` / `difficulty` / `level` がすべて `ready` になった状態では、M3-7解消順の残りは `song_title` / `artist` のOCR入口確認に絞ります。これは次に増やす素材や見るべきROIを決めるためのレビュー補助であり、DB保存可否判定やマスタ照合の結果ではありません。

M5のマスタ照合PoCは、M4で生成したSQLiteマスタDBと、同じ実行内で作った `m3_save_candidate_summary.csv` 相当の行を入力にします。

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc
```

`master_match_candidates.csv` は、confirmed-events由来の保存候補ごとに、曲名OCR文字列、最小正規化文字列、`play_style` / `difficulty` / `level` の候補絞り込み条件、候補曲数、候補譜面数、最上位候補、score、上位候補一覧、`match_status`、`failure_reason` を出します。`master_match_summary.json` と `master_match_report.md` は `matched` / `ambiguous` / `not_found` / `insufficient_input` を集計します。ここでの `matched` はPoC上の一意候補という意味だけで、DB保存可能、本番採用済み照合、曲ID/譜面ID確定を意味しません。`ambiguous`、`not_found`、`insufficient_input` はM7以降の保存不可理由や追加確認ログへ渡す観測語彙として読みます。曲名正規化はNFKC、casefold、空白除去、代表的な句読点除去と、5文字以上のマスタ曲名がOCR文字列に含まれる場合の包含一致だけの軽量入口です。上位候補一覧は失敗代表の観察用で、保存可能判定ではありません。詳細は `docs/design/09_master_match_poc.md` を参照します。

ジャケット特徴量PoCは、同じ実行内で `--m5-jacket-match` を指定します。`song_select` grid画面の右上選択中ジャケットプレビューをローカル特徴量マスタにし、metadata の `song_title` / `expected_song_title` を M4 `songs.title` へ一意照合できる行だけ採用します。M4で公式canonicalへ寄せた曲は `songs.title` / `songs.artist` を正とし、ローカルmetadataやWiki側表記が旧表記を持つ場合だけ `song_aliases` を補助的に参照します。これは `RЁVOLUTIФN` / `RËVOLUTIФN` のようなM4由来の表記差を吸収する入口で、候補集合外から別曲を拾うtitle補助ではありません。ラベルが空のgrid行は `jacket_feature_label_template.csv` へ出すだけで、`samples/screenshots/metadata.csv` は更新しません。result confirmed-events では既存 `jacket` ROIを特徴量化し、`play_style` / `difficulty` / `level` で絞った候補song_idに特徴量があるものだけ比較します。result側の `jacket` / `song_title` ROI特徴量は metadata の `screen_type=result` ラベルではなく、分類で `result_candidate=true` になったフレームから抽出するため、manifest / timestamped / dry-run 入力で手ラベルがない場合も confirmed-events 保存候補の特徴量を観察できます。

```powershell
python -m tools.vision_poc --m3-song-artist-ocr --m5-master-match --m5-jacket-match --master-db data\master\ddrgp-master.sqlite --output data\master_match_poc_jacket --no-rois
```

`jacket_match_candidates.csv` は候補曲数、候補譜面数、候補特徴量数、最上位候補、score、distance、参照元grid画像、上位候補一覧、期待曲名、期待song_id、期待song_idの距離、期待song_idの順位、最上位と次点songの距離差、title画像特徴量補助、title OCR suffix補助、title line-hash補助、後続保存判定へ渡すM5候補観測 `identity_signal_*`、`jacket_match_status`、`failure_reason` を出します。期待値由来の診断列はローカルmetadataに対する観察であり、保存可能判定には使いません。`jacket_match_status` は `matched` / `ambiguous` / `not_found` / `insufficient_input` / `missing_feature` です。ここでの `matched` もPoC上の一意候補であり、保存可能、本番採用済み照合、曲ID/譜面ID確定を意味しません。特徴量マスタはローカル素材由来の `data/` 出力であり、画像本体やmetadata実体はGit管理しません。

`jacket_match_report.md` の `Identity Signal Representatives` は、通常候補CSVの行から `identity_signal_status` ごとに数件だけ抜き出した観察補助です。通常候補60件の境界は変えず、未解決代表を探すときはこの表から `jacket_match_candidates.csv` の該当 `organized_file` へ戻って確認します。

`identity_signal_*` はM5からM7以降の保存判定へ渡すための候補観測列です。`identity_signal_status=jacket_resolved_candidate` はjacket特徴量単体のPoC一意候補、`identity_signal_status=composite_resolved_candidate` はjacket候補集合にtitle補助を合わせると候補集合内で1件を示した状態です。`identity_signal_source` は `jacket_feature` / `title_linehash_dict` / `title_ocr_suffix` / `title_image_feature` のいずれかです。これらは保存可能、曲ID/譜面ID確定、本番採用済み照合を意味しません。`jacket_match_status=ambiguous` は title補助で候補が出ても `matched` へ昇格しません。

`jacket_match_summary.json` は、通常候補60件に対する jacket照合PoC信号の集計です。`jacket_match_status=matched` や `identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` は、M5から後続へ渡す曲同定候補観測であり、保存OK、曲ID確定、譜面ID確定、本番採用済み照合ではありません。

`jacket_reference_coverage_summary.json` は、同じ通常候補60件について、chart-field条件で絞った候補song_idにローカル `song_select` 由来のjacket参照があるかを見る別診断です。`candidate_reference_status=missing_feature` は候補song_id側の参照不足です。`expected_song_reference_status=expected_missing_feature` は期待曲が候補集合内にあるが参照がない状態、`expected_not_in_chart_candidates` は期待曲がM4で解決していてもchart-field条件の候補集合に入っていない状態、`expected_unresolved` は期待曲名がM4 canonical/aliasへ解決できない状態です。これらの代表CSVとMarkdownは、保存候補昇格ではなく参照追加、metadata期待値、chart-field境界、M4 canonical/aliasのレビュー材料として読みます。

`jacket_reference_diagnostics_coverage_summary.json` は duplicate / unconfirmed を含む診断側のcoverage集計です。通常候補の `jacket_reference_coverage_summary.json` へ混ぜず、`m5_target_boundary_reason` を見て保存候補外の観察として扱います。参照不足時は不足として明示し、近傍の別曲へ寄せて解消扱いにはしません。

title画像特徴量PoCは、jacketで `ambiguous` になった候補集合内だけを補助的に再順位付けします。初期実装では result `song_title` ROIを横長の濃淡サムネイル、エッジサムネイル、右側サフィックス寄りの濃淡/エッジサムネイル、dHashに変換し、ローカルmetadataの期待曲名を M4 `songs.title` へ一意解決できた result 素材を参照にします。比較時は同じ `organized_file` の参照を除外します。`title_rerank_status=resolved_candidate` はtitle画像特徴量が解消候補を出したというPoC観測であり、`jacket_match_status` を `matched` に変更したり、保存可能や曲ID/譜面ID確定を意味したりしません。`title_rerank_status` は `not_run` / `missing_feature` / `resolved_candidate` / `ambiguous_candidate` として読みます。

title OCR suffix補助は、`--m3-song-artist-ocr` で得た result `song_title` OCR文字列から `TYPE1` / `TYPE2` / `TYPE3` だけを観測し、jacketで `ambiguous` になった候補song_id集合内に同じsuffixを持つ曲が1件だけあるかを診断します。`title_ocr_rerank_status=resolved_candidate` はsuffixが曖昧候補内の1曲へ対応したというPoC観測であり、`jacket_match_status` を `matched` に変更したり、保存可能や曲ID/譜面ID確定を意味したりしません。候補集合内にsuffix一致がない場合も、候補集合外から曲を拾いません。`title_ocr_rerank_status` は `not_run` / `missing_ocr` / `no_suffix` / `resolved_candidate` / `ambiguous_candidate` / `no_candidate_suffix_match` として読みます。

title line-hash補助は、jacketで `ambiguous` になった候補集合内だけを対象に、result `song_title` ROIの曲名行を固定しきい値で二値化した行ごとのhexキーで再順位付けします。参照はローカルmetadataの期待曲名を M4 `songs.title` へ一意解決できた result 素材だけで、同じ `organized_file` の参照は除外します。主観測は inf-notebook 風の行hexキー辞書 `title_linehash_dict_status` です。完全一致型の `title_linehash_exact_status` と、候補参照間で差が出るbitを重く見る距離比較型の `title_linehash_distance_status` は参考列として残します。`resolved_candidate` は候補集合内の補助観測であり、`jacket_match_status` を変えたり、保存可能や曲ID/譜面ID確定を意味したりしません。候補集合外から曲を拾いません。line-hash辞書で候補が出た場合だけ、`identity_signal_status=composite_resolved_candidate` / `identity_signal_source=title_linehash_dict` として複合根拠の曲同定候補観測を後続へ渡します。距離比較型は `identity_signal_source` に使いません。

2026-07-07のローカル追加素材とM4公式canonical/alias反映後は、jacket feature master が69件、confirmed-events 60件に対する jacket match が `matched=57` / `ambiguous=3` / `not_found=0` / `missing_feature=0` です。`Inner Spirit -GIGA HiTECH MIX-` はgrid参照追加で `jacket_resolved_candidate` になり、`RЁVOLUTIФN` は公式canonical化と `song_aliases` 経由のローカルラベル解決で `jacket_resolved_candidate` になりました。残る曖昧は、現ローカル素材では `osaka EVOLVED -毎度、おおきに！- (TYPE1/2/3)` の同一ジャケット3件です。これはEVOLVED系だけの特例ではなく、同一・類似ジャケットでタイトル側に分岐情報が出る曲群の代表ケースとして読みます。`result_098_sp_basic_lv07_if_score972200.png` はファイル名とmetadataが `If` になっていましたが、実画面表示は `桜 / Reven-G / SINGLE BASIC Lv7` だったためローカルmetadataを修正済みです。その後、`桜` のsong_select grid/result素材を追加して近距離曖昧は解消しました。title / artist は候補集合外から曲を拾う主キーにはしません。

同日の title OCR suffix 診断では、osaka 3件の `title_ocr_rerank_status` はすべて `no_suffix` でした。OCR文字列は `TYPE)`、`TYPED`、`TYPES` のようにsuffix末尾が崩れており、現行のM3 title OCR入口だけでは `TYPE1` / `TYPE2` / `TYPE3` を安定取得できない観測です。この結果も保存可能判定ではなく、title line-hash辞書の必要性を示す切り分け材料として扱います。

2026-07-05の line-hash辞書化後は、osaka 3件の `title_linehash_dict_status` がすべて `resolved_candidate` になり、辞書上位候補も期待TYPEと一致しました。`title_linehash_distance_status` は参考列として残しており、TYPE2は引き続き `ambiguous_candidate` です。今後は距離比較を本命にせず、行hexキー辞書の安定性確認と、保存判定へ渡す曲同定候補観測の整理を優先します。

X-Special付き譜面のように、通常版と同一ジャケットを共有し得る分岐も将来は同じ問題分類に入る可能性があります。ただし現時点ではGRAND PRIXプレー対象として扱わないため、M5の実装対象には含めません。

固定UI文字は将来的に汎用OCRより画像認識へ寄せます。ただし、スコア、判定数、EX SCORE のTesseract離脱や数字テンプレート認識は後続タスクに回し、次のM5作業ではtitle line-hashを優先します。

`difficulty` は5種類の文字色が強い手がかりになるため、`roi-template-nearest` 内ではROI全体ピクセルではなく前景文字色の比率パターンで比較します。直近ローカル素材では `play_style`、`difficulty`、`level` は 60/60 match です。ただしこれは同分布内の leave-one-out 診断であり、抽出ロジックの採用判断には外部検証や参照/評価セット分割が必要です。

2026-07-04時点の5件レビューでは、`difficulty` mismatch はすべてROI表示が metadata / ファイル名由来期待値と食い違うローカル期待値修正候補でした。ローカル `metadata.csv` はROI表示へ合わせて修正済みで、ファイル名は当面リネームしません。修正後は `roi-template-nearest` が 180/180 match、`filename-baseline` が difficulty 5件 mismatch になります。詳細は `docs/design/07_m3_chart_field_review.md` を参照します。`metadata.csv` とスクリーンショット画像はGit管理しないため、判断と修正内容は文書に残し、実体更新はローカル素材側で行います。

2026-07-04時点の初回 holdout 確認では、`play_style` は60/60 matchで `adoption_candidate` として読め、`difficulty` と `level` は参照不足により `needs_template_references` でした。その後、`chart_field_template_142` から `149` のローカルテンプレートを追加し、テンプレート数37枚の状態で再確認したところ、`play_style`、`difficulty`、`level` はすべて 60/60 match の `adoption_candidate` になりました。追加テンプレート素材が必要な場合も画像はコミットせず、必要ラベルと判断だけをdocsに残します。

現在のローカル metadata では、M3 metadata expected coverage の confirmed-events 対象は60件です。`song_title` / `artist` / `play_style` / `difficulty` / `level` は60件すべてが埋まっており、M3入口ではこの5項目を優先して評価します。`rank` / `expected_rank` は12件だけが埋まっているため、残り48件の不足は数字OCR expected coverage の不足とは別に読み、当面は補助ROIの部分評価として扱います。ランクOCR、ランクテンプレート照合、本格採用判断へ進む場合は、別途M3の評価列とレポートとして定義します。

`score_digits` 以外の判定数ROIと `ex_score` も同じ前処理APIを使えます。現時点ではOCR精度調整前の足場として、まずは `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の `*_original.png` / `*_enlarged.png` / `*_binary.png` を確認できる状態にしています。

```powershell
python -m tools.vision_poc --ocr-rois all
python -m tools.vision_poc --ocr-rois score_digits max_combo marvelous perfect great good miss ex_score
```

既存確認用の `score_digits_original.png`、`score_digits_enlarged.png`、`score_digits_binary.png` のファイル名は維持しています。

### 実行時間メモ

`python -m tools.vision_poc --no-ocr` は分類だけでなく、M3のCSV/JSON/Markdownレポートと `rois/` のPNG出力も行うデバッグ用バッチです。ROI PNG保存は重いため、反映速度や抽出ロジックだけを確認する場合は `--no-rois` を付けます。直近ローカル測定では、ROI出力ありの `--no-ocr` が約9.7秒、`--no-ocr --no-rois` が約5.5秒でした。実運用の保存判定では、PoCレポートとROI画像保存を毎フレーム行う前提にはしません。

### metadata期待値列

`samples/screenshots/metadata.csv` とローカル画像素材はGit管理対象外です。代わりに、このREADMEの列定義に沿って各環境のローカル metadata を整備します。

判定数ROIの精度評価には、以下の列を追加します。値はカンマや空白を含んでもよく、PoC側で数字だけに正規化します。空欄または列なしの場合は期待値なしとして扱い、`match` は成功にも失敗にもせず、`no_expected_value_count` に集計します。

- `max_combo` または `expected_max_combo`
- `marvelous` または `expected_marvelous`
- `perfect` または `expected_perfect`
- `great` または `expected_great`
- `good` または `expected_good`
- `miss` または `expected_miss`
- `ex_score` または `expected_ex_score`

`score_digits` は既存互換のため、引き続き `score`、`expected_score`、ファイル名内の `scoreXXXXXX` の順で期待値を取得します。

`expected_rank` はランクROIや低ランク素材の目視評価用メモとして扱います。数字OCRの expected coverage には含めず、`score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md` の `evaluated` / `partially_evaluated` / `no_expected_values` 判定とも混同しません。ランクOCRやランクテンプレート評価に進む場合は、別途M3の評価列とレポートとして定義します。

M3入口の曲・譜面情報ROIでは、以下の列をローカル metadata の期待値として読みます。値は文字列として前後空白と連続空白だけ正規化し、数字OCRのような桁正規化や match 判定はまだ行いません。

- `song_title` または `expected_song_title`
- `artist` または `expected_artist`
- `play_style` または `expected_play_style`
- `difficulty` または `expected_difficulty`
- `level` または `expected_level`
- `rank` または `expected_rank`

`m3_metadata_expected_coverage.md` は、confirmed-events 対象行について上記列が埋まっているかだけを `evaluated` / `partially_evaluated` / `no_expected_values` で集計します。`m3_metadata_expected_template.csv` は不足列がある confirmed-events 行だけを出力する補助CSVです。これは曲名OCR、難易度テンプレート照合、マスタ照合の精度評価ではなく、保存直前イベントで目視評価に使う期待値列がそろっているかを見るための別レポートです。

実行時には、期待値が不足している `screen_type=result` 行だけを `data/vision_poc/ocr_expected_template.csv` へ出力します。このCSVはローカル `metadata.csv` を破壊的に更新せず、どの行に `max_combo` / `marvelous` / `perfect` / `great` / `good` / `miss` / `ex_score` を埋めるべきか確認するための補助です。`missing_judgment_rois` を見て、対象画像の `ocr/<画像名>/<roi>_original.png` や実スクリーンショットを目視し、ローカルの `samples/screenshots/metadata.csv` に列と値を追加してから再実行します。

実キャプチャAPIへ進む前の期待値整備は、以下の順で行います。
判断フローは、期待値カバレッジ確認、profile比較、`miss` / `ex_score` 代表失敗確認、`miss` ROI内数字寄せ採用済み確認、`ex_score` 残り失敗PoC、confirmed-eventsで採否判断、result-candidateで副作用確認の順に固定します。

1. `python -m tools.vision_poc --ocr-target confirmed-events --ocr-rois all --ocr-profile all` を実行し、保存直前OCR相当の対象イベントだけで `ocr_expected_coverage.md` と `ocr_expected_template.csv` を確認します。
2. `ocr_expected_template.csv` の `missing_judgment_rois` をもとに、ローカルの `samples/screenshots/metadata.csv` へ judgment ROI 期待値列を追加します。このファイルはGit管理対象外のままです。
3. 同じコマンドを再実行し、`score_ocr_profiles_summary.json` と `ocr_roi_report.md` で `evaluated` / `partially_evaluated` / `no_expected_values` の違い、推奨profile候補、代表的な mismatch / empty_ocr、次に見る前処理画像を確認します。`no_expected_values` は成功扱いにせず、`partially_evaluated` は暫定判断として残りの期待値を埋めてから再判断します。
4. `miss` / `ex_score` の代表失敗を見て、ROI内数字寄せや二値化後の軽い黒文字膨張のような局所前処理だけを小さく試します。ROI座標定義、既存profile名、`score_ocr.csv` の列は変えません。
5. `miss` の右側数字寄せ採用済みを確認し、`ex_score` の残り失敗PoCを confirmed-events の保存直前評価で採否判断します。
6. confirmed-events の対象絞り込みを維持したうえで全result行の前処理傾向も見たい場合だけ、別出力先で `python -m tools.vision_poc --output data/vision_poc_result_candidate --ocr-target result-candidate --ocr-rois all --ocr-profile all` を実行し、保存直前評価とは分けて参考母数として副作用を確認します。

M7aのOCR非依存数字認識PoCは、同じ confirmed-events 境界で別出力として実行します。初回は `score_digits` だけを対象にし、既存Tesseract出力と比較したい場合は `--ocr-target confirmed-events` を合わせます。Tesseractを使わずテンプレート不足や桁分割失敗だけを確認したい場合は `--no-ocr` と併用できます。

```powershell
python -m tools.vision_poc --m7a-digit-recognition --ocr-target confirmed-events --output data\vision_poc_m7a_digit
python -m tools.vision_poc --m7a-digit-recognition --no-ocr --output data\vision_poc_m7a_digit_no_ocr
```

2026-07-07時点のローカル `score_digits` テンプレート配置後は、`python -m tools.vision_poc --m7a-digit-recognition --no-ocr --no-rois --output data\vision_poc_m7a_digit` で confirmed-events 60件すべてが `recognized` / `match=true` になり、`missing_reference`、`ambiguous`、`failed_segmentation` は0件です。`python -m tools.vision_poc --m7a-digit-recognition --ocr-target confirmed-events --no-rois --output data\vision_poc_m7a_digit_ocr_compare` では M7a は60/60 match、Tesseract比較は59件中56件が同一、3件がTesseract側の余分な桁または先頭誤読との差分、1件がOCR未取得でした。この結果は保存OK判定ではなく、M7aの保存値候補読み取り材料として扱います。

2026-07-07時点のローカル `max_combo` テンプレート配置後は、`python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo --no-ocr --no-rois --output data\vision_poc_m7a_digit_max_combo_templates` で `score_digits` 60/60、`max_combo` 60/60 がいずれも `recognized` / `match=true` です。テンプレート配置前でも `max_combo` は `missing_reference` のまま `segment_count_counts={1:1,2:4,3:55}`、`expected_digit_length_counts={1:1,2:4,3:55}` として分割分布を確認できます。`marvelous` は右側数字領域へ寄せると、テンプレート配置前でも `missing_reference` のまま `segment_count_counts={1:1,2:4,3:55}`、`expected_digit_length_counts={1:1,2:4,3:55}` を確認できます。2026-07-07時点のローカル `marvelous` テンプレート配置後は、`python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous --no-ocr --no-rois --output data\vision_poc_m7a_digit_marvelous_templates` で3 ROI合計180/180が `recognized` / `match=true` です。`perfect` は右側数字領域へ寄せると、テンプレート配置前でも `missing_reference` のまま `segment_count_counts={1:1,2:42,3:17}`、`expected_digit_length_counts={1:1,2:42,3:17}` を確認できます。2026-07-07時点のローカル `perfect` テンプレート配置後は、`python -m tools.vision_poc --m7a-digit-recognition --m7a-digit-rois score_digits max_combo marvelous perfect --no-ocr --no-rois --output data\vision_poc_m7a_digit_perfect_templates` で4 ROI合計240/240が `recognized` / `match=true` です。`great` と `good` も右側数字領域へ寄せるfixtureで、ROI別テンプレートと共有 `judgment_counts` のどちらからでも認識でき、テンプレート不足時は `missing_reference` のまま分割数を確認できます。2026-07-07時点のローカル `good` テンプレート配置後は、`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good` の6 ROI合計360/360が `recognized` / `match=true` です。2026-07-08時点のローカル `miss` ROI別テンプレート配置後は、同じ6 ROIに `miss` を加えた7 ROI合計420/420が `recognized` / `match=true` です。`miss` は白数字maskで明るい青背景を除外し、`0_result_069.png` のようなノイズ込みテンプレートなしで `segment_count_counts={1:51,2:9}`、`expected_digit_length_counts={1:51,2:9}` になります。共有 `judgment_counts` だけでは `miss` は58/60 `ambiguous`、2/60 `recognized` です。2026-07-08時点の `ex_score` は、右側数字領域へ寄せたcomponent分割と既存 `max_combo` テンプレートfallbackで60/60 `recognized` / matchになり、`segment_count_counts={1:1,3:30,4:29}`、`expected_digit_length_counts={1:1,3:30,4:29}` を確認できます。この確認ではローカル `metadata.csv` の `result_087_sp_basic_lv06_888_score986610.png` の `ex_score` をROI表示どおり `593` に修正しています。テンプレート画像は `samples/screenshots/organized/digit_templates/<roi>/0.png` から `9.png` やvariant画像、または共有 `digit_templates/combo_ex_score/` のローカル素材であり、Git管理しません。

### OCR前処理とTesseract設定

`score_digits` はローカル評価画像での初期調整として、以下の設定を既定値にしています。

- ROIを4倍に拡大
- `luma > 135` かつ `channel_spread < 140` を白文字候補として抽出
- OCR入力は黒文字/白背景へ反転
- 周囲に20pxの白余白を追加
- Tesseractは `--psm 8`、`--dpi 300`、`tessedit_char_whitelist=0123456789`

この設定では、現ローカル素材の `score_digits` は `score_ocr.csv` 上で16件すべて `match=true` です。判定数ROIも同じ前処理とTesseract設定で実行できます。metadata に `max_combo` や `expected_marvelous` のような真値列があれば `match_count` / `mismatch_count` で評価し、真値列がないROIは `no_expected_value_count` と画像、`score_ocr_raw` の目視確認を次の精度確認に使います。

## テスト

ローカル素材がある環境では、metadataを真値として分類結果を検証します。

```powershell
python -m pytest tests\test_vision_poc_classification.py
```

OCR前処理と正規化だけを確認するテストも追加しています。OCRエンジン本体の有無には依存しません。

```powershell
python -m pytest tests\test_vision_poc_ocr.py
```

全体確認は以下です。

```powershell
python -m pytest tests
```

テストでは以下を確認します。

- `screen_type=result` は `result_candidate=true`
- `song_select`、`gameplay`、`menu_setup`、`transition` は `result_candidate=false`
- `transition_countup_*` は `result_shape_candidate=true` かつ `result_candidate=false`
- `result_candidate=true` が継続したときだけ `confirmed_result=true` になる
- 1フレームだけの `result_candidate=true` は保存確定しない
- 同一キーの連続確定は `duplicate` として区別する
- `result-candidate` OCR対象モードは従来どおり `result_candidate=true` をOCRする
- `confirmed-events` OCR対象モードは `confirmed_result=true` かつ `duplicate=false` だけをOCRする
- metadata ではフレームベース、timestamped / manifest では時刻ベースの確定イベントをOCR対象にできる
- 連番画像からファイル名昇順かつ単調増加timestampの manifest を生成できる
- timestamped が生成する manifest は metadata の期待値列を保持し、manifest 再実行でもROI別カバレッジ評価に使える
- 不正fps、対象画像なし、生成 manifest の読み込み互換性を確認する
- `score` / `expected_score` / `organized_file` から `expected_score` を抽出できる
- 判定数ROIと `ex_score` は metadata の同名列または `expected_<roi_name>` 列がある場合だけ期待値として評価できる
- `score_ocr_summary.json` でROI別、ステータス別、失敗理由別のOCR集計を確認できる
- `ocr_roi_report.md` でROI別の弱点と代表的な `mismatch` / `empty_ocr` を確認できる
- `--ocr-profile all` で既存 `score_ocr.csv` の列を維持したまま、profile別summaryを出力できる
- `score_ocr_raw` から数字だけを `score_ocr_normalized` にできる
- M7a digit recognitionは confirmed-events 境界だけを対象にし、Tesseractなしで `recognized` / `missing_reference` / `failed_segmentation` / `not_evaluated` をfixtureで確認できる
- M7a digit recognitionの `score_digits` は、カンマを数字扱いせず、1桁から7桁までを可変桁として確認できる
- M7a digit recognitionは既存 `score_ocr.csv` を変更せず、同じ実行内のTesseract結果がある場合だけ別summaryで比較できる
- M7a digit recognitionは `max_combo` の右側数字領域を分割し、テンプレート不足時でも `segment_count_counts` と `expected_digit_length_counts` をsummary/reportで確認できる
- M7a digit recognitionは `marvelous` の右側数字領域を分割し、テンプレート不足時でも `segment_count_counts` と `expected_digit_length_counts` をsummary/reportで確認できる
- M7a digit recognitionは `perfect` の右側数字領域を分割し、テンプレート不足時でも `segment_count_counts` と `expected_digit_length_counts` をsummary/reportで確認できる
- M7a digit recognitionは `great` の右側数字領域を分割し、テンプレート不足時でも `segment_count_counts` と `expected_digit_length_counts` をsummary/reportで確認できる
- M7a digit recognitionは `good` の右側数字領域を分割し、テンプレート不足時でも `segment_count_counts` と `expected_digit_length_counts` をsummary/reportで確認できる
- M7a digit recognitionは判定数ROIの明るい青背景を数字として数えず、`miss` の右側数字領域と短いマーカー除外もfixtureで確認できる
- M7a digit recognitionは `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の4桁fixtureを認識できる
- M7a digit recognitionは判定数系共有テンプレート `judgment_counts` と、`max_combo` / `ex_score` 共有候補 `combo_ex_score` を探索できる
- M7a digit save candidate summaryは confirmed-events 1件を1行にし、選択した数字ROIの `recognized_digits` / `status` / `failure_reason` / `match` / `confidence` / `distance` を保存判定ではない読み取り材料として横持ち集約できる
- M7a digit save candidate reviewは横持ち集約の `needs_digit_review` だけをROI別 status / failure reason ごとに代表化し、保存判定やDB保存に進まない
- M7 save decision previewは `m7_save_readiness_review_rows` を入力にし、`preview_save_candidate` / `needs_identity_review` / `needs_digit_review` / `blocked_readiness` / `missing_required_material` を保存判定プレビューとして分け、M5 source / jacket status別代表、identity review理由別代表、digit review ROI別代表を出し、DB保存や曲ID/譜面ID確定に進まない
- M8 save payload previewは `m7_save_decision_preview_rows` を入力にし、`payload_ready` / `missing_identity_candidate` / `missing_digit_value` / `unsupported_preview_status` をdry-run payload語彙として分け、`preview_save_candidate` 以外をpayload材料にせず、DB insertや保存値確定に進まない
- M8 planned play recordsは `payload_ready` だけを `plays` 最小row contractへ変換し、非ready payloadを保存予定レコードへ進めない
- M8 score DB write previewは保存予定レコードだけを in-memory SQLite `plays` へinsertし、`schema_version=1`、`schema_contract_scope=preview_minimal_plays`、`production_schema_status=not_production_schema`、`created_by_preview` を出し、実DBファイル生成や本番保存成功、正式スキーマ確定として扱わない
- M8 score DB file output previewは `--m8-score-db-output` 明示時だけ `data/` 配下の新規SQLiteファイルへ保存予定レコードをinsertし、DBの `PRAGMA user_version=1`、`preview_metadata.created_by_preview`、`preview_metadata.schema_contract_scope`、`preview_metadata.production_schema_status`、summary/reportの `schema_version=1`、`schema_contract_scope`、`production_schema_status`、`created_by_preview`、DB readback欄の `database_schema_version`、`database_preview_metadata`、`database_plays_schema_columns`、readback一致診断欄の `database_readback_matches_preview_contract`、`database_readback_mismatch_reasons`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` を確認でき、本番保存成功や正式スキーマ確定として扱わない
- ローカル素材がある環境では `score_digits` の前処理画像を生成できる
- 曲・譜面情報の目視確認用ROIとして `play_style`、`difficulty`、`level`、`rank`、`song_title`、`artist` を `rois/` に生成できる
- M3 chart-field 抽出評価は confirmed-events 境界だけを対象にし、duplicate / rejected_transition / unconfirmed / non-result を `skipped` として区別できる
- M3 chart-field ROI画像特徴baselineは confirmed-events 境界だけを対象にし、既存の filename baseline と分けて出力できる
- M3 chart-field ROI画像特徴diagnosticsは mismatch の混同表と代表ROIを出し、`level` の単純特徴baselineを採用候補扱いしない
- M3 chart-field template比較PoCは confirmed-events 境界だけを対象にし、テンプレート素材がない環境では `no_template_references` の `empty_extraction` として扱える
- M3 chart-field template holdout比較PoCは confirmed-events result ROI を評価専用にし、参照を `chart_field_templates/` だけに限定できる
- M3 chart-field template diagnosticsは mismatch の混同表、代表ROI、`difficulty` の期待値レビュー候補を出し、採用済みテンプレート照合やマスタ照合の成功扱いにしない
- M3 chart-field adoption candidatesは `roi-template-holdout` を根拠に `adoption_candidate` / `needs_template_references` を分け、保存前判断向けの `missing_reference` / `no_expected_value` / `empty_extraction` / `low_confidence` 語彙を出せる
- M3 song/artist OCR入口は confirmed-events 境界だけを対象にし、`ocr_raw` / `pre_normalized_text` / `engine` / `status` / `failure_reason` を出し、マスタ照合や曲名正規化の成功扱いにしない
- M3 save candidate summaryは confirmed-events 1件を1行にし、song/artist OCR入口と chart-field adoption candidates を混同せず保存前向け状態へ集約できる
- M3 save candidate blocker representativesは M3 save candidate summary の未ready fieldだけをfield別・理由別に代表化し、保存可否判定やマスタ照合の成功/失敗扱いにしない

`samples/screenshots/metadata.csv` や画像がない環境では、ローカル素材が必要なテストだけ skip します。

## 判定方針

1280x720基準の固定ROIを実画像サイズへ線形スケールし、以下の特徴だけで分類します。

- `RESULTS` ヘッダー周辺の白文字量、エッジ量、明度分散
- 詳細リザルト枠のシアン外枠、明度、エッジ量
- スコア周辺の白い大数字とエッジ量
- ランク周辺の黄色/灰色大文字らしさ

スコア周辺とランク周辺は補助シグナルです。低スコア、D/B系ランク、0点リザルトでは色特徴が弱くなるため、現在の `result_candidate` は `RESULTS` ヘッダーと詳細リザルト枠がそろった完了済みリザルトフレームを基準にします。`result_shape_candidate` はリザルト形状の検出、`result_candidate` は保存処理に進める候補です。`transition_countup_*` は形状検出できても保存不可として扱えるよう、`transition_kind=countup` としてログ上で区別します。

### リザルト確定

実キャプチャでは一瞬の誤検出や遷移中フレームが混ざるため、単発の `result_candidate=true` だけでは保存確定しません。`result_candidate` は分類器が出した参考母数で、保存直前の境界は `result_events.csv` の `confirmed_result` と `duplicate` で読みます。PoCでは metadata の並びをフレーム順として扱い、`result_candidate=true` が `CONFIRMED_RESULT_MIN_FRAMES=2` フレーム継続した時点で `confirmed_result=true` にします。

`build_result_events()` は任意で `timestamps_ms` を受け取れます。timestamp がある入力では、フレーム数ではなく `result_candidate=true` が `CONFIRMED_RESULT_MIN_DURATION_MS=1000` ミリ秒以上継続した時点で `confirmed_result=true` にします。metadata には時刻情報がないため、現在の `python -m tools.vision_poc` の出力は従来どおりフレームベースです。

この値は、まずローカル素材や人工シーケンスで「単発誤検出を保存しない」ことを確認するための最小しきい値です。実キャプチャ導入時はフレーム番号ではなく、キャプチャした時点の単調増加時刻を `timestamp_ms` として渡します。FPS固定ではなくても、時刻ベースなら `result_candidate=true` が実時間でどれだけ続いたかを見られるため、FPS揺れやフレーム欠落があっても保存確定が安定します。たとえばフレーム数が少なくても1000ms以上継続していれば確定し、逆にフレーム数が多くても短時間に偏っていれば確定しません。

`transition_countup_*` は `result_shape_candidate=true` でも `transition_kind=countup` として扱い、`event_type=rejected_transition`、`confirmed_result=false` のままにします。

保存対象は原則として `confirmed_result=true` かつ `duplicate=false` のイベントだけです。`--ocr-target confirmed-events` もこの境界を使い、保存直前OCR相当の評価だけを対象にします。

### 重複保存防止

同じリザルト画面を連続検出しても保存候補イベントは1回だけ扱えるよう、PoCでは `duplicate_key` ごとに直近の確定フレームを記録します。同一キーが `DUPLICATE_WINDOW_FRAMES=90` フレーム以内に再度確定した場合は `event_type=duplicate`、`duplicate=true` にします。

timestamp がある入力では `DUPLICATE_WINDOW_MS=90000` ミリ秒以内の同一キーを重複として扱います。判定理由には時刻ベースなら `duplicate_within_ms=<差分>`、フレームベースなら `duplicate_within_frames=<差分>` を残します。timestamp がない metadata 順実行では、互換性のため引き続きフレーム窓を使います。

現在の `duplicate_key` は、ファイル名に `scoreXXXXXX` があれば `score:<数字>`、なければ `file:<ファイル名>` です。これは `duplicate_key_for_classification()` に閉じたローカルPoC用の簡易キーで、実キャプチャAPI導入時に差し替える境界です。score だけで寄せるため、同点別曲や同点別譜面を誤ってduplicate扱いする可能性があります。DB保存前には本格キーへ差し替える予定で、今回は実装差し替えはしません。将来候補は以下です。

- scoreのみ: 現行PoCに近く軽い一方、同じスコアの別リザルトを誤って重複扱いにする可能性があります。
- score + 曲名 + 難易度: OCR/マスタ連携後の実用候補で、同点別曲を分けやすくなります。
- perceptual hash: OCR確定前でも画像類似で寄せられる候補です。遷移やエフェクト差分への耐性を別途評価します。
- OCR確定後の正規化済みリザルトID: DB保存直前の最終候補です。曲、難易度、スコア、判定数などを正規化して構成します。

### result_events.csv

`data/vision_poc/result_events.csv` は分類結果を時系列イベントとして見直すためのCSVです。列は以下です。

- `frame_index`: metadata順または入力順の0始まりフレーム番号
- `organized_file`: 入力画像の相対パス
- `screen_type`: metadata上の画面種別
- `result_candidate`: 単発フレーム分類で保存候補に見えるか。`result-candidate` OCRモードの参考母数で、保存確定そのものではありません。
- `result_shape_candidate`: リザルト形状らしさがあるか。`transition_countup_*` はここが `true` でも保存対象外になり得ます。
- `confirmed_result`: 継続条件を満たして保存直前候補として確定したか。duplicate の行も `true` になり得ます。
- `event_type`: `none` は未確定、`confirmed` は重複ではない保存候補、`duplicate` は duplicate window 内の重複確定、`rejected_transition` は `transition_countup_*` などの保存不可遷移
- `duplicate`: `confirmed_result=true` のうち、同一 `duplicate_key` が duplicate window 内で再確定したか
- `duplicate_key`: 重複判定キー。現状はローカルPoC用の簡易キーで、実キャプチャ導入時の差し替え境界です。
- `reason`: 判定に使った分類理由、継続フレーム数、重複距離など
- `timestamp_ms`: 入力時刻。metadata 順実行では時刻情報がないため空欄。timestamped / manifest では単調増加するキャプチャ時刻相当のミリ秒値です。
- `candidate_duration_ms`: timestamp 付き入力で `result_candidate=true` が継続している時間。フレームベースでは空欄です。
- `confirmation_mode`: `frames` は metadata 順のフレーム数ベース、`time` は timestamped / manifest の時刻ベースです。

保存直前評価では、`event_type=confirmed`、または同等に `confirmed_result=true` かつ `duplicate=false` の行だけを保存対象として読みます。M1以降の基本境界は `event_type` ではなく `confirmed_result=true` かつ `duplicate=false` です。現行PoCでは `event_type=confirmed` も保存候補として読めますが、将来イベント種別が増えてもこの基本境界を維持します。

保存しない行は以下です。

- `duplicate=true`。`confirmed_result=true` でも保存しません。
- `event_type=rejected_transition`。`transition_countup_*` は `result_shape_candidate=true` でもこのまま保存対象外です。
- `event_type=none`。
- `result_candidate=true` だが `confirmed_result=false` の未確定候補。
- `result_shape_candidate=true` だけの行。

つまり、`result_events.csv` から保存候補行を抽出する最小ロジックは次の形です。

```text
confirmed_result == true and duplicate == false
```

DB保存はまだ実装しません。`confirmed-events` OCR対象選定が、この保存直前境界をPoC内で確認するための代替です。

### result_events_summary.json

`data/vision_poc/result_events_summary.json` は `result_events.csv` のイベント評価だけを集計したJSONです。分類精度向けの `summary.json` とは分けて、時系列の保存確定挙動を確認します。

- `confirmed_count`: 重複ではない保存確定イベント数
- `confirmed_result_count`: `duplicate` を含む `confirmed_result=true` のフレーム数
- `duplicate_count`: duplicate window 内の同一キー再確定数
- `rejected_transition_count`: `transition_countup_*` など保存不可遷移として除外した数
- `first_confirmed_frame_index`: 最初に保存確定した入力順インデックス
- `first_confirmed_timestamp_ms`: timestamp 付き入力で最初に保存確定した時刻。metadata モードでは `null`
- `confirmation_mode_counts`: `frames` / `time` ごとのイベント行数
