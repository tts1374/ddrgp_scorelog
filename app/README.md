# GP Score Log WPF app

正式個人スコアDB version 1を開き、保存済みプレー履歴、プレー詳細、譜面別自己ベストを確認するWPFアプリです。通常画面は`監視開始`／`監視停止`による監視を提供し、設定がON（初期値）なら起動後にDDR GRAND PRIX windowを1秒ごとに探索して、2回連続で検出した対象へ自動接続します。Debug buildだけが開発者向け領域から1フレーム取得、連続取得、単発保存を提供します。手動の`監視開始`でも、`process=ddr-konaste` かつ client `1280x720` のtop-level windowを自動特定し、該当1件だけへ接続します。監視中は1秒ごとに `results_header` を確認し、RESULT画面の候補が2回連続して安定した場合だけ既存のevent boundaryと正式保存workflowへ渡します。該当windowが0件または複数件なら推測で選択せず、capture・解析・正式保存を開始しません。監視中の候補画像はsession原本として保管せず、一時workflow入力の処理後に破棄します。監視状態と最新結果はWPFとtask trayから確認できます。productionのインストール済みpackageでは、main window表示後にGitHub Releasesのアプリ更新を非同期確認し、ユーザーの明示操作でdownload・完全終了・再起動を行います。正式個人スコアDB、M4 master DB、M5b jacket reference catalogは環境ごとの固定pathで扱い、次回起動時に3つとも検証して再利用します。DBの任意path選択、汎用window探索、手動pickerへのfallback、DB repairは提供しません。手動停止後は同一app session中に自動再開せず、DB・runtime・更新・終了処理の異常時も自動開始しません。score DB migrationは対応する明示的converterがあるschema変更時だけ行い、事前backupと失敗時rollbackを必須とします。

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
dotnet test app\tests\DDRGpScoreViewer.Tests\DDRGpScoreViewer.Tests.csproj --configuration Release --no-restore
```

Debug専用のcapture・手動保存APIに依存するテストはDebug configurationだけでコンパイル・実行し、Release configurationではRelease appに存在する通常機能と境界のテストを実行します。

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

1. 「アプリ起動時に監視を開始」がON（初期値）の場合だけ、起動後に自動監視が始まり、1秒ごとに対象windowを探索する。OFFでもWPFまたはtask trayの `監視開始` は利用できる。
2. 起動時に現在の環境（Debugで明示またはsource checkoutから検出したdevelopment root、またはReleaseのLocalAppData production）の固定pathを使う。DBの任意pathへの切替操作はありません。
3. `process=ddr-konaste` かつ client `1280x720` のtop-level windowを2回連続で確認した場合だけ既存の監視へ接続する。0件または複数件なら推測で選択せず、capture・解析・正式保存を開始しない。手動pickerへのfallbackはありません。
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

監視状態は `idle`、`starting`、`waiting_for_game`、`selecting_target`、`monitoring`、`stopping`、`stopped`、`manually_stopped`、`blocked`、`shutting_down`、`target_closed`、`resized`、`device_lost`、`capture_failed`、`workflow_failed` を区別します。検出したwindowのtitle、process、client sizeは監視surfaceへ表示し、auto-detectionの判定はprocess名とclient sizeだけで行います。対象windowは2回連続検出で開始し、2回連続消失で安全停止します。単発の探索失敗では待機を続けます。手動停止後は`manually_stopped`、DBまたはruntime異常時は`blocked`、終了処理中は`shutting_down`としてtrayと画面へ反映します。最新結果は `saved`、`duplicate`、`excluded`、`unresolved`、`analysis_failed`、`db_rejected`、`workflow_failed` を別々に数え、transaction済みのsaved playだけread-only再読込します。

windowの×ボタンはwindowをtrayへ格納し、最小化ボタンは通常どおりtaskbarへ最小化します。trayのダブルクリックまたは`GP Score Logを開く`でメイン画面を表示・前面化できます。tray menuは`GP Score Logを開く`、`監視開始`、`監視停止`、`終了`を提供し、監視状態に応じて開始・停止を有効化します。メインwindowをtrayへ格納しても自動監視workerは継続し、検出・消失に応じて同じtray状態を更新します。`終了`だけが新規処理受付を止め、pending pickerをcancelし、進行中処理の完了または安全な中断、監視polling・worker/runtime停止、DB connection解放、一時data削除、tray解除の順でprocessを終了します。Windows終了・ログオフ時も可能な範囲で同じ終了処理を開始し、未完了結果を正式保存へ昇格しません。二重起動時は新しいprocessを終了し、既存windowを表示・前面化します。通知はsavedがある完了、監視停止が必要な重大失敗、capture event単位の自動保存不能結果を対象とし、WPF/trayへ非ブロッキング表示します。同じcapture eventの反復frameは重複通知しません。

## 再起動・path再検証・失敗からの復帰

- 正式個人スコアDB、M4 master DB、M5b jacket reference catalogの固定pathとdev/prod環境タグだけを `%LOCALAPPDATA%\DDRGpScoreViewer\viewer-paths.json` に保存します。起動時監視、保存できない結果の通知、既定プレイスタイル、起動時画面は同じdirectoryの`user-settings.json`へ別に保存します。いずれもGit管理外で、候補値、解析結果、保存statusは持ちません。旧形式、任意path、別環境のpathは暗黙復元せず、現在の既定pathだけを使用します。
- 起動時、解析・正式保存開始直前に、M4 master DBとM5b jacket reference catalogを別々のread-only connectionで検査します。M4は必須table、metadata、曲・譜面件数、source snapshotのURL/hash整合を確認し、M5bはtable identity、column、metadata identity、schema version、unique index、foreign keyを確認します。両方とも `missing`、`read不可`、`schema incompatible`、`compatible` を区別します。
- どちらか一方がmissing / read不可 / incompatibleなら、理由を表示して対象windowの解析と正式保存workflowを開始しません。capture後にも同じ2ファイルを再検証します。networkからの最新版確認やhashの継続監視は行いません。
- `target_closed`、`resized`、`device_lost`、`capture_failed`、`workflow_failed` は監視状態として残ります。window終了やresizeではsessionを安全に終了し、対象windowが一度消失してから再出現した場合だけ自動復帰します。DBまたはruntime異常は`blocked`として自動開始を抑止し、必要なmaster DBを現在の環境の固定pathへ用意してから再起動してください。手動停止後は同一app session中に自動復帰せず、明示的な`監視開始`だけを受け付けます。再実行時も対象windowを1件だけ自動特定し、古いsessionを再利用しません。Debug buildの `連続取得を開始` はcapture-onlyの開発者向け入口として手動pickerで対象windowを選び直します。
- saved、duplicate、excluded、unresolved、解析失敗、DB拒否、workflow失敗はprocess内の表示と既存workflowのartifact/logで追跡します。再起動時に保存されるのはtransaction完了した正式playだけで、過去のskip・拒否・失敗statusをsavedへ昇格するcheckpointはありません。

### 表示言語

設定画面の「言語」では、日本語（`ja`）、英語（`en`）、韓国語（`ko`）を選択できます。変更は「変更を保存」を押した後、アプリを再起動すると反映されます。`user-settings.json` の既存設定に言語項目がない場合は日本語、対応外の保存値は英語として扱います。

新しい環境ではOSの表示言語が日本語（`ja*`）なら日本語、韓国語（`ko*`）なら韓国語、それ以外は英語を初期値にします。翻訳が用意されていない表示文は日本語を基底言語として表示します。起動時画面の保存値は `home` / `best` / `history` を使用し、旧形式の「ホーム」／「自己ベスト」／「直近プレー履歴」も読み込めます。

## M10-2 既定保存先と責務境界

実行環境は、Debugで`DDRGP_SCORE_VIEWER_DEVELOPMENT_ROOT`を明示した場合、またはDebugのcurrent directory／Debug出力directoryから親方向にsource checkout（`databases/`とScore Viewer project）が検出できた場合だけdevelopmentです。Releaseは常にproduction固定pathを使用し、repository rootやapp配置場所の親を探索しません。developmentとproductionのpathを相互にfallbackしません。

| 対象 | development | production |
| --- | --- | --- |
| M4 master DB | `databases/ddrgp-master.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\ddrgp-master.sqlite` |
| M5b jacket reference catalog | `databases/jacket-catalog-release.sqlite` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\master\jacket-catalog.sqlite` |
| 正式個人スコアDB | `databases/score.dev.db` | `%LOCALAPPDATA%\DDRGpScoreViewer\data\score\score.db` |
| 評価用DB | `databases/evaluation.db`（M10-3専用） | 既定pathなし |

