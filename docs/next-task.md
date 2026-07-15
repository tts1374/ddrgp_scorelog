# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、`docs/implementation-roadmap.md` の M5c、`docs/design/09_master_match_poc.md`、`tools/jacket_catalog_collector/`、公開app側の既存Windows Graphics Capture実装をread-only参照し、master/catalog DB、capture、crop、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

xhigh

window候補識別、明示確認、capture resource lifecycle、ring buffer、中断・対象終了・device lossの競合を同時に固定するためです。catalog観測生成やOCRへは進みません。

## 作業ブランチ

```powershell
codex/m5c-window-capture-lifecycle
```

## Normalized Summary

M5c-3aとしてdeveloper-only jacket catalog collectorへ、DDR GRAND PRIX window候補検出、preview付きの明示選択、Windows Graphics Captureの明示開始・停止、bounded raw-frame ring buffer、window/resource lifecycleを追加します。

このPRはcapture lifecycleの観測と安全な停止だけを扱います。jacket変化/安定検出、session checkpoint、観測自動生成、catalog投入、title/artist OCR、ゲーム操作は後続PRへ残します。

## In Scope

- top-level windowを列挙し、DDR GRAND PRIX候補のhandle、process、title、client size、visible/minimized状態、候補理由をdeveloper UIへ表示する。
- 自動候補は確定windowとして扱わず、開発者がpreviewと根拠を確認して1件を明示選択する。
- 選択windowに対しWindows Graphics Captureを明示開始・明示停止し、最新preview、取得件数、drop件数、開始時/現在のsize、window/resource状態を表示する。
- raw frameは固定上限のin-memory ring bufferに保持し、古いframeをdropする。通常停止・取消・window消失・resize・device loss・capture例外でresourceを決定的に解放する。
- handle再利用、選択後のprocess/title/size変化、最小化、閉鎖、再選択、二重開始、二重停止、停止中のin-flight frameを安全に扱う。
- fake window enumerator/capture session/frame source/dispatcherで通常系、空候補、誤候補、競合、resource解放をテストする。
- capture frame、preview、diagnosticを既定でdiskへ保存しない。明示diagnostic exportを追加する場合も `data/` 配下の新規pathに限定し、catalogへ接続しない。
- collector README、`docs/implementation-roadmap.md`、`docs/design/09_master_match_poc.md` を同期する。

## Out of Scope

- jacket ROI変化/安定検出、同一preview dedupe、採用frame判定
- session checkpoint、中断再開、観測自動生成、catalog ingest/mutation
- title/artist OCR、auto-confirm条件、manual review契約の変更
- windowへの入力、focus移動、grid巡回、ゲーム操作自動化
- 公開 `DDRGpScoreViewer` のwindow探索契約変更、M9 monitoring/tray、正式保存workflow、正式個人スコアDB
- capture/crop/source imageのretention、cleanup、物理削除
- installer、Release、telemetry、GitHub Actions artifact

## Fixed Decisions

- 候補検出と選択確定を分け、候補が1件でも自動開始しない。開始には現在表示中の候補identityと明示選択を要求する。
- window identityは生handleだけでなくprocess ID、process start identity、title/class、client sizeなど再検査可能なsnapshotを持ち、handle再利用を同一対象と誤認しない。
- capture開始時にwindow identityとsizeを固定し、resize、identity drift、対象終了はsession終了理由として扱う。暗黙再選択・暗黙再開始しない。
- ring bufferはboundedで、producerをUI描画やdisk I/Oで無制限にblockしない。frame/resource ownershipとdispose担当を1箇所に固定する。
- stop/cancel/failureはidempotentで、in-flight callback完了後にresourceを1回だけ解放する。停止後frameをUI/catalogへ渡さない。
- previewは観測用であり、catalog reference、正式値、保存候補、OCR入力へ自動昇格しない。
- 公開appの既存captureコードは再利用可能性を確認するが、developer候補検出の採用を公開appへ逆流させない。

## Pending Decisions

- jacket変化/安定閾値、同一preview判定、採用frame契約
- checkpoint schema、session再開、frame locator、capture/crop retention
- title/artist取得方式、OCR採用条件、auto-confirm閾値
- developer候補条件を公開appへ採用するかどうか

これらは後続PRの仕様判断であり、このPRを止めない。

## Deliverables

- window candidate model/enumerator/strict identity revalidation
- preview付き明示選択UIと開始前確認
- capture session coordinator、bounded frame ring buffer、状態/終了理由model
- start/stop/cancel/window close/resize/device loss/exception時の決定的resource cleanup
- fake依存によるlifecycle・競合・drop・side-effect test
- README・設計docs・次PR仕様の同期

## Boundary Condition Matrix

| 状態/操作 | 期待結果 | 副作用境界/test |
| --- | --- | --- |
| 候補0件 | empty state表示 | capture resource未作成 |
| 候補1件/複数件 | 根拠とpreviewを表示、明示選択待ち | 自動開始なし |
| stale候補で開始 | identity再検査で拒否 | session/ring buffer未作成 |
| 正常開始→frame→停止 | bounded preview/countを更新し停止 | 全resource 1回dispose |
| 二重開始 | 2件目を拒否 | active sessionを維持 |
| 二重停止/取消競合 | 冪等完了 | dispose重複なし |
| ring buffer満杯 | oldestをdropしcount表示 | memory上限維持 |
| resize/handle drift/window close | 明示終了理由で停止 | 暗黙再選択なし |
| device loss/capture例外 | failure表示し停止 | partial catalog/data出力なし |
| stop中in-flight frame | callback drain後破棄 | 停止後UI/catalog更新なし |

## Validation

- .NET: candidate enumeration、identity、ViewModel selection、capture coordinator、ring buffer、resource cleanupの対象testとcollector全test。
- `dotnet build`、collector全test、関連Python test、Ruff、compileall、`git diff --check` を実行する。
- 共通capture helperを変更した場合は公開app側の関連capture testとsolution buildも実行する。
- catalog schema/runtime loaderを変更しないためPython全テストは原則不要。共通helperへ影響した場合は全テストを実行する。
- 画像分類、ROI、OCR、confirmed-eventsを変更しないため `python -m tools.vision_poc` 本体は省略し、理由と残るリスクを報告する。
- 実window/実captureの任意目視確認はmerge条件にせず、fake lifecycle testを必須にする。

## Acceptance Criteria

- window候補が自動確定・自動開始されず、preview/根拠を見た明示選択だけでcaptureを開始できる。
- bounded ring bufferが上限とdropをテストで固定し、disk/catalogへ暗黙出力しない。
- stop/cancel/close/resize/device loss/例外でresource leak、二重dispose、停止後frame反映が起きない。
- handle再利用やstale UIから別windowを誤captureしない。
- catalog v1/v2、manual history、runtime matcher、公開app、正式保存workflowを変更していない。
- read-only branch diffレビューでmedium以上の未対応指摘がない。

## Open Risks / Blockers

- Windows Graphics CaptureはOS/driver/window状態に依存するため、実機確認は任意補助とし、merge gateはfake resource ownershipと既存capture testに置く。
- Draft PR #27など別trackもroadmapを変更し得る。後からmergeするbranchが最新mainを取り込み、M5cとM9の両方を残してdocs競合を解消する。

完了後はM5c-3bのjacket変化/安定検出・checkpoint・観測生成を独立した次PR仕様へ更新し、今回変更だけをcommit、現在のbranchへ通常pushしてdraft PRを作成してください。更新後のM5c-3bには着手しないでください。
