# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、`docs/implementation-roadmap.md` の M5b/M5c、`docs/design/09_master_match_poc.md`、`tools/vision_poc/jacket_reference_catalog.py`、`tools/vision_poc/jacket_catalog_review_projection.py`、`tools/jacket_catalog_collector/` と関連testsを読み、既存のmaster DB、catalog v1、capture、crop、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

xhigh

catalog schema version 2、v1からの非破壊移行、manual provenance、review history、transaction、再投入・競合・旧projection互換を同時に固定するためです。capture/OCR/window lifecycleへは進みません。

## 作業ブランチ

```powershell
codex/m5c-manual-jacket-review-workflow
```

## Normalized Summary

M5c-1で完成したdeveloper-only collectorのread-only coverage/review queueを、取り消し可能で監査可能な手動review workflowへ進めます。catalog v1を直接破壊せずversion 2へcopy-on-write移行し、`manual_confirmed`、reject、reopen、reassignment historyをcollectorから明示操作できるようにします。

このPRはmanual mutationだけを扱います。window探索、live capture、jacket安定検出、title/artist OCR、物理削除、source image削除は後続PRへ残します。

## In Scope

- catalog schema version 2とstrict validationを追加する。
- v1 sourceを変更しない明示的なv1→v2 copy-on-write migrationを追加し、staging検証成功後だけ新規v2 targetへatomic publishする。
- referenceにcurrent review status、manual provenance、monotonic revisionを持たせ、review action historyをappend-onlyで保持する。
- collectorで候補・観測・master driftを確認し、明示song選択によるmanual confirm/reassign、reject、reopenを実行する。
- mutationはcurrent projectionのreference revisionをpreconditionにし、stale UI、二重click、再投入、競合を安全に拒否または冪等処理する。
- manual confirm先はcurrent masterに存在し、GP対象であるsongだけを受け入れる。候補配列外のsongも明示検索・選択できるが、暗黙選択しない。
- `auto_confirmed` と `manual_confirmed` を区別し、manual referenceをruntime matcherへ渡す条件をcurrent master、current extractor、GP可否、rejectedでないこととして固定する。
- Python projectionをversion 2へ拡張し、catalog v1はread-only/migration-required、v2はmutation capabilityとrevision/historyを返す。C# loaderはversion 1 fixtureのread-only表示互換を維持し、version 2をstrictに読む。
- fake process、fixture DB、temporary `data/` rootでmigration、service、ViewModel、strict loader、mutation side-effect testを追加する。
- `docs/implementation-roadmap.md`、`docs/design/09_master_match_poc.md`、`tools/vision_poc/README.md`、collector READMEを同期する。

## Out of Scope

- 公開 `DDRGpScoreViewer`、正式保存workflow、正式個人スコアDB、M9 monitoring/trayの変更
- v1 catalogのin-place migration、既存v1 fileの上書き・削除
- reference row、history row、capture、crop、source imageの物理削除
- purge、retention、cleanup UI
- DDR GP window探索、Windows Graphics Capture、連続capture、ring buffer、jacket変化/安定検出
- title/artist OCR、confidence閾値、観測自動生成、auto-confirm条件の緩和
- grid操作、キー入力、focus移動、ゲーム操作自動化
- installer、Release、telemetry、GitHub Actions artifactへのcollector追加

## Fixed Decisions

- catalog v1はimmutable sourceとして扱い、migrationは別pathのv2 catalogをstaging生成・strict検証・atomic publishする。失敗、取消、競合時はv1と既存v2 targetを変更しない。
- v2 status語彙は `auto_confirmed`、`manual_confirmed`、`needs_review`、`unresolved`、`rejected` とする。master/extractor driftは保存statusを暗黙更新せずprojection上のcurrent state/reasonとして示す。
- manual action語彙は `manual_confirm`、`reassign`、`reject`、`reopen` とし、各actionはreference ID、before/after status・song、opaque reason/note、UTC timestamp、before/after revisionをappend-only historyへ記録する。
- `manual_confirm` は未確定またはreopen済みreferenceを明示songへ確定する。確定済みreferenceの別song変更は `reassign` として区別する。
- `reject` は取り消し可能な論理状態であり、reference、特徴量、候補、観測、historyを削除しない。`reopen` は直前確定songを自動復元せず、review対象へ戻す。
- mutationは1 transactionでcurrent rowとhistoryを更新する。historyだけ/current rowだけの部分成功を許さない。
- requestはexpected revision、expected current state、action IDを持つ。同一action ID・同一payloadの再投入は冪等成功、同一IDの異なるpayloadとstale revisionは副作用なしで拒否する。
- reason/note、candidate reason、observation statusはopaque UTF-8文字列として保持し、未知値を拒否・解釈しない。status/action/versionは閉じた語彙としてstrictに検査する。
- expected song ID、OCR raw、近傍候補をmanual songへ暗黙昇格しない。manual確定は開発者の明示選択だけで行う。
- same image bytesを共有する別songの別reference、song 1:N、current extractorだけをruntime matcherへ渡すM5b境界を維持する。

## Pending Decisions

- 完全削除、source image削除、retention、欠損capture/cropの最終操作契約
- live collection sessionのcheckpoint、ring buffer、採用frame、診断frame保持方針
- title/artist取得方式、OCR精度評価、auto-confirm採用閾値、更新ずれ対策
- DDR GP window候補条件、誤検出評価、handle消失後の再選択契約

これらは後続PRの仕様判断であり、このPRを止めない。

## Deliverables

