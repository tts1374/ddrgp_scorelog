# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、`docs/implementation-roadmap.md` の M5c、`docs/design/08_master_db_generation.md`、`docs/design/09_master_match_poc.md`、`master`、`tools/vision_poc/jacket_reference_catalog.py` と関連testsを読み、既存のmaster DB、jacket catalog、capture、crop、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

開発者専用WPF app、既存Python CLI orchestration、master DBのatomic publish、strict catalog read、将来のmanual review/capture境界を同時に扱うためです。このPRではcatalog schema migration、live capture、OCR採用判断へ進まないため、通常はxhighまで上げません。

## 作業ブランチ

```powershell
codex/m5c-developer-jacket-catalog-collector
```

## Normalized Summary

M5bで完成したlocal jacket catalog基盤を、約1200曲の手作業画像保存・CSV記入に依存せず運用するため、公開WPF appとは独立した開発者専用collector appを開始します。

このPRはM5c第1段階です。独立appの土台、既存M4 master builderの安全な実行・更新、M5b catalogのread-only coverageとreview queue表示を追加します。manual紐付け・削除、catalog schema変更、DDR GP window自動特定、live capture、jacket安定検出、title/artist OCRは後続PRへ残します。

## In Scope

- `tools/` 配下に、公開・配布対象外の独立したWindows開発者appとtest projectを追加する。
- collector appと公開 `app/src/DDRGpScoreViewer` のproject、UI、resource、設定、Release対象を分離する。
- 既存 `python -m master` と `python -m master.inspect` を再利用し、Wiki譜面表と公式収録曲一覧からmaster DBをstaging生成・検証して、明示操作後だけ選択pathへatomic publishできるようにする。
- master更新前後のversion、source hash、song/chart/GP対象件数を表示し、取得・生成・inspection失敗時は既存masterを変更しない。
- 既存master DBとM5b catalogを明示選択し、master version、catalog identity/schema、GP対象分母、coverage status件数を表示する。
- `referenced`、`needs_review`、`uncollected`、`unresolved`、orphan、旧extractorを区別して一覧・filter表示する。
- `needs_review` / `unresolved` referenceについて、reference ID、観測title/artist、候補song、理由、master driftをread-only表示する。
- catalogのtableをC#側で直接解釈せず、既存Python責務へversion付きread-only review projectionを追加してUIへ渡す。
- UIがSQLite schemaを独自更新せず、master生成・inspection・catalog coverageは既存Python責務を再利用する。
- fake process、fixture DB、temporary `data/` rootでViewModel/service testを追加する。

## Out of Scope

- 公開 `DDRGpScoreViewer`、M9 monitoring/task tray、正式保存workflowの変更
- catalog rowの手動紐付け、再割当、reject、物理削除
- `manual_confirmed` / `rejected`、review history table、catalog schema version更新、migration
- DDR GP window探索・選択、Windows Graphics Capture、連続capture、jacket変化/安定検出
- title/artist OCR、OCR confidence閾値、jacketと文字領域の同期判定、auto-confirm率改善
- grid操作、キー入力、focus移動、ゲーム操作自動化
- installer、Release、配布、telemetry、GitHub Actions artifactへのcollector追加
- master DB、catalog、capture、crop、review結果のGit管理

## Fixed Decisions

- collectorは開発者本人だけが使う独立appとし、公開WPF appへdeveloper-only機能を混ぜない。
- master生成/parser/schema検証は既存M4実装を再利用し、collector側でscraperやDB writerを再実装しない。
- master更新は明示操作、staging生成、inspection成功、atomic publishの順に行い、失敗時は既存masterを保つ。
- catalogとcoverageの初期画面はread-onlyとし、このPRではlocal catalogを変更しない。
- Python→C#のread-only projectionはUTF-8 stdoutへ単一JSONを出すversion 1契約とし、temporary projection fileを作らない。stderrは診断専用とし、非0終了や不正JSONを部分成功へ丸めない。
- projection top-levelは `projection_schema_version`、`master`、`catalog`、`coverage`、`songs`、`review_references` を必須とする。review rowはreference ID、review status/reason、観測title/artist、master drift、候補song配列を持ち、status語彙と必須/任意fieldをPython schemaとC# strict loaderで共有する。現行M5bのreason/candidate reasonは自由TEXTと任意observation status由来の値を含むため、opaqueな診断文字列として型と必須性だけを検査し、未知reasonを拒否・解釈しない。
- 将来のcollection sessionは開始時のmaster version/hashとfeature extractor versionを固定し、session中に切り替えない。
- 将来の手動確定は `auto_confirmed` と区別できるprovenanceを持つ。
- 将来のcollectionは中断・再開と冪等再投入を前提にする。
- collector、master生成物、catalog、capture、crop、review結果はローカル専用で、公開成果物へ含めない。

