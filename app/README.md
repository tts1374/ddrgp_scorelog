# DDR GP Score Tracker WPF app

正式個人スコアDB version 1を読み取り専用で開き、保存済みプレー履歴、プレー詳細、譜面別自己ベストを確認するWPFビューアです。明示選択したversion 1 workflow入力JSONを既存Python workflowで1回だけ保存するmanual入口に加え、明示pickerで選んだwindowから1フレームまたは停止までの連続フレームを取得できます。`監視開始` を明示した場合だけ、完成したsession manifestを既存解析pipelineと正式保存workflowへ接続します。監視状態と最新結果はWPFとtask trayから確認できます。正式個人スコアDB、M4 master DB、M5b jacket reference catalogは環境ごとの固定pathで扱い、次回起動時に3つともread-only検証して再利用します。DBの任意path選択、自動window探索、自動再接続、migration、backup、repairは接続しません。

## 必要環境

- Windows 11
- .NET 10 SDK
- Python 3（`python`、または `DDRGP_PYTHON` 環境変数で指定）
- uv（Python依存のlock固定環境を構築する場合）
- 正式個人スコアDB version 1（例: `ddrgp-scores.sqlite`）
- `python -m master` またはmaster DB生成workflowで作られたM4 master DB
- current schema version 1のM5b jacket reference catalog（`jacket-catalog.sqlite`）

ローカルDBはGit管理しません。developmentでは`databases/`、productionでは`%LOCALAPPDATA%\DDRGpScoreViewer\data\`配下のIssue固定pathへ配置してください。DBのファイル選択はアプリから行いません。

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

## 連続フレーム取得

1. アプリ右上の `連続取得を開始` を押す。
2. Windowsのpickerで対象windowを明示選択する。
3. 必要な区間を取得したら `監視停止` を押す。
4. 完了表示に出た `data/windows_capture/session-*/` を確認する。

session directoryには `frames/frame-*.png`、`frame_manifest.csv`、`capture_session_metadata.json` を出力します。manifestの各行はdirectory相対pathとstrictly increasingな単調時刻ミリ秒を持ち、capture補助列も単発と同じです。明示停止かつ1フレーム以上取得済みの場合だけ、`data/` 直下のstagingからdirectory renameで公開します。停止前のframeは完成出力に見せず、0フレーム、picker cancel、access拒否、対象終了、resize、device lost、write失敗ではstagingごと破棄します。

session中は最初に選択したcapture itemとD3D11 device、frame pool、capture sessionを維持します。resizeには自動追従せず安全側でsessionを停止するため、windowを目的のサイズに戻してから再選択してください。開始済みの二重開始と停止中の再操作は無視し、明示停止とwindow close時の停止は冪等にresourceを解放します。取得frameがPNG encodingより速い場合は、resourceを無制限に保持しないため満杯のframe queueで中間frameをdropします。

生成manifestはそのまま既存manifest modeへ渡せます。`連続取得を開始` は従来どおりcapture bundle生成だけで、分類・OCR・identity・confirmed event・正式save input・DB保存を起動しません。

```powershell
python -m tools.vision_poc `
  --sequence-mode manifest `
  --frame-manifest data\windows_capture\session-<id>\frame_manifest.csv `
  --output data\windows_capture_session_replay
```

## 監視と正式保存workflow

1. WPFまたはtask trayの `監視開始` を押す。
2. 起動時に現在の環境（repository rootを検出したdevelopment、またはLocalAppDataのproduction）の固定pathを使う。DBの任意pathへの切替操作はありません。
3. 2種類のmaster DBの固定path、read-only読込可否、schema互換性が `compatible` であることを確認してから、pickerで対象windowを選ぶ。
4. 必要区間の後にWPFまたはtrayの `監視停止` を押し、監視surfaceで状態、対象window、frame数、開始・最新event時刻、event status別の保存結果を確認する。

