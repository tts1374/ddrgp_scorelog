# 次PR作業仕様

直近のM5c-3c PRで、catalog schema 2の既存列だけを使う明示的な `ingest-v2` API/CLIとcollector retryを追加した。新規observationはsong未割当、空title/artist、`unresolved`、revision/history/candidateなしで作成され、同一ID・同一canonical payloadは同じreceiptへ収束する。同一ID・異payload、manual review済みrowとの衝突、master/catalog/extractor drift、欠損・改変artifactはcatalog/checkpoint副作用なしで拒否する。collectorはv1 `pending` とv2 `pending/deferred`をschema別にretryし、catalog成功後のcheckpoint保存失敗も次回retryで重複なく収束できる。

`C:\work\ddrgp_scorelog` で作業してください。最初に `AGENTS.md`、本書、`docs/implementation-roadmap.md` のM5b/M5c、`docs/design/09_master_match_poc.md`、`tools/jacket_catalog_collector/README.md`、M4照合とcatalog v2 review/runtime consumerを読み、ローカルDB、`data/jacket_catalog_collector/` のsource/crop/checkpoint、実capture画像、評価出力を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

実capture由来のtitle/artist取得方式、誤確定防止、既存M4照合・manual review境界を同時に評価するためです。評価器、fixture、docs同期など、仕様と判断境界を固定できた実装はGPT-5.6 Luna / xhighへ委譲できます。

## 作業ブランチ

```powershell
codex/m5c-title-artist-evaluation
```

## Normalized Summary

M5c-4として、developer-only collectorが明示採用した実capture artifactからtitle/artist候補を取得する方式を再現可能に比較評価し、既知誤自動確定0件を優先した採用判定を追加します。

このPRは評価可能な取得・照合契約と、採用条件を満たす方式のcollector接続だけを扱います。評価根拠が不足する方式はauto-confirmへ接続せず、既存のunresolved/manual review経路へ残します。

## In Scope

- M5c-3b/3cのimmutable source/crop/manifestを入力とし、title/artist候補、取得方式/version、raw値、正規化値、信頼度、失敗理由を再現可能に生成するdeveloper-only評価経路を追加する。
- 既存M4のtitle primary、artist tie-breaker、GP対象、current master/catalog/extractor条件を再利用し、image hashやartist単独でsongを確定しない。
- local metadata/manifestの期待値があるartifactだけをaccuracy評価の分母にし、evaluated、partially evaluated、no expected valuesを区別する。
- 方式別に母数、完全一致、不一致、空、候補一意/曖昧、既知誤確定を集計し、同一入力でbyte-stableなCSV/JSON/Markdown reportを `data/` 配下へ生成する。
- 採用条件をfixtureと実capture評価の両方で満たした方式だけ、collectorの明示採用後にM4候補生成へ接続する。条件未達、複数候補、不一致、空、低信頼度はunresolved/manual reviewへ残す。
- auto-confirm候補を作る場合も、catalog v2の既存provenance、current reference条件、manual review revision/history、observation ID冪等性を維持する。
- 同一artifact再評価、同一observation再投入、別observationの同一画像、master/catalog/extractor/方式version drift、部分失敗をPython/.NET testで固定する。
- `tools/jacket_catalog_collector/README.md`、roadmap、design docsを同期する。

## Out of Scope

- ゲーム操作、focus操作、grid自動巡回、window自動確定
- score/判定数OCR、結果画面OCR、正式保存workflow
- catalog schema v3、migration、既存DB修復、正式個人スコアDB
- auto-confirm閾値やM4一意性条件の根拠なし緩和
- local source/crop/checkpointのcleanup、retention、物理削除
- 公開 `DDRGpScoreViewer`、installer、Release、telemetry

## Fixed Decisions

- 実capture artifactはdeveloperがcollectorで明示採用したものだけを評価対象にし、memory previewや未採用frameをdisk/catalogへ昇格しない。
- titleをprimary、artistをtie-breakerとするM4境界を維持し、artist単独、image hash単独、近傍候補への寄せで自動確定しない。
- 既知誤自動確定0件を自動確定率より優先する。母数や期待値が不足する方式は採用済みと扱わない。
- expected値はGit管理外のlocal metadataから読み、実画像、実入力JSON/CSV、評価reportをGitへ追加しない。
- raw/normalized候補、方式/version、失敗理由を監査可能に保ち、低信頼度や不一致を空文字や成功へ丸めない。
- catalog ingest、manual review action、評価は別契約とし、評価の再実行でreview revision/historyを変更しない。
- schema変更、migration、既存DB修復、cleanupは実行しない。fixtureだけを作成・破棄する。