M4 master DBとM5b jacket reference catalogは、同じdirectoryに置かれていても別ファイル・別責務です。developmentではcollectorが更新する未binding source `databases/jacket-catalog.sqlite`をそのままruntimeへ渡さず、`bind-master`で生成した`databases/jacket-catalog-release.sqlite`をWPFの固定runtime pathとして読みます。Release packageは両DBを1つのreference data setとして同梱し、production起動時はGitHub Releasesの同じreference data setも確認します。正式個人スコアDBはアプリ更新、reference DB操作、評価用DB初期化では上書き・初期化しません。固定score pathがmissingまたは0 byteの場合だけ、master 2種類の検証後に既存の正式DB準備境界を使って空の正式schemaを作成します。既存非空DBは現行schemaならそのまま利用し、明示converterがある旧schemaだけ事前backup後にtransaction migrationします。対応より新しいschemaとconverterのないschemaは変更せず拒否します。データ管理画面から確認して実行する個人スコアデータの復元だけは、後述の個人プレー履歴置換契約に従う明示操作です。

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
3. `自己ベスト`、`プレー履歴`、または `データ管理` を開く。
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

VeloPack 1.2.0をrepository-local .NET toolとして固定しています。packageはunsignedのWindows x64 self-contained buildで、`packId=com.tts1374.ddrgp_scorelog`、表示名`GP Score Log`、Start Menu shortcutのみを持つper-user installerです。管理者権限、Desktop shortcut、code signing、強制更新、複数channel、background service、telemetryは使用しません。通常のinstaller完了時はアプリが起動します。

