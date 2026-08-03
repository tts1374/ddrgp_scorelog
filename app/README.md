# GP Score Log WPF app

正式個人スコアDB version 1を開き、保存済みプレー履歴、プレー詳細、譜面別自己ベストを確認するWPFアプリです。通常画面は`監視開始`／`監視停止`による自動監視を提供し、Debug buildだけが開発者向け領域から1フレーム取得、連続取得、単発保存を提供します。`監視開始` を明示した場合だけ、`process=ddr-konaste` かつ client `1280x720` のtop-level windowを自動特定し、該当1件だけへ接続します。監視中は1秒ごとに `results_header` を確認し、RESULT画面の候補が2回連続して安定した場合だけ既存のevent boundaryと正式保存workflowへ渡します。該当windowが0件または複数件なら推測で選択せず、capture・解析・正式保存を開始しません。監視中の候補画像はsession原本として保管せず、一時workflow入力の処理後に破棄します。監視状態と最新結果はWPFとtask trayから確認できます。正式個人スコアDB、M4 master DB、M5b jacket reference catalogは環境ごとの固定pathで扱い、次回起動時に3つとも検証して再利用します。DBの任意path選択、汎用window探索、自動再接続、自動再開、起動時の自動監視、手動pickerへのfallback、DB repairは提供しません。score DB migrationは対応する明示的converterがあるschema変更時だけ行い、事前backupと失敗時rollbackを必須とします。

## 必要環境

- Windows 11
- .NET 10 SDK
- Release packageに含まれるapp-owned runtime資材（`RuntimeAssets/`）
- 正式個人スコアDB version 1（例: `ddrgp-scores.sqlite`）
- 別のmaster DB生成workflowで作られたM4 master DB
- current schema version 1のM5b jacket reference catalog（`jacket-catalog.sqlite`）

ローカルDBはGit管理しません。developmentでは`databases/`、productionでは`%LOCALAPPDATA%\DDRGpScoreViewer\data\`配下のIssue固定pathへ配置してください。DBのファイル選択はアプリから行いません。

## Build configuration

Debug buildでは、通常の監視操作と区別した開発者向け領域に、`1フレーム取得`、`連続取得を開始`、`単発保存`を表示します。Release buildではこの領域、button、menu、command入口を生成せず、`監視開始`と`監視停止`だけを通常画面とtask trayへ残します。

```powershell
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --configuration Debug --no-restore
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --configuration Release --no-restore
dotnet test app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --configuration Debug --no-restore
```

## Debug buildの開発者向け操作

### 1フレーム取得

1. Debug buildの開発者向け領域にある `1フレーム取得` を押す。
2. Windowsのpickerで取得対象のwindowを明示選択する。
3. 完了表示に出た `data/windows_capture/capture-*/` を確認する。

各capture directoryには `frame.png`、`frame_manifest.csv`、`capture_metadata.json` をまとめて出力します。capture出力はappの既定data path（Debugの明示またはsource checkoutから検出したdevelopment root、またはReleaseの`%LOCALAPPDATA%\DDRGpScoreViewer\data\`）から解決します。Releaseではrepository rootを探索しません。runtime data pathが必要な場合は `DDRGP_SCORE_VIEWER_RUNTIME_DATA` に明示します。manifestの必須列は既存契約と同じ `image_path,timestamp_ms` で、`screen_type=unknown`、capture source、幅、高さ、UTC取得時刻を任意列として付けます。画像pathはmanifest directory相対です。staging directoryで3ファイルを書いた後にdirectory単位で公開するため、cancel、対象終了、0x0/resize、device lost、access拒否、write失敗では空画像や部分manifestを最終出力へ残しません。既存capture directoryは上書きしません。

pickerとWindows Graphics Captureは明示操作時だけ起動します。取得後に分類、OCR、identity解決、workflow、正式DB保存、viewer再読込を自動実行しません。同じprocessで再度ボタンを押すと、resourceを作り直して別の1フレームを取得します。

生成した1行manifestは、manifest directoryを基準に `frame.png` を解決してそのまま再実行できます。

以下の`tools\vision_poc`コマンドはoffline PoCの再現・評価専用です。Score Viewer appのDebug/Release runtime、通常監視、capture-save、正式DB保存からは呼び出されません。

```powershell
python -m tools.vision_poc `
  --sequence-mode manifest `
  --frame-manifest data\windows_capture\capture-<id>\frame_manifest.csv `
  --output data\windows_capture_replay
```