capture成功時だけ `python -m tools.vision_poc.capture_save_workflow_app` を起動します。完成manifestを取得順・`timestamp_ms` 順のまま既存manifest modeへ渡し、M5 jacket候補観測とM7a全数字ROIを生成します。`confirmed_result=true` かつ `duplicate=false` だけを通常の昇格候補とし、eventを直列に処理します。capture失敗、0 frame、resize、target close、device lost、write失敗では解析processを起動しません。

自動formal昇格はfieldごとの採用済み根拠sourceとconfidence、全必須値の完全性をpure adapterで検査します。M5 `identity_signal_*`、M7a `recognized_digits`、expected値、M8 preview payload、相対 `timestamp_ms` は候補材料のままです。現行pipelineにはrank/clear typeを含む全必須項目の採用済み根拠がまだないため、実captureで根拠が欠けるeventは `unresolved` となりplayを作りません。これはcandidateを正式値へ暗黙昇格しないための意図した停止です。manualのreviewed workflow入力は従来の `単発保存` に残り、自動由来と混同しません。

各confirmed eventは既存正式workflowを1回だけ呼びます。DB内duplicate、policy excluded、unresolved、invalid、artifact failure、DB拒否をstatusのまま集計し、`invalid`、artifact failure、DB拒否などが1件でもあればsessionを `workflow_failed` として非0終了します。同じsessionにtransaction済みの `saved` playがある場合はそれだけread-only再読込し、部分成功件数と失敗理由を同時に表示します。解析出力は `data/capture_save_workflow/`、画像原本は `data/windows_capture/`、正式DBは明示pathに分離します。

`IsSaving` はmanual単発保存と監視capture-save全体の共通排他です。DB path変更操作はなく、監視中はmanual保存を開始しません。capture開始からworkflow完了まで状態を保持し、同じ正式DBへの並行writerとsave statusの競合を防ぎます。capture-only入口も監視開始と同じoperation gateへ入り、開始要求を二重実行しません。session世代が古いprogress callback、停止後のcallback、終了後の新しい解析・保存は受け付けません。

監視状態は `idle`、`selecting_target`、`monitoring`、`stopping`、`stopped`、`target_closed`、`resized`、`device_lost`、`capture_failed`、`workflow_failed` を区別します。window titleは選択済み対象の表示だけに使い、自動探索には使いません。最新結果は `saved`、`duplicate`、`excluded`、`unresolved`、`analysis_failed`、`db_rejected`、`workflow_failed` を別々に数え、transaction済みのsaved playだけread-only再読込します。

通常のwindow closeと最小化はwindowを隠すだけで、監視とworkflowはtrayから確認・停止できます。tray menuは監視開始、監視停止、メインwindow表示、アプリ終了を提供します。アプリ終了だけがpending pickerのcancel、監視停止、in-flight workflowの完了またはcancelを待ち、tray iconとcontext menuをdisposeしてprocessを終了します。終了後のViewModel callbackはtrayへ反映しません。通知はsavedがある完了と、監視停止が必要な重大失敗だけです。duplicate、excluded、unresolvedの連続通知は行いません。

## 再起動・path再検証・失敗からの復帰

