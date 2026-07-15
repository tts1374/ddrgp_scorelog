# Jacket Catalog Collector (developer-only)

M5c のローカル jacket catalog 運用を支援する、公開 `DDRGpScoreViewer` とは独立した Windows WPF app です。master DB の明示更新、coverage/review projection、catalog v1からv2への非破壊移行、監査可能なmanual confirm/reassign/reject/reopenを扱います。通常の公開app build、installer、Release、GitHub Actions artifactには含めません。

## 実行

リポジトリrootで実行します。

```powershell
dotnet restore tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj
dotnet build tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj --no-restore
dotnet test tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj --no-restore
dotnet run --project tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj --no-build
```

appはカレントディレクトリをリポジトリrootとして `python -X utf8 -m master`、`python -X utf8 -m master.inspect`、`python -X utf8 -m tools.vision_poc.jacket_catalog_review_projection`、`python -X utf8 -m tools.vision_poc.jacket_reference_catalog` を実行します。子processのstdout/stderrはWindows localeに依存させずUTF-8として扱います。Python環境には既存M4/M5bと同じ依存が必要です。

## Master update boundary

`masterを更新` は次の順序を固定します。

1. 既存targetが非空なら、network/buildより前に `python -m master.inspect` で互換性を確認する。0 byte fileは明示placeholderとして許可する。
2. OS temporary directoryで `python -m master --output <staging>` を実行する。
3. stagingだけを `python -m master.inspect` し、version、source hash、song/chart/GP件数を読む。
4. inspection成功後にだけtarget親directoryを作り、同じdirectoryのpublish fileをatomic renameしてtargetへ公開する。
5. failure/cancel/publish failureではstaging、summary、publish fileを削除し、新規targetのためだけに作った空parentも削除する。取消時は子process treeの終了とstdout/stderr drainを待ってからcleanupする。

既存のcompatible targetまたは0 byte targetは、build/inspection/publish成功前には変更しません。diagnosticはUI状態とprocess stderrに限定し、部分DBを保持しません。実network成功は通常testの条件にせず、processとpublisherをfakeにしたtestで境界を固定します。

## Projection contract

UIはcatalog SQLite tableを直接読みません。Python projectionがmaster/catalogをstrictかつread-onlyで検証し、UTF-8 stdoutへversion 2の単一JSONを出します。catalog v1はmigration-required/read-only、v2はmanual-review capableとして投影します。temporary projection fileは作りません。stderrは診断専用です。C# loaderは既存version 1 fixtureのread-only表示互換を維持します。

```powershell
python -m tools.vision_poc.jacket_catalog_review_projection `
  --master-db data\master\ddrgp-master.sqlite `
  --catalog data\jacket_catalog\catalog.sqlite
```

top-level必須fieldは `projection_schema_version`、`master`、`catalog`、`coverage`、`songs`、`review_references` です。C# loaderはversion、全object/arrayの未知field、必須field、型、coverage/review status語彙、候補/history配列、revision連続性、capability/schema、分母整合をstrictに検査します。unsupported version、空/truncated stdout、Python非0終了は部分表示せず失敗にします。一方、`reason`、`note`、`candidate.reason`、`observation_status` はopaqueな診断文字列として未知値をそのまま表示します。

表示とfilterは、GP対象songを `referenced` / `needs_review` / `uncollected` / `unresolved` の同じ分母・同じstatus histogramで数えます。orphan、候補なし未割当観測、旧extractor、master driftに加え、破損したmanual referenceを `needs_review` / `persisted_feature_invalid` として表示します。この派生状態で保存済みstatus、revision、historyは変更しません。生成中にmaster/catalogのfile identity、size、mtime、hashが変わった場合はsnapshotを混在させず再読込要求として拒否します。表示前後でmaster/catalog、capture、crop、`data/`、`logs/`を変更しません。

## v2 migrationとmanual review

`v1→v2移行` は現在選択中のcatalog v1を上書きせず、別の新規 `data/` pathへv2 stagingを生成・strict検証してからexclusive publishします。既存target、unsupported/破損source、失敗、取消ではv1と既存targetを変更しません。

v2ではreview行を選び、current GP曲をsong ID/title/artistで検索して明示選択します。`confirm` と `reassign` は選択songとcurrent extractorの完全な永続特徴量を必要とし、`feature_extraction_failed` や欠損vectorは確定できません。`reject` と `reopen` はsongを受け取りません。実行前にreference ID、revision、action、songを確認dialogへ表示します。requestはprojectionのrevision/stored status/assigned songをpreconditionにし、競合は `review更新失敗/競合` として再読込を促します。current rowとhistoryはPython側の1 transactionで更新され、同一action ID・同一payloadだけが冪等成功です。候補、expected song、OCR rawは明示選択へ昇格しません。

## Scope boundary

このapp/project/testは `tools/jacket_catalog_collector/` 内で完結し、`app/src/DDRGpScoreViewer` を参照しません。物理削除、source image削除、capture、window探索、OCR、ゲーム操作は実装しません。これらは後続M5cへ分離します。
