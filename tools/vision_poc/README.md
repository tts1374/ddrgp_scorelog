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

出力先には `results.csv`、`result_events.csv`、`result_events_summary.json`、`summary.json`、`misclassifications.md`、`rois/<画像名>/` 配下の主要ROI画像が生成されます。`data/` はGit管理対象外です。

この既定実行は metadata 評価モードです。`samples/screenshots/metadata.csv` の並びをフレーム順として扱いますが、キャプチャ時刻は持たないため、`result_events.csv` の `timestamp_ms` と `candidate_duration_ms` は空欄、`confirmation_mode` は `frames` になります。

入力モードの役割は以下です。

- `metadata`: 分類評価用。`samples/screenshots/metadata.csv` の `organized_file` と `screen_type` を真値として、既存画像の分類精度を確認します。時刻を持たないため、イベント確定は従来どおりフレーム数ベースです。
- `timestamped`: 実キャプチャ前の人工時系列PoC。metadata と同じ画像列へ単調増加する人工 `timestamp_ms` を付け、時刻ベースのリザルト確定を確認します。
- `manifest`: 実フレーム列入力PoC。録画切り出しや将来のキャプチャ処理が供給する `image_path,timestamp_ms` のCSVを読み、実キャプチャに近い入力境界を確認します。

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

生成した manifest はそのまま manifest モードで処理できます。

```powershell
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_sequences/capture_manifest.csv --frame-root data/captured_frames
```

実キャプチャ導入時は、この manifest 生成処理をリアルタイム供給に置き換え、キャプチャAPIが同等の `image_path,timestamp_ms` 列を渡す方針です。FPSが揺れたりフレームが欠落したりしても、時刻ベースなら `result_candidate=true` が実時間で何ミリ秒継続したかを見られるため、固定フレーム数だけに依存するより保存確定が安定します。

導入前の調整では `result_events_summary.json` を見て、`confirmed_count`、`duplicate_count`、`rejected_transition_count`、`first_confirmed_timestamp_ms`、`confirmation_mode_counts` を確認します。実キャプチャ列で確定が早すぎる、遅すぎる、または同一リザルトの重複が多い場合は、この summary と `result_events.csv` の `candidate_duration_ms` / `reason` を見比べて、保存確定しきい値や duplicate window を調整します。

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

`score_ocr.csv` には `roi_name`、`score_ocr_raw`、`score_ocr_normalized`、`expected_score`、`match`、`engine`、`status`、`error`、`original_path`、`enlarged_path`、`binary_path` を出力します。`score_digits` の `expected_score` は metadata の `score` / `expected_score` 列があれば優先し、なければファイル名内の `scoreXXXXXX` から取得します。`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` は metadata に同名列または `expected_<roi_name>` 列がある場合だけ期待値として使います。期待値がないROIは `match` を空欄のままにし、summary の `no_expected_value_count` で集計します。

`score_ocr.csv` は行単位の詳細確認用です。個別画像のOCR生文字列、正規化後の数字、期待値、前処理画像パスを見るときに使います。`score_ocr_summary.json` はROI別・失敗理由別の俯瞰用です。トップレベルには `total_ocr_attempts`、`ok_count`、`engine_unavailable_count`、`match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count`、`skipped_duplicate_count`、`skipped_unconfirmed_count`、`ocr_target_mode` を出力します。

`score_ocr_summary.json` の `by_roi` はROI名ごとの集計です。各ROIに `total_ocr_attempts`、`ok_count`、`engine_unavailable_count`、`match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count` が入り、`--ocr-rois all` でどのROIが弱いかを横並びで確認できます。`by_status` は `ok`、`engine_unavailable`、`ocr_failed` などOCR実行ステータスの件数、`failure_reasons` は `engine_unavailable`、`ocr_failed`、`empty_ocr`、`mismatch`、`no_expected_value` の観点別件数です。

`ocr_roi_report.md` は `score_ocr_summary.json` と `score_ocr.csv` を人間が読みやすい形に並べたROI別弱点レポートです。ROIごとに `total_ocr_attempts`、`match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count`、`engine_unavailable_count` を表で確認し、代表的な `mismatch` / `empty_ocr` の `organized_file` を見て、対応する `ocr/<画像名>/<roi>_*.png` を目視します。`--ocr-target confirmed-events` を指定した場合は、未確定候補やduplicateを除いた対象イベントだけでレポートされます。

OCRエンジンはPATH上の `tesseract` を使います。未導入または利用不可の場合でもPoCは落ちず、前処理画像を保存したうえで `score_ocr.csv` に `engine=none`、`status=engine_unavailable` を残します。

