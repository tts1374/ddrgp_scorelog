# Jacket Catalog Collector (developer-only)

M5c のローカル jacket catalog 運用を支援する、公開 `DDRGpScoreViewer` とは独立した Windows WPF app です。master DB の明示更新、current-only coverage/review projection、監査可能なmanual review、DDR GRAND PRIX window候補の確認とmemory-only capture lifecycle観測を扱います。通常の公開app build、installer、Release、GitHub Actions artifactには含めません。

## 実行

リポジトリrootで実行します。

DDR GRAND PRIXが管理者権限で起動している環境では、windowのprocess start identityを検査できるよう、collectorも管理者として起動したPowerShellから実行してください。権限が不足するwindowは候補へ部分追加せず、0件として扱います。

```powershell
dotnet restore tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj
dotnet build tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj --no-restore
dotnet test tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj --no-restore
dotnet run --project tools\jacket_catalog_collector\src\JacketCatalogCollector\JacketCatalogCollector.csproj --no-build
```

appは実行ファイルの配置場所からrepository rootを解決し、processのcurrent directoryに依存せず `python -X utf8 -m master`、`python -X utf8 -m master.inspect`、`python -X utf8 -m tools.vision_poc.jacket_catalog_review_projection`、`python -X utf8 -m tools.vision_poc.jacket_reference_catalog` を実行します。子processのstdout/stderrはWindows localeに依存させずUTF-8として扱います。Python環境には既存M4/M5bと同じ依存が必要です。

## 基本的な使い方

初期表示の `ジャケット収集` タブだけで通常の収集を進められます。

1. 起動時に固定pathの曲情報DBとジャケット情報DBをread-onlyで検証・読込する。曲情報DBがない場合は `曲情報を更新` で作成し、曲情報DBが有効でジャケット情報DBがない場合はcurrent schemaの空catalogを自動作成する。
2. DDR GRAND PRIXを曲選択画面にし、`収集を開始` を押す。押下時にtop-level windowから、process名が`ddr-konaste`でclient領域が`1280x720`のwindowを自動検出する。
3. 候補が1件だけの場合は手動選択なしでcaptureを開始する。0件の場合は「DDR GPのウィンドウが見つかりません」、複数件の場合は理由を表示して開始しない。
4. DDR GPで曲を移動し、`新しいジャケットを検出` と表示されたら `このジャケットを保存` を押す。session単位の自動保存を使う場合だけ、開始後に既定OFFのcheckboxを明示的に有効化する。
5. `このジャケットは保存済み` と表示されたら、DDR GPで次の曲へ移動する。終了時は `収集を終了` を押す。開始済みのframe処理と保存処理をdrainし、同じsessionのpendingを最終catalog retryした後、current projectionでmatchingを評価し、既存M5のjacket gateまたは#53の既存auto-confirm対象だけを1 transactionで反映してからprojectionを再読込する。

収集終了時の処理順は `pending確定 → matching評価 → auto-confirm一括transaction → projection再読込` です。完成済みDDR WORLD snapshotの`songs.jsonl`と32x32公式jacket画像をcurrent masterへ対応付けて公式feature masterを作り、収集したjacket特徴量へ既存のdistance threshold / ambiguity gateを適用します。他songと競合しないjacket top-1だけを`jacket_gate`として自動確定します。jacketで一意に決まらない行は、従来どおりprojectionの`exact_unique` / `alias_unique`だけを`ocr_title_artist_pair`として自動確定します。`CollectionResult` には `auto-confirm` 件数と `manual残件` 件数が表示されます。曖昧、候補なし、低confidence、評価失敗・評価不能、GP対象外などはjacket単独では自動確定せず、再読込後も未レビュー一覧に残ります。既存のauto/manual/rejected、revision、manual history、artifact、checkpointは上書きしません。同じ終了処理を再実行しても、同一根拠はno-opとして重複作成されません。

