# DDR GP Score Tracker WPF app

正式個人スコアDB version 1を読み取り専用で開き、保存済みプレー履歴、プレー詳細、譜面別自己ベストを確認する最小WPFビューアです。自動キャプチャ、保存、migration、backup、repairはまだ接続しません。

## 必要環境

- Windows 11
- .NET 10 SDK
- 正式個人スコアDB version 1（例: `ddrgp-scores.sqlite`）
- `python -m master` またはmaster DB生成workflowで作られたマスタDB

ローカルDBはGit管理しません。`data/` 配下など、既存のローカル保存場所に置いたまま明示選択してください。

## Build / test / run

```powershell
dotnet restore app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --no-restore
dotnet test app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --no-restore
dotnet run --project app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --no-build
```

## 利用手順

1. アプリ右上の `データを選択` を押す。
2. 正式個人スコアDB version 1を選ぶ。
3. 生成済みマスタDBを選ぶ。
4. `自己ベスト` または `プレー履歴` を開く。
5. プレー履歴の行を選び、判定数、MAX COMBO、EX SCORE、保存日時、データ取得元を確認する。

個人DBとマスタDBは別々のSQLite read-only connectionで開きます。viewerはschema初期化、insert、update、migration、backup、repairを実行せず、connection poolingも使いません。

## 表示契約

- 履歴と最終プレー日時は `plays.played_at` のtimezone offsetを考慮した時系列順で表示する。
- timezone付き時刻は端末のローカル時刻へ変換し、SQLite `CURRENT_TIMESTAMP` 由来のoffsetなし `created_at` はUTCとして変換する。
- 曲名、SP/DP、難易度、レベルは `chart_id` と `song_id` が一致するマスタ行から表示する。
- マスタ参照が欠ける行は捨てず、`song_id` / `chart_id` と `参照情報なし` を表示する。
- 譜面別自己ベストは `plays` 全履歴を `song_id` / `chart_id` ごとに集計し、通常スコアとEX SCOREをそれぞれ `MAX` で算出する。
- v1に列がない `O.K.` は値を補完せず `—` と表示する。
- 空履歴では、次の行動を示す空状態を表示する。

## DB検査と拒否

個人DBは次を検査します。

- `PRAGMA user_version = 1`
- 正式 `score_db_metadata` identity
- v1必須tableと列順
- `001_initial_personal_score_db_schema` とversionの一致
- M8 preview DBでないこと

マスタDBは必須table、必須metadata、曲・譜面件数、source snapshotのURL/hash整合を検査します。preview、unknown、identity mismatch、newer unsupported、partial state、非SQLite、読取失敗は変更せず拒否し、ユーザー向けの理由を表示します。

## UI resources

- `Resources/Theme.xaml`: light themeの色トークンと難易度色
- `Resources/Components.xaml`: button、sidebar、card、table、badgeの共通style
- `Controls/StatePanel.xaml`: 空状態・エラー状態の共通component

今回の画面範囲は共通sidebar、自己ベスト、プレー履歴、プレー詳細です。ホーム、検索・絞り込み、グラフ、要確認、設定、データ管理、自動記録状態、常駐機能は後続PRへ分けます。
