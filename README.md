# GP Score Log

GP Score Logは、DanceDanceRevolution GRAND PRIXのゲーム画面を監視し、十分な認識根拠がそろったスコアだけをローカルの個人スコアDBへ保存・閲覧するWindowsアプリです。保存できない結果は正式DBへ書き込まず、理由を通知します。

## 対応環境と対応範囲

- Windows 11
- Release packageに必要なruntime資材を同梱します。利用者が.NET SDKやPythonを別途用意する必要はありません。
- 対応プレー: DDR GRAND PRIXのグランプリプレー。SINGLE (SP) と DOUBLE (DP) の譜面を扱います。
- 「アーケードプレミアムプレー」および「アーケードノーマルプレー」は、認識・自動保存の対応対象外です。

## 最新版を入手

[GitHub Releasesの最新版](https://github.com/tts1374/ddrgp_scorelog/releases/latest)からWindows用Setupを入手してください。未署名packageのため、Windows SmartScreenなどの警告が表示されることがあります。Releaseページとhashを確認できる場合に限り、配布元を確認したうえで続行してください。

## Quick start

1. [最新版のRelease](https://github.com/tts1374/ddrgp_scorelog/releases/latest)からSetupをダウンロードしてインストールします。
2. `GP Score Log`を起動し、画面に表示される楽曲・譜面データ、jacket reference、個人スコアDBの状態を確認します。
3. DDR GRAND PRIXをclient size `1280x720`で起動します。
4. 「アプリ起動時に監視を開始」がON（初期値）なら、対象windowが見つかるまで待ちます。手動で始める場合は`監視開始`を押します。
5. RESULTをプレーすると、保存できる結果だけが自動保存されます。終了するときは`監視停止`を押してから、task trayの`終了`を選びます。

ゲームwindowがまだ起動していない場合は待機が続きます。対象windowの検出、認識、保存に失敗した結果は正式DBへ保存されません。

## 主な機能

- DDR GRAND PRIXのRESULTを自動監視し、SINGLE / DOUBLE、難易度、スコア、判定数、MAX COMBO、EX SCORE、rank、CLEAR、FLAREを認識できた結果だけ保存
- ホームで今日のプレー状況、最新プレー、自己ベスト更新を確認
- 自己ベストでSINGLE / DOUBLE、難易度、レベル、曲名などから絞り込み
- 楽曲・譜面詳細で自己ベスト、スコア推移、保存済みプレーを確認
- プレー履歴で保存済みプレーを検索し、行ごとの詳細を確認
- 設定で起動時監視、保存できない結果の通知、既定のプレイスタイル、起動時画面、表示言語を変更
- データ管理で個人スコアデータをbackup / restoreし、同梱された楽曲・譜面データの状態を確認
- GitHub Releasesからアプリ本体とreference data setを安全に更新

## 保存の安全境界

画面がRESULTらしく見えるだけでは保存しません。認識根拠、現在のmaster/reference dataとの整合、capture eventの重複確認がそろった結果だけを正式個人スコアDBへ保存します。低確信度、不完全な結果、重複、DB検査失敗、解析失敗は保存せず、すでに保存済みのプレーを変更しません。

保存できない結果が発生した場合は、WPF画面またはtask trayへ「自動保存できないプレーが発生しました。正式DBには保存されていません。」という通知を表示します。通知は次のcapture event処理を妨げず、同じeventを繰り返し通知しません。

## 利用者向け文書

- [利用ガイド](docs/user-guide.md): インストール、通常操作、設定、backup / restore、更新、終了、トラブルシューティング、既知制限の正本
- [利用ガイドのトラブルシューティング](docs/user-guide.md#トラブルシューティング)
- [最新版のGitHub Release](https://github.com/tts1374/ddrgp_scorelog/releases/latest)

## 開発者向け文書

- [要求定義](docs/requirements.md)
- [実装ロードマップ](docs/implementation-roadmap.md): milestoneの目的と現在地
- [Windowsアプリの技術README](app/README.md): build、Debug操作、runtime、保存境界、package生成、validation
- [設計資料](docs/design/): 入力、event、保存、DB、I/O、回帰ガード
- [マスタDB生成](master/README.md)
- [画像解析PoC](tools/vision_poc/README.md)
- [jacket catalog collector](tools/jacket_catalog_collector/README.md)

アプリ挙動、配布物、保存形式は変更せず、通常利用の詳しい手順だけを利用ガイドへ集約しています。開発用コマンド、package生成、repository内部構成は開発者向け文書を参照してください。