- 正式個人スコアDB、M4 master DB、M5b jacket reference catalogの固定pathとdev/prod環境タグだけを `%LOCALAPPDATA%\DDRGpScoreViewer\viewer-paths.json` に保存します。この設定はGit管理外で、候補値、解析結果、保存statusは持ちません。旧形式、任意path、別環境のpathは暗黙復元せず、現在の既定pathだけを使用します。
- 起動時、解析・正式保存開始直前に、M4 master DBとM5b jacket reference catalogを別々のread-only connectionで検査します。M4は必須table、metadata、曲・譜面件数、source snapshotのURL/hash整合を確認し、M5bはtable identity、column、metadata identity、schema version、unique index、foreign keyを確認します。両方とも `missing`、`read不可`、`schema incompatible`、`compatible` を区別します。
- どちらか一方がmissing / read不可 / incompatibleなら、理由を表示して対象windowの解析と正式保存workflowを開始しません。capture後にも同じ2ファイルを再検証します。networkからの最新版確認やhashの継続監視は行いません。
- `target_closed`、`resized`、`device_lost`、`capture_failed`、`workflow_failed` は監視状態として残ります。停止完了後に対象windowを再選択し、必要なmaster DBは現在の環境の固定pathへ用意してから `監視開始` を再実行してください。window終了、resize、capture失敗で古いsessionを再利用しません。
- saved、duplicate、excluded、unresolved、解析失敗、DB拒否、workflow失敗はprocess内の表示と既存workflowのartifact/logで追跡します。再起動時に保存されるのはtransaction完了した正式playだけで、過去のskip・拒否・失敗statusをsavedへ昇格するcheckpointはありません。

## M10-2 既定保存先と責務境界

実行環境は、現在のdirectoryまたはapp配置場所の親から既存のrepository rootを解決できる場合をdevelopment、それ以外をproductionとします。developmentとproductionのpathを相互にfallbackしません。