単発manifestは `confirmation_mode=time` ですが、1フレームだけではconfirmed resultになりません。実captureのconfirmed-events評価では、同じresultを1秒以上空けて複数回取得し、`data/` 配下のローカル評価manifestへ時刻順にまとめます。`screen_type` と期待値列は評価用manifest側で補い、capture原本のmanifest、画像、metadataは変更しません。

### 連続フレーム取得

1. Debug buildの開発者向け領域にある `連続取得を開始` を押す。
2. Windowsのpickerで対象windowを明示選択する。
3. 必要な区間を取得したら `監視停止` を押す。
4. 完了表示に出た `data/windows_capture/session-*/` を確認する。

session directoryには `frames/frame-*.png`、`frame_manifest.csv`、`capture_session_metadata.json` を出力します。manifestの各行はdirectory相対pathとstrictly increasingな単調時刻ミリ秒を持ち、capture補助列も単発と同じです。明示停止かつ1フレーム以上取得済みの場合だけ、`data/` 直下のstagingからdirectory renameで公開します。停止前のframeは完成出力に見せず、0フレーム、picker cancel、access拒否、対象終了、resize、device lost、write失敗ではstagingごと破棄します。

session中は最初に選択したcapture itemとD3D11 device、frame pool、capture sessionを維持します。resizeには自動追従せず安全側でsessionを停止するため、windowを目的のサイズに戻してから再選択してください。開始済みの二重開始と停止中の再操作は無視し、明示停止とwindow close時の停止は冪等にresourceを解放します。取得frameがPNG encodingより速い場合は、resourceを無制限に保持しないため満杯のframe queueで中間frameをdropします。

生成manifestはそのまま既存manifest modeへ渡せます。`連続取得を開始` は従来どおりcapture bundle生成だけで、分類・OCR・identity・confirmed event・正式save input・DB保存を起動しません。

上記のmanifest replayはoffline PoCの評価用であり、app-owned runtimeの通常操作とは別工程です。

```powershell
python -m tools.vision_poc `
  --sequence-mode manifest `
  --frame-manifest data\windows_capture\session-<id>\frame_manifest.csv `
  --output data\windows_capture_session_replay