detectorの内部状態は通常画面へ表示しません。同じ画像が連続する間も未保存のstable候補は保存可能なまま維持し、保存後は次の曲へ移動する案内を表示します。自動保存は起動時・fresh session・resumeのたびにOFFへ戻り、端末設定へ保存しません。session再開とcatalog retryは `詳細・復旧操作`、master更新とtitle/artist評価は `管理・設定` にあります。

`収集を終了`以外の停止（window終了、resize、device loss、capture failure、例外、collectorのwindow close）では安全停止だけを行い、自動catalog retryは開始しません。artifact、source/crop、manifest、checkpointとpending件数は保持されます。`詳細・復旧操作`へcompatibleなsession IDを入力して `catalog retry` を押すと、captureやwindow再選択を行わず、そのsessionのidentityとartifactを検証してからpendingだけを明示retryできます。drift、非互換checkpoint、artifact破損はcatalogを変更せず拒否します。

## Issue #78 実環境確認

1. DDR GPを終了した状態でcollectorを起動し、`収集を開始`を押す。`DDR GPのウィンドウが見つかりません。ゲームを起動してから再度実行してください。`が表示され、capture、detector、artifact、checkpoint、catalogが開始・変更されないことを確認する。
2. DDR GPを起動して曲選択画面にし、`収集を開始`を押す。手動のwindow選択なしで `ddr-konaste / 1280x720` が検出され、captureが接続中になることを確認する。
3. preview、ジャケット検出、明示保存または既定OFFの自動保存、`収集を終了`を従来どおり確認する。
4. 収集中にDDR GPを終了する。対象window消失として安全停止し、自動再接続・自動再開・自動catalog retryを行わず、既存artifact/checkpointを保持することを確認する。

## Fixed master/catalog paths

repository rootはアプリ配置場所の親directoryを`.git`まで探索して決定します。collectorが所有する正本は次の2ファイルだけです。

```text
<repository-root>/databases/ddrgp-master.sqlite
<repository-root>/databases/jacket-catalog.sqlite
```

収集終了時のjacket gateは、別管理の完成済みsnapshotから最新の有効な
`<repository-root>/data/ddrworld_music_snapshot/<snapshot-id>/`をread-onlyで選び、DDR WORLD公式32x32 jacket画像を照合先に使います。snapshotがない場合はauto-confirmを完了扱いにせず、理由を表示して手動レビュー経路へ残します。

起動時はmasterをstrict read-onlyで検証します。masterがない場合は曲情報なしで起動を継続し、masterが非互換・破損・読取不可の場合は理由を表示してcatalog作成と収集を開始しません。有効なmasterがありcatalogがない場合だけcurrent schemaの空catalogを作成します。既存catalogは必ずread-only検証し、非互換・破損・読取不可でも空DBへ置換しません。

旧`database-paths.v1.json`、`data/`配下の既存DB、任意pathのDBは参照・コピー・移動・自動migrationしません。旧設定ファイル自体も自動削除しません。master更新のbuild、inspect、atomic publish成功後は、固定master/catalogからprojectionを再読込します。

## Master update boundary

`曲情報を更新` は固定master pathをtargetとして、次の順序を固定します。

1. 既存targetが非空なら、network/buildより前に `python -X utf8 -m master.inspect` で互換性を確認する。0 byte fileは明示placeholderとして許可する。
2. OS temporary directoryで `python -X utf8 -m master --output <staging>` を実行する。
3. stagingだけを `python -X utf8 -m master.inspect` し、version、source hash、song/chart/GP件数を読む。
4. inspection成功後にだけtarget親directoryを作り、同じdirectoryのpublish fileをatomic renameしてtargetへ公開する。
5. failure/cancel/publish failureではstaging、summary、publish fileを削除し、新規targetのためだけに作った空parentも削除する。取消時は子process treeの終了とstdout/stderr drainを待ってからcleanupする。