## Pending Decisions

- title/artist取得方式の候補と局所前処理。既存依存と実capture evidenceを比較し、追加依存は必要最小限のoptional dependencyに限定する。
- 採用に必要な最低評価母数とconfidence threshold。既知誤確定0件を必須とし、実測値をdocsへ記録してLeadが固定する。
- collector UIへ表示するraw候補、候補song、信頼度、失敗理由の最小構成。
- 実capture環境でしか確認できない項目はfixture検証と分離し、ユーザー操作が必須ならAGENTS.mdの「ユーザー対応が必要」形式で具体化する。

## Deliverables

- version付きtitle/artist取得・正規化・M4候補評価経路
- local実capture dataset向けのstrict loaderと再現可能な方式別accuracy report
- 採用条件を満たす場合だけのcollector候補接続。満たさない場合は評価結果とunresolved維持をmerge可能な成果とする
- 正常、空/欠損、曖昧、誤候補、再投入、drift、部分失敗の回帰test
- README、roadmap、design docs、次PR仕様の同期

## Boundary Condition Matrix

| 状態/操作 | 期待結果 | 副作用境界/test |
| --- | --- | --- |
| expected付き実capture | 方式別候補と一致結果を記録 | local reportのみ |
| expectedなし | `no_expected_values` | 成功率の分子/分母へ混入しない |
| title/artist空または取得失敗 | unresolvedと理由 | song assignmentなし |
| title一意、artist整合、採用条件達成 | auto-confirm候補 | current GP/master/catalog/extractor再検査 |
| title曖昧またはartist不一致 | reviewへ残す | 近傍songへ寄せない |
| 同一入力再評価 | 同じ結果/report | catalog/history重複なし |
| 同一observation再投入 | 同じreceiptへ収束 | reference/revision/history重複なし |
| 別ID・同一画像bytes | 別observation維持 | source hash統合なし |
| master/catalog/extractor/方式version drift | strict rejectまたは明示再評価 | 旧結果をcurrent扱いしない |
| corrupt/欠損/root外artifact | reject | catalog/checkpoint/report副作用なし |
| 取得成功→catalog/checkpoint失敗 | retry可能 | saved/confirmedへ丸めない |
| manual review済みrowとの競合 | manual stateを優先 | revision/history不変 |
| option/方式混在 | strict reject | 別方式のreceiptを再利用しない |

## Validation

- Python: strict loader、normalization、M4候補、accuracy集計、catalog v2 ingest/review/runtime consumerの対象・影響範囲test。
- .NET: collector adapter/UI、unresolved/manual review fallback、retry/checkpoint、部分失敗fake testとcollector全test。
- 共通loader、catalog writer、option解析を変更した場合はPython全テストを実行する。
- `python -m ruff check tools\vision_poc pyproject.toml tests`
- `python -m compileall master tools\vision_poc`
- `dotnet build tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj`
- `dotnet test tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj`
- `git diff --check`
- OCR、ROI、profile評価、PoC runnerを変更した場合だけ `python -m tools.vision_poc` を実行し、実capture素材はGit管理外で評価する。

## Acceptance Criteria

- 同じlocal実capture入力から方式別のtitle/artist精度と失敗理由を再現生成できる。
- evaluated、partially evaluated、no expected valuesを区別し、期待値不足を成功扱いしない。
- 採用方式は固定した最低母数・精度・既知誤確定0件を満たし、曖昧/不一致/低信頼度はreviewへ残る。条件未達ならauto-confirmへ接続しない。
- 同一入力/observation再投入、別ID同一画像、drift、old/corrupt入力、partial failureの副作用境界をtestで確認できる。
- v1/v2 ingest、manual review revision/history、runtime current reference、ゲーム非操作、local artifact非Git管理を維持する。
- read-only branch diffレビューでmedium以上の未対応指摘がない。

## Open Risks / Blockers

- 実captureの評価母数はローカル環境と手動巡回に依存する。fixtureだけでは採用条件を満たした扱いにしない。
- title/artist領域の画面状態、animation、言語、解像度差により方式別の追加評価が必要になる可能性がある。
- 新しい外部service、認証情報、費用発生、非互換schema変更が必要なら実装を広げず「ユーザー対応が必要」として停止する。

完了後は今回作業分だけをstageし、diff、対象/影響範囲/条件付き全体test、独立read-onlyレビューを完了してからcommit、現在の `codex/*` branchへ通常pushし、draft PRを作成してください。今回の実績から次PR仕様を更新し、更新後の作業には着手しないでください。