- catalog v2 schema、strict validator、v1→v2 copy-on-write migrator
- append-only review history、revision/action IDによるtransaction・冪等・競合契約
- manual confirm/reassign/reject/reopen Python service/CLI境界
- collectorのmigration導線、song検索・明示選択、確認、成功/失敗/競合表示、history read-only表示
- projection v2 producerとC# strict loader、projection v1 read-only互換
- runtime feature loaderのmanual reference採用条件と回帰test
- empty v1、compatible v1、既存v2 target、破損/別種DB、unsupported schema、master drift、旧extractor、stale revision、同一action再投入、transaction failure fixture
- README・設計docs・次PR仕様の同期

## Invariants

- 公開app、monitoring/tray、正式保存workflow、正式個人スコアDBを変更しない。
- v1 source、既存v2 target、master DB、capture、crop、`data/`、`logs/` は成功条件を満たす前に変更しない。
- reject/reopen/reassignでreference evidenceやhistoryを失わない。
- candidate、expected、OCR raw、近傍候補を明示操作なしで確定songへ昇格しない。
- current masterにないsong、GP対象外song、旧extractor、orphan、rejected referenceをruntime matcherへ渡さない。
- local DB、capture、crop、review結果、process logをGit、CI artifact、Releaseへ含めない。
- collectorを通常solution build、installer、公開app packageへ暗黙追加しない。

## Boundary Condition Matrix

| 状態/操作 | 期待結果 | 副作用境界/test |
| --- | --- | --- |
| compatible v1→新規v2 | 全row/evidenceを保持してv2をatomic publish | v1 hash不変、temporaryなし |
| empty catalog v1 | 有効な空v2を生成 | coverage/review空、history空 |
| migration parse/validate/cancel/publish失敗 | v2を公開しない | v1/既存target hash不変、parent/tempなし |
| targetが既存v2/非catalog/directory | 実行前拒否 | source/target不変、migration開始なし |
| manual confirm/reassign | current GP songへrevision+1、history 1件 | current row/history同一transaction |
| reject/reopen | evidenceを保持して論理状態だけ更新 | 物理削除なし、history 1件 |
| stale revision/state | conflict表示 | DB hash不変、historyなし |
| 同一action ID・同一payload再投入 | 冪等に同じreceipt | revision/history増加なし |
| 同一action ID・異なるpayload | conflict拒否 | DB hash不変 |
| transaction途中失敗 | 全rollback | current row/history片側更新なし |
| master missing/GP対象外 song | manual action拒否 | catalog/master不変 |
| master/extractor drift | review表示、manual再確認要求 | 暗黙status/song更新なし |
| projection v1/v2 | v1はread-only、v2はcapability/revision/history表示 | unknown field/status/versionは拒否、opaque reasonは保持 |

## Validation

- Python: migration、schema、mutation、runtime loader、projectionの対象testと関連M5b test。
- .NET: projection v1/v2 strict loader、migration/mutation fake service、ViewModel filter/action/history/競合test。
- migration/mutation前後でsource/target/catalog/master hash、row、history、revisionを確認する。
- unsupported version、必須/未知field、null/型不正、未知status/action、candidate/history配列不正、truncated JSON、空stdout、Python非0終了を副作用なしで拒否する。
- `dotnet build`、collector全test、関連Python test、Ruff、compileall、`git diff --check` を実行する。
- catalog schema、transaction、runtime loaderを変更するためPython全テストを1回実行する。
- capture、画像分類、ROI、OCR、confirmed-eventsを変更しないため `python -m tools.vision_poc` 本体は省略し、理由と残るリスクを報告する。

## Acceptance Criteria

- v1を変更せず、明示操作でだけ検証済みv2 catalogを新規作成できる。
- manual confirm/reassign/reject/reopenがrevision preconditionとappend-only historyを伴う1 transactionで実行される。
- stale UI、二重click、同一action再投入、部分失敗でsilent overwriteやhistory重複が起きない。
- current master/GP/extractor/review状態を満たすauto/manual referenceだけをruntime matcherへ渡す。
- collectorで候補外を含むcurrent GP songを検索・明示選択できるが、候補やexpectedから暗黙確定しない。
- projection v1のread-only表示互換とprojection v2のstrict mutation capabilityがtestで固定される。
- physical delete、capture、OCR、window探索が混入していない。
- read-only branch diffレビューでmedium以上の未対応指摘がない。

## Open Risks / Blockers

- catalog v2はlocal-onlyでも将来のcapture sessionとruntime matcherが読むため、schema fieldとstatus consumerを全検索し、v1をv2として誤解しないことを確認する。
- v1 local catalogはGit管理外なので、migration testはfixture DBで固定し、実local DB migrationをmerge条件にしない。
- Draft PR #27など別trackもroadmap/next-taskを変更し得る。後からmergeするbranchが最新mainを取り込み、M5cとM9の両方を残してdocs競合を解消する。

## Issue Body Patch Or Append Text

M5c第2段階として、catalog v1を保持するcopy-on-write v2 migrationと、revision・append-only historyを持つmanual confirm/reassign/reject/reopen workflowをdeveloper-only collectorへ追加する。capture、OCR、window探索、物理削除は後続PRとする。

## Issue Comment Draft

M5c-1のread-only collectorを土台に、次はcatalog v2と監査可能なmanual reviewだけを実装します。v1は上書きせず、stale UI・再投入・transaction部分失敗を副作用なしで扱い、live capture/OCRは別PRへ維持します。

完了後はM5c-3aのwindow候補検出・明示capture lifecycleを独立した次PR仕様へ更新し、今回変更だけをcommit、現在のbranchへ通常pushしてdraft PRを作成してください。