1. `databases/ddrgp-master.sqlite`と、`bind-master`で作成した`databases/jacket-catalog-release.sqlite`を同じcurrent master versionに揃え、catalogの`catalog_metadata.master_version`とmaster DBの実metadataが一致することをread-only検証する。collector source `databases/jacket-catalog.sqlite`を使う場合は、developer向けPoC READMEの`bind-master`でsourceを変更せずruntime/release用catalogへ変換し、package commandへ`-CatalogDatabase databases\jacket-catalog-release.sqlite`を渡す。
2. repository rootで次を実行する。

   ```powershell
   .\app\packaging\Build-Release.ps1 -Version 0.1.0 `
     -CatalogDatabase databases\jacket-catalog-release.sqlite
   ```

3. `data/releases/0.1.0/`の`com.tts1374.ddrgp_scorelog-win-Setup.exe`、full package、`RELEASES`、`assets.win.json`、`releases.win.json`を確認する。`data/release-build/0.1.0/publish/ReferenceData/`には2つのDBと`reference-set.json`が別fileのまま入る。
4. tagとGitHub Releaseを同じversion（例: `v0.1.0`）で作り、少なくともSetup、full package、`RELEASES`、2つのrelease JSONを添付する。署名していないこととこのREADMEの既知制限をRelease notesへ記載する。

5. 同じGitHub Releaseへ、`data/release-build/0.1.0/publish/ReferenceData/`から次の3 assetを名前を変えずに添付する。

   | asset | 内容 |
   | --- | --- |
   | `reference-set.json` | `content_version`、master/catalog schema version、master content version対応、SHA-256 |
   | `ddrgp-master.sqlite` | M4 master DB |
   | `jacket-catalog.sqlite` | M5b jacket reference catalog |

   アプリはreference data setについては従来どおり `https://api.github.com/repos/tts1374/ddrgp_scorelog/releases/latest` を使い、アプリ本体についてはVeloPack `GithubSource`で同じrepositoryのstable Releaseを確認します。reference data setの取得・検証・切替は#117の責務であり、アプリ本体更新へ統合しません。アプリ本体のVeloPack feedは既定のWindows channelの`releases.win.json`とfull packageを使い、任意version選択、複数channel、署名検証は提供しません。