```

## 監視と正式保存workflow

1. WPFまたはtask trayの `監視開始` を押す。
2. 起動時に現在の環境（Debugで明示またはsource checkoutから検出したdevelopment root、またはReleaseのLocalAppData production）の固定pathを使う。DBの任意pathへの切替操作はありません。
3. `監視開始` が `process=ddr-konaste` かつ client `1280x720` のtop-level windowを確認する。該当1件だけなら既存の監視へ接続し、0件または複数件なら推測で選択せず、capture・解析・正式保存を開始しない。手動pickerへのfallbackはありません。
4. 監視中にRESULT候補の解析・正式保存が進み、WPFまたはtrayの `監視停止` で現在の候補処理を完了して停止する。監視surfaceで状態、対象window名・process・client size、frame数、サンプリング数、RESULT検出数、候補・破棄・待機数、event status別の保存結果を確認する。

監視では1秒ごとの候補をapp-owned runtimeで解析します。RESULTSがないframeと同じRESULT署名の後続frameは後続処理へ渡しません。RESULT画面を検出しても必須の画像認識根拠が揃わない候補はcandidate materialと失敗理由を一度だけ既存workflowへ渡し、`unresolved` または保存拒否理由を維持してplayを作りません。候補のevent boundary、capture lifecycle、formal evidence、正式DB保存境界は既存契約を維持します。candidate PNG、manifest、解析CSV/JSONはOS一時directoryまたはapp data pathだけに置き、workflow終了後に不要な一時入力を残しません。capture-onlyの連続取得は従来どおり停止後に完成manifestを解析できます。capture失敗、resize、target close、device lost、write失敗では新しい解析・正式保存を開始しません。

### Windows Graphics Captureの同意と枠ありfallback

通常の `監視開始` で自動特定した対象window用sessionを開始するときだけ、Windows Graphics Captureの枠なしaccessを試行します。Windows 10 version 2104 / build 20348以降でruntime APIとWindowsの同意が利用できる場合だけ色付き枠を非表示にします。未署名VeloPack packageはMSIX capabilityを付与しないため、OSが枠なしaccessを許可しない環境では枠ありcaptureへ戻ります。

- 初回の通常監視開始ではWindowsの同意promptが表示されることがあります。許可すると、その後の対象window用capture sessionで枠なし設定を適用します。
- 同意を拒否した場合、非対応OS/API、manifest capability不足、権限取得失敗、Windows APIの例外が発生した場合は、枠ありのまま監視を開始・継続します。これらはcapture failure statusへ変換せず、frame取得、RESULT解析、正式保存workflowの境界も変更しません。
- Debug buildの `連続取得を開始` と `1フレーム取得` はpickerで選ぶ開発者向けcaptureのため、borderless同意を要求しません。
- アプリ独自の同意設定は保存しません。監視停止、対象window終了、capture failure、再度の監視開始は既存session lifecycleで処理し、枠を強制的に隠すOS overlayやwindow位置変更は行いません。

枠なし動作はWindows build、API、Windowsの同意に依存します。同じwindowまたはdisplayに対して別アプリが枠を要求している場合は、許可済みでも枠が表示されることがあります。VeloPack packageで枠が表示されても監視・解析・保存の失敗とは扱いません。

### RESULT数値認識 runtime

RESULTの `score`、`max_combo`、`marvelous`、`perfect`、`great`、`good`、`miss`、`ex_score` は、app-ownedのbitmap-template画像認識で読み取ります。`score=0` は有効な数字として扱い、scoreは1桁から7桁の可変桁です。認識状態は `recognized`、`missing_reference`、`ambiguous`、`failed_segmentation`、`not_evaluated` をcandidate materialへ記録し、採用済みのfieldだけが明示的な`RESULT数値認識根拠`になります。

通常監視では期待値を渡さないため、テンプレート照合に成功したfieldは `recognized`（`match=null`）として扱います。期待値比較を明示して期待値が空の場合だけ `not_evaluated` となり、いずれも正式値への自動昇格を意味しません。

テンプレートはRelease packageの `RuntimeAssets/digit_templates/`、または `DDRGP_SCORE_VIEWER_RUNTIME_DATA` 配下の明示data pathから解決します。`score` はROI別 `score_digits`、判定数（`marvelous`、`perfect`、`great`、`good`、`miss`）は共有 `judgment_counts`、`max_combo` と `ex_score` は共有 `combo_ex_score`、`ex_score` は `max_combo` fallbackも探します。repositoryの `samples` や `tools/vision_poc` はruntime探索せず、PythonやTesseractも起動しません。`recognized_digits`、confidence、fieldごとの根拠、必須fieldの完全性はapp-owned formal evidence bridgeへ渡し、明示的な採用境界を通らない候補値は正式値やplayへ昇格しません。

要件レベルでは、`RESULT同定根拠`、`RESULT数値認識根拠`、`RESULT状態認識根拠`、`capture event根拠`が揃ったformal evidenceだけを正式保存の入力にします。song/chart identityは、current masterとcatalogをread-onlyで参照したジャケット画像照合、play style/difficultyの色画像認識、levelの数字画像認識が一意に揃った場合だけ`result_identity_visual_evidence`として採用します。8つの数字fieldは`result_numeric_visual_evidence`、rankは`result_rank_visual_evidence`、clear typeは`result_clear_type_visual_evidence`をsourceとし、各fieldのconfidenceが0.98以上で全必須値が揃う場合だけ既存formal workflowへ渡します。clear typeの内部判定に使う`O.K.`数も画像認識根拠が必須ですが、正式DB schemaへは追加しません。master versionは`master_metadata`、play_idとduplicate keyはconfirmed event ID、played_atはcapture UTCから構築し、同一RESULT画面の後続frame・再送・再処理では同じIDを再利用し、別confirmed eventではformal値が一致しても新しいIDを使います。RESULT fingerprintは画面内イベントグルーピング専用です。liveのsource kindは`capture`です。`flare_rank=null`は許容します。実装クラス名や工程コードは要件名の代わりに使わず、formal source名には使いません。

jacket catalogの`master_version`が過去の値でも、song ID・canonical title・canonical artistがcurrent GP masterと完全一致するconfirmed referenceはcurrent-master-compatibleな正式参照として利用します。masterとの不一致、orphan、未確認、旧featureのreferenceは利用せず、catalog自体も書き換えません。

jacket照合が一意なら、`M7 result-text feature`の`song_title`／`artist`は実行・読込しません。jacket候補がambiguousの場合だけ、current masterとchart contextから得た譜面候補集合とjacket曖昧候補song ID集合の共通部分に対して`song_title`画像featureを先に比較し、通常featureがambiguousのときだけ`title_linehash_rows`で再順位付けし、解消しなければ同じ集合で`artist`画像featureへfallbackします。title/artistともに欠落、旧version、ROI不一致、payload/hash不整合、canonical title/artist不一致、current master drift、confidence不足、複数候補のままの場合は`unresolved`とし、候補集合外の曲を検索・選択しません。比較距離は正規化距離`0.35`以下を採用条件とし、これを超える最良候補はmarginに関係なく未解決とします。feature versionは`m7-result-text-image-v1`、ROI versionは`m7-result-title-artist-roi-v1`で、現行flat shape `[1536]` / `[640]` のみを受理し、nested shape `[96,16]` / `[40,16]` はリリース前の現行形式外として読み込み対象外とします。いずれも既存のformal evidence source/confidence/完全性検査を通過した場合だけ正式保存workflowへ渡します。catalogのschema、migration、writer、jacket threshold、ambiguity deltaは変更しません。

標準`AppOwnedLiveResultAnalyzer`はRESULT画面の数値・rank・clear type・flare rankをapp-owned画像認識で採用し、live保存入口はcurrent master/catalogから同定・譜面画像認識を追加してからformal evidence bridgeへ渡します。catalogまたはmaster/chart contextが不足・不整合・ambiguousなら、数字認識成功とは別の`formal_evidence.*`理由で`unresolved`となります。`RESULT同定根拠`、`RESULT状態認識根拠`をcandidate値、OCR、`known-result`から補完しません。

live保存入口は、current masterとcurrent-master-compatibleなcatalog参照から同定・譜面画像認識を追加します。

`identity_signal_*`、`recognized_digits`、expected値、M8 preview payload、相対 `timestamp_ms`、`known-result`は候補材料のままです。formal evidenceが未指定・不足、`ambiguous`、`missing_reference`、`failed_segmentation`、identity/rank/clear type欠落、confidence不足の場合は`formal_evidence.*`または`digit_recognition.*`理由の`unresolved`となり、正式DBへplayを作りません。Debug buildのreviewed workflow入力は開発者向け領域の `単発保存` から実行し、自動由来と混同しません。

各confirmed eventは既存正式workflowを1回だけ呼びます。DB内duplicate、policy excluded、unresolved、invalid、artifact failure、DB拒否をstatusのまま集計し、`invalid`、artifact failure、DB拒否などが1件でもあればsessionを `workflow_failed` として非0終了します。同じsessionにtransaction済みの `saved` playがある場合はそれだけread-only再読込し、部分成功件数と失敗理由を同時に表示します。解析出力は `data/capture_save_workflow/`、画像原本は `data/windows_capture/`、正式DBは明示pathに分離します。

`IsSaving` はDebug buildの単発保存と監視capture-save全体の共通排他です。DB path変更操作はなく、監視中は単発保存を開始しません。capture開始からworkflow完了まで状態を保持し、同じ正式DBへの並行writerとsave statusの競合を防ぎます。Debug buildのcapture-only入口も監視開始と同じoperation gateへ入り、開始要求を二重実行しません。session世代が古いprogress callback、停止後のcallback、終了後の新しい解析・保存は受け付けません。

監視状態は `idle`、`selecting_target`、`monitoring`、`stopping`、`stopped`、`target_closed`、`resized`、`device_lost`、`capture_failed`、`workflow_failed` を区別します。検出したwindowのtitle、process、client sizeは監視surfaceへ表示し、auto-detectionの判定はprocess名とclient sizeだけで行います。最新結果は `saved`、`duplicate`、`excluded`、`unresolved`、`analysis_failed`、`db_rejected`、`workflow_failed` を別々に数え、transaction済みのsaved playだけread-only再読込します。

windowの×ボタンはwindowをtrayへ格納し、最小化ボタンは通常どおりtaskbarへ最小化します。trayのダブルクリックまたは`GP Score Logを開く`でメイン画面を表示・前面化できます。tray menuは`GP Score Logを開く`、`監視開始`、`監視停止`、`終了`を提供し、監視状態に応じて開始・停止を有効化します。`終了`だけが新規処理受付を止め、pending pickerをcancelし、進行中処理の完了または安全な中断、監視worker/runtime停止、DB connection解放、一時data削除、tray解除の順でprocessを終了します。Windows終了・ログオフ時も可能な範囲で同じ終了処理を開始し、未完了結果を正式保存へ昇格しません。二重起動時は新しいprocessを終了し、既存windowを表示・前面化します。通知はsavedがある完了と、監視停止が必要な重大失敗だけです。

## 再起動・path再検証・失敗からの復帰

- 正式個人スコアDB、M4 master DB、M5b jacket reference catalogの固定pathとdev/prod環境タグだけを `%LOCALAPPDATA%\DDRGpScoreViewer\viewer-paths.json` に保存します。この設定はGit管理外で、候補値、解析結果、保存statusは持ちません。旧形式、任意path、別環境のpathは暗黙復元せず、現在の既定pathだけを使用します。
- 起動時、解析・正式保存開始直前に、M4 master DBとM5b jacket reference catalogを別々のread-only connectionで検査します。M4は必須table、metadata、曲・譜面件数、source snapshotのURL/hash整合を確認し、M5bはtable identity、column、metadata identity、schema version、unique index、foreign keyを確認します。両方とも `missing`、`read不可`、`schema incompatible`、`compatible` を区別します。
- どちらか一方がmissing / read不可 / incompatibleなら、理由を表示して対象windowの解析と正式保存workflowを開始しません。capture後にも同じ2ファイルを再検証します。networkからの最新版確認やhashの継続監視は行いません。
- `target_closed`、`resized`、`device_lost`、`capture_failed`、`workflow_failed` は監視状態として残ります。停止完了後に必要なmaster DBを現在の環境の固定pathへ用意し、`監視開始` を明示的に再実行してください。再実行時も対象windowを1件だけ自動特定し、window終了、resize、capture失敗で古いsessionを再利用しません。Debug buildの `連続取得を開始` はcapture-onlyの開発者向け入口として手動pickerで対象windowを選び直します。
- saved、duplicate、excluded、unresolved、解析失敗、DB拒否、workflow失敗はprocess内の表示と既存workflowのartifact/logで追跡します。再起動時に保存されるのはtransaction完了した正式playだけで、過去のskip・拒否・失敗statusをsavedへ昇格するcheckpointはありません。

## M10-2 既定保存先と責務境界

実行環境は、Debugで`DDRGP_SCORE_VIEWER_DEVELOPMENT_ROOT`を明示した場合、またはDebugのcurrent directory／Debug出力directoryから親方向にsource checkout（`databases/`とScore Viewer project）が検出できた場合だけdevelopmentです。Releaseは常にproduction固定pathを使用し、repository rootやapp配置場所の親を探索しません。developmentとproductionのpathを相互にfallbackしません。

| 対象 | development | production |
| --- | --- | --- |
| M4 master DB | `databases/ddrgp-master.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\ddrgp-master.sqlite` |
| M5b jacket reference catalog | `databases/jacket-catalog.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\jacket-catalog.sqlite` |
| 正式個人スコアDB | `databases/score.dev.db` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\score\score.db` |
| 評価用DB | `databases/evaluation.db`（M10-3専用） | 既定pathなし |

