# イベントと保存境界

分類結果を時系列イベントとして解釈し、DB保存へ進めてよい行と保存してはいけない行を分ける設計です。現時点ではPython PoCの `result_events.csv` と `confirmed-events` OCR対象選定が正本です。

## 目的

- `result_candidate=true` だけで保存しない。
- 継続確認済みのリザルトだけを保存候補にする。
- duplicate と transition を保存対象外にする。
- OCR対象とDB保存候補の境界を一致させる。

## 主要概念

### `result_shape_candidate`

リザルト画面らしい形状検出。

例:

- RESULTSヘッダー
- 詳細リザルト枠
- ランク周辺やスコア周辺の形状

これは保存候補そのものではない。`transition_countup_*` はここが `true` でも保存対象外になり得る。

### `result_candidate`

単発フレーム分類で保存候補に見えるか。

これはOCR候補の参考母数ではあるが、保存確定ではない。実キャプチャでは一瞬の誤検出や遷移フレームが混ざるため、継続条件を満たすまで保存しない。

### `confirmed_result`

継続条件を満たし、保存直前候補として確定したか。

metadata mode:

- フレーム数ベース
- `confirmation_mode=frames`

timestamped / manifest / dry-run / 将来キャプチャ:

- 時間ベース
- `confirmation_mode=time`

### `duplicate`

同一リザルトが duplicate window 内で再確定したか。

`duplicate=true` の行は `confirmed_result=true` でも保存しない。

### `event_type`

イベントの読み分け。

- `none`: 未確定。保存しない。
- `confirmed`: 重複ではない保存候補。
- `duplicate`: duplicate window 内の重複確定。保存しない。
- `rejected_transition`: `transition_countup_*` など保存不可遷移。保存しない。

## 保存境界

DB保存へ進めてよい条件:

```text
confirmed_result=true
duplicate=false
```

同等に、現行PoCでは以下も保存候補として読める。

```text
event_type=confirmed
```

ただし将来 `event_type` が増える場合でも、保存境界の基本は `confirmed_result=true` かつ `duplicate=false` とする。

保存しない行:

- `duplicate=true`。`confirmed_result=true` でも保存しない。
- `event_type=rejected_transition`。
- `event_type=none`。
- `result_candidate=true` だが `confirmed_result=false` の未確定候補。
- `result_shape_candidate=true` だけの行。

## OCR対象境界

`--ocr-target confirmed-events` は保存直前OCR相当の評価モードです。

対象:

```text
confirmed_result=true
duplicate=false
```

対象外:

- `result_candidate=true` だが未確定
- `duplicate=true`
- `event_type=rejected_transition`
- non-result

`--ocr-target result-candidate` は従来互換と副作用確認用であり、保存直前評価の成功扱いにはしない。

OCR対象境界、expected coverage、profile採用判断は別条件として読む。`confirmed-events` は「OCRを試す保存直前イベント」を選ぶ境界であり、DB保存成功やOCR成功を意味しない。対象イベントに対して期待値がないROIは `no_expected_values`、一部だけ期待値があるROIは `partially_evaluated` として扱い、どちらも最終的な保存品質の採用根拠にはしない。profile採用候補として読めるのは、confirmed-events 対象で expected coverage が `evaluated` かつ `recommended_profiles` があるROIだけです。`partially_evaluated` は暫定、`no_expected_values` は `reference_profiles` が出ていても目視参考に留める。OCR品質は `score_ocr_summary.json`、`score_ocr_profiles_summary.json`、`ocr_expected_coverage.md`、`ocr_roi_report.md` のROI別 `match_count` / `mismatch_count` / `empty_ocr_count` / `no_expected_value_count` を見て判断する。

M7aの `--m7a-digit-recognition` も同じ confirmed-events 境界だけを対象にするが、Tesseract OCRとは別の保存値候補読み取りPoCとして扱う。出力は `m7a_digit_recognition.csv`、`m7a_digit_recognition_summary.json`、`m7a_digit_recognition_report.md` で、既存 `score_ocr.csv` / `score_ocr_summary.json` は変更しない。status は `recognized`、`ambiguous`、`missing_reference`、`failed_segmentation`、`not_evaluated` を分ける。`recognized` はテンプレート/bitmap比較で数字候補が出た状態であり、DB保存OKではない。`not_evaluated` は数字候補は出たが期待値がなく成功判定できない状態、`missing_reference` はローカルテンプレート不足、`failed_segmentation` は桁分割失敗として読む。`score_digits` は0から1,000,000までの可変桁表示を前提にし、カンマや背景ノイズを除いた大きな数字成分を左から比較する。1桁から7桁までを固定桁数へ寄せない。テンプレート画像は `samples/screenshots/organized/digit_templates/` などのローカル素材で、Git管理しない。テンプレート探索はROI別ディレクトリを優先し、判定数系は `digit_templates/judgment_counts/`、`max_combo` / `ex_score` 系は `digit_templates/combo_ex_score/` も共有候補として見られる。同じ実行でTesseract OCR結果がある場合だけ、summary の `tesseract_comparison` を参考比較として読む。

`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` はROI左側のラベルや下線を保存値候補の数字として扱わないため、M7aではROI右側の数字領域へ寄せてから数字らしい前景コンポーネントを分割する。判定数ROIでは明るい青背景を数字扱いしないよう、高明度かつチャンネル差が大きい成分を除外する。`miss` は短いマーカーを数字として拾わないよう数字候補の高さを少し絞り、白数字向けの明度 + チャンネル差maskで前景を切る。`ex_score` は `combo_ex_score` 共有テンプレートがない環境でも、既存 `max_combo` テンプレートをfallbackとして参照する。fixtureでは3桁だけでなく4桁も固定し、`max_combo`、判定数ROI、`ex_score` の将来4桁表示に備える。`m7a_digit_recognition_summary.json` のROI別 `segment_count_counts` と `expected_digit_length_counts` は、テンプレート不足で `missing_reference` の段階でも分割数と期待桁数の一致を確認するための診断であり、DB保存可否判定ではない。

