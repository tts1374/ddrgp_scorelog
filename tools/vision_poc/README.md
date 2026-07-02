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

出力先には `results.csv`、`result_events.csv`、`result_events_summary.json`、`summary.json`、`misclassifications.md`、`m3_metadata_expected_coverage.md`、`m3_metadata_expected_template.csv`、`rois/<画像名>/` 配下の主要ROI画像が生成されます。`data/` はGit管理対象外です。`rois/<画像名>/` には分類確認用ROIに加えて、M3入口の目視確認用として `play_style`、`difficulty`、`level`、`rank`、`song_title`、`artist` も出力します。この段階では切り出し足場だけで、本格OCR、テンプレート照合、マスタ照合には進みません。

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

現在のローカル metadata では、M3 metadata expected coverage の confirmed-events 対象は60件です。`song_title` / `artist` / `play_style` / `difficulty` / `level` は60件すべてが埋まっており、M3入口ではこの5項目を優先して評価します。`rank` / `expected_rank` は12件だけが埋まっているため、残り48件の不足は数字OCR expected coverage の不足とは別に読み、当面は補助ROIの部分評価として扱います。ランクOCR、ランクテンプレート照合、本格採用判断へ進む場合は、別途M3の評価列とレポートとして定義します。

`score_digits` 以外の判定数ROIと `ex_score` も同じ前処理APIを使えます。現時点ではOCR精度調整前の足場として、まずは `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` の `*_original.png` / `*_enlarged.png` / `*_binary.png` を確認できる状態にしています。

```powershell
python -m tools.vision_poc --ocr-rois all
python -m tools.vision_poc --ocr-rois score_digits max_combo marvelous perfect great good miss ex_score
```

既存確認用の `score_digits_original.png`、`score_digits_enlarged.png`、`score_digits_binary.png` のファイル名は維持しています。

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
- ローカル素材がある環境では `score_digits` の前処理画像を生成できる
- 曲・譜面情報の目視確認用ROIとして `play_style`、`difficulty`、`level`、`rank`、`song_title`、`artist` を `rois/` に生成できる

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