M4 master DBとM5b jacket reference catalogは、同じdirectoryに置かれていても別ファイル・別責務です。Release packageは両DBを1つのreference data setとして同梱しますが、network取得や最新版照合は行いません。正式個人スコアDBはアプリ更新、reference DB操作、評価用DB初期化で上書き・初期化しません。固定score pathがmissingまたは0 byteの場合だけ、master 2種類の検証後に既存の正式DB準備境界を使って空の正式schemaを作成します。既存非空DBは現行schemaならそのまま利用し、明示converterがある旧schemaだけ事前backup後にtransaction migrationします。対応より新しいschemaとconverterのないschemaは変更せず拒否します。

初回起動では親directory（`databases/`、またはproductionの`data/master/`・`data/score/`）と`data/`・`logs/`を作成し、master 2種類がcompatibleなら固定score pathのmissing／0 byteだけを初期化します。既存の非空score DBはread-only検証だけを行い、unknown、preview、identity mismatch、manual migration候補、非SQLite、directoryは変更せず拒否します。captureはdevelopmentでは`data/windows_capture/`、productionでは`%LOCALAPPDATA%\DDRGpScoreViewer\data\windows_capture/`へ出し、解析artifactは`data/capture_save_workflow/`、失敗画像と診断ログは`logs/`配下へ分離します。これらは再生成・退避可能なlocal dataで、Git管理しません。