既存のcompatible targetまたは0 byte targetは、build/inspection/publish成功前には変更しません。diagnosticはUI状態とprocess stderrに限定し、部分DBを保持しません。実network成功は通常testの条件にせず、processとpublisherをfakeにしたtestで境界を固定します。

## Projection and manual review contract

UIはcatalog SQLite tableを直接読みません。Python projectionがmasterとcurrent catalog schema version 1をstrictかつread-onlyで検証し、UTF-8 stdoutへprojection version 6の単一JSONを出します。temporary projection fileは作らず、stderrは診断専用です。旧catalog schemaや旧projection versionはfallback表示せずunsupportedとして拒否します。

```powershell
python -m tools.vision_poc.jacket_catalog_review_projection `
  --master-db databases\ddrgp-master.sqlite `
  --catalog databases\jacket-catalog.sqlite `
  --artifact-root data\jacket_catalog_collector
```

top-level必須fieldは `projection_schema_version`、`master`、`catalog`、`coverage`、`songs`、`review_references` です。catalog objectはidentity、schema version 1、created-at、current feature extractorを持ち、旧互換専用のmigration/capability fieldは持ちません。song rowはcanonical title/artistに加えて必須のtitle alias一覧を持ち、未レビュー下書きのtruth song検索へ利用します。review rowはcurrent status/song、notes、登録経路、実行時刻、artifact/checkpoint照合済みのnullable `source_image_path` とversion付き `candidate_evaluation` を持ちます。`candidate_evaluation` はpersisted status/revision、observation ID、jacket preview path、OCR title/artistとconfidence、候補song/reason、failure分類を持ちます。C# loaderは全object/arrayの未知field、必須field、型、coverage/review status、候補/history、candidate分類、revision連続性、schema、分母整合をstrictに検査します。unsupported version、空/truncated stdout、Python非0終了は部分表示しません。`reason`、`note`、`candidate.reason`、`observation_status` はopaque診断文字列として保持します。

表示とfilterは、GP対象songを `referenced` / `needs_review` / `uncollected` / `unresolved` の同じ分母・status histogramで数えます。orphan、候補なし未割当観測、旧extractor、master drift、不正manual featureを派生状態で表示しても、保存済みstatus、revision、historyは変更しません。生成中にmaster/catalogのfile identity、size、mtime、hashが変わった場合はsnapshot混在を拒否します。

既存のcatalog反映用manual review serviceは、projectionのrevision/current status/current song/current notesをpreconditionにし、複数行の`confirmed` / `rejected`だけをPython側の1 transactionで更新します。1行でもvalidationまたは競合に失敗した場合は全行をrollbackし、未レビュー・`hold`はcatalogへ反映しません。同じ内容の再実行はno-opとして安全に収束し、候補、expected song、OCR rawは明示選択へ昇格しません。下書きは `data/jacket_catalog_collector/manual-review-drafts.v1.json` に保存し、catalog反映成功後に対象行だけ削除します。

### 未レビュー下書き

`未レビュー` タブは、catalogへ未反映の `needs_review` / `unresolved` / `orphaned` rowを表示し、title ROI、artist ROI、`status`、`truth song`、`notes`を1行ずつ編集します。自動登録済み（`auto_confirmed`）、manual確定済み（`manual_confirmed`）、reject反映済み（`rejected`）は表示しません。下書きのstatusは `unreviewed` / `confirmed` / `rejected` / `hold` で、truth songを選ぶと `confirmed` になり、`confirmed` はsong ID必須、`rejected` はsong IDを空にします。statusだけを変更しても未反映rowは一覧から消えません。

保存先は `data/jacket_catalog_collector/manual-review-drafts.v1.json` です。ファイルには `observation_id`、`status`、`truth_song_id`、`notes`だけを保存し、再読込後も下書きを復元します。未レビュー側の一括反映は`confirmed` / `rejected`だけを対象にし、`unreviewed` / `hold`は残します。Master検索はcanonical title、title alias、canonical artist、song IDの順に、exact title、exact alias、title prefix、title partial、artist、IDの固定順位で行い、fuzzy matchingは行いません。ROIは既存のimmutable `source.png`を画面内で切り出し、追加の画像生成物は作りません。

### Manual review XLSX export

current projectionから未反映の`needs_review` / `unresolved`だけを、画像埋め込みの単一XLSXへ出力できます。保存先は明示指定した任意の`.xlsx`ファイルで、catalog、Master、下書き、source画像は変更されません。`source.png`がprojectionで検証できない対象は、画像を省略せずexportを拒否します。

```powershell
python -m tools.vision_poc.jacket_catalog_review_projection `
  --master-db databases\ddrgp-master.sqlite `
  --catalog databases\jacket-catalog.sqlite `
  --artifact-root data\jacket_catalog_collector `
  --manual-xlsx-output data\jacket_catalog_collector\manual-review-export.xlsx