## Pending Decisions

- catalog v1から、`manual_confirmed`、`rejected`、review/reassignment historyを持つ次versionへの移行または再構築方針
- 行の除外、取り消し可能なreject、完全削除、source image削除を分ける最終操作契約
- title/artist取得方式、OCR精度評価、auto-confirm採用閾値、jacketと文字領域の更新ずれ対策
- DDR GP window自動特定の候補条件、誤検出評価、初回確認、handle消失後の再選択契約
- live collection時のring buffer、採用frame、前後診断frame、crop保持・cleanup policy
- catalog referenceとlocal source capture/cropを再表示可能に結ぶlocator、retention、欠損時表示の契約

これらは後続PRの仕様判断であり、このPRを止めない。

## Deliverables

- developer-only app/project/testの明確な配置とREADME
- master build/update service、状態model、明示実行UI、失敗/取消/成功表示
- staging生成、inspection、atomic publish、既存master不変を固定するfixture test
- master/catalog選択とstrict read-only validation
- coverage summary、song一覧、status/reason filter
- needs-review/unresolved一覧と候補・理由のread-only表示
- Python側のversion付きread-only review projectionとstrict loader
- 空master、空catalog、非対応schema、破損DB、master drift、旧extractor、process失敗の状態表示
- `docs/implementation-roadmap.md`、`docs/design/08_master_db_generation.md`、`docs/design/09_master_match_poc.md`、collector READMEの同期

## Invariants

- 公開 `DDRGpScoreViewer` のproject、monitoring状態、tray、正式保存workflow、正式個人スコアDBを変更しない。
- master生成とinspectionは既存Python入口を使い、成功前に既存masterを上書きしない。
- coverage/review表示だけでmaster DB、catalog、capture、crop、`data/`、`logs/` を変更しない。
- candidate、OCR raw、expected、近傍候補を確定songへ昇格しない。
- 同じ画像bytesを共有する別songの別reference、song 1:N、current extractorだけをruntime matcherへ渡すM5b境界を維持する。
- ローカルDB、HTML snapshot、capture、crop、review結果、process logをGit、CI artifact、Releaseへ含めない。
- collectorを通常solution build、installer、公開app packageへ暗黙追加しない。

## Boundary Condition Matrix

| target状態 | build / inspect / publish結果 | 期待する副作用境界 |
| --- | --- | --- |
| 新規path | 全段階成功 | inspection済みstagingだけをtargetへatomic publishし、temporary staging/summaryを残さない |
| 新規path | download/build/inspect失敗、cancel、publish失敗 | targetと元々存在しない親directoryを残さず、temporary staging/summary/publish fileを残さない |
| compatible既存master | 全段階成功 | inspection済みstagingだけで置換し、更新後metadata/hashを再読込する |
| compatible既存master | download/build/inspect失敗、cancel、publish失敗 | 既存targetのhash/metadataを変えず、temporary staging/summary/publish fileを残さない |
| 0 byte既存file | 全段階成功 | 明示選択されたplaceholderとしてinspection済みstagingだけで置換する |
| 0 byte既存file | download/build/inspect失敗、cancel、publish失敗 | 0 byte targetをそのまま保持し、部分DBを公開しない |
| incompatible非空file / directory | 実行前validation | network/buildを開始せず拒否し、targetと親directoryを変更しない |
| read-only projection | unsupported older/newer version、必須/未知field、型、status語彙、候補配列、truncated JSON、Python非0終了が不正 | UI errorとして副作用なしで拒否し、master/catalog/`data/`/`logs/`を変更しない。未知reasonはopaque文字列として表示する |

