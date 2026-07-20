# 実装ロードマップ

DDR GRAND PRIXのゲーム画面からスコア情報を取得し、十分に確認できた結果だけをローカルDBへ保存・閲覧できる初期版までの計画です。

具体的な作業内容、依存関係、Acceptance criteriaはGitHub Issuesを正本とします。この文書は各milestoneの目的、現在地、次の移行条件だけを示し、完了済みPRの詳細や一時的な次作業は保持しません。

## Product Goal

Windowsアプリが明示選択されたDDR GRAND PRIXのwindowをcaptureし、リザルト画面を検出・解析する。解析結果が必要な根拠を満たし、同一playの重複でない場合だけ、正式個人スコアDBへ1play 1recordとして保存する。

次のデータ境界を分離して維持します。

- マスタDB: 楽曲・譜面情報
- 正式個人スコアDB: 確定したplay履歴
- capture画像・manifest: ローカル原本
- 解析artifact・log: 候補、根拠、失敗理由
- developer-only catalog: jacket候補収集とmanual review

candidate、OCR raw、expected値、preview材料を正式値へ暗黙昇格させません。低確信度、不一致、解析失敗、重複疑いは正式DBへ保存せず、理由を確認できる状態を維持します。

## Current Phase

現在はM9の監視運用を受け入れ可能な状態へ収束させ、その後M10で本人が継続利用できる初期版として固定する段階です。

- M9-5: Issue #61でPR #27の監視UIとtask trayを受け入れ可能な状態へ収束させる
- M9-6: Issue #62で長時間動作、再起動、window再選択、master DB再検証を確認する
- M10: Issue #63で配布・導入・backup・運用手順を初期版として固める
- M10-1: Issue #66でPython / NuGet依存関係を固定し、再現可能なbuildへ移行する
- M10前提: Issues #55〜#60でmanual review機能と実データ整備を完了する

## Milestone Status

| Milestone | Status | Outcome |
| --- | --- | --- |
| M0 | 完了 | timestamp付きframe列、manifest、dry-run capture providerの入力境界 |
| M1 | 完了 | confirmed result、duplicate、transition除外を分ける保存直前event契約 |
| M2 | 完了 | 数字ROIのOCR profile評価と保存候補品質の確認 |
| M3 | 完了 | 曲・譜面候補照合に必要な参照・template・診断 |
| M4 | 完了 | 楽曲・譜面マスタDBと照合境界 |
| M5 | core完了 | jacket候補観測、catalog、identity、候補評価・登録pipeline |
| M5c | 進行中 | developer-only manual review UI、ODS export/import、一括反映、実データ整備（#55〜#60） |
| M6 | 完了 | 保存直前payloadと解析根拠の分離 |
| M7 | 完了 | 必須数字fieldの抽出・検証と正式値への変換境界 |
| M8 | 完了 | 正式個人スコアDB version 1、duplicate、transaction、単発保存 |
| M9 | 進行中 | WPF viewer、Windows capture、capture-save、監視UI、task tray、実運用確認 |
| M10 | 未完了 | 初期版リリース、再現可能なbuild、導入・backup・復旧手順 |

完了済みmilestoneの詳細な実装判断と検証結果は、関連するmerged PR、`docs/design/`、ADR、各component READMEを参照します。

## M9 Completion

M9では新しい解析方式やDB schemaを増やさず、既存のcapture-save経路を通常利用できるWindowsアプリへ接続します。

完了条件:

- 監視状態、対象window、capture進捗、最新event結果を確認できる
- 保存成功、duplicate、excluded、unresolved、解析失敗、DB拒否、workflow失敗を混同しない
- task trayから開始、停止、画面表示、明示終了を実行できる
- close / minimizeで監視resourceを意図せず破棄しない
- 明示終了時に新しい処理を開始せず、進行中resourceを既存契約の範囲で停止・解放する
- 数時間程度の通常監視、app再起動、window再選択、master DB再検証から復帰できる
- resource使用量が明らかに増え続けないことを確認する

M9では自動window探索、汎用scheduler、永続queue、複数process協調、厳密なSLA、長期間の耐久試験を要求しません。

## M10 Initial Release

M10では、単一ユーザーがローカルPCで継続利用できる初期版として、導入とデータ保全の境界を固定します。

必須項目:

- installerまたは採用した配布方法で、本人が再現可能に導入できる
- 正式DB、必要な設定、master DBの保存場所を説明できる
- 正式DBと必要な設定を手動でbackup / restoreできる
- uninstall時に正式DBやlocal dataを意図せず削除しない
- 実行に必要なPython / .NET runtimeまたは同梱方針を固定する
- Python依存を`uv.lock`、NuGet依存を`packages.lock.json`で固定する
- CIとローカルbuildがlock fileを暗黙更新せず、固定状態から再現できる
- Release対象とdeveloper-only tool / local dataを分離する
- 既知の制限、未実施の実機確認、復旧手順をREADMEまたはrelease docsへ記載する

初期版では次を要求しません。

- 商用コード署名
- auto update
- cloud backup / 同期
- telemetry、remote monitoring、中央管理
- 無停止migration、自動rollback、災害復旧framework
- 複数ユーザー、複数PC、複数process writer対応

## Dependency Management

Issue #66完了前は`pyproject.toml`と`.csproj`を依存manifestとし、CIで通常のinstall / restoreを検証します。

Issue #66で次へ移行します。

- Python: `uv.lock`をcommitし、通常開発とCIで`uv sync --frozen`を使用する
- NuGet: appとtest projectの`packages.lock.json`をcommitし、locked restoreを使用する
- Dependabot: Python、NuGet、GitHub Actionsをmonthlyで更新し、ecosystem単位にgroup化する
- dependency更新PRは自動mergeせず、CI成功と変更内容を確認する

lock file更新はdependency変更を目的とするPRに限定し、機能変更PRへ無関係なlock差分を混入させません。

## Change Control

- GitHub Issueを実装契約とする
- 原則として1 Issueを1 PRで実装する
- 親Issueは背景と全体保証範囲を示し、子Issueへ明記されていない要件を暗黙継承しない
- 実装中に判明した別課題は現在のPRへ混入させず、別Issue候補へ送る
- 公開操作、CLI、永続化形式、判定契約、ユーザー手順を変えた場合だけ関連docsを同期する
- roadmapはmilestone状態が変わる場合だけ更新し、個別PRの一時的な次作業を記載しない
