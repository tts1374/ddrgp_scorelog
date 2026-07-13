# DDR GP Score Tracker WPF app

正式個人スコアDB version 1を読み取り専用で開き、保存済みプレー履歴、プレー詳細、譜面別自己ベストを確認する最小WPFビューアです。明示選択したversion 1 workflow入力JSONを既存Python workflowで1回だけ保存するmanual入口に加え、明示pickerで選んだwindowから1フレームだけ取得する入口を持ちます。連続キャプチャ、自動解析・保存、常駐監視、migration、backup、repairはまだ接続しません。

## 必要環境

- Windows 11
- .NET 10 SDK
- Python 3（`python`、または `DDRGP_PYTHON` 環境変数で指定）
- 正式個人スコアDB version 1（例: `ddrgp-scores.sqlite`）
- `python -m master` またはmaster DB生成workflowで作られたマスタDB

ローカルDBはGit管理しません。`data/` 配下など、既存のローカル保存場所に置いたまま明示選択してください。

## 1フレーム取得

1. アプリ右上の `1フレーム取得` を押す。
2. Windowsのpickerで取得対象のwindowを明示選択する。
3. 完了表示に出た `data/windows_capture/capture-*/` を確認する。

各capture directoryには `frame.png`、`frame_manifest.csv`、`capture_metadata.json` をまとめて出力します。capture操作時にcurrent directoryとapp配置場所からrepository rootを探索し、process cwdに関係なくrepository rootの `data/windows_capture/` へ限定します。repository rootを見つけられない場合はwrite失敗として表示し、通常viewer起動は妨げません。manifestの必須列は既存契約と同じ `image_path,timestamp_ms` で、`screen_type=unknown`、capture source、幅、高さ、UTC取得時刻を任意列として付けます。画像pathはmanifest directory相対です。staging directoryで3ファイルを書いた後にdirectory単位で公開するため、cancel、対象終了、0x0/resize、device lost、access拒否、write失敗では空画像や部分manifestを最終出力へ残しません。既存capture directoryは上書きしません。

pickerとWindows Graphics Captureは明示操作時だけ起動します。取得後に分類、OCR、identity解決、workflow、正式DB保存、viewer再読込を自動実行しません。同じprocessで再度ボタンを押すと、resourceを作り直して別の1フレームを取得します。

生成した1行manifestは、manifest directoryを基準に `frame.png` を解決してそのまま再実行できます。

```powershell
python -m tools.vision_poc `
  --sequence-mode manifest `
  --frame-manifest data\windows_capture\capture-<id>\frame_manifest.csv `
  --output data\windows_capture_replay
```

単発manifestは `confirmation_mode=time` ですが、1フレームだけではconfirmed resultになりません。実captureのconfirmed-events評価では、同じresultを1秒以上空けて複数回取得し、`data/` 配下のローカル評価manifestへ時刻順にまとめます。`screen_type` と期待値列は評価用manifest側で補い、capture原本のmanifest、画像、metadataは変更しません。

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

## 単発保存

1. `単発保存` を押す。
2. `workflow_schema_version=1` の既存strict workflow入力JSONを選ぶ。
3. 新規、0 byte、またはcompatibleな正式v1 DBを保存先として選ぶ。
4. 保存後の表示に使う生成済みマスタDBを選ぶ。

アプリは `python -m tools.vision_poc.personal_score_db_workflow_app` をリポジトリrootで1回だけ実行します。この薄いprocess adapterは入力内の既存 `log_path` をartifact出力先として渡すだけで、strict loader、save adapter、artifact orchestration、file saveをC#で再実装しません。repository root探索は `単発保存` 実行時まで遅延し、current directoryまたはapp配置場所の親から検出できない場合は保存だけを失敗状態にします。read-only viewerの起動と `データを選択` はPythonやrepository配置を必要としません。

`saved` かつtransaction完了済みの `play_id` が返った場合だけ、同じ `ScoreViewerRepository` でDBをread-only再読込し、履歴・詳細・自己ベストへ反映します。`excluded` / `duplicate` はsource captureとanalysisが記録されても成功playとして表示せず、`unresolved` / `invalid` / DB拒否 / artifact失敗は理由を表示します。`artifact_created_db_failed` はartifactが残ったpartial successとして表示し、DB保存成功へ丸めません。

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

今回の画面範囲は共通sidebar、自己ベスト、プレー履歴、プレー詳細、明示単発保存、明示1フレーム取得です。ホーム、検索・絞り込み、グラフ、要確認、設定、データ管理、自動記録状態、連続capture、常駐機能は後続PRへ分けます。