```

XLSXは`Manual Review`（`observation_id`、埋め込み`title_roi` / `artist_roi`、`status`、`truth_song_id`、`notes`）、`Master Songs`（current Master全曲）、`Metadata`（XLSX schema/export ID/catalog version/master version/export日時/対象件数）の3 sheetです。編集対象は`status`、`truth_song_id`、`notes`だけです。既存XLSXは、WPFの標準保存ダイアログで確認した場合に上書きされます。

WPFの`未レビュー`画面では、`XLSXをエクスポート`を`一括反映`の左側から実行できます。`一括反映`の右側にある`↻`（projectionを更新）で、current master/catalogのprojectionを再読込します。保存先は標準保存ダイアログで任意に選べます。既定の表示先は`data/jacket_catalog_collector`です。

### レビュー済み訂正

`レビュー済み` タブは、`auto_confirmed` / `manual_confirmed` / `rejected` のcurrent status、current song、下書き予定status/song、notes、登録経路、実行時刻を表示します。予定statusは `unchanged` / `confirmed` / `rejected` / `hold` です。`unchanged` はcurrent status/songを維持し、notesだけの変更はnotes更新として反映します。`hold` はstatus/song/notesを一切反映せず、下書きを残します。song変更、confirmedとrejectの相互変更、notes変更は未レビュー行と同じ一括transactionで処理し、成功した行の下書きだけを消去します。

`unresolved` observationは `data/jacket_catalog_collector` のmanifest/source/crop/checkpointとcatalog rowのidentity/version/hashを照合してから、既存 `tesseract-autocontrast-v1` とM4 title-primary/artist tie-breakerへread-onlyで通します。`exact_unique`、`alias_unique`、`ambiguous`、`no_candidate`、`low_confidence`、`evaluation_failed`、`evaluation_unavailable`、`not_eligible`を丸めず表示し、候補filterと安定sort、明示refreshを提供します。候補表示・refreshはcatalog writerを呼びません。

`候補report` は `data/jacket_catalog_collector/unresolved-candidate-evaluation/` へ `unresolved_candidates.csv/json/md` をatomic生成します。total/current unresolved/eligible/evaluated、候補分類、title/artist別statusとfailure reasonを集計します。expectedや人手確認がないrowを正解扱いせず、precisionを推測しません。

```powershell
python -m tools.vision_poc.jacket_catalog_review_projection `
  --master-db databases\ddrgp-master.sqlite `
  --catalog databases\jacket-catalog.sqlite `
  --artifact-root data\jacket_catalog_collector `
  --report-output-dir data\jacket_catalog_collector\unresolved-candidate-evaluation
