# Jacket Catalog Collector (developer-only)

M5c のローカル jacket catalog 運用を支援する、公開 `DDRGpScoreViewer` とは独立した Windows WPF app です。M5c-1 では master DB の明示更新と、M5b catalog の coverage/review queue の read-only 表示だけを扱います。通常の公開app build、installer、Release、GitHub Actions artifactには含めません。

## 実行

リポジトリrootで実行します。

```powershell
dotnet restore tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj
dotnet build tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj --no-restore
dotnet test tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj --no-restore
dotnet run --project tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj --no-build
```

appはカレントディレクトリをリポジトリrootとして `python -X utf8 -m master`、`python -X utf8 -m master.inspect`、`python -X utf8 -m tools.vision_poc.jacket_catalog_review_projection` を実行します。子processのstdout/stderrはWindows localeに依存させずUTF-8として扱います。Python環境には既存M4/M5bと同じ依存が必要です。

## Master update boundary

`masterを更新` は次の順序を固定します。

1. 既存targetが非空なら、network/buildより前に `python -m master.inspect` で互換性を確認する。0 byte fileは明示placeholderとして許可する。
2. OS temporary directoryで `python -m master --output <staging>` を実行する。
3. stagingだけを `python -m master.inspect` し、version、source hash、song/chart/GP件数を読む。
4. inspection成功後にだけtarget親directoryを作り、同じdirectoryのpublish fileをatomic renameしてtargetへ公開する。
5. failure/cancel/publish failureではstaging、summary、publish fileを削除し、新規targetのためだけに作った空parentも削除する。取消時は子process treeの終了とstdout/stderr drainを待ってからcleanupする。

既存のcompatible targetまたは0 byte targetは、build/inspection/publish成功前には変更しません。diagnosticはUI状態とprocess stderrに限定し、部分DBを保持しません。実network成功は通常testの条件にせず、processとpublisherをfakeにしたtestで境界を固定します。

## Read-only projection contract

UIはcatalog SQLite tableを直接読みません。Python projectionがmaster/catalogをstrictかつread-onlyで検証し、UTF-8 stdoutへversion 1の単一JSONを出します。temporary projection fileは作りません。stderrは診断専用です。

```powershell
python -m tools.vision_poc.jacket_catalog_review_projection `
  --master-db data\master\ddrgp-master.sqlite `
  --catalog data\jacket_catalog\catalog.sqlite
```

top-level必須fieldは `projection_schema_version`、`master`、`catalog`、`coverage`、`songs`、`review_references` です。C# loaderはversion、全object/arrayの未知field、必須field、型、coverage/review status語彙、候補配列、分母整合をstrictに検査します。unsupported version、空/truncated stdout、Python非0終了は部分表示せず失敗にします。一方、`reason`、`candidate.reason`、`observation_status` はopaqueな診断文字列として未知値をそのまま表示します。

表示とfilterは、GP対象songを `referenced` / `needs_review` / `uncollected` / `unresolved` の同じ分母・同じstatus histogramで数えます。orphan、候補なし未割当観測、旧extractor、master driftは別のreview状態・理由として表示します。生成中にmaster/catalogのfile identity、size、mtime、hashが変わった場合はsnapshotを混在させず再読込要求として拒否します。表示前後でmaster/catalog、capture、crop、`data/`、`logs/`を変更しません。

## Scope boundary

このapp/project/testは `tools/jacket_catalog_collector/` 内で完結し、`app/src/DDRGpScoreViewer` を参照しません。M5c-1ではcatalog rowの手動確定・reject・再割当・削除、schema migration、capture、window探索、OCR、ゲーム操作を実装しません。これらは後続M5cへ分離します。
