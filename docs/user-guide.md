# GP Score Log 利用ガイド

この文書は、GP Score Logの通常利用手順の正本です。初めて使うときの導入から、監視、保存結果の確認、設定、backup / restore、更新、終了、復旧までをまとめます。build、Debug専用操作、runtime資材、package生成などの開発者向け技術契約は[`app/README.md`](../app/README.md)を参照してください。

## 必要環境と対応範囲

- Windows 11
- Release packageからインストールしたGP Score Log
- DDR GRAND PRIXを起動できる環境
- 監視対象のゲームwindowはclient size `1280x720`を使用します

Release packageにはアプリが使用するruntime資材が含まれるため、通常利用のために.NET SDKやPythonを別途インストールする必要はありません。個人スコアDBとreference dataは利用者のPC内に保存され、外部サービスへ自動アップロードされません。

対応プレーはDDR GRAND PRIXのグランプリプレーです。アプリではSINGLE (SP) と DOUBLE (DP) の譜面を扱います。

「アーケードプレミアムプレー」および「アーケードノーマルプレー」は、認識・自動保存の対応対象外です。

## 最新版の入手

[GitHub Releasesの最新版](https://github.com/tts1374/ddrgp_scorelog/releases/latest)からWindows用Setupをダウンロードします。Releaseページのnotesとasset名を確認し、通常は`com.tts1374.ddrgp_scorelog-win-Setup.exe`を使用します。

packageは未署名です。Windows SmartScreenなどの警告が表示された場合は、Releaseページの配布元とhashを確認できる場合に限り、本人の判断で続行してください。

## インストールと初回起動

1. 起動中の旧版がある場合は、task trayの`終了`で先に終了します。
2. Setupを実行してインストールします。インストール完了後、通常はGP Score Logが起動します。
3. 起動後、画面または「データ管理」で、楽曲・譜面データ、jacket reference、個人スコアDBの状態を確認します。
4. 初回起動では、packageに含まれるreference data setが利用者用の固定pathへ配置されます。個人スコアDBが存在しない、または0 byteの場合だけ空の正式DBを初期化します。

既存の個人スコアDBをアプリが勝手に上書き・repairすることはありません。対応していないDBや壊れたDBは理由を表示して使用を止めるため、先にbackupを確保し、対応するアプリversionを使用してください。アプリからDBの任意pathを選ぶ操作はありません。

## DDR GRAND PRIXの起動と監視

1. DDR GRAND PRIXをclient size `1280x720`で起動します。
2. 設定の「アプリ起動時に監視を開始」がON（初期値）なら、アプリは対象windowを探し続け、条件に合うwindowを2回連続で確認したあと監視を開始します。ゲームを先に起動していない場合は待機します。
3. 手動で始めるときは、WPF画面またはtask trayの`監視開始`を押します。対象windowが1件に定まらない場合は推測で選択せず、監視を開始しません。
4. 監視中はRESULTを自動で認識し、保存結果と監視状態を画面・task trayで確認できます。
5. 一時停止するときは`監視停止`を押します。手動停止後、同じアプリsession中に自動で再開することはないため、再開時は`監視開始`を押してください。

通常の監視では、Windowsの同意やOS/APIの制約によってcapture枠が表示される場合があります。枠の表示は、それだけで認識・保存の失敗を意味しません。

### ウィンドウとtask tray

- メインwindowの×はアプリを終了せずtask trayへ格納します。
- 最小化は通常どおりtaskbarへ最小化します。
- task trayのダブルクリック、または`GP Score Logを開く`でメイン画面を表示します。
- 完全終了はtask trayの`終了`を使います。終了時は監視、進行中の処理、runtime、DB接続を順に停止します。

## 保存成功と保存できない結果の通知

### 保存される結果

RESULT画面であることだけでは正式DBへ保存しません。次の確認がそろった結果だけを保存します。

- 曲・譜面が現在のmasterとreference dataに一意に整合する
- スコア、判定数、MAX COMBO、EX SCORE、rank、CLEARなど必要な値を画像から認識できる
- capture eventが確定し、同じプレーの重複ではない
- 正式DBの検査と保存transactionが成功する

認識できた候補、OCRの途中値、期待値、preview、低確信度の結果を正式値へ補完しません。

### 保存後の確認

transactionが完了したプレーだけが、ホーム、自己ベスト、楽曲・譜面詳細、プレー履歴へ反映されます。`duplicate`、`excluded`、`unresolved`、解析失敗、DB拒否、workflow失敗は保存成功として表示されません。

### 保存できない結果

自動保存できないcapture eventが発生すると、WPF画面またはtask trayへ次の通知を表示します。

> 自動保存できないプレーが発生しました。正式DBには保存されていません。

通知には確定できた理由が補足されることがありますが、確定していない曲名、日時、スコアなどを正式値として扱いません。通知が表示されても、すでに保存済みのプレーは変更されず、監視と次のcapture event処理は継続します。同じcapture eventを繰り返し通知することはありません。通知を非表示にする設定にしても、正式DBへ保存しない安全境界は変わりません。

## 画面の基本操作

### ホーム

ホームでは、今日のプレー数、通常スコアの自己ベスト更新数、EX SCORE更新数、フルコンボ数を確認できます。最新プレー、最近のプレー、最近の自己ベスト更新から、自己ベストやプレー履歴へ移動できます。保存済みプレーがない場合は、次に行う操作を示す空状態が表示されます。

### 自己ベスト

`SINGLE`または`DOUBLE`を選び、難易度、レベル、曲名、バージョン、プレー状況、rank、CLEARなどで譜面を絞り込みます。譜面行を選ぶと楽曲・譜面詳細を開けます。未プレーの譜面も一覧に表示され、記録がない項目は`—`になります。

### 楽曲・譜面詳細

自己ベストから選んだ1譜面について、通常スコアBEST、EX SCORE BEST、rank、CLEAR、FLARE、プレー回数、通常スコアの推移、保存済みプレーを確認できます。`全プレー`と`自己ベスト推移`を切り替えられます。

### プレー履歴

保存済みのプレーを日時、曲名、プレイスタイル、難易度、レベル、通常スコア、EX SCORE、rankなどで確認できます。期間、プレイスタイル、難易度、曲名などで絞り込み、行を選ぶと判定数、MAX COMBO、保存日時、データ取得元を含む詳細を確認できます。

## 設定

設定画面では、次の項目を変更できます。変更内容は「変更を保存」を押すまで実行中の監視へ反映しません。

| 項目 | 初期値 | 説明 |
| --- | --- | --- |
| アプリ起動時に監視を開始 | ON | 起動後に対象windowを自動探索します。OFFでも手動の`監視開始`は使えます。 |
| 保存できない結果を通知 | ON | 自動保存できない結果のローカル通知だけを切り替えます。保存境界は変わりません。 |
| 既定のプレイスタイル | SINGLE | 自己ベストの初期表示をSINGLEまたはDOUBLEにします。 |
| 起動時に表示する画面 | ホーム | 起動後に最初に表示する画面をホーム、自己ベスト、直近プレー履歴から選びます。 |
| 言語 | 日本語 | 表示言語を日本語、英語、韓国語から選びます。 |

「初期値に戻す」は画面上の設定を初期値へ戻します。保存済み設定がない、または読み込めない場合は初期値を使用します。

### 表示言語

言語の変更を保存すると、監視と進行中の処理を安全に停止してアプリを自動的に再起動し、選択した言語を適用します。自動再起動に失敗した場合は、表示された案内に従って手動で再起動してください。

新しい環境では、OSの表示言語が日本語なら日本語、韓国語なら韓国語、それ以外は英語を初期値にします。未翻訳の表示文は日本語を基底言語として表示します。

## データ管理

データ管理では、保存済みプレー件数、自己ベスト譜面数、最後の保存、個人スコアDBの状態、同梱された楽曲・譜面データのversionと収録譜面数を確認できます。

### backup

1. データ管理で`バックアップを作成`を押します。
2. 保存先を選びます。
3. 作成されたJSONを、アプリ本体とは別の安全な場所へ保管します。

backupには保存済みプレー履歴だけが含まれます。設定、保存先path、楽曲・譜面master、jacket reference、source capture、解析ログ、診断ログは含まれません。ファイルはUTF-8（BOMなし）JSONです。

### restore

1. データ管理で`バックアップから復元`を押し、backup JSONを選びます。
2. アプリの検証結果を確認します。
3. 表示された確認ダイアログで続行します。

restoreは現在の個人スコアデータをbackupの内容で置き換えます。キャンセル、未対応形式、壊れたファイル、実行中の保存・監視・更新・終了処理中は復元せず、現在のデータを変更しません。置き換え前の解析ログと取得元は保持されます。復元完了後は履歴と自己ベストを再読込します。

同梱された楽曲・譜面データは読み取り専用です。個別の置き換えや削除は行いません。

## アプリ本体とreference dataの更新

### アプリ本体

production packageでは、main windowを表示して通常利用を開始したあと、stable GitHub Releaseをバックグラウンドで確認します。更新が見つかると、download、通常の終了処理、再起動まで自動で行います。

更新確認、download、適用に失敗した場合やofflineの場合は、現在のversionで通常利用を続けます。アプリ本体の更新は、個人スコアDB、設定、reference data、ログを置き換えません。

### 楽曲・譜面のreference data set

起動時に最新版Releaseの`reference-set.json`、`ddrgp-master.sqlite`、`jacket-catalog.sqlite`を同じReleaseから取得できるか確認します。checksum、DB schema、masterとjacket referenceのversion整合を検査してから切り替えるため、片方だけ古いdataへ切り替わることはありません。

更新に失敗した場合は現在のreference dataを保持し、通信が戻って正しいReleaseが公開されたあとにアプリを再起動して再確認します。現在の個人スコアDBと設定は変更されません。

## 通常終了とアンインストール後のデータ

一時停止は`監視停止`、完全終了はtask trayの`終了`を使います。メインwindowの×はtray格納です。Windowsの終了・ログオフ時も可能な範囲で同じ終了処理を行い、未完了の結果を正式DBへ昇格しません。

個人データはinstall directoryとは別の`%LOCALAPPDATA%\DDRGpScoreViewer`に保存されます。アプリ本体をアンインストールしても、次のデータは残ります。

- 個人スコアDB
- 設定とpath情報
- reference dataの現行世代と直前世代
- Release log

アンインストール前後にデータを残したくない場合は、先にbackupを作成し、不要なことを確認してから利用者自身で`%LOCALAPPDATA%\DDRGpScoreViewer`を削除してください。アプリはこのdirectoryを自動削除しません。

## トラブルシューティング

### DDR GRAND PRIXのwindowを検出できない

DDR GRAND PRIXが起動していること、対象windowのclient sizeが`1280x720`であること、同じprocessに対象候補が複数ないことを確認します。自動監視は一時的な探索失敗では待機を続けます。手動停止後は`監視開始`を押して再開します。

### reference dataを更新できない

GitHubへ接続できること、最新版Releaseに`reference-set.json`、`ddrgp-master.sqlite`、`jacket-catalog.sqlite`の3 assetがあること、空き容量があることを確認します。現行のreference data、個人スコアDB、設定は保持されるため、そのまま通常利用して構いません。正しいReleaseが公開されたあとに再起動してください。

### アプリ更新を確認できない／downloadに失敗する

GitHubへ接続できること、stable Releaseの`releases.win.json`とfull packageがあること、空き容量があることを確認します。現在のversionと個人データは保持されるため、通常利用を続け、Release修正後に再起動して再試行します。

### master DBまたはjacket referenceがmissing / incompatibleになる

個人スコアDBを手動でrepairせず、アプリが表示する理由とReleaseのreference data setを確認します。master DBとjacket referenceを片方だけ手動交換せず、同じReleaseの正しいsetを使用してください。

### capture failure、resize、target close、device lostが起きる

ゲームwindowを`1280x720`へ戻すかゲームを再起動し、現在の停止処理が完了してから`監視開始`を押します。未完了の結果は保存されません。

### 保存できない結果が続く

通知に表示された理由を確認します。すでに保存済みの件数や履歴は変更されないため、履歴を確認し、必要なら次のRESULTで再試行します。Release版のログは`%LOCALAPPDATA%\DDRGpScoreViewer\logs\gp-score-log.log`にあります。

### backupを復元できない

backupが対応形式のJSONで、途中で編集・破損していないことを確認します。復元に失敗した場合、現在の個人スコアデータは変更されません。現在のデータをbackupしてから、別の対応backupを選び直してください。

## 既知制限

- 実機で確認した保証範囲はWindows上のDDR GRAND PRIX、client size `1280x720`、SINGLE中心の評価条件です。対象外解像度、すべてのGPU、意図的なdevice lost、0点RESULTは実機保証の対象外です。
- 「アーケードプレミアムプレー」および「アーケードノーマルプレー」は、認識・自動保存の対応対象外です。
- 未署名installerのためSmartScreenなどの警告が表示されることがあります。code signing、cloud backup、telemetry、複数channel、任意version選択、署名検証は提供しません。
- 手動停止後の同一app session内の自動再開、DB・runtime異常時の自動開始、更新・reference data処理中の自動開始は行いません。
- 通知履歴、候補の手動訂正、救済保存、専用の保存不能結果確認画面はありません。

## 関連文書

- [ルートREADME](../README.md)
- [Windowsアプリの技術README](../app/README.md)
- [実装ロードマップ](implementation-roadmap.md)
- [設計資料](design/)
