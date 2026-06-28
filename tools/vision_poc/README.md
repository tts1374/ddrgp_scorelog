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

出力先には `results.csv`、`result_events.csv`、`summary.json`、`misclassifications.md`、`rois/<画像名>/` 配下の主要ROI画像が生成されます。`data/` はGit管理対象外です。

OCR前処理も既定で実行されます。対象は `result_candidate=true` の画像だけで、既定では `score_digits` ROIから以下を出力します。

- `data/vision_poc/ocr/<画像名>/score_digits_original.png`
- `data/vision_poc/ocr/<画像名>/score_digits_enlarged.png`
- `data/vision_poc/ocr/<画像名>/score_digits_binary.png`
- `data/vision_poc/score_ocr.csv`

`score_ocr.csv` には `roi_name`、`score_ocr_raw`、`score_ocr_normalized`、`expected_score`、`match`、`engine`、`status`、`error`、`original_path`、`enlarged_path`、`binary_path` を出力します。`expected_score` は metadata の `score` / `expected_score` 列があれば優先し、なければファイル名内の `scoreXXXXXX` から取得します。

OCRエンジンはPATH上の `tesseract` を使います。未導入または利用不可の場合でもPoCは落ちず、前処理画像を保存したうえで `score_ocr.csv` に `engine=none`、`status=engine_unavailable` を残します。

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

この設定では、現ローカル素材の `score_digits` は `score_ocr.csv` 上で16件すべて `match=true` です。判定数ROIも同じ前処理とTesseract設定で実行できますが、真値列がまだないため、現段階では画像と `score_ocr_raw` の目視確認を次の精度確認に使います。

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
- `score` / `expected_score` / `organized_file` から `expected_score` を抽出できる
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

この値は、まずローカル素材や人工シーケンスで「単発誤検出を保存しない」ことを確認するための最小しきい値です。実キャプチャ導入時はキャプチャ時刻を `timestamp_ms` として渡し、FPS固定ではなくても安定する時刻ベース判定を優先します。FPSが揺れる、またはフレーム欠落が起きる環境では、フレーム数より継続ミリ秒を保存確定の基準にします。

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