M10-3の評価用DBはdevelopmentでだけ使います。WPF viewerは評価用DBを開かず、正式個人スコアDBとの相互初期化も行いません。評価をやり直す場合は、評価processとWPFを停止し、既存の`databases/evaluation.db`を`data/evaluation/backups/evaluation-<UTC timestamp>.db`へ新規コピーしてから、M10-3評価器の明示initializerで同じpathを初期化します。backupの存在確認・SQLite integrity check・path確認後に評価を再実行し、既存backupを上書き・削除しません。M10-3のschema/initializerが未実装の環境では、DBを手作業で作り替えず、評価器の初期化を未実施として扱います。

## M9-6 validation record

2026-07-27 JSTに次を確認しました。

- 自動検証: `.NET build`、`.NET test`、capture-save / personal-score workflowの回帰テスト、Ruff、`compileall` はすべて成功。
- Windows smoke: WPF起動、固定pathのmaster DB未配置による `missing` 表示、capture target pickerの開始・キャンセルを2回実施。実windowを選択せず、解析・正式保存workflowは0回。キャンセル後は `停止済み` に戻り、アプリprocessを1つだけ確認。
- resource観測: 55.5秒、5秒間隔12サンプル。working setは164.33–164.75 MB、private memoryは97.02–97.29 MB、handle数は693–707、thread数は15–18で、観測中の単調増加はなし。確認後にprocessを明示終了し、残留processは0件。
- 未実施: 実DDR GRAND PRIX windowを使う数時間soak、実capture中のtarget close/resize/device lost、実task trayからのstart/stop/exit、実ファイルを使うアプリ再起動、固定pathへ配置したmaster DBのmissing/incompatible切替確認。
- 残存リスク: Windows Graphics Capture、実ゲームwindow、GPU device、長時間のapp-owned解析・DB保存、tray経由の終了順序は実機条件で追加確認が必要。これらはM10の初期版運用確認へ引き継ぐ。