`m7a_digit_save_candidate_summary.csv`、`m7a_digit_save_candidate_summary.json`、`m7a_digit_save_candidate_summary.md` は、M7aの縦持ち認識結果を confirmed-events 保存候補1件につき1行へ横持ち集約するレポートです。選択した数字ROIごとに `recognized_digits`、`expected_value`、`status`、`failure_reason`、`match`、`confidence`、`distance`、`segment_count` を並べる。`aggregate_status` は `all_digits_recognized`、`needs_digit_review`、`no_digit_rois` のみで、`all_digits_recognized` でも保存OKやDB保存成功を意味しない。duplicate、`rejected_transition`、未確定候補、non-result は集約対象外のまま維持し、M8へ渡す数値読み取り材料としてだけ読む。

`m7a_digit_save_candidate_review.json` と `m7a_digit_save_candidate_review.md` は、M7a横持ち集約の `aggregate_status=needs_digit_review` 行から、レビューすべき数字ROIを代表化する補助レポートです。入力は既存の `m7a_digit_save_candidate_summary_rows` の行に限定し、ROI別 `status` / `failure_reason` ごとに `organized_file`、ROI名、`recognized_digits`、`expected_value`、`status`、`failure_reason`、`match`、`confidence`、`distance`、`segment_count` を代表として出す。`missing_reference` はテンプレート不足、`ambiguous` は距離や余白不足、`failed_segmentation` は桁分割失敗、`not_evaluated` は期待値不足または未試行として読み分ける。これはレビュー補助であり、保存OK/NG判定、DB保存、曲ID/譜面ID確定には進まない。

`m7a_tesseract_comparison_review.json` と `m7a_tesseract_comparison_review.md` は、同じ実行内のM7a数字認識結果と既存Tesseract OCR結果の比較差分を代表化する補助レポートです。入力は `m7a_digit_results` と `score_ocr_results` に限定し、`m7a_digit_recognition_summary.json` の `tesseract_comparison` counts を置き換えない。比較状態は `same_normalized`、`different_normalized`、`tesseract_unavailable`、`m7a_unavailable` として読み、代表には `organized_file`、ROI名、M7a `recognized_digits` / `status` / `failure_reason`、Tesseract raw / normalized / status / error、`expected_value`、M7a match、Tesseract match を含める。`different_normalized` の実例と、Tesseract側で正規化数字列がないROI/理由を確認するためのレビュー補助であり、保存OK/NG判定、DB保存、OCR方式刷新には進まない。

`m7_save_readiness_review.csv`、`m7_save_readiness_review.json`、`m7_save_readiness_review.md` は、M3保存候補材料、M7a数字材料、任意のM5 jacket候補観測を confirmed-events 保存候補1件単位で束ねるM7保存判定前レビューです。通常入力は `m3_save_candidate_summary_rows` と `m7a_digit_save_candidate_summary_rows` で、`--m5-jacket-match` 実行時だけ `jacket_match_rows` 由来の `identity_signal_*` / `jacket_match_status` を参照列として追加する。readiness status は `ready_for_save_review`、`blocked_m3_material`、`blocked_digit_review`、`blocked_identity_signal`、`missing_required_material` を出す。`identity_signal_status=jacket_resolved_candidate` / `composite_resolved_candidate` の場合だけM5側材料をレビュー可能な候補観測として扱い、未解決のM5候補観測は `blocked_identity_signal` として読む。M5 identity材料がレビュー可能な場合、曲同定はM5候補観測を主材料として読むため、`song_title` / `artist` OCR不足だけでは `blocked_m3_material` にしない。元のM3集約の未ready項目は `m3_blocking_fields` に残し、M7保存前レビュー上のM3 blockerは `m7_m3_blocking_fields` として別に読む。M5未実行時は従来どおりM3材料とM7a数字材料だけでレビューし、`song_title` / `artist` 不足もM3 blockerとして扱う。`ready_for_save_review` は保存判定へ進むためのPoC材料が揃った状態であり、保存OK、DB保存成功、曲ID/譜面ID確定を意味しない。`blocked_m3_material` はM7保存前レビュー上のM3 blockerが残っている状態、`blocked_digit_review` はM7a数字材料にレビュー対象ROIがある状態、`missing_required_material` はM7a集約など必須PoC材料が欠けている状態として読む。duplicate、`rejected_transition`、未確定候補、non-result は対象外のまま維持する。

`m7_save_decision_preview.csv`、`m7_save_decision_preview.json`、`m7_save_decision_preview.md` は、`m7_save_readiness_review_rows` を入力にしたM7保存判定プレビューです。1行は同じ confirmed-events 保存候補で、preview status は `preview_save_candidate`、`blocked_readiness`、`needs_identity_review`、`needs_digit_review`、`missing_required_material` に限る。`preview_save_candidate` はM8へ渡す候補材料が揃ったプレビュー状態であり、保存OK、DB保存成功、曲ID/譜面ID確定ではない。`ready_for_save_review` でもM5が未実行、M5候補観測がレビュー不能、または `identity_signal_song_id` / `identity_signal_chart_id` が欠ける場合は `needs_identity_review` として読む。`preview_reason` は `m5_not_run`、`m5_identity_not_reviewable`、`identity_signal_id_missing` を分け、M5未実行、M5候補観測未解決、候補ID欠落を混同しない。`blocked_digit_review` は `needs_digit_review`、M7 readiness上のM3 blockerは `blocked_readiness`、必須PoC材料欠落は `missing_required_material` へ寄せる。CSVにはM5の `identity_signal_*` と、選択M7a ROIごとの `recognized_digits`、`expected_value`、`match` を候補観測として出すが、どちらも保存値確定として扱わない。duplicate、`rejected_transition`、未確定候補、non-result は上流のM7 readiness対象外のまま維持する。