実キャプチャ導入時は、まず `confirmed-events` と `--ocr-rois all` を組み合わせて、保存直前OCR相当の精度と失敗理由をROI別に見ます。ここで `skipped_duplicate_count` が増えすぎる、`skipped_unconfirmed_count` に保存したいフレームが混ざる、特定ROIの `empty_ocr_count` / `mismatch_count` が多い、または `no_expected_value_count` が多く評価できない場合は、キャプチャAPIやDB保存を作る前にイベント確定しきい値、duplicate window、metadata真値列、ROI前処理を調整します。

### OCR profile比較

実キャプチャ導入前の前処理調整PoCとして、`--ocr-profile` で複数の軽量profileを比較できます。これはローカル画像に対するROI別弱点分析用であり、本番キャプチャAPI、常駐監視ループ、非同期処理、DB保存ではありません。

```powershell
python -m tools.vision_poc --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

利用できるprofileは以下です。

- `default`: 既定の前処理。従来の `score_ocr.csv` は常にこの設定で出力します。
- `high-contrast`: 明るい白文字に寄せ、`luma_threshold` を上げて `channel_spread` を狭めます。
- `low-threshold`: 暗めの文字に寄せ、`luma_threshold` を下げて `channel_spread` を広げます。

profile比較を有効にすると、追加で以下を出力します。

- `data/vision_poc/score_ocr_profiles.csv`
- `data/vision_poc/score_ocr_profiles_summary.json`
- `data/vision_poc/ocr_profiles/<profile>/<画像名>/<roi>_*.png`

`score_ocr_profiles_summary.json` は `profiles -> profile -> roi -> counts` の形で、profile別・ROI別の `match_count`、`mismatch_count`、`empty_ocr_count`、`no_expected_value_count` などを集計します。`best_by_roi` にはROIごとに `match_count` が最も多いprofileと、`empty_ocr_count` が最も少ないprofileを出します。期待値がないROIは成功/失敗にせず、`no_expected_value_count` として扱います。

まずは以下のように confirmed-events と全OCR ROI、profile比較を組み合わせ、保存直前OCR相当で弱いROIを特定します。

```powershell
python -m tools.vision_poc --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode timestamped --ocr-target confirmed-events --ocr-rois all --ocr-profile all
python -m tools.vision_poc --sequence-mode manifest --frame-manifest data/vision_poc_timestamped/frame_manifest.csv --frame-root samples/screenshots --ocr-target confirmed-events --ocr-rois all --ocr-profile all
```

この段階では `empty_ocr_count` が多いROIは二値化条件やROI位置、`mismatch_count` が多いROIは余白・拡大率・Tesseract設定、`no_expected_value_count` が多いROIは metadata の真値列不足を疑います。調整後も既存互換の `score_ocr.csv` は `profile` 列を追加せず、比較結果は別ファイルで確認します。

OCR前処理を省略したい場合は以下を使います。

```powershell
python -m tools.vision_poc --no-ocr
```

`score_digits` 以外の判定数ROIも同じ前処理APIを使えます。現時点ではOCR精度調整前の足場として、まずは `max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss` の `*_original.png` / `*_enlarged.png` / `*_binary.png` を確認できる状態にしています。

```powershell
python -m tools.vision_poc --ocr-rois all
python -m tools.vision_poc --ocr-rois score_digits max_combo marvelous perfect great good miss
```

既存確認用の `score_digits_original.png`、`score_digits_enlarged.png`、`score_digits_binary.png` のファイル名は維持しています。

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
- 不正fps、対象画像なし、生成 manifest の読み込み互換性を確認する
- `score` / `expected_score` / `organized_file` から `expected_score` を抽出できる
- 判定数ROIは metadata の同名列または `expected_<roi_name>` 列がある場合だけ期待値として評価できる
- `score_ocr_summary.json` でROI別、ステータス別、失敗理由別のOCR集計を確認できる
- `ocr_roi_report.md` でROI別の弱点と代表的な `mismatch` / `empty_ocr` を確認できる
- `--ocr-profile all` で既存 `score_ocr.csv` の列を維持したまま、profile別summaryを出力できる
- `score_ocr_raw` から数字だけを `score_ocr_normalized` にできる
- ローカル素材がある環境では `score_digits` の前処理画像を生成できる

`samples/screenshots/metadata.csv` や画像がない環境では、ローカル素材が必要なテストだけ skip します。

## 判定方針

1280x720基準の固定ROIを実画像サイズへ線形スケールし、以下の特徴だけで分類します。

- `RESULTS` ヘッダー周辺の白文字量、エッジ量、明度分散
- 詳細リザルト枠のシアン外枠、明度、エッジ量
- スコア周辺の白い大数字とエッジ量
- ランク周辺の黄色/灰色大文字らしさ

`result_shape_candidate` はリザルト形状の検出、`result_candidate` は保存処理に進める候補です。`transition_countup_*` は形状検出できても保存不可として扱えるよう、`transition_kind=countup` としてログ上で区別します。

### リザルト確定

実キャプチャでは一瞬の誤検出や遷移中フレームが混ざるため、単発の `result_candidate=true` だけでは保存確定しません。PoCでは metadata の並びをフレーム順として扱い、`result_candidate=true` が `CONFIRMED_RESULT_MIN_FRAMES=2` フレーム継続した時点で `confirmed_result=true` にします。

`build_result_events()` は任意で `timestamps_ms` を受け取れます。timestamp がある入力では、フレーム数ではなく `result_candidate=true` が `CONFIRMED_RESULT_MIN_DURATION_MS=1000` ミリ秒以上継続した時点で `confirmed_result=true` にします。metadata には時刻情報がないため、現在の `python -m tools.vision_poc` の出力は従来どおりフレームベースです。

この値は、まずローカル素材や人工シーケンスで「単発誤検出を保存しない」ことを確認するための最小しきい値です。実キャプチャ導入時はフレーム番号ではなく、キャプチャした時点の単調増加時刻を `timestamp_ms` として渡します。FPS固定ではなくても、時刻ベースなら `result_candidate=true` が実時間でどれだけ続いたかを見られるため、FPS揺れやフレーム欠落があっても保存確定が安定します。たとえばフレーム数が少なくても1000ms以上継続していれば確定し、逆にフレーム数が多くても短時間に偏っていれば確定しません。

`transition_countup_*` は `result_shape_candidate=true` でも `transition_kind=countup` として扱い、`event_type=rejected_transition`、`confirmed_result=false` のままにします。

### 重複保存防止

同じリザルト画面を連続検出しても保存候補イベントは1回だけ扱えるよう、PoCでは `duplicate_key` ごとに直近の確定フレームを記録します。同一キーが `DUPLICATE_WINDOW_FRAMES=90` フレーム以内に再度確定した場合は `event_type=duplicate`、`duplicate=true` にします。

timestamp がある入力では `DUPLICATE_WINDOW_MS=90000` ミリ秒以内の同一キーを重複として扱います。判定理由には時刻ベースなら `duplicate_within_ms=<差分>`、フレームベースなら `duplicate_within_frames=<差分>` を残します。timestamp がない metadata 順実行では、互換性のため引き続きフレーム窓を使います。

現在の `duplicate_key` は、ファイル名に `scoreXXXXXX` があれば `score:<数字>`、なければ `file:<ファイル名>` です。これはローカル評価向けの簡易キーで、実キャプチャでは perceptual hash、`score+曲名+難易度`、またはDB保存前の正規化済みリザルトIDに置き換える想定です。置き換え箇所は `duplicate_key_for_classification()` です。

### result_events.csv

`data/vision_poc/result_events.csv` は分類結果を時系列イベントとして見直すためのCSVです。列は以下です。

- `frame_index`: metadata順または入力順の0始まりフレーム番号
- `organized_file`: 入力画像の相対パス
- `screen_type`: metadata上の画面種別
- `result_candidate`: 単発フレーム分類で保存候補か
- `result_shape_candidate`: リザルト形状らしさがあるか
- `confirmed_result`: 継続条件を満たして保存確定したか
- `event_type`: `none` / `confirmed` / `duplicate` / `rejected_transition`
- `duplicate`: 重複候補か
- `duplicate_key`: 重複判定キー
- `reason`: 判定に使った分類理由、継続フレーム数、重複距離など
- `timestamp_ms`: 入力時刻。metadata 順実行では時刻情報がないため空欄
- `candidate_duration_ms`: timestamp 付き入力で `result_candidate=true` が継続している時間。フレームベースでは空欄
- `confirmation_mode`: `frames` / `time`

### result_events_summary.json

`data/vision_poc/result_events_summary.json` は `result_events.csv` のイベント評価だけを集計したJSONです。分類精度向けの `summary.json` とは分けて、時系列の保存確定挙動を確認します。

- `confirmed_count`: 重複ではない保存確定イベント数
- `confirmed_result_count`: `duplicate` を含む `confirmed_result=true` のフレーム数
- `duplicate_count`: duplicate window 内の同一キー再確定数
- `rejected_transition_count`: `transition_countup_*` など保存不可遷移として除外した数
- `first_confirmed_frame_index`: 最初に保存確定した入力順インデックス
- `first_confirmed_timestamp_ms`: timestamp 付き入力で最初に保存確定した時刻。metadata モードでは `null`
- `confirmation_mode_counts`: `frames` / `time` ごとのイベント行数