## Build / test / run

```powershell
dotnet restore app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --locked-mode
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --configuration Debug --no-restore
dotnet build app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --configuration Release --no-restore
dotnet test app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --configuration Debug --no-restore
dotnet run --project app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj --configuration Debug --no-build
```

## 利用手順

1. アプリを起動する。現在の環境に対応する固定pathが自動的に設定される。
2. 画面に表示されたscore DB、M4 master DB、M5b jacket reference catalogのpathと検証結果を確認する。
3. `自己ベスト` または `プレー履歴` を開く。
4. プレー履歴の行を選び、判定数、MAX COMBO、EX SCORE、保存日時、データ取得元を確認する。

個人DBとマスタDBは別々のSQLite connectionで開きます。起動時の固定score pathに対するmissing／0 byteの初期化だけはWPF側の正式schema初期化境界へ委譲します。通常閲覧はread-onlyで、正式保存は既存transaction writerだけを使います。schema migrationは明示converterが登録された旧versionだけを事前backup付きで処理し、repairは実行しません。connection poolingも使いません。

## Debug buildの単発保存

1. Debug buildの開発者向け領域にある `単発保存` を押す。
2. `workflow_schema_version=1` の既存strict workflow入力JSONを選ぶ。
3. 現在の環境の固定score DBへ保存する。保存先DBを画面から変更する操作はない。

アプリはapp-ownedのstrict loader、save adapter、formal artifact orchestration、v1 file writerを同じprocess内で1回だけ実行します。candidate materialや未確認の数字をformal playへ補完せず、`saved` / `written=true` / 非null `play_id` がtransaction完了した場合だけread-only再読込します。固定score pathがmissingまたは0 byteの場合の起動時初期化と、単発保存時のfile preparationは、WPF側が既存の正式score DB schema契約を使って実行します。Release packageにはrepository root探索、repository内module、Python executable、Tesseract fallbackを持たず、必要な認識資材はapp packageまたは`DDRGP_SCORE_VIEWER_RUNTIME_DATA`で明示したdata pathから解決します。

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

## Release packageの生成と公開

VeloPack 1.2.0をrepository-local .NET toolとして固定しています。packageはunsignedのWindows x64 self-contained buildで、`packId=com.tts1374.ddrgp_scorelog`、表示名`GP Score Log`、Start Menu shortcutのみを持つper-user installerです。管理者権限、Desktop shortcut、code signing、アプリ内自動更新は使用しません。通常のinstaller完了時はアプリが起動します。

