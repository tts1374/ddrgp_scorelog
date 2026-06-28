# Windows常駐アプリ

DanceDanceRevolution GRAND PRIX のウィンドウを直接キャプチャし、リザルト画面からスコア情報を抽出してローカルDBに保存するアプリをここに実装する。

## Planned Stack

- C# / .NET 10 LTS
- WPF
- Windows Graphics Capture API
- OpenCvSharp
- Tesseract OCR
- SQLite

## Planned Behavior

- DDR GPウィンドウを検出する。
- リザルト画面を自動検出する。
- 固定ROI + 解像度正規化で読み取り対象領域を切り出す。
- OCR/画像処理でスコア、判定数、曲名、SP/DP、難易度を取得する。
- マスタDBと照合し、一意に特定できる場合のみ保存する。
- 低確信度の場合はDB保存せず、画像とログを残す。

## Local Notes

現環境では .NET SDK が未導入のため、初期セットアップではプロジェクト生成までは行っていない。
SDK導入後に `net10.0-windows` のWPFプロジェクトを作成する。