## Validation

- master build/inspect process success、download/build/inspect failure、cancel、既存targetあり、0 byte target、atomic publish失敗をfakeで固定する。
- 新規targetの失敗・取消時はtargetを作らず、既存targetの失敗・取消時はhash/metadataを変えず、stagingを完成masterとして表示しないことを確認する。
- success/failure/cancel後にtemporary staging、summary、publish fileを残さない。診断はUI状態とtest fakeのmemory記録に限定し、部分DBをdiagnosticとして保持しない。
- compatible master/catalog、空coverage、needs-review/unresolved、orphan、master drift、旧extractor、破損/別種SQLiteをfixtureで確認する。
- read-only表示前後でmaster/catalog hashが不変であることを確認する。
- filterと件数が同じcoverage分母・status/reason集合から生成されることを確認する。
- Python producerが出すversion 1 fixtureをC# strict loaderで読み、master/catalog identity、coverage counts、song rows、review rows、candidate配列、status、opaque reason文字列が一致するcross-language contract testを追加する。
- unsupported older/newer version、必須field欠損、未知field、null/型不正、未知status、candidate配列不正、truncated JSON、空stdout、Python非0終了を副作用なしで拒否する。未知reasonを持つcompatible fixtureは拒否せず、その文字列を変更せず表示する。
- collectorの .NET test、`dotnet build`、関連Python catalog/master test、Ruff、compileall、`git diff --check` を実行する。
- capture、画像分類、ROI、OCR、confirmed-eventsを変更しないため、`python -m tools.vision_poc` 本体は省略し、理由と残るリスクを報告する。

## Acceptance Criteria

- 公開appと独立したdeveloper-only appを起動でき、公開project・Release対象へ依存しない。
- 明示操作で既存M4 builderを実行し、inspection成功時だけmasterをpublishでき、失敗時は既存masterが不変である。
- 新規targetの失敗時にtargetやtemporary fileが残らず、compatible既存/0 byte targetの失敗時に元fileが不変である。
- 選択したmasterとcatalogのidentity/version、GP対象件数、coverage status/reasonを確認できる。
- needs-review/unresolved referenceと候補理由をread-onlyで確認でき、表示だけではDBやローカル画像を変更しない。
- Python projection version 1とC# strict loaderが同じfixture・status語彙・opaque reason文字列を解釈し、不正/非対応payloadやPython失敗を部分表示せず拒否する。
- manual mutation、live capture、OCRがこのPRへ混入していない。
- read-only branch diffレビューでmedium以上の未対応指摘がない。

## Open Risks / Blockers

- M9 Draft PR #27も`docs/next-task.md`とロードマップを変更している。M5c-1とPR #27の後続merge順は固定せず、後からmergeするbranchが最新mainを取り込み、M5c milestoneとM9残り作業を両方残してdocs競合を解消する。
- master buildはnetwork/source構造へ依存するため、通常testはfake/fixtureを使い、実network成功をmerge条件にしない。
- manual review mutationとlive collectionはcatalog contractの追加判断を必要とするため、このPRから分離する。

## Issue Body Patch Or Append Text

M5c第1段階として、公開appと独立したdeveloper-only jacket catalog collectorの土台を追加する。既存M4 builderをstaging生成・inspection・atomic publishで実行し、M5b catalogのcoverageとreview queueをread-only表示する。manual確定、catalog migration、window探索、live capture、OCRは後続PRとする。

## Issue Comment Draft

M5b catalog基盤と実用収集workflowを分離し、M5cをdeveloper-only collector milestoneとして開始します。初回PRはmaster更新とread-only coverage/review UIに限定し、公開M9 app、正式保存、catalog mutation、capture/OCRには触れません。

完了後はM5c第2段階の具体的な次PR仕様へ更新し、今回変更だけをcommit、現在のbranchへ通常pushしてdraft PRを作成してください。