`m8_save_payload_preview.csv`、`m8_save_payload_preview.json`、`m8_save_payload_preview.md` は、`m7_save_decision_preview_rows` を入力にしたM8 dry-run payload previewです。`preview_status=preview_save_candidate` の行だけをpayload候補として扱い、それ以外は `unsupported_preview_status` としてpayload材料へ昇格せず、summary の `excluded_preview_status_counts` とMarkdown代表で読む。payload status は `payload_ready`、`missing_identity_candidate`、`missing_digit_value`、`unsupported_preview_status` に限る。`payload_ready` は候補IDとM7a数字列がdry-run payload材料として揃った状態であり、DB保存可能、DB保存成功、曲ID/譜面ID確定、保存値確定を意味しない。CSVの `identity_signal_song_id` / `identity_signal_chart_id` / `identity_signal_source` はM5候補観測を写したもので、保存用確定IDではない。`score_digits`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` はM7aの `*_recognized_digits` を写したdry-run値であり、`*_expected_value` と `*_match` はレビュー材料としてだけ読む。DB insert、低信頼度ログ本番仕様には進まない。

`m8_planned_play_records.csv`、`m8_planned_play_records.json`、`m8_planned_play_records.md` は、`m8_save_payload_preview_rows` の `payload_ready` 行だけを個人スコアDB `plays` 相当の最小row contractへ変換するプレビューです。`unsupported_preview_status`、`missing_identity_candidate`、`missing_digit_value` は保存予定レコードへ変換しない。最小列は `played_at_ms`、`song_id`、`chart_id`、`score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score`、`source_organized_file`、`source_confirmation_mode`、`analysis_payload_status`、`identity_signal_source`、`m5_identity_signal_status`、`m5_jacket_match_status` とする。これはin-memory SQLite fixtureでスキーマ契約を確認するためのrowであり、実DBファイル生成、DB保存成功、曲ID/譜面ID確定、保存値確定を意味しない。timestampなし入力では `played_at_ms=0` の暫定値として扱う。

`m8_score_db_write_preview.csv`、`m8_score_db_write_preview.json`、`m8_score_db_write_preview.md` は、`m8_planned_play_records_rows` だけを新規 in-memory SQLite `plays` テーブルへinsertするdry-runプレビューです。非ready payloadは上流の planned records で止まり、このpreviewの入力にはしない。write preview status は `inserted_in_memory`、`skipped_invalid_planned_record` に限る。previewスキーマ識別は `schema_name=m8_score_db_preview`、`schema_version=1`、`schema_version_source=PRAGMA user_version`、`created_by_preview=tools.vision_poc.m8_score_db_preview` とする。SQLite側にも軽量な `preview_metadata` 表を作るが、これは正式マイグレーションではなくpreview生成物識別だけに使う。`inserted_in_memory` はin-memory fixture上でinsert境界を確認できた状態であり、実ファイルDB生成、本番DB保存成功、曲ID/譜面ID確定、保存値確定を意味しない。timestampなし入力の `played_at_ms=0` は暫定値のままinsert境界へ渡す。

`--m8-score-db-output data\...\ddrgp-scores.sqlite` は、明示指定された場合だけ実ファイルSQLiteへ書くM8出力プレビューです。出力先は `data/` 配下の新規ファイルに限定し、`data/` 外や既存ファイルへの書き込みは拒否する。入力は `m8_planned_play_records_rows` だけで、非ready payload、M5未実行、identity不足、digit不足の行をこの段階で再判定しない。実ファイルDBにも `PRAGMA user_version=1` と `preview_metadata.created_by_preview=tools.vision_poc.m8_score_db_preview` を設定し、summary/reportの `schema_version=1`、`created_by_preview` と一致させる。summary/report には実DBから読み戻した `database_schema_version`、`database_preview_metadata`、`database_plays_row_count`、`database_plays_schema_columns`、それらがpreview契約、insert件数、preview最小 `plays` schema と一致するかを示す `database_readback_matches_preview_contract`、`database_readback_mismatch_reasons`、`database_plays_row_count_matches_insert_counts`、`database_plays_row_count_mismatch_reasons`、`database_plays_insert_columns_match_planned_contract`、`database_plays_integer_fields_match_preview_contract`、`database_plays_schema_mismatch_reasons` も出し、定数として出すpreview識別欄とDB readback診断を分けて読む。file output preview status は `inserted_to_file_preview`、`skipped_invalid_planned_record` に限る。`inserted_to_file_preview`、DB readback欄、readback一致診断欄、row count一致診断欄、schema readback診断欄は明示指定されたpreview DBへのinsert/識別/schema確認であり、本番DB保存成功、正式スキーマ確定、曲ID/譜面ID確定、保存値確定を意味しない。

正式個人スコアDBの明示ファイル保存は `save_personal_score_db_file(db_path, adapter_input)` に分離する。`adapt_personal_score_db_save_input()` をファイル準備より先に実行し、`unresolved` は理由を返してDBを作成・変更しない。`ready` はsource/play/analysis、duplicateや明示された低信頼度/error/skipの `excluded` はsource/analysisだけを既存writerの1 transactionで記録する。この入口はレビュー済み正式値を別入力として要求し、上記M8 preview rowや `--m8-score-db-output` のDBを正式DBへ昇格しない。

M9 manual WPF入口はversion 1 workflow入力と保存先DBをユーザーが明示選択し、`personal_score_db_workflow_app` process adapterから同じstrict loader / adapter / orchestration / file saveを1回だけ呼ぶ。C#側は候補材料や正式値を解釈しない。transaction完了した `workflow_status=saved` かつ非null `play_id` だけをread-only viewerで再読込し、`excluded` / `duplicate` / `unresolved` / `invalid` / DB拒否 / artifact失敗を成功playへ写像しない。

`ready` の明示 `formal_play.duplicate_key` が既存の正式 `plays` と衝突した場合は、保存直前preflightで2件目のplayを作らず、今回入力のsource captureとanalysisだけを同じtransactionで記録する。analysisは `analysis_status=skipped`、`save_boundary_status=duplicate`、`skip_reason=duplicate_key_already_saved`、`duplicate=true` に固定し、Python API/CLI結果は `adapter_status=excluded`、`written=true`、`play_id=null` とする。`capture_id` / `analysis_id` の同一再送は冪等成功へ変換せず、UNIQUE拒否時に今回rowをrollbackする。preflightは単一プロセスPoCの境界であり、並行writer間のraceは既存UNIQUE制約を最終防壁として残す。

単発CLIは `--personal-score-db-save-input` と `--personal-score-db-save-database` の両方が明示された場合だけ、このファイル保存を1回呼ぶ。`input_schema_version=1` のJSONは候補材料、source/analysis値、任意の正式play、任意の除外を構造として分ける。loaderは全階層の必須/未知keyと型をadapter前に検査し、M5/M7a候補値や相対時刻を正式playへ補完しない。片方だけのoption、入力schema不正、adapterの `unresolved` はDB準備前に非0終了する。

保存前validationは `--personal-score-db-save-input-validate` の単独指定だけで同じstrict loaderとadapterを各1回実行する。`ready` はplayつき、`excluded` はplayなしの正式save inputを構築できたことだけを表し、`unresolved` は正式save inputを返さない。validationはDB pathを受け取らず、DB互換性、既存DB内duplicate、並行writer、実保存を判定しない。候補値や相対時刻の正式値への昇格、正式JSONの生成・補完も行わない。他のsave/diagnostic/通常PoC/M8 preview optionとの混在は、出力副作用より前に拒否する。

人手レビューのvalidation結果を残す場合だけ、`--personal-score-db-save-input-validate-output <data/...json>` をvalidation inputと必須ペアで明示する。receiptは標準出力と同じschema version、入力path、adapter status、save input構築可否、理由だけを持ち、正式値、候補材料、template本文、DB情報を記録しない。出力path、拡張子、既存ファイル、option排他はinput loadより先に検査し、receiptの有無でready/excluded/unresolved/invalidや終了コードを変えない。receiptはレビュー記録であって、レビュー承認、DB互換性、duplicate非衝突、並行writer安全性、実保存成功を意味しない。

正式JSONの人手レビュー開始点は `--personal-score-db-save-input-template <data/...json>` の単独modeで作る空templateとする。現行schema version 1の構造だけを固定し、候補材料は空、正式文字列は空文字、正式整数と任意値はnull、除外はnullにする。metadata、M5/M7a/M8 preview、manifest、画像、DBから値を転記せず、未編集templateはadapter/validationで `unresolved` のままに保つ。template生成はイベント確定、レビュー完了、保存許可、duplicate判定、DB保存を行わず、他optionとの混在と既存ファイル上書きを副作用前に拒否する。

M3入口の曲・譜面情報ROIも、評価対象は同じ confirmed-events 境界へ寄せる。ただし `song_title`、`artist`、`play_style`、`difficulty`、`level`、`rank` / `expected_rank` は数字OCRの expected coverage ではなく、`m3_metadata_expected_coverage.md` と `m3_metadata_expected_template.csv` で別に読む。これは期待値列が保存直前イベントにそろっているかの確認であり、曲名OCR、ランクテンプレート照合、マスタ照合の成功を意味しない。

M3 chart-field 抽出PoCの入口では、有限候補で扱いやすい `play_style`、`difficulty`、`level` を優先する。`m3_chart_fields.csv` は全イベント行を出し、`confirmed_result=true` かつ `duplicate=false` の行だけを `chart_field_target=true` にする。duplicate、`rejected_transition`、未確定候補、non-result は対象外理由を付ける。これは chart-field 評価対象の一覧であり、数字OCR expected coverage、曲名OCR、artist OCR、rank OCR、テンプレート照合、マスタ照合の成功とは分けて読む。

`m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` は、同じ対象境界に対して抽出値、期待値、match、status、failure_reason を出す別レポートです。現行の extractor は `filename-baseline` で、ローカル `organized_file` 名から `SP/DP`、難易度、`lvXX` を正規化する初期baselineに留める。これはROI画像特徴、OCR、テンプレート照合、マスタ照合の成功ではない。

`m3_chart_field_image_feature_extraction.csv` と `m3_chart_field_image_feature_extraction_summary.json` は、ROI画像特徴由来の軽い比較baselineです。extractor は `roi-feature-nearest-centroid` で、confirmed-events 対象の `play_style`、`difficulty`、`level` ROIから明度、白/黄/シアン/緑比率、エッジ比率などを取り、期待値ラベルごとの leave-one-out centroid に最も近い値を出す。これは画像特徴の診断用であり、OCR、テンプレート照合、マスタ照合の成功ではない。画像特徴やテンプレート比較へ進む場合も、対象境界と `match` / `mismatch` / `empty_extraction` / `no_expected_value` / `skipped` の status 語彙を維持する。

`m3_chart_field_image_feature_diagnostics.md` は、同じ画像特徴baselineの mismatch を混同表と代表ROIで確認する補助レポートです。`play_style` の単発mismatch、`difficulty` の期待値/抽出値の混同、`level` の弱さを次のPoC単位へ渡すために読み、採用根拠やテンプレート照合成功として扱わない。

`m3_chart_field_template_extraction.csv` と `m3_chart_field_template_extraction_summary.json` は、ローカル `samples/screenshots/organized/chart_field_templates/` 画像と confirmed-events 対象の result ROIを参照する最近傍テンプレート比較PoCです。extractor は `roi-template-nearest` で、テンプレートファイル名や metadata 期待値から期待ラベルを読み、confirmed-events 対象の `play_style`、`difficulty`、`level` ROIと同じROIを比較する。confirmed-events 由来の参照は評価中の同一フレームを除く leave-one-out にする。これは追加テンプレート素材と評価セット由来参照を使った同分布内の比較実験であり、OCR、マスタ照合、採用済みテンプレート照合の成功ではない。参照がない環境では対象行を `empty_extraction` / `no_template_references` として扱い、期待ラベルの参照テンプレートがない mismatch は `missing_expected_template_reference` として通常の mismatch と区別する。通常の112件分類回帰セットとは混同しない。

`m3_chart_field_template_holdout_extraction.csv` と `m3_chart_field_template_holdout_extraction_summary.json` は、参照を `chart_field_templates/` だけに限定し、confirmed-events result ROIを評価専用に分ける最近傍テンプレート比較PoCです。extractor は `roi-template-holdout` で、`roi-template-nearest` の同分布 leave-one-out 診断とは別に読む。テンプレート素材がない環境では confirmed-events 参照で補わず、対象行を `empty_extraction` / `no_template_references` として扱う。これは外部検証に近づけるための分割レポートであり、OCR、マスタ照合、採用済みテンプレート照合の成功ではない。

`m3_chart_field_template_diagnostics.md` は、同じ `roi-template-nearest` の mismatch を混同表、代表ROI、`difficulty` の期待値レビュー候補として読む補助レポートです。`difficulty` mismatch は、抽出ロジックの失敗だけではなく、ROI画像の見た目と metadata / ファイル名由来期待値の食い違い候補として確認する。これも同分布内の leave-one-out 診断であり、OCR、採用済みテンプレート照合、マスタ照合の成功扱いにはしない。

`m3_chart_field_template_holdout_diagnostics.md` は、同じ `roi-template-holdout` の mismatch と参照不足を読む補助レポートです。参照元に confirmed-events result ROI を含めないことを確認し、`chart_field_templates/` と評価対象の混同を避けるために使う。

`m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` は、M3-3の採用候補レビュー用レポートです。候補根拠は `roi-template-holdout` に寄せ、fieldごとに `adoption_candidate`、`needs_template_references`、`needs_expected_values`、`needs_references_or_extraction`、`low_confidence` を分ける。2026-07-04時点のローカル37テンプレート配置後は、`play_style`、`difficulty`、`level` がすべて confirmed-events 60/60 match のためPoC上の採用候補として読める。ただし、まだ本番採用済みテンプレート照合、OCR、マスタ照合の成功ではない。参照不足が再発したfieldは、保存前判断へ渡す語彙では `missing_reference` として扱う。

`m3_song_artist_ocr.csv`、`m3_song_artist_ocr_summary.json`、`m3_song_artist_ocr.md` は、M3-4の曲名/artist OCR入口レポートです。`--m3-song-artist-ocr` 指定時だけ生成し、対象は confirmed-events 境界の `confirmed_result=true` かつ `duplicate=false` に限定する。`song_title` と `artist` の `ocr_raw`、改行と連続空白だけを畳んだ `pre_normalized_text`、`engine`、`status`、`error`、`expected_value`、`roi_path`、`failure_reason` を出す。`failure_reason` は `engine_unavailable`、`ocr_failed`、`empty_ocr`、`no_expected_value` に寄せる。このレポートはOCR入口の観察用であり、曲名正規化、ファジーマッチ、マスタ照合、保存可否の成功扱いにはしない。`artist` は左右切れがある補助項目として読む。

`m3_song_artist_ocr_entry_failures_summary.json` と `m3_song_artist_ocr_entry_failures.md` は、M3-9の曲名/artist OCR入口失敗代表整理です。M3-4のOCR入口行から `engine_unavailable`、`ocr_failed`、`empty_ocr` だけを入口失敗として集約し、`song_title` を主要項目、`artist` を左右切れがある補助項目として別々に代表化する。`no_expected_value` は期待値整備の問題として入口失敗代表には含めない。このレポートも confirmed-events 境界だけを元にし、曲名正規化、ファジーマッチ、マスタ照合、DB保存可否判定の成功/失敗扱いにはしない。

`m3_save_candidate_summary.csv`、`m3_save_candidate_summary.json`、`m3_save_candidate_summary.md` は、M3-5の保存候補向け集約レポートです。1行は1つの confirmed-events 保存候補で、`song_title`、`artist`、`play_style`、`difficulty`、`level` の状態を横並びにする。状態語彙は `ready`、`missing_reference`、`ocr_unavailable`、`ocr_failed`、`empty_ocr`、`no_expected_value`、`not_adopted` に小さく保つ。`play_style` / `difficulty` / `level` の `ready` は M3-3 の `adoption_candidate` を反映するだけで、本番採用済みテンプレート照合ではない。`song_title` / `artist` の `ready` も OCR入口観察が得られた状態であり、曲名正規化、ファジーマッチ、マスタ照合、DB保存可能を意味しない。duplicate、`rejected_transition`、未確定候補、non-result は集約対象外のまま維持する。

`m3_save_candidate_blockers_summary.json` と `m3_save_candidate_blockers_summary.md` は、M3-6の保存候補ブロッカー代表整理です。M3-5集約行のうち `ready` ではないfieldだけを、status / failure reasonごとに数件代表化する。代表には `organized_file`、期待値、抽出値、extractor、`roi_path` を含め、`song_title empty_ocr`、`artist empty_ocr`、`difficulty missing_reference`、`level missing_reference` のような保存前に止まる理由を人間が確認しやすくする。対象境界は M3-5と同じ confirmed-events だけで、duplicate、`rejected_transition`、未確定候補、non-result は含めない。このレポートはレビュー補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化の成功/失敗判定にはしない。

`m3_save_candidate_blocker_resolution_plan.json` と `m3_save_candidate_blocker_resolution_plan.md` は、M3-7の保存前ブロッカー解消順レビューです。M3-5集約内の未ready fieldを、`add_template_references`、`rerun_after_reference_update`、`run_m3_song_artist_ocr`、`inspect_ocr_entry_failures` などの次手へ分け、追加すべきローカルテンプレート参照ラベル、代表 `organized_file`、期待値、抽出値、extractor、`roi_path` を出す。対象境界はM3-5/M3-6と同じ confirmed-events だけで、duplicate、`rejected_transition`、未確定候補、non-result は含めない。このレポートは次に増やすローカル素材や確認するOCR入口を決めるためのレビュー補助であり、DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化の成功/失敗判定にはしない。

ローカル37テンプレート配置後のM3-8整理では、`play_style`、`difficulty`、`level` はPoC上の採用候補としてM3-5集約で `ready` へ進める状態になった。M3の残りは `song_title empty_ocr` と `artist empty_ocr` のOCR入口代表失敗を読む単位に絞る。これはM3内のレビュー範囲の整理であり、DB保存、マスタ照合、ファジーマッチ、曲名正規化へは進まない。

M3-9では、M3-8後に残った `song_title empty_ocr` と `artist empty_ocr` を `m3_song_artist_ocr_entry_failures_summary.json` / Markdown で役割別に固定して読む。`song_title` は主要項目のOCR入口失敗代表、`artist` は左右切れがある補助項目のOCR入口失敗代表であり、同じ改善対象として混ぜない。ローカル確認では `song_title empty_ocr=2`、`artist empty_ocr=22` だが、この数はPoC入力とOCRエンジンに依存するため、採用済みマスタ照合や保存可否の成否として扱わない。

`difficulty` は5種類の文字色が分かれているため、`roi-template-nearest` では difficulty ROIに限って前景文字色の比率パターンで比較する。残る mismatch は、ROI画像の見た目と metadata / ファイル名由来期待値の食い違い候補として確認する。

2026-07-04時点のローカルレビューでは、`difficulty` mismatch 5件はすべてROI表示が metadata / ファイル名由来期待値と食い違う修正候補だった。ローカル `metadata.csv` はROI表示へ合わせて修正済みで、ファイル名は当面リネームしない。レビュー結果は `docs/design/07_m3_chart_field_review.md` に残し、Git管理外の `metadata.csv` 実体やスクリーンショット画像はコミットしない。

## transition_countup の扱い

`transition_countup_*` はリザルト形状が出ていても保存対象外とする。

期待されるイベント:

```text
result_shape_candidate=true
result_candidate=false
confirmed_result=false
event_type=rejected_transition
duplicate=false
```

この扱いは、カウントアップ中のスコアや判定数を誤保存しないための回帰ガードです。

## time-based confirmation

timestamp 付き入力では、フレーム数ではなく継続時間で確定する。

現行PoCの基準:

```text
CONFIRMED_RESULT_MIN_DURATION_MS = 1000
```

考え方:

- FPS固定に依存しない。
- フレーム欠落やFPS揺れがあっても、実時間で保存確定を判断する。
- timestamp が短時間に偏っている場合は、フレーム数が多くても確定しない。

## duplicate window

timestamp 付き入力では時間窓で重複判定する。

現行PoCの基準:

```text
DUPLICATE_WINDOW_MS = 90000
```

timestamp なし入力ではフレーム窓で重複判定する。

```text
DUPLICATE_WINDOW_FRAMES = 90
```

## 現行duplicate key

現行PoCでは簡易キーを使う。

- ファイル名に `scoreXXXXXX` があれば `score:<digits>`
- なければ `file:<filename>`

これはローカルPoC用の簡易キーであり、本格実装ではない。score だけで寄せるため、同点別曲や同点別譜面を誤って duplicate 扱いする可能性がある。DB保存へ進む前に、本格キーへ差し替える。

将来候補:

- score + 曲名 + 難易度
- score + 判定数
- perceptual hash
- マスタ照合後の正規化済み result id

## `result_events.csv` の読み方

`result_events.csv` は保存直前イベント契約のCSVです。各列の意味は以下です。

- `frame_index`: 入力順の0始まりフレーム番号。
- `organized_file`: 入力画像を表す相対名またはmanifest上の画像名。
- `screen_type`: metadataまたはmanifest上の画面種別。未知なら空欄や `unknown` もあり得る。
- `result_candidate`: 単発フレーム分類で保存候補に見えるか。これだけでは保存しない。
- `result_shape_candidate`: リザルト画面らしい形状検出。`transition_countup_*` では `true` でも保存対象外。
- `confirmed_result`: 継続条件を満たしたか。duplicate行でも `true` になり得る。
- `event_type`: `none` / `confirmed` / `duplicate` / `rejected_transition` の現行イベント解釈。
- `duplicate`: duplicate window 内の同一キー再確定か。`true` の行は保存しない。
- `duplicate_key`: 現行PoCの簡易重複キー。
- `reason`: 分類理由、継続条件、重複距離などの確認用メモ。
- `timestamp_ms`: timestamped / manifest / dry-run 由来の入力時刻。metadata modeでは空欄。
- `candidate_duration_ms`: timestamp付き入力で候補が継続した時間。metadata modeでは空欄。
- `confirmation_mode`: `frames` または `time`。

保存候補確認では以下を見る。

- `event_type`
- `confirmed_result`
- `duplicate`
- `timestamp_ms`
- `candidate_duration_ms`
- `confirmation_mode`
- `reason`

`result_candidate=true` の行数だけで保存成功を判断しない。

## `result_events_summary.json` の読み方

見るべき値:

- `confirmed_count`: 重複ではない保存候補数
- `confirmed_result_count`: duplicate を含む確定フレーム数
- `duplicate_count`: 重複除外数
- `rejected_transition_count`: 遷移除外数
- `first_confirmed_timestamp_ms`: timestamp付き入力で最初に保存確定した時刻
- `confirmation_mode_counts`: `time` / `frames` の分布

confirmed-events OCR評価では、あわせて `score_ocr_summary.json` の `skipped_duplicate_count`、`skipped_unconfirmed_count`、`skipped_rejected_transition_count` を見る。duplicate、未確定候補、`rejected_transition` がOCR対象外のまま維持されていることを確認するための値であり、OCR精度そのものはROI別の expected coverage と match / mismatch で読む。
M7a digit recognition評価では、`m7a_digit_recognition_summary.json` の `target_boundary`、`status_counts`、`failure_reason_counts`、`by_roi`、`tesseract_comparison` を見る。`target_count` が保存直前イベント数と一致し、duplicate、未確定候補、`rejected_transition` が対象外に残っていることを確認する。`missing_reference` はテンプレート不足、`failed_segmentation` は桁分割失敗、`not_evaluated` は期待値不足として読み、保存可否判定やDB保存には進まない。
ROI別bucketの `segment_count_counts` と `expected_digit_length_counts` は、追加ROIへ広げるときに桁分割が期待値桁数と合っているかを確認する補助診断として読む。特に `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` はテンプレート配置前でもこの分布を確認し、`missing_reference`、`ambiguous`、`failed_segmentation` を混同しない。2026-07-08時点のローカル確認では、`miss` は白数字maskとROI別テンプレート込みで `score_digits` から `miss` まで420/420 `recognized` / match、共有 `judgment_counts` だけでは `miss` が58/60 `ambiguous`、2/60 `recognized` として読む。`ex_score` は右側数字領域へのcomponent分割と `max_combo` テンプレートfallbackで60/60 `recognized` / matchになり、`score_digits` から `ex_score` まで8 ROI合計480/480 `recognized` / matchとして読む。この確認ではローカル `metadata.csv` の `result_087_sp_basic_lv06_888_score986610.png` の `ex_score` をROI表示どおり `593` に修正している。
M7a横持ち集約後は、`m7a_digit_save_candidate_review.json` / Markdown の `review_candidate_count` とROI別代表を見て、`needs_digit_review` の理由がテンプレート不足、距離/余白不足、桁分割失敗、期待値不足または未試行のどれかを確認する。この代表整理は保存判定ではなく、M8へ渡す数字読み取り材料のレビュー補助に留める。
Tesseract比較ありのM7a実行では、`m7a_tesseract_comparison_review.json` / Markdown で `different_normalized` の代表と `tesseract_unavailable` のROI別理由を確認する。この代表整理も、既存Tesseract OCRとの参考比較であり、保存可否やDB保存の根拠にはしない。
M7保存判定前レビューでは、`m7_save_readiness_review.json` / Markdown の `readiness_status_counts`、`m7_m3_material_status_counts`、`m5_identity_material_status_counts`、代表を見て、M3材料不足、M7a数字レビュー、M5候補観測未解決、必須材料欠落のどこで止まるかを確認する。`m3_blocking_fields` は元のM3集約の未ready項目、`m7_m3_blocking_fields` はM7保存前レビュー上のM3 blockerとして読み分ける。`ready_for_save_review` でも保存可否やDB保存値の確定ではなく、次のM7保存判定ロジックへ渡すレビュー材料に留める。`identity_signal_*` は候補観測であり、曲ID/譜面ID確定として扱わない。
M7保存判定プレビューでは、`m7_save_decision_preview.json` / Markdown の `preview_status_counts`、`preview_candidate_count`、status別代表を見て、M8へ渡す候補材料が揃った行と、M3 readiness、M7a数字レビュー、M5 identity review、必須材料欠落で止まる行を分けて読む。`preview_save_candidate` については `preview_save_candidate_identity_signal_source_counts`、`preview_save_candidate_m5_jacket_match_status_counts`、`preview_save_candidate_m5_identity_signal_status_counts` と M5 source / jacket status / identity status 別代表を確認する。`needs_identity_review` は `m5_not_run`、`m5_identity_not_reviewable`、`identity_signal_id_missing` の代表を分け、`needs_digit_review` はROI別の `recognized_digits`、`expected_value`、`match`、`failure_reason` を代表で読む。`preview_save_candidate` でも保存OKやDB保存成功ではなく、`identity_signal_*` とM7a数字列は候補観測のまま扱う。
M8 dry-run payload previewでは、`m8_save_payload_preview.json` / Markdown の `payload_status_counts`、`payload_candidate_count`、`payload_ready_count`、`excluded_preview_status_counts` を見て、`preview_save_candidate` からpayload材料へ進む行と、候補ID欠落、数字欠落、preview対象外で止まる行を分けて読む。`payload_ready` 代表は将来DBへ渡すなら使う材料の確認であり、保存OKやDB保存成功ではない。`missing_identity_candidate` はM5候補観測のID欠落、`missing_digit_value` はM7a recognized digits 欠落、`unsupported_preview_status` はM7 preview側でまだ候補外の行として読み、互いに混同しない。
M8 planned play recordsでは、`m8_planned_play_records.json` / Markdown の `planned_record_count` と `excluded_payload_status_counts` を見て、`payload_ready` から保存予定レコードへ進んだ行と、それ以外で止まった行を分けて読む。保存予定レコードは `plays` へinsertする直前のrow contract確認であり、保存OK、DB保存成功、曲ID/譜面ID確定、保存値確定ではない。
M8 score DB write previewでは、`m8_score_db_write_preview.json` / Markdown の `schema_version`、`created_by_preview`、`insert_target_count`、`inserted_count`、`row_count_after_insert`、`excluded_count`、`write_preview_status_counts` を見て、保存予定レコードが新規 in-memory SQLite `plays` へinsertできるかを確認する。これはDB insert境界のdry-runであり、実ファイルDB生成や本番保存成功ではない。`schema_version=1` はpreviewスキーマ契約の識別子、`created_by_preview=tools.vision_poc.m8_score_db_preview` はpreview生成物識別子であり、どちらも保存成功や確定ID/値を意味しない。`skipped_invalid_planned_record` は planned row contractの不足や整数列不正の検出であり、非ready payloadをここで再判定するための語彙ではない。
M8 score DB file output previewでは、`--m8-score-db-output` を明示した実行だけ `m8_score_db_file_output_preview.json` / Markdown を読み、指定された `data/` 配下DBへのinsert件数、`plays` 行数、`schema_version=1`、`created_by_preview` を確認する。明示オプションなしでは生成しない。実ファイルDBの `PRAGMA user_version` も `1` に固定し、`preview_metadata` 表にも同じpreview識別情報を入れる。`database_schema_version`、`database_preview_metadata`、`database_plays_schema_columns` は実DBからの読み戻し診断で、summary定数欄やpreview最小 `plays` 契約と一致するかを見るための補助欄として読む。`database_readback_matches_preview_contract=true` と空の `database_readback_mismatch_reasons` は、readback値がpreview識別契約と一致したことだけを示す。`database_plays_row_count` は実DBの `plays` 行数readbackで、`database_plays_row_count_matches_insert_counts=true` と空の `database_plays_row_count_mismatch_reasons` は、`inserted_count` / `row_count_after_insert` と一致したことだけを示す。`database_plays_insert_columns_match_planned_contract=true`、`database_plays_integer_fields_match_preview_contract=true`、空の `database_plays_schema_mismatch_reasons` は、実ファイルpreview DBの `PRAGMA table_info(plays)` がpreview最小列順とINTEGER列契約に一致したことだけを示す。`inserted_to_file_preview` はpreview DBへのinsert確認であり、本番保存成功ではない。M5なし実行などで planned rows が0件の場合は、空の `plays` スキーマDB、`preview_metadata`、readback欄、一致診断欄、`inserted_count=0` として読み、非ready payloadを保存予定レコードやfile outputへ進めない境界を確認する。

M3曲・譜面情報の期待値列確認では、`m3_metadata_expected_coverage.md` の `total confirmed-events` が保存直前イベント数と一致していること、duplicate、`rejected_transition`、未確定候補、non-result が対象外であることを見る。数字OCR用の `ocr_expected_coverage.md` とは別レポートとして扱う。

M3 chart-field 評価の足場では、`m3_chart_fields.csv` と `m3_chart_fields_summary.json` を見る。`chart_field_target_count` が保存直前イベント数と一致し、`excluded_counts` で duplicate、`rejected_transition`、未確定候補、non-result が対象外に残っていることを確認する。対象fieldは当面 `play_style`、`difficulty`、`level` に限定する。抽出評価へ進む場合は `m3_chart_field_extraction.csv` と `m3_chart_field_extraction_summary.json` を合わせて読み、`filename-baseline` の match をROI/OCR/テンプレート照合の成功扱いにしない。ROI画像特徴の比較は `m3_chart_field_image_feature_extraction.csv`、`m3_chart_field_image_feature_extraction_summary.json`、`m3_chart_field_image_feature_diagnostics.md` を読み、`roi-feature-nearest-centroid` を本格テンプレート照合の成功扱いにしない。ローカルテンプレート素材との比較は `m3_chart_field_template_extraction.csv`、`m3_chart_field_template_extraction_summary.json`、`m3_chart_field_template_diagnostics.md` を読み、`roi-template-nearest` を採用済みテンプレート照合やマスタ照合の成功扱いにしない。参照を `chart_field_templates/` だけに限定した分割確認は `m3_chart_field_template_holdout_extraction.csv`、`m3_chart_field_template_holdout_extraction_summary.json`、`m3_chart_field_template_holdout_diagnostics.md` を読み、`roi-template-holdout` を外部検証に近い診断として扱う。
M3-3の採用候補レビューでは、さらに `m3_chart_field_adoption_candidates_summary.json` と `m3_chart_field_adoption_candidates.md` を読み、`adoption_candidate` と参照不足を分ける。ローカル37テンプレート配置後は `play_style`、`difficulty`、`level` の3項目をPoC上の `adoption_candidate` として読めるが、採用済みテンプレート照合やマスタ照合の成功にはしない。保存前判断へ渡す failure reason は、参照不足を `missing_reference`、期待値不足を `no_expected_value`、抽出空を `empty_extraction`、参照ありの不一致を `low_confidence` として読む。
M3-4の曲名/artist OCR入口では、`m3_song_artist_ocr.csv`、`m3_song_artist_ocr_summary.json`、`m3_song_artist_ocr.md` を読み、confirmed-events 対象だけに OCR が試行されていること、OCRエンジンがない場合は `engine_unavailable` として記録されること、`pre_normalized_text` を本格正規化やマスタ照合の成功扱いにしていないことを確認する。
M3-5の保存候補向け集約では、`m3_save_candidate_summary.csv`、`m3_save_candidate_summary.json`、`m3_save_candidate_summary.md` を読み、confirmed-events 1件につき曲名、artist、`play_style`、`difficulty`、`level` が保存前向け状態へ集約されていることを確認する。この集約はレポート同士の読み分けを助けるためのもので、DB保存、マスタ照合、ファジーマッチ、曲名正規化の成功扱いにはしない。
M3-6の保存候補ブロッカー代表整理では、`m3_save_candidate_blockers_summary.json` と `m3_save_candidate_blockers_summary.md` を読み、M3-5集約内の未ready項目からfield別・理由別の代表 `organized_file` と `roi_path` を確認する。これは保存前に止まる理由のレビュー補助であり、保存可否判定そのものではない。

## M0/M1で固定すること

- 保存境界は `confirmed_result=true` かつ `duplicate=false`。
- `transition_countup_*` は保存対象外。
- manifest / dry-run / 将来キャプチャは `confirmation_mode=time`。
- dry-run sequence scenario でも short result、sustained result、duplicate、`transition_countup_*` を同じ保存境界で確認する。
- duplicate key の本格差し替えは別フェーズ。
- DB保存はこの境界が固まってから実装する。
