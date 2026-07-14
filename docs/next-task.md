# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、`docs/implementation-roadmap.md` の M9残り実行順5、WPF capture/session/save workflow設計、`app/src/DDRGpScoreViewer` と関連testsを読み、既存のローカルcapture、正式個人スコアDB、master DB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

長寿命の監視状態、WPF UI thread、capture resource、明示開始・停止、manual/capture-save排他、task tray lifecycle、保存結果の状態写像を同時に扱うためです。正式DB schemaや保存値昇格policyは変更しないため、通常はxhighまで上げません。

## 作業ブランチ

```powershell
codex/m9-monitoring-ui-task-tray
```

## Goal

既存の明示continuous capture + save workflowを、WPFの監視状態モデルとtask trayから安全に開始・停止・確認できるようにします。対象window状態、capture状態、最新の保存成功・skip・解析失敗を1つの監視surfaceへ統合し、windowを閉じてもユーザーが明示終了するまではtrayから状態確認と停止を行えるようにします。

このPRはM9残り実行順5です。長時間soak、再起動・自動再接続、resource leak最終監査、installer、配布、M10精度保証には進みません。

## Deliverables

- `idle`、`selecting_target`、`monitoring`、`stopping`、`stopped`、`target_closed`、`resized`、`device_lost`、`capture_failed`、`workflow_failed` を区別する監視状態modelと状態遷移を追加する。
- 既存continuous captureとcapture-save workflowを再利用し、開始・停止を明示操作だけに限定する。二重開始、停止競合、manual saveとの共通排他を維持する。
- 対象windowの選択済み/閉鎖/resize/device lost、取得frame数、開始時刻、最新event時刻をWPFへ表示する。window titleなど取得可能な識別情報は表示用に限定し、対象自動探索へ使わない。
- 最新保存結果を `saved`、`duplicate`、`excluded`、`unresolved`、`analysis_failed`、`db_rejected`、`workflow_failed` の件数・時刻・理由で表示し、savedだけを成功playとして扱う。
- task tray iconとcontext menuを追加し、監視開始、監視停止、メインwindow表示、アプリ終了を提供する。trayの終了は監視停止とresource解放を待ってからprocessを終了する。
- メインwindowのclose/最小化とtray常駐の契約を明示し、通常closeで監視中resourceやworkflowを孤立させない。ユーザーの明示終了と単なるwindow非表示を区別する。
- 通知は保存成功、監視停止を要する重大失敗など最小限にし、duplicate/unresolvedの連続通知spamを避ける。通知なしでもUI/trayから最新状態を確認できる。
- 監視状態とtray操作のunit test、ViewModel test、capture fakeを使った状態遷移testを追加し、Windows Graphics Capture実機を通常testの必須条件にしない。
- `docs/implementation-roadmap.md`、関連 `docs/design/`、WPF README/画面仕様を実績に同期する。

## Invariants

- pickerと監視開始はユーザーの明示操作でだけ実行し、DDR GRAND PRIX window自動探索、ゲーム操作自動化、自動再接続を行わない。
- `連続取得・保存` の既存manifest、分類、confirmed event、candidate/formal値境界、正式workflow、DB transaction、duplicate keyを変更しない。
- candidate、OCR raw、expected、preview、相対時刻を正式playへ暗黙昇格しない。
- `saved` かつtransaction済み非null `play_id` だけをviewerへ再読込し、duplicate、excluded、unresolved、解析/DB失敗を成功表示しない。
- manual saveとmonitoring capture-saveの `IsSaving` 共通排他を維持し、同一正式DBへの並行writerを開始しない。
- stop、target closed、resize、device lost、app exitでcapture session、frame pool、queue、event購読、tray resourceを一度だけ解放する。
- 正式個人スコアDB schema version 1、M4 master DB、M5b jacket catalogを変更しない。
- ローカルcapture、DB、ログ、status履歴をGit、CI artifact、Releaseへ含めない。

## Validation

- 状態遷移、二重開始、停止冪等性、stop中再開始拒否、0frame停止、target closed、resize、device lost、capture/write/workflow失敗をfakeで固定する。
- manual save中の監視開始拒否、監視中のmanual save拒否、終了時stop待機、commit済みpartial successの表示を確認する。
- tray menuの開始/停止enable状態、window表示、close-to-tray、明示exit、resource disposeをfixtureで確認する。
- saved/duplicate/excluded/unresolved/analysis_failed/db_rejected/workflow_failedの表示写像と、savedだけのread-only reloadを確認する。
- 関連 .NET test、`dotnet build`、影響するPython workflow test、Ruff、compileall、`git diff --check` を実行する。
- 利用可能なら実windowで明示開始・停止・tray復帰を1回目視確認する。実機/GUI確認が必須になった場合は `AGENTS.md` の `ユーザー対応が必要` 形式で依頼し、fixture検証を止めない。
- 画像分類、ROI、OCR、M5/M7a、confirmed-events生成を変更しない場合は `python -m tools.vision_poc` 本体を省略し、理由と残るリスクを報告する。

## Non-goals

- 対象window自動探索、自動再接続、ゲーム操作自動化
- 長時間soak、再起動復旧、resource leak最終監査、M9完了判定
- installer、auto-update、Release、配布、telemetry
- 正式個人スコアDB schema/migration/backup/repair、保存workflow変更
- M5b catalog収集・review UI、master DB更新実装
- OCR、ROI、identity、数字認識、正式値昇格policyの変更

## Acceptance Criteria

- ユーザーがWPFまたはtrayから監視を明示開始・停止でき、現在状態、対象window状態、最新保存/skip/失敗を確認できる。
- 二重開始、停止競合、manual saveとの並行実行が発生せず、終了時にcapture/tray resourceが解放される。
- savedだけが成功playとしてviewerへ反映され、duplicate、excluded、unresolved、解析/DB失敗が成功へ丸められない。
- target closed、resize、device lost、capture/write/workflow失敗が個別状態と理由で残り、安全に停止する。
- fixtureで主要状態遷移とtray lifecycleを再現でき、read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、現在のbranchへ通常pushしてdraft PRを作成してください。
