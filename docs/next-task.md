# 現在PR完了記録

developer-only jacket collectorへ、song select画面の`INFORMATION`表示と曲名行画像hashを観測するversion付きdetectorを追加し、通常の収集画面へread-only表示した。保存可否、artifact、observation manifest/checkpoint、catalog schema/writerは変更していない。

## 今回の完了範囲

- 1280x720基準の`INFORMATION`見出しROI `(286,35,134,23)` と曲名行ROI `(286,64,504,25)` を実capture sizeから固定sizeへ正規化する。
- RGB各channel `>=170`かつchannel差`<=45`の白系文字をbinary mask化し、見出し100 pixel以上かつ曲名行32 pixel以上の場合だけ`INFORMATION`表示ありと判定する。
- 曲名行binary maskをSHA-256 lower hexへ変換し、同じhashが連続3 frameかつ100ms以上続いた場合だけ安定と判定する。
- detector、ROI、feature方式をそれぞれ`m5c-information-title-line-detector-v1`、`m5c-song-select-information-panel-roi-v1`、`m5c-information-title-line-binary-sha256-v1`としてversion固定した。
- 通常の収集画面へ`INFORMATION`表示有無、曲名行の確認中/安定、hashを表示し、詳細欄へversion付きdiagnosticを表示する。
- panel/title line欠損、invalid PNG/size/ROI、timestamp逆行では安定状態をresetし、hashを保存経路へ渡さない。
- start/resume/stopと既存bounded frame queueへ接続し、停止後frameを処理せず、開始時と停止時にread-only表示をresetする。
- synthetic frameのdetector/ViewModel/XAML回帰testを追加した。Git管理外のsong-select素材64件を読み取り検証し、見出し・曲名行を全件検出、同一曲名の2 captureで同一hash、New York EVOLVED Type A/B/CとLondon EVOLVED ver.A/B/Cで相互に異なるhashとなることを確認した。

## 維持した境界

- 曲名行hashをOCR文字列、master song/chart ID、保存可能判定として扱っていない。
- jacket stable、`CanAdopt`、明示保存、observation ID、manifest/checkpoint schema、catalog ingestへ接続していない。
- source/crop/artifact/checkpoint、master/catalog DB、local setting、capture画像、評価dataset/reportを作成・変更・移動・削除していない。
- catalog schema/migration/backfill、catalog/checkpoint重複判定、opt-in自動保存へ進んでいない。
- 公開`DDRGpScoreViewer`、正式個人スコアDB、ゲーム操作、cleanup/retentionへ接続していない。

# 次PR仕様

次PRで許容する実装は、`observation manifest/checkpointへversion付き複合identityを保存`の1件だけとする。catalog schema/migrationと自動保存には進まない。

## 目的

jacket image featureと、同じcapture frameで安定済みの`INFORMATION`曲名行featureを組み合わせ、同一jacketを共有する別曲をlocal observation artifact/checkpoint内で区別できる決定的なidentityとして保持する。

## In scope

- `m5c-jacket-title-composite-identity-v1`を追加し、jacket feature version/hash、title-line feature version/hashからSHA-256 lower hexを決定的に生成する。
- jacket stable候補へ同じframeの安定済みtitle-line featureを関連付け、明示採用時に表示中の候補と同じ組合せを保存する。
- 新規observation manifestとcheckpoint ledgerへ、各feature version/hashとcomposite identity version/hashをstrictな必須fieldとして保存する。
- manifest/checkpoint schema versionを更新し、unknown/missing/null/empty/非lower-hex、version不一致、artifact/checkpoint間identity driftを副作用なしで拒否する。
- 既存versionのartifact/checkpointを変更・backfill・削除せず、legacy resume/retryの互換経路と新version経路を明示的に分離する。
- artifact publish、checkpoint save、resume/retry、同一ID異payload、rollbackの対象・影響範囲testとREADMEを同期する。

## Fixed decisions

- canonical inputは、UTF-8の`composite identity version`、`jacket feature version`、`jacket feature hash`、`title-line feature version`、`title-line hash`をこの順序でNUL区切りしたbyte列とする。
- 同じ曲の難易度変更は曲名行hashが同じため同一composite identityとして扱う。
- jacketが同じでも曲名行が異なるType/ver A/B/Cは別composite identityとして扱う。
- title-line未表示、未安定、detector/ROI/feature version不明、jacket候補とtitle-lineのframe不一致ではcomposite identityを作らず、明示採用を許可しない。
- observation IDとcatalog ingest契約は次PRでは変更しない。composite identityはmanifest/checkpoint内のlocal観測fieldに限定する。

## Acceptance criteria

- 同一jacket hashと同一title-line hashから、retry/restart後も同じversion付きcomposite identityが生成される。
- 同一jacket hashでもNew York EVOLVED Type A/B/CおよびLondon EVOLVED ver.A/B/Cのtitle-line hashが異なれば別identityになる。
- 難易度表示などROI外だけが変わってもidentityは変わらない。
- UIで表示したstable jacket/title-lineの組合せと、manifest/checkpointへ保存された各hash・composite identityが一致する。
- missing/unknown/corrupt/identity drift、partial publish、checkpoint save失敗、resume/retryでは既存artifact/checkpoint/catalogを不正に変更しない。
- 既存versionのlocal artifact/checkpointを暗黙migrationせず、互換経路の挙動をtestで固定する。

## Out of scope

- catalogへのcomposite identity field/index追加、既存`source.png`からのmigration/backfill。
- checkpointとcatalogを横断した重複判定、保存ボタン無効化、自動保存。
- OCR文字列による曲ID確定、master song/chartへの自動割当。
- DDR GPのキー/focus操作、grid自動巡回。
- 個人スコアDB、M7/M8保存判定、公開`DDRGpScoreViewer`への接続。

## それ以降の順序（次PRでは着手しない）

1. catalogへcomposite identityを保持・検索できるschemaと、既存local `source.png`からの非破壊migration/backfillを追加する。
2. current checkpointとcatalogのidentity集合を保存前に照合する、明示opt-inの自動保存を追加する。

各項目は直前PRのmerge後に1件ずつ`次PR仕様`へ昇格し、複数を同時実装しない。
