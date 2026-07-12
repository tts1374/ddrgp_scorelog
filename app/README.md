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

## First Viewer Slice

最初のWPF実装は、自動キャプチャや常駐監視より先に、正式個人スコアDB version 1をread-onlyで閲覧する最小スコアビューアとする。

- 明示選択したcompatibleなv1 DBと生成済みマスタDBをread-onlyで開く。
- 全プレー履歴を、マスタDB由来の曲名、SP/DP、難易度、レベルとともに新しい順で表示する。
- 選択プレーのスコア、ランク、クリア種別、判定数、MAX COMBO、EX SCOREを表示する。
- 譜面別自己ベストは保存済み全履歴からqueryで算出する。
- DB初期化、保存、migration、backup、repairは実行しない。
- マスタDB更新、検索、グラフ、常駐、実キャプチャは後続へ分ける。

## Local Notes

現環境には.NET runtimeだけがあり、.NET SDKは未導入である。次PRの開始前に.NET 10 SDKを導入し、`net10.0-windows` のWPFプロジェクトを作成する。
