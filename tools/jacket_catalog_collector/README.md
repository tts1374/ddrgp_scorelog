# Jacket Catalog Collector (developer-only)

M5c のローカル jacket catalog 運用を支援する、公開 `DDRGpScoreViewer` とは独立した Windows WPF app です。master DB の明示更新、coverage/review projection、catalog v1からv2への非破壊移行、監査可能なmanual reviewに加え、DDR GRAND PRIX window候補の確認とmemory-only capture lifecycle観測を扱います。通常の公開app build、installer、Release、GitHub Actions artifactには含めません。

## 実行

リポジトリrootで実行します。

DDR GRAND PRIXが管理者権限で起動している環境では、windowのprocess start identityを検査できるよう、collectorも管理者として起動したPowerShellから実行してください。権限が不足するwindowは候補へ部分追加せず、0件として扱います。

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

v2ではreview行を選び、current GP曲をsong ID/title/artistで検索して明示選択します。`confirm` と `reassign` は選択songとcurrent extractorの完全な永続特徴量を必要とし、`feature_extraction_failed` や欠損vectorは確定できません。`reject` と `reopen` はsongを受け取りません。実行前にreference ID、revision、action、songを確認dialogへ表示します。requestはprojectionのrevision/stored status/assigned songをpreconditionにし、競合は `review更新失敗/競合` として再読込を促します。current rowとhistoryはPython側の1 transactionで更新され、同一action ID・同一payloadだけが冪等成功です。保存済みreceiptはcurrent master検証より先に返すため、commit後のmaster一時障害や曲の削除・GP対象外化でretryを妨げません。異なるpayloadの同一ID再利用と未保存actionのmaster不整合は拒否します。候補、expected song、OCR rawは明示選択へ昇格しません。

## Window capture lifecycle

`Window capture (memory only)` はtop-level windowを列挙し、titleまたはprocess名からDDR GRAND PRIX候補になった理由、handle、PID、process start identity、title/class、client size、visible/minimized状態と取得可能なpreviewを表示します。候補が1件でも自動選択・自動開始しません。開発者が行を選び、preview、根拠、identityを確認dialogで再確認した場合だけWindows Graphics Captureを開始します。

保護された`ddr-konaste` windowでは`PrintWindow`が応答しないため、候補一覧のpreview取得を試行せず、process名、handle/PID/process start、title/class、client size/stateを確認対象とします。これは明示開始後のWindows Graphics Captureを無効化するものではありません。

開始直前と各frame受領時にhandle、PID、process start、process名、title/class、client size、visible/minimized状態を再検査します。stale候補、handle再利用、resize、identity drift、最小化、対象終了では暗黙に別windowを選択・再開始せず、終了理由を表示します。二重開始を拒否し、停止・開始取消・window close・device loss・capture例外は冪等停止としてin-flight callbackをdrainしてからresourceを1回だけ解放します。collector終了時もactive sessionの停止完了を待ちます。

WGCのnative frame queueとimmutable PNG ring bufferは固定上限です。満杯時はframeをdropして `captured` / `dropped` を表示し、producerをUI描画やdisk I/Oで無制限にblockしません。latest preview、件数、開始時/現在size、resource状態は観測専用です。capture単独ではdiskへ保存せず、後述の明示採用だけが選択したsource/cropをlocal evidenceへ昇格します。

## Jacket observation session (M5c-3b)

capture開始前にmaster/catalogのprojectionを読み込み、選択windowのidentity、master version/source hash、catalog identity/schema/created-at、feature extractor、`m5c-capture-utc-clock-v1` frame clock、ROI/detector versionをsessionへ固定します。capture frameは1280x720基準の `(812,28,150,150)` ROIを実サイズへ線形scaleし、16x16 RGB feature、SHA-256 hash、mean absolute differenceを作ります。既定の安定判定は差分 `<=0.08`、連続3 frame、最小100msです。値はversion付きでobservation manifestへ記録します。

`change_candidate`、`stable_candidate`、`duplicate_preview` は別状態です。session内で一度stableになったfeature hashは、別jacketを挟んで再出現しても既存候補として扱います。stable候補は自動採用せず、capture tabの `候補を採用` を明示的に押したときだけ、`data/jacket_catalog_collector/<session-id>/observations/<observation-id>/` に `source.png`、`jacket-crop.png`、`observation.json` と checkpoint をstagingからatomic publishします。publish前にcurrent master/catalog/extractorを再検査し、失敗stagingは残しません。source/crop/hashの不一致、同一observation IDの異なるpayload、破損・旧version・identity drift checkpointは副作用なしで拒否します。