```

旧catalogのv1→v2/v2→v3 migration UI/serviceはありません。current schemaとexact一致しない既存DBは副作用なしで拒否し、既存local DB、artifact、checkpoint、source/crop画像を削除・上書き・repairしません。

## Window capture lifecycle

`収集を開始` または明示的な `session再開` の押下時だけtop-level windowを列挙し、process名が`ddr-konaste`かつclient領域が`1280x720`のwindowをDDR GPとして検出します。候補が1件だけの場合に限り、検出したwindowを既存captureへ渡します。0件または複数件では収集を開始せず、通常画面に短い理由を表示します。手動の候補一覧、汎用window選択、自動開始は行いません。

保護された`ddr-konaste` windowでは`PrintWindow`が応答しないため、検出時のpreview取得を試行しません。capture開始後のWindows Graphics Captureでpreviewを取得します。

開始直前と各frame受領時にhandle、PID、process start、process名、title/class、client size、visible/minimized状態を再検査します。stale候補、handle再利用、resize、identity drift、最小化、対象終了では暗黙に別windowを選択・再開始せず、終了理由を表示します。二重開始を拒否し、停止・開始取消・window close・device loss・capture例外は冪等停止としてin-flight callbackをdrainしてからresourceを1回だけ解放します。collector終了時もactive sessionの停止完了を待ちます。

WGCのnative frame queueとimmutable PNG ring bufferは固定上限です。満杯時はframeをdropして `captured` / `dropped` を表示し、producerをUI描画やdisk I/Oで無制限にblockしません。latest preview、件数、開始時/現在size、resource状態は観測専用です。capture単独ではdiskへ保存せず、後述の明示採用だけが選択したsource/cropをlocal evidenceへ昇格します。

## Jacket observation session (M5c-3b)

capture開始前にmaster/catalogのprojectionを読み込み、選択windowのidentity、master version/source hash、catalog identity/schema/created-at、feature extractor、`m5c-capture-utc-clock-v1` frame clock、ROI/detector versionをsessionへ固定します。capture frameは1280x720基準の `(809,27,149,149)` ROIを実サイズへ線形scaleし、16x16 RGB feature、SHA-256 hash、mean absolute differenceを作ります。既定の安定判定は差分 `<=0.08`、連続3 frame、最小100msです。値は`m5c-song-select-jacket-roi-v2`と`m5-jacket-v2`を含むversion付きmanifestへ記録し、旧ROI/artifact/referenceをcurrent扱いへ混在させません。

同じcapture frameから、1280x720基準の`INFORMATION`見出し `(286,35,134,23)` と曲名行 `(286,64,504,25)` も観測します。各ROIを基準sizeへ正規化し、RGB各channel `>=170`かつchannel差`<=45`の白系文字をbinary maskへ変換します。見出し100 pixel以上かつ曲名行32 pixel以上で表示ありとし、曲名行maskのSHA-256が連続3 frame・最小100ms一致した場合だけ安定と表示します。detector、ROI、feature方式はいずれもversion付きで診断表示します。

fresh sessionでは、stable jacketと同じcapture sequence/timestampのstable曲名行だけを組み合わせます。`m5c-jacket-title-composite-identity-v2`、jacket feature version/hash、title-line feature version/hashをこの順のUTF-8 NUL区切りbyte列にし、SHA-256 lower hexをlocal composite identityとします。ROI/extractor v2と同時にcomposite identityもv2へ分離するため、16x16 jacket feature hashが偶然旧cropと同じでもv1 referenceへ重複収束しません。旧v1 identityは既存catalogとidentity-set receiptのstrict検証対象として保持しますが、fresh observationには生成せず、current preflightの重複集合から除外します。title-line未表示・未安定、version不明、frame不一致ではidentityを作らず、明示保存を許可しません。難易度など曲名行ROI外だけの変更はidentityへ含めません。曲名行hashやcomposite identityをOCR文字列、master song/chart ID、catalog field、保存可否の自動判定としては扱いません。

`change_candidate`、`stable_candidate`、`duplicate_preview` は内部的には別状態です。session内で一度stableになったjacket feature hashは、別jacketを挟んで再出現しても既存候補として扱います。通常画面はこれらとtitle-line安定状態を `ジャケットを確認中`、`曲名行を確認中`、`新しいジャケットを検出`、`このジャケットは保存済み` などの操作案内へ変換します。通常は`このジャケットを保存`を明示的に押したときだけ、明示opt-in時はstable composite候補ごとに1回だけ自動で、`data/jacket_catalog_collector/<session-id>/observations/<observation-id>/` に `source.png`、`jacket-crop.png`、`observation.json` と checkpoint をstagingからatomic publishします。どちらもpublish前にcurrent checkpointとcurrent catalogの全review状態を含むcomposite identity集合を照合します。checkpointにあるidentityは既存receipt/retry経路へ留め、catalogだけにあるidentityは保存済み表示にしてartifact/checkpointを新規作成しません。照合後に別processが同じidentityを投入してもcatalogの一意制約と冪等ingestへ収束します。observation IDは従来どおりsession内の決定的なlower SHA-256 idempotency keyですが、fresh sessionでは同一jacketの別title-lineを共存させるためcomposite identityを入力にします。IDの形式とcatalog ingest APIは変更しません。publish前にcurrent master/catalog/extractorと各feature/composite identityを再検査し、失敗stagingは残しません。source/crop/hashの不一致、同一observation IDの異なるpayload、破損・旧version・identity drift checkpointは副作用なしで拒否します。

checkpointは最初の明示採用と同時に作成し、それ以前のframeをdiskへ書きません。fresh sessionは`m5c-observation-manifest-v2` / `m5c-observation-checkpoint-v2`を使い、jacket feature version/hash、title-line feature version/hash、composite identity version/hashを必須fieldとして保存します。unknown/missing/null/empty/非lower-hex、version不一致、canonical hash不一致、manifest/checkpoint driftを副作用なしで拒否します。以後は停止時にも、`session_id`、master/catalog/extractor/window/ROI/detector identity、session内stable jacket feature集合、最後のstable feature、処理frame/drop件数、採用済みobservation ID/source hash、catalog statusをatomic更新します。

capture tabへ既存session IDを入力して `session再開` を押すと、current projection・選択window・全artifact manifest/hashが一致するcompatible checkpointだけを再開します。artifact manifest/checkpointのv1/v2形式自体は変更せず、version混在やcatalog identity/schema/created-at driftを拒否します。current catalogへの投入はmanifest v2のjacket/title/composite identity一式を必須とし、全review status（`rejected`を含む）の同一identityを既存referenceへ収束させます。catalog failure後の `pending` は `catalog retry` からcurrent writerへ明示再投入できます。

capture停止、resize、close、device loss、例外、取消ではsessionを停止し、停止後frameをdetector、artifact、catalogへ渡しません。source/crop/manifest/checkpointはこのcollectorのlocal dataだけで、`logs/`、通常stdout、Git、公開app、正式個人スコアDBへ出力しません。

## Current unresolved observation ingest (M5c-3c)

Pythonの`ingest`はcurrent catalog schema version 1、catalog identity/created-at、current master version/source hash、current feature extractor、artifact image hash、既知versionのjacket/title-line/composite identityとcanonical hash一致をstrictに検査します。新規rowは`source_capture_id=observation_id`、`song_id=NULL`、空title/artist、`review_status=unresolved`、`review_revision=0`、manual action/noteなし、candidate/historyなしで作成されます。

同一observation ID・同一canonical payloadは同じreference receiptを返し、異payload、空ID、空title/artist以外のstatus、欠損/改変artifact、driftはcatalog/checkpointを変更せず拒否します。別IDの同一image bytesは別referenceです。既にmanual reviewされたreferenceと同じobservation IDは、payloadが同じでもcurrent row/revision/historyを上書きせず衝突として拒否します。catalog mutation後のcheckpoint保存失敗は、次回retryでcatalog側の既存receiptを読み、checkpointだけを`ingested`へ収束させます。

## title/artist方式評価 (M5c-4)

`title/artist評価`は、collectorで明示採用済みのimmutable `observation.json` / `source.png` / `jacket-crop.png`だけを読むdeveloper-only評価入口です。current catalog schema version 1、current master version/source hash、catalog identity/created-at、current feature extractor、manifest/image hash、artifact root内相対pathをstrictに検査し、catalog、checkpoint、manual review revision/historyを変更しません。manifest v1/v2は各artifact契約として検査しますが、旧catalog schema identityへのfallbackは行いません。

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
  --master-db databases\ddrgp-master.sqlite `
  --catalog databases\jacket-catalog.sqlite `
  --output-dir data\jacket_catalog_collector\title-artist-evaluation
```

