# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、`docs/implementation-roadmap.md` の M9残り実行順6、WPF monitoring/task tray設計、capture/session/save workflow設計、`app/src/DDRGpScoreViewer` と関連testsを読み、既存のローカルcapture、正式個人スコアDB、master DB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

xhigh

長時間運用、再起動、複数回の明示再接続、WPF/tray/capture resourceの所有権、部分成功後の復旧、leak監査を同時に扱う最終M9監査のためです。正式DB schemaや保存値昇格policyは変更せず、maxは原則不要です。

## 作業ブランチ

```powershell
codex/m9-operational-hardening
```

## Goal

既存の明示監視workflowを、長時間利用、アプリ再起動、対象終了後の明示再選択、反復開始・停止、主要失敗からの再試行に耐える運用状態へ固め、M9完了条件を検証します。capture、workflow、tray、window、processのresource所有権と解放を監査し、保存成功・skip・解析失敗を通常ログと解析失敗ログから追跡できる状態にします。

このPRはM9残り実行順6です。installer、配布、auto-update、Release、M10精度保証、ROI調整UI、正式DB migrationには進みません。

## Deliverables

- 監視開始・停止、0frame、target closed、resize、device lost、capture/write/workflow失敗、partial success後に、同一processでユーザーがwindowを明示再選択して次sessionを開始できる復旧契約を固定する。
- app restart後に前sessionのin-memory状態やtray resourceを復元したものとして誤解せず、`idle` から明示選択で安全に再開できることを固定する。未完了stagingは完成sessionとして読まない。
- capture session、frame pool、D3D device、bounded queue、event購読、workflow process、read-only DB connection、tray icon/context menu、window event購読の所有者とdispose順序を監査し、反復開始・停止と明示exitで一度だけ解放する。
- 通常ログと解析失敗ログの最小ローカル契約を追加または既存契約へ接続し、app起動・終了、明示開始・停止、対象状態、master version、saved/skip/failure、例外理由を追跡できるようにする。画像、実入力、DB本文をlogへ埋め込まない。
- log path、retention、rotationが未決定なら、このPRに必要な最小のbounded local policyだけを設計docsへ明示する。既存logやcaptureの削除、migration、起動時cleanupは行わない。
- fake clock/fake capture/fake workflow/fake trayを使った反復・復旧・終了testと、resource create/dispose countを固定するtestを追加する。
- 短い自動soak fixtureで複数sessionの開始・frame progress・停止・失敗・再開始を反復し、handle/task/event購読数がsession終了後のbaselineへ戻ることを確認する。
- 利用可能なら実windowで開始・停止・close-to-tray・tray復帰・target終了後の明示再選択・明示exitを確認し、M9完了判定と残課題を記録する。
- `docs/implementation-roadmap.md`、関連 `docs/design/`、`app/README.md` を実績とM9完了判定へ同期する。

## Invariants

- pickerと再接続はユーザーの明示操作でだけ実行する。DDR GRAND PRIX windowのtitle探索、自動window探索、無断の自動再接続、ゲーム操作自動化は行わない。
- 既存continuous capture manifest、分類、confirmed event、candidate/formal値境界、正式workflow、DB transaction、duplicate keyを変更しない。
- candidate、OCR raw、expected、preview、相対時刻を正式playへ暗黙昇格しない。
- `saved` かつtransaction済み非null `play_id` だけをviewerへ再読込し、partial successでもその他statusを成功表示しない。
- manual saveとmonitoring capture-saveの共通排他を維持し、同一正式DBへの並行writerを開始しない。
- stop/exit/restart相当のcleanupは冪等にし、既存ローカルcapture、正式DB、master DB、logを削除・修復・上書きしない。
- 正式個人スコアDB schema version 1、M4 master DB、M5b jacket catalogを変更しない。
- ローカルcapture、DB、通常log、解析失敗log、soak出力をGit、CI artifact、Releaseへ含めない。

## Validation

- 50回以上のfake session反復で、正常停止、0frame、target closed、resize、device lost、capture/write/workflow失敗、partial success、明示再選択を混在させる。
- 各反復後にactive capture、queue、event購読、workflow process、DB connection、tray resourceのcreate/dispose countと未完了Taskを確認する。
- 二重開始、stop中再開始、反復stop、exit競合、window close/minimize、manual save競合、app restart相当の新規composition rootを固定する。
- logの正常系、空理由、例外文字列、連続同一skip、partial success、write失敗を確認し、秘密情報・画像本文・DB本文を含めない。
- 関連 .NET test、`dotnet build`、影響するPython workflow test、Ruff、compileall、`git diff --check` を実行する。共通runnerやlog helperへ影響する場合は全testも実行する。
- 画像分類、ROI、OCR、M5/M7a、confirmed-events生成を変更しない場合は `python -m tools.vision_poc` 本体を省略し、理由と残るリスクを報告する。
- 実機/GUI確認が必須になった場合は、fixture検証を止めず、`AGENTS.md` の `ユーザー対応が必要` 形式で明示する。

## Non-goals

- 対象windowのtitle/process自動探索、無断の自動再接続、ゲーム操作自動化
- installer、auto-update、Release、配布、telemetry、Windows自動起動登録
- M10の精度保証値、実機評価セット拡張、OCR/ROI/identity方式変更
- ROI調整画面、失敗ログ閲覧UI、検索、グラフ、マスタDB更新実装
- 正式個人スコアDB schema/migration/backup/repair、保存workflow変更
- 既存capture/logの削除、retention cleanup scheduler

## Acceptance Criteria

- 長時間相当の反復fixtureと主要失敗後の明示再選択で、新sessionを同一process内に安全に開始できる。
- stop、target closed、resize、device lost、capture/write/workflow失敗、window非表示、明示exitの各経路でresourceが一度だけ解放され、dangling task/event購読が残らない。
- restart相当の新規app compositionは常にidleから始まり、前sessionをmonitoring/savedとして誤表示しない。
- saved/skip/解析失敗と主要lifecycleがローカルlogで追跡でき、private capture/DB本文や秘密情報を含まない。
- M9完了条件に対する自動検証結果と、任意または必須の実機確認結果がdocsへ記録される。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、現在のbranchへ通常pushしてdraft PRを作成してください。
