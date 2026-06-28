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

出力先には `results.csv`、`summary.json`、`misclassifications.md`、`rois/<画像名>/` 配下の主要ROI画像が生成されます。`data/` はGit管理対象外です。

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