package生成はmaster/catalog実metadataの一致検証、locked NuGet restore、Release self-contained publish、VeloPack packagingを順に実行します。入力DBと成果物はGit管理しません。versionだけを変えて同じrepository revisionと同じ2 DBから再実行できます。

## アプリ本体の更新

productionのインストール済みVeloPack packageでは、main windowを表示して通常利用を開始した後にstable GitHub Releaseをバックグラウンドで確認します。更新確認は30秒、package downloadは30分の全体上限とdownload requestごとの5分上限を持ち、アプリ終了要求時はCancellationTokenで中断できます。確認失敗、GitHub到達不能、download中断、対応しない起動方法では現在のversionをそのまま使えます。更新確認は`更新を確認`、更新がある場合のdownloadと適用は`更新して再起動`を明示的に押して行います。

download後は既存の明示終了経路でpending picker、監視、capture worker、解析・保存workflow、runtime、open handleを先に停止・完了させてからVeloPackの`WaitExitThenApplyUpdates`を使います。updater起動後の終了処理が失敗しても最終終了要求を行い、通常利用へ戻らない経路を維持します。準備段階の失敗ではupdaterを起動しません。VeloPackの起動時自動適用は無効にしているため、ユーザーが適用を選ぶまで次回起動へ強制連鎖しません。アプリ更新のpackageはapp binary/runtimeだけを置き換え、`%LOCALAPPDATA%\DDRGpScoreViewer`配下のscore DB、settings、reference DB、ログは更新対象にしません。

## 初回導入と通常操作

1. 起動中の旧版があればtrayの`終了`で明示終了する。
2. Setupを実行する。未署名のためWindows SmartScreen等の警告が出る場合は、配布元とhashを確認した本人だけが続行する。
3. install後に自動起動した`GP Score Log`で、M4 master DB、M5b jacket reference catalog、score DBの表示を確認する。初回起動は組み込みreference data setをproduction固定pathへ配置し、master/catalog検証後にmissingまたは0 byteのscore DBだけを正式schemaへ初期化する。
4. DDR GRAND PRIXを`1280x720` client sizeで起動する。「アプリ起動時に監視を開始」がONなら自動監視が2回連続で対象を検出すると開始し、対象が一意に見つからない場合は待機する。手動で開始する場合は`監視開始`を押す。
5. 一時停止は`監視停止`、手動停止後の同一app session内の再開は`監視開始`を明示する。window終了後の自動復帰は、windowが一度消失してから再出現した場合だけ行う。×ボタンはtray格納、最小化はtaskbar最小化、完全終了はtrayの`終了`を使う。

起動時の自動監視は設定で切り替えられ、初期値は有効です。production起動時はmain windowを表示してからreference DBとアプリ本体のlatest Release確認を開始し、更新・reference DB処理中は設定がONでも自動開始を抑止します。通信できない場合も既存reference DBと現在のアプリversionで通常利用を続けます。

## Reference data setの配置・更新・復旧