1. `databases/ddrgp-master.sqlite`と`databases/jacket-catalog.sqlite`を同じcurrent master versionに揃え、catalogの`catalog_metadata.master_version`とmaster DBの実metadataが一致することをread-only検証する。初期版Release前の未binding catalogは、developer向けPoC READMEの`bind-master`でsourceを変更せず`databases/jacket-catalog-release.sqlite`へ変換し、package commandへ`-CatalogDatabase databases\jacket-catalog-release.sqlite`を渡す。
2. repository rootで次を実行する。

   ```powershell
   .\app\packaging\Build-Release.ps1 -Version 0.1.0
   ```

3. `data/releases/0.1.0/`の`com.tts1374.ddrgp_scorelog-win-Setup.exe`、full package、`RELEASES`、`assets.win.json`、`releases.win.json`を確認する。`data/release-build/0.1.0/publish/ReferenceData/`には2つのDBと`reference-set.json`が別fileのまま入る。
4. tagとGitHub Releaseを同じversion（例: `v0.1.0`）で作り、少なくともSetup、full package、`RELEASES`、2つのrelease JSONを添付する。署名していないこととこのREADMEの既知制限をRelease notesへ記載する。

package生成はmaster/catalog実metadataの一致検証、locked NuGet restore、Release self-contained publish、VeloPack packagingを順に実行します。入力DBと成果物はGit管理しません。versionだけを変えて同じrepository revisionと同じ2 DBから再実行できます。VeloPackの取得、更新適用、network source設定は#116/#117の範囲なので実装していません。

## 初回導入と通常操作

1. 起動中の旧版があればtrayの`終了`で明示終了する。
2. Setupを実行する。未署名のためWindows SmartScreen等の警告が出る場合は、配布元とhashを確認した本人だけが続行する。
3. install後に自動起動した`GP Score Log`で、M4 master DB、M5b jacket reference catalog、score DBの表示を確認する。初回起動は組み込みreference data setをproduction固定pathへ配置し、master/catalog検証後にmissingまたは0 byteのscore DBだけを正式schemaへ初期化する。
4. DDR GRAND PRIXを`1280x720` client sizeで起動し、`監視開始`を押す。対象が一意に見つからない場合は表示理由を直してから再度`監視開始`を押す。
5. 一時停止は`監視停止`、再開は停止完了後の`監視開始`を使う。×ボタンはtray格納、最小化はtaskbar最小化、完全終了はtrayの`終了`を使う。

起動時の自動監視開始、自動復帰、reference DBのnetwork取得、アプリ内自動更新はありません。監視は毎回明示的に開始します。

## Reference data setの配置・更新・復旧

