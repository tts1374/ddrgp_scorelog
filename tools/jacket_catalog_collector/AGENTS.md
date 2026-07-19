# Jacket Catalog Collector Agent Rules

このファイルは `tools/jacket_catalog_collector/` 配下の実装・検証に追加適用する。コマンド、UI操作、機能説明は `README.md` を事実源とし、ここでは複数PRで維持する実装境界を定める。既存設計・READMEから固定済みの規則だけを記載し、新しい状態遷移や製品仕様を決めない。

## Scope And Data Safety

- developer-only collectorを公開 `DDRGpScoreViewer`、installer、Release、GitHub Actions artifact、正式保存workflow、正式個人スコアDBへ接続しない。
- capture、crop、feature、checkpoint、catalog/master DBはlocal dataとし、Git、通常stdout、公開logへ混入させない。物理削除、retention cleanup、既存DB修復は明示scopeなしに行わない。
- Python subprocessのstdoutは契約データ、stderrはdiagnosticとして分離し、C#側で非0終了、empty/truncated/unknown/invalid JSONを部分成功へ丸めない。

## Master And Catalog Publication

- 既存非空masterはnetwork/build前にinspectし、0 byte placeholderだけを明示的に許容する。
- build/migrationはtargetと別のstagingへ出し、strict inspection成功後だけ同一directoryのpublish fileからatomic/exclusive publishする。
- failure、cancel、inspection/publish failureでは既存source/targetを不変に保ち、staging、summary、publish file、新規target用に作った空parentだけをcleanupする。cancel時はchild process tree終了とstdout/stderr drainを待つ。
- catalog v1→v2はsourceを上書きせず、別pathのv2 stagingをstrict検証後にだけpublishする。unsupported/corrupt source、既存target、競合、失敗時はsource/targetを変更しない。

## Projection And Manual Review

- UIはSQLite tableを直接解釈せず、Pythonのstrict read-only projectionだけを読む。projection中にmaster/catalogのidentity、size、mtime、hashが変わった場合はsnapshot混在を拒否する。
- C# loaderはschema version、必須/未知field、型、status語彙、revision連続性、capability、分母整合をstrictに検査する。diagnostic用のopaque文字列だけは未知値を表示可能とする。
- derived coverage/review表示でpersisted status、revision、historyを変更しない。UI表示candidate、filter、histogramは同じGP対象分母とstatusを使う。
- manual reviewはexpected revision/status/songとaction IDをpreconditionにし、current rowとappend-only historyをPython側の1 transactionで更新する。同一action ID・同一payloadだけを冪等成功とし、異payload、stale state、identity driftは副作用なしで拒否する。
- candidate、expected song、OCR rawを明示選択へ昇格させない。`confirm` / `reassign` は完全なcurrent extractor featureを要求し、`reject` / `reopen` にsongを渡さない。

## Capture Lifecycle And Ownership

- window候補を自動選択・自動開始しない。developerがpreview、候補理由、handle/PID/process start/title/class/client size/stateを確認した場合だけcaptureを開始する。
- 開始直前と各frameでidentity/size/visible/minimized状態を再検査し、stale候補、handle再利用、resize、identity drift、minimize、target終了では暗黙に別windowを選択・再開しない。
- 二重開始を拒否する。stop、cancel、window close、device loss、exception、collector終了は冪等停止とし、in-flight callbackをdrainしてからsession/frame/resourceを1回だけdisposeする。停止後frameをdetector、artifact、catalogへ渡さない。
- WGC native frame queueとimmutable PNG ring bufferはboundedとする。満杯時はdrop countを観測可能にし、producerをUI描画やdisk I/Oで無制限にblockしない。
- capture単独はmemory-onlyとし、disk publicationは明示採用経路だけに限定する。

## Observation, Artifact, Checkpoint

- session開始前にcurrent projectionと選択windowを検査し、master/catalog/extractor/window/frame-clock/ROI/detector identityをsessionへ固定する。
- `change_candidate`、`stable_candidate`、`duplicate_preview`、adopt済みを混同しない。stable候補は自動採用せず、UI表示candidateと実際のadopt対象を一致させる。
- 明示採用時だけsource、crop、manifest、checkpointをstagingからatomic publishする。publish前にcurrent master/catalog/extractorとsource/crop/hashを再検査し、失敗stagingを残さない。
- checkpointは初回採用と同時に作り、それ以前のframeをdiskへ書かない。停止時のprogress更新もatomicにし、既存artifactをrollback対象にしない。
- resume/retryはcurrent identities、version、全artifact manifest/hash、observation ID/payloadをstrict検査する。corrupt/old/drift、同一ID異payloadはcatalog/checkpoint副作用なしで拒否する。
- catalog failureは `pending` / `deferred` を成功扱いへ丸めず明示retry可能にする。catalog commit後のcheckpoint失敗では、同一receiptの冪等再投入でcheckpointだけを収束可能に保つ。
- catalog v1/v2、schema、manual review、auto-confirm、observation ingestの境界は、指定Issue、関連する親Issue、設計docsに従う。Issueと設計docsが矛盾する場合は推測で仕様を拡張せず、矛盾内容を報告する。nested規則から新しいwriterやmigrationを推測しない。

## Validation Focus

- 正常系に加え、cancel、partial failure、duplicate/replay、異payload、stale revision、identity drift、old/corrupt checkpoint、queue満杯、stop後frame、publish failureを変更責務に応じて確認する。
- 拒否経路では、既存master/catalog/artifact/checkpointのbyte不変、不要なdirectory/file/log生成なし、resourceの単一disposeを確認する。
- Python/C# error分類やprojection schemaを変えた場合はproducerと全consumer、fixture、README/design docsを同期する。
