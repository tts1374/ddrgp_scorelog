# 次PR作業仕様

直近のM5c-3a PRで、developer-only jacket catalog collectorへDDR GRAND PRIX window候補検出、preview付き明示選択、strict identity再検査、Windows Graphics Capture lifecycle、bounded memory-only frame ring buffer、drop/size/resource状態表示を追加した。frame、preview、diagnosticはdisk/catalogへ保存せず、停止・取消・対象終了・resize・device loss・例外・collector終了時のcallback drainとresource解放をfake testで固定した。

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md`、`docs/implementation-roadmap.md` の M5b/M5c、`docs/design/09_master_match_poc.md`、`tools/jacket_catalog_collector/`、`tools/vision_poc/jacket_reference_catalog.py` を読み、master/catalog DB、既存capture/crop、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

xhigh

capture lifecycleを維持しながら、frame dedupe、安定判定、checkpoint、atomic local artifact、catalog再投入の副作用境界を同時に固定するためです。title/artist OCRや公開app連携へは進みません。

## 作業ブランチ

```powershell
codex/m5c-jacket-observation-session
```

## Normalized Summary

M5c-3bとしてdeveloper-only collectorへ、song select jacket ROIの変化/安定検出、同一preview抑制、明示採用、session checkpointと中断再開、local observation artifact生成、既存M5b catalogへの冪等投入を追加します。

このPRは収集sessionと観測evidenceの安全な生成・再投入だけを扱います。title/artist OCR、auto-confirm条件の緩和、ゲーム操作、公開app、正式保存workflowには接続しません。実装前に、検出・checkpoint・catalog投入を1つのtransaction/検証セットで安全にレビューできないと判明した場合は、観測生成までとcatalog投入を分割し、後半を `docs/next-task.md` の次PRへ残してください。

## In Scope

- M5c-3aの明示選択済みcapture sessionだけを入力とし、1280x720基準のsong select jacket ROIを実frame sizeへ線形scaleして抽出する。
- jacket ROIの変化候補と安定状態を、固定version付きfeature/hash/差分指標、連続frame数、最小継続時間で判定する。閾値と判定根拠をUI/diagnosticへ表示する。
- 同一preview/同一安定jacketの連続frameをsession内で抑制し、ring buffer dropと「同一なので採用しない」を区別する。
- 安定候補は自動でcatalogへ投入せず、開発者の明示採用でcapture/crop/observationをlocal stagingへ生成してからatomic publishする。
- session開始時にmaster version/hash、catalog identity/schema、feature extractor version、window identity、ROI/detector versionを固定する。
- checkpointでsession ID、固定identity/version、最後の安定候補、採用済みobservation ID/source hash、進捗を保持し、中断・再開時にstrict再検査する。
- 同一session/observationの再投入は冪等にし、同じaction/observation IDの異なるpayload、master/catalog/extractor drift、破損checkpointを副作用なしで拒否する。
- local source capture、jacket crop、manifest/checkpoint/observationは `data/jacket_catalog_collector/` 配下のsession固有pathに限定し、`logs/` や通常stdoutへimage bytes/pathを漏らさない。
- 既存M5b catalog APIを再利用し、観測title/artistを自動推測しない。title/artist未取得の観測はauto-confirmせず、候補/evidenceを保持した `needs_review` / `unresolved` 境界へ送る。既存APIが空title/artist観測を安全に受けられない場合はAPI契約を推測で緩めず、catalog投入を次PRへ分割する。
- fake frame clock/source、detector、checkpoint store、artifact publisher、catalog adapterで正常系、重複、競合、再開、部分失敗、side-effect境界をテストする。
- collector README、`docs/implementation-roadmap.md`、`docs/design/09_master_match_poc.md` を同期する。

## Out of Scope

- title/artist OCR、外部metadata取得、OCR confidence/auto-confirm閾値
- 候補title/artist、expected song、近傍候補からの暗黙song確定
- catalog v3、新しい正式schema、M5 runtime matcher契約変更
- window候補条件、WGC resource ownership、公開 `DDRGpScoreViewer` のcapture契約変更
- windowへの入力、focus移動、grid巡回、ゲーム操作自動化
- 正式個人スコアDB、`source_captures`、`analysis_logs`、`plays`、正式保存workflow
- retention期限、既存source/cropのcleanup・物理削除
- installer、Release、telemetry、GitHub Actions artifact

## Fixed Decisions

- detectorはM5c-3aの停止/identity/size/resource lifecycleより下流に置き、停止後frameや異常終了sessionからartifact/catalog side effectを発生させない。
- change candidate、stable candidate、adopted observation、catalog ingestedを別状態にし、stableを自動採用・自動確定しない。
- detector version、ROI、閾値、frame clockはcheckpoint/observationへ記録し、異なるversionのcheckpointを現行sessionとして黙って再開しない。
- checkpointとartifactはstagingをstrict検証した後だけatomic publishする。失敗・取消・競合では既存checkpoint/catalogと完成済みartifactを変更しない。
- observation/action IDとcanonical payloadで冪等性を判定し、同じ画像hashだけで別song候補や別sessionの観測を統合しない。
- capture/cropはlocal evidenceであり、Git、CI artifact、Release、通常公開logへ含めない。cleanupは明示操作を別PRで設計するまで自動実行しない。
- title/artistがない観測はauto-confirmしない。M5c-4の取得方式評価前に一意性条件を緩めない。

## Pending Decisions

- jacket change/stableの具体的閾値と必要frame/継続時間
- session内同一previewのfeature/hash組合せと許容差
- title/artist未取得観測を既存catalogへ保持する最小互換経路。安全な既存経路がなければcatalog投入を次PRへ分割する
- source capture/crop retention期限、cleanup UX、欠損locator表示
- title/artist取得方式とauto-confirm採用条件

これらはfixture/実測と既存契約からPR内で安全に決められる範囲だけ固定し、公開契約やcatalog schema変更が必要なら後続PRへ送ります。

## Deliverables

- versioned jacket ROI detector、change/stable state machine、同一preview抑制
- 明示採用UIと採用候補/evidence表示
- versioned checkpoint model/store、strict resume/reject、atomic artifact publisher
- observation manifest/source capture/jacket cropのlocal session layout
- 冪等catalog adapterまたは、安全な既存契約がない場合の明示分割結果
- fake依存によるdetector・checkpoint・resume・競合・side-effect test
- README・設計docs・次PR仕様の同期

## Boundary Condition Matrix

| 状態/操作 | 期待結果 | 副作用境界/test |
| --- | --- | --- |
| frameなし/ROI不正 | stable候補なし、診断表示 | artifact/checkpoint/catalog作成なし |
| 同一preview継続 | 1候補として安定度更新 | observation重複なし、dropと別集計 |
| jacket変化→安定 | change/stable根拠を表示 | 明示採用前はdisk/catalog変更なし |
| 安定前に次jacketへ変化 | 前候補を不採用で破棄 | partial crop/observationなし |
| 明示採用 | staging検証後にsession artifact公開 | source/crop/manifest整合 |
| 同じobservation再投入 | 同一receipt/既存結果を返す | reference/history重複なし |
| 同じID・異なるpayload | 競合拒否 | checkpoint/artifact/catalog不変 |
| 中断→compatible resume | 固定version/identityを再検査して継続 | 採用済みobservation再生成なし |
| corrupt/旧version/drift checkpoint | 再開拒否、明示新規session待ち | 暗黙migration/上書きなし |
| artifact publish途中失敗 | staging cleanup、失敗表示 | 完成session/checkpoint/catalog不変 |
| catalog投入失敗 | evidence/checkpointから安全にretry可能 | saved扱い・部分成功へ丸めない |
| capture stop/resize/close/device loss | detector停止、終了理由を維持 | 停止後artifact/catalog更新なし |

## Validation

- .NET: detector、dedupe、adoption ViewModel、checkpoint、artifact publisher、catalog adapterの対象testとcollector全test。
- Python: jacket reference catalog/projectionの関連test。Python側catalog APIを変更した場合はその責務とconsumerの影響範囲testを実行する。
- `dotnet build`、collector全test、関連Python test、Ruff、compileall、`git diff --check` を実行する。
- 共通catalog schema/loader/helperを変更した場合はPython全テストを実行する。
- jacket ROI/detectorを変更するため、collector用fixtureだけでなく `python -m tools.vision_poc` が同じ画像処理共通コードへ影響する場合はVision PoC本体を実行する。collector内独立実装で既存PoCへ影響しない場合は省略理由と残る実capture riskを報告する。
- 実window/実captureの任意目視確認はmerge条件にせず、deterministic fake clock/frame fixtureを必須にする。

## Acceptance Criteria

- 同一preview、変化中、安定候補、明示採用を機械的に区別し、stableだけでdisk/catalogへ自動昇格しない。
- checkpoint再開がversion/identity/driftをstrictに検査し、再投入・競合・部分失敗でobservation/referenceを重複させない。
- 完成artifactだけが `data/jacket_catalog_collector/` 配下へatomic publishされ、停止後frameや失敗stagingがcatalogへ届かない。
- title/artist OCRなしにauto-confirm条件を緩めず、既存M5b catalog/runtime/manual historyの不変条件を維持する。
- capture lifecycle、公開app、正式保存workflow、正式個人スコアDBを変更していない。
- read-only branch diffレビューでmedium以上の未対応指摘がない。

## Open Risks / Blockers

- 実gameのjacket切替速度とanimationはfixtureだけで完全再現できないため、閾値はversion付き・診断可能にし、実機確認は任意補助とする。
- title/artist未取得観測を既存catalogへ安全に保持できない場合、catalog schemaを推測で変更せず、M5c-3bをartifact/checkpointまででmerge可能にしてcatalog投入を独立次PRへ分割する。
- capture/cropはlocal dataであり、retention/cleanup未確定の間は自動削除しないため、disk使用量が増える。明示cleanupは別PRとする。

完了後はM5c-4のtitle/artist取得方式・auto-confirm採用条件の実capture評価、または上記分割が必要ならM5c-3c catalog投入を独立した次PR仕様へ更新し、今回変更だけをcommit、現在のbranchへ通常pushしてdraft PRを作成してください。更新後の次PRには着手しないでください。