production固定pathは`%LOCALAPPDATA%\DDRGpScoreViewer\data\master\`です。組み込み候補は一時directoryへコピーし、2 DBをread-only openしてschema version、master content version、catalog内referenceのmaster version整合を検査します。初回は検証済みの2 DBとmanifestをセットで配置します。更新時は`content_version`が現在より新しい場合だけ、現行セットを`.previous/`へ退避して3 fileを切り替え、切替後に再openします。同一versionはno-op、古いversionは拒否、片方欠落・不整合・切替失敗・再検証失敗は直前セットへ戻します。保持するのは現行と直前1世代だけです。

アプリ更新やreference data set更新は`data\score\score.db`と`viewer-paths.json`を変更しません。VeloPackのinstall directoryは`%LOCALAPPDATA%\com.tts1374.ddrgp_scorelog`、永続dataは別の`%LOCALAPPDATA%\DDRGpScoreViewer`なので、uninstallしてもscore DB、settings、ログ、配置済みreference DBは残ります。不要になった場合だけ、backup確認後に利用者が永続data directoryを手動削除します。

## Releaseログとdata保持

`%LOCALAPPDATA%\DDRGpScoreViewer\logs\gp-score-log.log`へ、起動・終了、app version、DB検証、reference data set処理、監視状態、保存集計、重大例外を記録します。5MB到達時にrotationし、現行を含め最大3 fileです。Release版は失敗画像、詳細解析中間情報、runtime stdout/stderrを既定保存しません。Debug buildだけが既存の詳細artifactを生成できます。

| data | 保持 |
| --- | --- |
| `data\score\score.db`、`viewer-paths.json` | 無期限。利用者がbackup確認後に削除するまで保持 |
| reference data set | 現行＋直前1世代 |
| score migration backup | `data\score\migration-backup\score.db.bak`の最新1件 |
| Release log | 5MB × 3 file |
| `data\cache\`、`data\temp\` | 処理完了時または次回起動時に削除 |

## 正式個人スコアDBとsettingsのbackup / restore

backupとrestoreは必ずtrayの`終了`後に行います。通常backup対象は次の2 fileです。reference DBは再配布できるため対象外です。

- `%LOCALAPPDATA%\DDRGpScoreViewer\data\score\score.db`
- `%LOCALAPPDATA%\DDRGpScoreViewer\viewer-paths.json`（存在する場合）

backup先に新しいdirectoryを作り、2 fileをコピーします。コピー後は元とbackupのfile sizeを確認します。restore時はアプリを終了し、現在の`score.db`と`viewer-paths.json`を削除せず、それぞれ`score.before-restore.db`、`viewer-paths.before-restore.json`など未使用名へ移動してからbackupを元の固定pathへコピーします。起動後にDB検証、履歴件数、最新playを確認し、問題があれば再度終了して復元前fileを戻します。SQLiteの`score.db-wal`や`score.db-shm`が残っている場合はアプリが完全終了していないため、copy/restoreを開始しません。

## トラブルシューティング

- `DDR GRAND PRIX windowを自動検出できません`: `ddr-konaste`が1 processだけでclient `1280x720`か確認し、対象を開いたまま`監視開始`を再実行する。起動時自動監視は行わない。
- master DB missing / incompatible: installerを同じversionで再実行しても同一versionは上書きしない。ログのreference data set結果を確認し、正しい新versionのinstallerを再配布する。score DBは変更されない。
- jacket catalog異常: master DBと別fileとして拒否される。片方だけ手動交換せず、正しいセットのinstallerを使用する。
- capture failure、resize、target close、device lost: 状態を確認し、windowを`1280x720`へ戻すか再起動してから、停止完了後に`監視開始`を明示する。未完了結果は保存されない。
- workflow failure / 保存不能: 表示された「データが変更されたか」を確認する。保存済み件数があれば履歴を確認し、失敗分だけ次のRESULTで再試行する。詳細pathと例外はReleaseログを確認する。
- score DB拒否: 新しいschema、unknown、preview、identity mismatch、converterなし旧schemaは変更しない。アプリを終了してbackupを取り、対応版または明示converterのあるversionを使う。手動repairはしない。

## M10-3保証範囲・既知制限・release停止条件

実機評価はWindowsの`ddr-konaste`、client `1280x720`、SINGLE 28曲・29譜面の94 RESULTです。`saved=94`、他status=0、自動保存成功率100%で、全件を画面と正式DBで目視照合し、誤保存0件でした。target close、resize、tray exit、再起動・固定path再利用を確認し、二重保存はありませんでした。

保証範囲はこのWindows環境と`1280x720`条件に限定します。対象外解像度、全GPU、意図的なdevice lost、0点RESULTは実機保証外です。未署名installerはSmartScreen警告があり、code signing、telemetry、cloud backup、network reference DB取得、自動更新、監視自動開始・自動復帰はありません。

次のいずれかがあるreleaseは完成扱いにしません: 誤保存が1件以上、固定条件の自動保存成功率が95%未満、既定CI失敗、VeloPack package/clean環境相当smoke失敗、reference DBのセット検証・rollback失敗、既存score DBの上書きまたはrestore不能、Release buildへの開発者向け操作混入。device lostと対象外環境は既知制限として扱い、保証範囲を暗黙に拡張しません。

## UI resources

- `Resources/Theme.xaml`: light themeの色トークンと難易度色
- `Resources/Components.xaml`: button、sidebar、card、table、badgeの共通style
- `Controls/StatePanel.xaml`: 空状態・エラー状態の共通component

今回の画面範囲は共通sidebar、自己ベスト、プレー履歴、プレー詳細、Debug buildの開発者向け単発操作、監視surface、master DB検証表示、明示した監視session後のevent単位保存workflow、task tray lifecycleです。Release buildの通常画面には開発者向け領域を含めず、`監視開始`と`監視停止`を残します。ホーム、検索・絞り込み、グラフ、要確認、設定画面、自動再接続は対象外です。