`title_artist_evaluation.csv/json/md`は同じ入力でbyte-stableに生成され、raw/normalized title/artist、field confidence/status/failure、M4のtitle-primary・artist tie-breakerによる候補と理由、expected coverageを記録します。expectedが両方ある行だけ`evaluated`、片方だけは`partially_evaluated`、両方ない行は`no_expected_values`で、後二者はaccuracy gateへ混入しません。

方式採用gateは、fixture gateに加えて実captureの`evaluated >= 30`、title/artist完全一致率95%以上、field confidence 0.90以上、auto-confirm候補precision 100%、既知誤自動確定0件です。条件、matching policy、threshold、OCR方式は変更しません。明示的な`収集を終了`では、完成済みDDR WORLD snapshotの32x32公式jacket画像をcurrent masterへ対応付けた公式feature masterをread-onlyで読み、既存のjacket gateをlive observationのauto-confirmへ接続します。jacketで解決しない行だけ、既存projectionが返す`exact_unique` / `alias_unique`の#53 auto-confirm対象を既存writerへ渡し、それ以外は従来の空title/artist・`unresolved` ingest/manual review経路へ残します。XLSXの再構築、jacket top3 routeやOCR方式の変更は行いません。評価reportや通常のingest/manual review/coverageからauto-confirmを暗黙起動することもありません。