| 対象 | development | production |
| --- | --- | --- |
| M4 master DB | `databases/ddrgp-master.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\ddrgp-master.sqlite` |
| M5b jacket reference catalog | `databases/jacket-catalog.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\jacket-catalog.sqlite` |
| 正式個人スコアDB | `databases/score.dev.db` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\score\score.db` |
| 評価用DB | `databases/evaluation.db`（M10-3専用） | 既定pathなし |

M4 master DBとM5b jacket reference catalogは、同じdirectoryに置かれていても別ファイル・別責務です。master生成、catalog収集、catalog更新、最新版照合はこのアプリの責務ではありません。正式個人スコアDBは既存DBをアプリ更新、master DB操作、評価用DB初期化で上書き・初期化・migrationしません。固定score pathがmissingまたは0 byteの場合だけ、master 2種類の検証後に既存の正式DB準備境界を使って空の正式schemaを作成します。

初回起動では親directory（`databases/`、またはproductionの`data/master/`・`data/score/`）と`data/`・`logs/`を作成し、master 2種類がcompatibleなら固定score pathのmissing／0 byteだけを初期化します。既存の非空score DBはread-only検証だけを行い、unknown、preview、identity mismatch、manual migration候補、非SQLite、directoryは変更せず拒否します。captureはdevelopmentでは`data/windows_capture/`、productionでは`%LOCALAPPDATA%\DDRGpScoreViewer\data\windows_capture/`へ出し、解析artifactは`data/capture_save_workflow/`、失敗画像と診断ログは`logs/`配下へ分離します。これらは再生成・退避可能なlocal dataで、Git管理しません。

M10-3の評価用DBはdevelopmentでだけ使います。WPF viewerは評価用DBを開かず、正式個人スコアDBとの相互初期化も行いません。評価をやり直す場合は、評価processとWPFを停止し、既存の`databases/evaluation.db`を`data/evaluation/backups/evaluation-<UTC timestamp>.db`へ新規コピーしてから、M10-3評価器の明示initializerで同じpathを初期化します。backupの存在確認・SQLite integrity check・path確認後に評価を再実行し、既存backupを上書き・削除しません。M10-3のschema/initializerが未実装の環境では、DBを手作業で作り替えず、評価器の初期化を未実施として扱います。

## M9-6 validation record

2026-07-27 JSTに次を確認しました。

- 自動検証: `.NET build`、`.NET test` 106件、capture-save / personal-score workflow Python test 45件、Ruff、`compileall` はすべて成功。
- Windows smoke: WPF起動、固定pathのmaster DB未配置による `missing` 表示、capture target pickerの開始・キャンセルを2回実施。実windowを選択せず、解析・正式保存workflowは0回。キャンセル後は `停止済み` に戻り、アプリprocessを1つだけ確認。
- resource観測: 55.5秒、5秒間隔12サンプル。working setは164.33–164.75 MB、private memoryは97.02–97.29 MB、handle数は693–707、thread数は15–18で、観測中の単調増加はなし。確認後にprocessを明示終了し、残留processは0件。
- 未実施: 実DDR GRAND PRIX windowを使う数時間soak、実capture中のtarget close/resize/device lost、成功したcapture-saveとPython subprocess、実task trayからのstart/stop/exit、実ファイルを使うアプリ再起動、固定pathへ配置したmaster DBのmissing/incompatible切替確認。
- 残存リスク: Windows Graphics Capture、実ゲームwindow、GPU device、長時間のPython解析・DB保存、tray経由の終了順序は実機条件で追加確認が必要。これらはM10の初期版運用確認へ引き継ぐ。

## Build / test / run

```powershell
dotnet restore app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --locked-mode
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --no-restore
dotnet test app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --no-restore
dotnet run --project app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --no-build
```

## 利用手順

1. アプリを起動する。現在の環境に対応する固定pathが自動的に設定される。
2. 画面に表示されたscore DB、M4 master DB、M5b jacket reference catalogのpathと検証結果を確認する。
3. `自己ベスト` または `プレー履歴` を開く。
4. プレー履歴の行を選び、判定数、MAX COMBO、EX SCORE、保存日時、データ取得元を確認する。

個人DBとマスタDBは別々のSQLite read-only connectionで開きます。起動時の固定score pathに対するmissing／0 byteの初期化だけはWPF側の正式schema初期化境界へ委譲し、初期化後のviewerはschema変更、insert、update、migration、backup、repairを実行しません。connection poolingも使いません。

## 単発保存

1. `単発保存` を押す。
2. `workflow_schema_version=1` の既存strict workflow入力JSONを選ぶ。
3. 現在の環境の固定score DBへ保存する。保存先DBを画面から変更する操作はない。

アプリは `python -m tools.vision_poc.personal_score_db_workflow_app` をリポジトリrootで1回だけ実行します。この薄いprocess adapterは入力内の既存 `log_path` をartifact出力先として渡すだけで、strict loader、save adapter、artifact orchestration、file saveをC#で再実装しません。固定score pathがmissingまたは0 byteの場合の起動時初期化は、WPF側の `PersonalScoreDbInitializer` が既存の正式score DB schema契約を使って実行するため、production pathでもrepository rootやPython module配置を必要としません。repository root探索は `単発保存` または連続取得した結果の正式保存などPython workflowが必要な操作まで遅延し、current directoryまたはapp配置場所の親から検出できない場合はその操作だけを失敗状態にします。既存score DBのread-only viewer起動と起動時の空DB初期化はPythonを必要としません。

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

マスタDBは必須table、必須metadata、曲・譜面件数、source snapshotのURL/hash整合、現在のmaster生成契約に対応するschemaをread-only検査します。missing、非SQLite、読取失敗、schema不一致、metadata不整合は変更せず拒否し、ユーザー向けの理由を表示します。保存開始時にも同じ検査を再実行します。

## UI resources

- `Resources/Theme.xaml`: light themeの色トークンと難易度色
- `Resources/Components.xaml`: button、sidebar、card、table、badgeの共通style
- `Controls/StatePanel.xaml`: 空状態・エラー状態の共通component

今回の画面範囲は共通sidebar、自己ベスト、プレー履歴、プレー詳細、明示単発保存、明示1フレーム取得、capture-only連続取得、監視surface、master DB検証表示、明示した監視session後のevent単位保存workflow、task tray lifecycleです。ホーム、検索・絞り込み、グラフ、要確認、設定、データ管理、自動再接続、installerは後続PRへ分けます。厳密な精度保証、実機評価セット、配布・backup手順の固定はM10へ残ります。
