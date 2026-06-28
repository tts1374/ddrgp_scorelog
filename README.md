# ddrgp_scorelog

DanceDanceRevolution GRAND PRIX のリザルト画面からスコア情報を取得し、ローカルDBへ保存するためのプロジェクトです。

## Status

初期PoC段階です。現時点では要求定義、スクリーンショット収集方針、マスタ取得機能とWindows常駐アプリの置き場に加え、リザルト候補分類とOCR前処理のPoCを用意しています。

## Goals

- BEMANIWiki の全曲リストから楽曲・譜面マスタDBを生成する。
- Windows上で DDR GRAND PRIX のリザルト画面を直接キャプチャする。
- OCR/画像解析で曲名、譜面、スコア、ランク、判定数を抽出する。
- 個人スコアDBへ1プレー1レコードで自動保存する。
- 低確信度の解析結果は保存せず、画像とログを残す。

## Repository Layout

```text
.
├─ app/                         # Windows常駐アプリ
├─ docs/                        # 要求定義・設計メモ
├─ master/                      # BEMANIWikiマスタDB生成
├─ samples/                     # スクショ収集ルールとメタデータ例
├─ tests/                       # 画像分類/OCR前処理PoCのテスト
├─ tools/vision_poc/            # 画面分類、ROI切り出し、OCR前処理PoC
└─ pyproject.toml               # Python側の依存・開発ツール定義
```

## Current Documents

- [要求定義](docs/requirements.md)
- [スクリーンショット収集マトリクス](docs/screenshot-collection.md)
- [画面解析PoC準備メモ](docs/vision-poc-prep.md)
- [画面解析PoCツール](tools/vision_poc/README.md)
- [マスタ取得機能メモ](master/README.md)
- [Windowsアプリ機能メモ](app/README.md)

## Next Step

まずは `docs/screenshot-collection.md` に沿って、リザルト画面・選曲画面・プレー中画面のスクリーンショットを収集します。

その後、画面解析PoCとして以下を確認します。

- リザルト画面と非リザルト画面を分類できるか。
- スコア・判定数の数字領域を固定ROIで読み取れるか。
- 曲名・SP/DP・難易度をマスタDBと照合できるか。
- 低確信度の結果を安全に破棄できるか。