production固定pathは`%LOCALAPPDATA%\DDRGpScoreViewer\data\master\`です。起動時はlatest GitHub Releaseのmanifestを先に取得し、asset名 `reference-set.json`、`ddrgp-master.sqlite`、`jacket-catalog.sqlite` の3つが同じReleaseに存在することを確認します。候補は`data`配下の一時directoryへ保存し、2 DBをread-only openしてschema version、content version、catalog内referenceのmaster version整合、manifest checksumを検査します。初回は検証済みの2 DBとmanifestをセットで配置します。更新時は`content_version`が現在より新しい場合だけ、現行の`master` directory全体を`data\.reference-previous\`へrenameしてから候補directoryを`master`へrenameし、切替後に再openします。master/catalogの片方だけをrenameしないため、更新途中に新旧fileを混在させません。切替後の再検証失敗は直前directoryへ戻し、同一versionはno-op、古いversionは拒否、asset欠落・不整合・通信失敗・download中断・空き容量不足は現行セットを変更しません。保持するのは現行と直前1世代だけで、download stagingは処理後に削除します。

アプリ更新やreference data set更新は`data\score\score.db`、`viewer-paths.json`、`user-settings.json`を変更しません。VeloPackのinstall directoryは`%LOCALAPPDATA%\com.tts1374.ddrgp_scorelog`、永続dataは別の`%LOCALAPPDATA%\DDRGpScoreViewer`なので、uninstallしてもscore DB、settings、ログ、配置済みreference DBは残ります。不要になった場合だけ、backup確認後に利用者が永続data directoryを手動削除します。

## Releaseログとdata保持

`%LOCALAPPDATA%\DDRGpScoreViewer\logs\gp-score-log.log`へ、起動・終了、app version、DB検証、reference data set処理、監視状態、保存集計、重大例外を記録します。Level画像認識は`level_recognition`の構造化JSONイベントとして、event ID、status、認識桁、候補、距離、margin、適用閾値、理由を記録します。ログには画像や正式個人スコアDBの診断値を保存しません。5MB到達時にrotationし、現行を含め最大3 fileです。Release版は失敗画像、詳細解析中間情報、runtime stdout/stderrを既定保存しません。Debug buildだけが既存の詳細artifactを生成できます。

| data | 保持 |
| --- | --- |
| `data\score\score.db`、`viewer-paths.json`、`user-settings.json` | 無期限。利用者がbackup確認後に削除するまで保持 |
| 利用者が作成した個人スコアバックアップJSON | 選択した保存先で利用者が管理。アプリは自動削除・自動アップロードしない |
| `data\master\` と `data\.reference-previous\` | reference data setの現行＋直前1世代 |
| score migration backup | `data\score\migration-backup\score.db.bak`の最新1件 |
| Release log | 5MB × 3 file |
| `data\cache\`、`data\temp\`、reference download staging | 処理完了時または次回起動時に削除 |

## 個人スコアデータのバックアップ / 復元

データ管理画面の `バックアップを作成` は、現在の正式個人スコアDBをread-onlyで検証し、保存済みプレー履歴だけをUTF-8（BOMなし）JSONへ書き出します。バックアップファイルの保存先はユーザーが選択します。設定、保存済みpath、楽曲・譜面マスタ、jacket参照、source capture、解析ログ、診断ログは含めません。migration用のSQLite file backupとは別の形式・用途です。

`バックアップから復元` はJSON全体を先に検証し、形式が未対応または壊れている場合は正式DBを変更せずにエラーを表示します。対応形式の復元でも、確認ダイアログでユーザーが続行した場合だけ現在のプレー履歴を置き換えます。置換前の未解決を含む解析ログと取得元は保持し、旧プレーへの参照だけを切り離します。置換は既存の正式schemaを検証したSQLite transaction内で行い、失敗時はcommitせず、完了後に既存のread-only viewerで履歴・自己ベストを再読込します。設定、同梱楽曲・譜面データ、jacket参照は変更しません。復元後の取得元・解析ログはバックアップから復元せず、履歴表示に必要な最小の内部参照だけをアプリが再構成します。

キャンセル、未対応ファイル、壊れたファイル、実行中の保存・監視・更新・終了処理中は復元を開始しません。通常起動時のDB初期化、Debug/Releaseの開発者向け操作、既存の正式保存workflowはこの操作で変更しません。

## トラブルシューティング

- `DDR GRAND PRIX windowを自動検出できません`: `ddr-konaste`が1 processだけでclient `1280x720`か確認する。自動監視は一時的な探索失敗や0件を待機として再探索し、条件に一致するwindowが2回連続で見つかると開始する。手動停止後は同一app session中に自動再開しないため、必要なら`監視開始`を明示する。
- `reference DBを更新できませんでした`: GitHub到達、Releaseの3 asset、空き容量を確認する。現行reference DBは保持され、score DBとsettingsは変更されないため、オフラインのまま通常利用できる。正しい新versionを再公開した後にアプリを再起動する。
- `アプリ更新を確認できませんでした` / `アプリ更新のdownloadに失敗しました`: GitHub到達、stable Releaseの`releases.win.json`とfull package、空き容量を確認する。現在のアプリversion、score DB、settings、reference DB、ログは保持されるため、そのまま通常利用するか、Release修正後に`更新を確認`を再実行する。unsigned packageのためSmartScreen等の警告は解消しない。
- master DB missing / incompatible: installerを同じversionで再実行しても同一versionは上書きしない。ログのreference data set結果を確認し、正しい新versionのinstallerを再配布する。score DBは変更されない。
- jacket catalog異常: master DBと別fileとして拒否される。片方だけ手動交換せず、正しいセットのinstallerを使用する。
- capture failure、resize、target close、device lost: 状態を確認し、windowを`1280x720`へ戻すか再起動してから、停止完了後に`監視開始`を明示する。未完了結果は保存されない。
- workflow failure / 保存不能: 表示された「データが変更されたか」を確認する。保存済み件数があれば履歴を確認し、失敗分だけ次のRESULTで再試行する。詳細pathと例外はReleaseログを確認する。
- score DB拒否: 新しいschema、unknown、preview、identity mismatch、converterなし旧schemaは変更しない。アプリを終了してbackupを取り、対応版または明示converterのあるversionを使う。手動repairはしない。

## M10-3保証範囲・既知制限・release停止条件

実機評価はWindowsの`ddr-konaste`、client `1280x720`、SINGLE 28曲・29譜面の94 RESULTです。`saved=94`、他status=0、自動保存成功率100%で、全件を画面と正式DBで目視照合し、誤保存0件でした。target close、resize、tray exit、再起動・固定path再利用を確認し、二重保存はありませんでした。

保証範囲はこのWindows環境と`1280x720`条件に限定します。対象外解像度、全GPU、意図的なdevice lost、0点RESULTは実機保証外です。未署名installerはSmartScreen警告があり、code signing、telemetry、cloud backup、強制更新、複数channelはありません。自動監視は設定で切り替えられ、初期値は有効ですが、手動停止後の同一app session内再開、DB・runtime異常時の開始、更新・終了処理中の開始は保証しません。アプリ本体はstable Releaseのfull packageをユーザー操作で更新し、reference DBはlatest Releaseの3 assetを起動後に確認しますが、任意version選択と署名検証は行いません。

次のいずれかがあるreleaseは完成扱いにしません: 誤保存が1件以上、固定条件の自動保存成功率が95%未満、既定CI失敗、VeloPack package/clean環境相当smoke失敗、reference DBのセット検証・rollback失敗、既存score DBの上書きまたはrestore不能、Release buildへの開発者向け操作混入。device lostと対象外環境は既知制限として扱い、保証範囲を暗黙に拡張しません。

## UI resources

- `Resources/Theme.xaml`: light themeの色トークンと難易度色
- `Resources/Components.xaml`: button、sidebar、card、table、badgeの共通style
- `Controls/StatePanel.xaml`: 空状態・エラー状態の共通component

今回の画面範囲は共通sidebar、ホーム、自己ベスト、検索・絞り込み、プレー履歴、プレー詳細とグラフ、設定、データ管理、個人スコアデータのバックアップ・復元、楽曲・譜面データのread-only状態表示、Debug buildの開発者向け単発操作、監視surface（自動保存できない結果の通知と自動再接続を含む）、master DB検証表示、event単位保存workflow、task tray lifecycleです。Release buildの通常画面には開発者向け領域を含めず、個人スコアデータ操作と`監視開始`・`監視停止`だけを残します。保存できない結果の専用確認画面は設けず、通知とlogで理由を確認します。
