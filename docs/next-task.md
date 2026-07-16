# 次PR作業仕様

直近のM5c-3b PRで、developer-only jacket catalog collectorへversion付きjacket ROI detector、session全体のstable feature dedupe、明示採用、atomic local artifact、durable checkpoint、UIからのstrict resume、catalog v1のobservation ID単位の冪等投入を追加した。master/catalog/extractor drift、同一ID異payload、corrupt checkpoint、root外path、欠損・改変artifactは副作用前に拒否し、capture stop/resize/close/device loss後のframeをsession境界へ渡さない。catalog v2は安全な既存observation投入経路がないため、artifact/checkpointのcatalog statusを `deferred` として保持した。

`C:\work\ddrgp_scorelog` で作業してください。最初に `AGENTS.md`、本書、`docs/implementation-roadmap.md` のM5b/M5c、`docs/design/09_master_match_poc.md`、`tools/jacket_catalog_collector/README.md`、catalog v2 schema/migration/review実装を読み、ローカルDB、`data/jacket_catalog_collector/` のsource/crop/checkpoint、その他の生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

xhigh

catalog v2のtransaction、既存manual review history、observation/checkpointの再投入境界を同時に監査するためです。仕様固定後の実装・テスト・docs同期はGPT-5.6 Luna / xhighへ委譲できます。

## 作業ブランチ

```powershell
codex/m5c-v2-observation-ingest
```

## Normalized Summary

M5c-3cとして、M5c-3bがcatalog v2に対して `deferred` とした未解決observationを、既存v2のmanual review契約を壊さず冪等投入できるversion-aware APIとcollector retry経路を追加します。

このPRはcatalog v2への未解決observation投入とdeferred checkpointの再処理だけを扱います。title/artist OCR、auto-confirm条件変更、schema v3、実capture評価、cleanup、公開app、正式保存workflowへ進みません。

## In Scope

- catalog v2 schema、validator、v1→v2 migration、review mutation/historyの不変条件を再確認し、schema変更なしで未解決observationを表現できるか固定する。
- schema変更なしで可能なら、catalog identity/schema、master version/source hash、feature extractorをstrict検査するv2 observation ingest API/CLIを追加する。
- 非空observation IDを冪等keyとし、同一ID・同一canonical payloadは同じreceipt、同一ID・異payloadはtransaction開始前またはrollbackでDB byte不変の競合拒否にする。
- 別session/別observation IDの同一画像bytesを別referenceとして保持し、source hashだけで統合しない。
- title/artist空、`observation_status=unresolved` の観測をauto/manual confirmedへ昇格せず、候補なし未割当のreview対象として保持する。
- v2のmanual review revision、current status/song、append-only history、既存action ID replayを変更しない。observation ingestで架空のmanual action/historyを作らない。
- collector adapterをversion-awareにし、v2 `deferred` checkpointを明示retryして `ingested` へ更新する。v1 `pending` retryと既存receiptは維持する。
- catalog成功後のcheckpoint更新失敗を再試行可能にし、再投入でreference/historyを重複させない。
- v1 ingest、v2 ingest、deferred retry、競合、部分失敗、旧version/drift、option混在をPython/.NET fakeと実SQLite fixtureで固定する。
- `tools/jacket_catalog_collector/README.md`、roadmap、design docsを同期する。

## Out of Scope

- title/artist OCR、外部metadata、auto-confirm閾値・一意性条件の緩和
- catalog v3、正式個人スコアDB、`source_captures`、`analysis_logs`、`plays`
- jacket detector、ROI、capture lifecycle、window候補、ゲーム操作
- local source/crop/checkpointのcleanup、retention期限、物理削除
- 公開 `DDRGpScoreViewer`、installer、Release、telemetry
- M5c-4の実capture精度評価

## Fixed Decisions

- v1 catalogをmigrationやv2投入のために変更しない。v1/v2は同じ入力からschema別の明示経路を選ぶ。
- observation IDはsession IDとfeature evidenceからM5c-3bが生成した値を正とし、image hash単独を冪等keyにしない。
- 空title/artistは `unresolved` のまま保持し、auto-confirm、manual-confirm、song assignmentを暗黙生成しない。
- observation ingestとmanual review actionは別契約。ingestはreview revision/historyを偽装せず、既存review rowを上書きしない。
- current master/catalog/extractor drift、旧schema、corrupt artifact/checkpoint、同一ID異payloadはcatalog/checkpoint副作用なしで拒否する。
- catalog mutation成功後にcheckpoint receipt保存が失敗しても、次回retryはcatalog側の同一receiptを読んでcheckpointだけを収束させる。
- migration、既存DB修復、cleanupは実行しない。fixture DBだけを作成・破棄する。