checkpointは最初の明示採用と同時に作成し、それ以前のframeをdiskへ書きません。以後は停止時にも、`session_id`、master/catalog/extractor/window/ROI/detector identity、session内stable feature集合、最後のstable feature、処理frame/drop件数、採用済みobservation ID/source hash、catalog statusをatomic更新します。capture tabへ既存session IDを入力して `session再開` を押すと、current projection・選択window・全artifact manifest/hashが一致するcompatible checkpointだけを再開します。catalog v1にはtitle/artistを推測せず空文字と `unresolved` で既存M5b ingest APIを呼び、非空observation IDを冪等keyとして別sessionの同一画像を統合しません。catalog v2には明示選択した `ingest-v2` 経路で空title/artist・`unresolved` のrowを投入します。旧M5c-3bで `deferred` になったv2 artifactとcatalog failure後の `pending` は、`catalog retry (pending/deferred)` からv2 writerへ明示再投入できます。

capture停止、resize、close、device loss、例外、取消ではsessionを停止し、停止後frameをdetector、artifact、catalogへ渡しません。source/crop/manifest/checkpointはこのcollectorのlocal dataだけで、`logs/`、通常stdout、Git、公開app、正式個人スコアDBへ出力しません。

## v2 unresolved observation ingest (M5c-3c)

Pythonの`ingest-v2`はv1の`ingest`とは別のversion-aware CLIです。catalog schema 2、catalog identity/created-at、current master version/source hash、current feature extractor、artifact image hashをstrictに検査し、schema変更やv1 writerの暗黙変更は行いません。新規rowは`source_capture_id=observation_id`、`song_id=NULL`、空title/artist、`review_status=unresolved`、`review_revision=0`、manual action/noteなし、candidate/historyなしで作成されます。

同一observation ID・同一canonical payloadは同じreference receiptを返し、異payload、空ID、空title/artist以外のstatus、欠損/改変artifact、driftはcatalog/checkpointを変更せず拒否します。別IDの同一image bytesは別referenceです。既にmanual reviewされたreferenceと同じobservation IDは、payloadが同じでもcurrent row/revision/historyを上書きせず衝突として拒否します。catalog mutation後のcheckpoint保存失敗は、次回retryでcatalog側の既存receiptを読み、checkpointだけを`ingested`へ収束させます。

## title/artist方式評価 (M5c-4)

`title/artist評価`は、collectorで明示採用済みのimmutable `observation.json` / `source.png` / `jacket-crop.png`だけを読むdeveloper-only評価入口です。catalog v2、current master version/source hash、catalog identity/created-at、current feature extractor、manifest/image hash、artifact root内相対pathをstrictに検査し、catalog、checkpoint、manual review revision/historyを変更しません。

期待値はGit管理外のlocal dataset JSONへ記述します。dataset自体、source/crop、reportは`data/`配下に置き、Gitへ追加しません。

```json
{
  "dataset_schema_version": "m5c-title-artist-evaluation-dataset-v1",
  "entries": [
    {
      "observation_manifest": "session-id/observations/observation-id/observation.json",
      "expected_title": "expected title or null",
      "expected_artist": "expected artist or null",
      "expected_song_id": "current master song id or null"
    }
  ]
}
```

UIまたは次の1コマンドで、追加Python packageを使わずlocal Tesseractの`autocontrast` / `white-threshold`方式を比較します。Tesseract未導入、非0終了、空、confidence不足は成功へ丸めません。

```powershell
python -m tools.vision_poc.title_artist_evaluation `
  --dataset data\jacket_catalog_collector\title-artist-dataset.json `
  --artifact-root data\jacket_catalog_collector `
  --master-db data\master\ddrgp-master.sqlite `
  --catalog data\jacket_catalog\catalog-v2.sqlite `
  --output-dir data\jacket_catalog_collector\title-artist-evaluation
```

`title_artist_evaluation.csv/json/md`は同じ入力でbyte-stableに生成され、raw/normalized title/artist、field confidence/status/failure、M4のtitle-primary・artist tie-breakerによる候補と理由、expected coverageを記録します。expectedが両方ある行だけ`evaluated`、片方だけは`partially_evaluated`、両方ない行は`no_expected_values`で、後二者はaccuracy gateへ混入しません。

方式採用gateは、fixture gateに加えて実captureの`evaluated >= 30`、title/artist完全一致率95%以上、field confidence 0.90以上、auto-confirm候補precision 100%、既知誤自動確定0件です。条件未達では`not_adopted`となり、collectorは既存の空title/artist・`unresolved` ingest/manual review経路を維持します。現時点では採用済み実capture datasetがないため、どの方式もauto-confirmへ接続していません。

## Scope boundary

このapp/project/testは `tools/jacket_catalog_collector/` と既存のdeveloper-only Python評価経路で完結し、`app/src/DDRGpScoreViewer` を参照しません。物理削除、source image削除、retention cleanup、ゲーム操作、公開app、正式保存workflow、正式個人スコアDBは実装しません。title/artist評価はlocal reportまでで、採用gate未達の方式をcatalog auto-confirmへ接続しません。