## title/artist OCR診断比較 (M5c-6)

current unresolved artifactを、catalog/master/checkpointとstrict read-onlyで照合したうえで、同じsource captureへtitleの`psm=6/7`、artistの現行5倍・追加2倍相当10倍・追加3倍相当15倍、sharpen有無、`eng` / `jpn+eng`を比較できます。実行前に`--list-langs`と各available language構成のTSV contractを実行probeし、language不足は別languageへfallbackせず`m5c-title-artist-ocr-diagnostics-report-v1`内の`profile.available=false`と`unavailable_reason=tesseract_language_unavailable_v1:<lang>`へ固定します。TSV config欠損、非0終了、invalid outputは516件処理前にfail-fastします。

```powershell
python -m tools.vision_poc.title_artist_ocr_diagnostics `
  --artifact-root data\jacket_catalog_collector `
  --master-db databases\ddrgp-master.sqlite `
  --catalog databases\jacket-catalog.sqlite `
  --output-dir data\jacket_catalog_collector\title-artist-ocr-diagnostics
```

`ocr_diagnostics.csv/json/md`はfield status組合せ、confidence分布、OCR raw、M4 candidate結果をprofile/configuration別に保持します。`representative_contact_sheet.png`は現行M5c baseline（title `psm=7`、artist 5倍sharpen）のstatus/candidate reason組合せから決定的に代表を選び、source縮小、title ROI、artist ROIを並べます。全出力はlocal reportであり、truth、precision、方式採用、auto-confirm、catalog/manual review更新の根拠へ自動昇格しません。ROI座標、confidence gate、catalog schema、writerは変更しません。

## Scope boundary

このapp/project/testは `tools/jacket_catalog_collector/` と既存のdeveloper-only Python評価経路で完結し、`app/src/DDRGpScoreViewer` を参照しません。物理削除、source image削除、retention cleanup、ゲーム操作、公開app、正式保存workflow、正式個人スコアDBは実装しません。title/artist評価reportはread-onlyのままで、auto-confirmは明示的な収集終了から既存jacket gate・projection・policy・writerへ接続する経路に限定します。