## Pending Decisions

- catalog v2既存schemaに未解決observationを追加する際、historyを作らずに必要な初期revision/statusを表現する最小writer経路。
- `deferred` をretry対象として選択するUI/adapterの最小表示とreceipt文言。
- schema変更が不可避と判明した場合のPR分割。公開契約やmigrationが必要なら推測で実装せず、M5c-3cを設計・fixture境界まででmerge可能にできるかLeadが再判定する。

## Deliverables

- strict catalog v2 observation ingest API/CLI、またはschema変更が必要な場合の明示分割結果
- version-aware collector adapterとdeferred retry/checkpoint収束
- v1不変、v2 unresolved、別observation同一画像、同一ID競合、partial failureの回帰test
- README、roadmap、design docs、次PR仕様の同期

## Boundary Condition Matrix

| 状態/操作 | 期待結果 | 副作用境界/test |
| --- | --- | --- |
| v2へ新規unresolved観測 | reference 1件、song未割当、history偽装なし | transaction fixture |
| 同一observation同一payload再投入 | 同じreceipt | row/revision/history重複なし |
| 同一ID・異payload | conflict | DB/checkpoint byte不変 |
| 別ID・同一画像bytes | 別reference | source hash統合なし |
| 空title/artist | unresolved | auto/manual confirmedなし |
| master/catalog/extractor drift | strict reject | artifactは既存のまま、DB/checkpoint不変 |
| v1 catalog | 既存v1 ingest維持 | v2 writer混入なし |
| v2 deferred retry成功 | checkpointをingestedへ更新 | reference 1件 |
| catalog成功→checkpoint失敗 | retry可能 | catalog重複なし |
| checkpoint成功前のcatalog失敗 | deferred/pending維持 | saved扱いへ丸めない |
| manual review済みreference衝突 | 上書きしない | revision/history不変 |
| old/corrupt artifact/checkpoint | retry拒否 | DB不変 |

## Validation

- Python: catalog v1/v2 schema、migration、ingest、review/history、projection、runtime consumerの対象・影響範囲test。
- .NET: version-aware adapter、deferred retry、receipt/checkpoint、部分失敗fake testとcollector全test。
- catalog writer/transactionと共通helperを変更するためPython全テストを実行する。
- `python -m ruff check tools\vision_poc pyproject.toml tests`
- `python -m compileall master tools\vision_poc`
- `dotnet build tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj`
- `dotnet test tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj`
- `git diff --check`
- 画像分類/ROI/OCR/profile/PoC runnerを変更しない限り `python -m tools.vision_poc` は省略し、理由と残る実DB retry riskを報告する。

## Acceptance Criteria

- v2 `deferred` observationを明示retryで1件のunresolved referenceへ収束できる。
- 同一ID replay、異payload conflict、別ID同一画像、catalog成功後checkpoint失敗を機械的に区別する。
- v1、manual review revision/history、runtimeのcurrent reference条件、auto-confirm境界を変更しない。
- drift/旧version/corrupt入力の拒否時にcatalog/checkpoint副作用がない。
- ルート `AGENTS.md` のReview Policyに従った独立review gateで、P1/P2の未対応指摘がない。

## Open Risks / Blockers

- v2 schemaが未解決observationの安全な初期状態を既に表現できない場合、schema/migration判断が必要になる。既存資料から一意に決まらなければ「ユーザー対応が必要」として停止する。
- 実local DBの修復・migrationはこのPRで実行しないため、fixtureでのtransaction検証と実運用データ適用は分ける。

完了後は今回作業分だけをstageし、diff、対象/影響範囲/全体test、ルート `AGENTS.md` のReview Policyに従った独立review gateを完了してからcommit、現在の `codex/*` branchへ通常pushし、draft PRを作成してください。次PR仕様は実績に基づいてM5c-4または必要な分割PRへ更新し、更新後の作業には着手しないでください。
