# 現在PR完了記録

developer-only jacket catalogへversion付きcomposite identityを保持・検索するschema v3と、既存local observation artifactからのcopy-on-write migration/backfillを追加した。明示保存writerはv3 identityをtransaction内で一意化するが、checkpoint/catalog照合UIとopt-in自動保存には進んでいない。

## 今回の完了範囲

- catalog schema v3へjacket feature version/hash、title-line feature version/hash、composite identity version/hashを全nullまたは全非nullの1組として追加した。
- 非null identityは既知version、lower SHA-256、既存NUL区切りcanonical hashをstrict検証し、`(composite_identity_version, composite_identity_hash)`のpartial unique indexでcatalog全体を一意化した。
- `load_composite_identities()`を追加し、`unresolved`、review待ち、確定、再割当、`reopen`、`rejected`を区別せず、存在する全identityをstrict read-only集合として取得できるようにした。
- v2 source catalog、未作成v3 target、local artifact rootを明示する`migrate-v3`を追加した。source read snapshotから別stagingへreference/candidate/review historyを複製し、strict検証後だけ新規targetをexclusive publishする。
- `source_capture_id`に対応する一意な`observation.json`、`source.png`、`jacket-crop.png`のversion、hash、寸法、jacket featureを検証する。manifest v1は固定済みROI/binary maskからtitle-line featureとcomposite identityを再計算し、manifest v2は保存済みidentityとも照合する。
- artifact欠損、改変、unknown version、複数locator、identity競合は推測でbackfillせず、rowをnullのままreceiptの`counts`/`rows`へ理由付きで残す。
- v3 fresh ingestはmanifest v2のidentity一式を必須にし、同じidentityの別observation/retry/競合をreview statusに関係なく既存reference receiptへtransaction内で収束させる。
- projection/manual review/title-artist evaluation/collector adapterをv3互換へ更新した。v2→v3でcatalog created-atを維持した場合だけ、元v2 schema identityを持つimmutable artifactをevaluationで許容する。
- schema、migration、missing/corrupt artifact、canonical validation、identity lookup、transactional duplicate、projection、collector adapterの回帰testと設計docs/READMEを同期した。

## 維持した境界

- v1/v2 catalog、local artifact、checkpoint、source/crop画像、master DBを上書き・修復・削除していない。migration targetは常に別の未作成pathとする。
- backfill不能rowを別identityへ推測せず、title-line hashをOCR文字列、master song/chart ID、正式保存値へ昇格していない。
- manual review revision/history、候補、確定song、`rejected`をmigrationで変更していない。
- current checkpointとcatalog identity集合を保存前に照合するUI、保存ボタン無効化、opt-in自動保存、ゲーム操作へ進んでいない。
- 公開`DDRGpScoreViewer`、正式個人スコアDB、M7/M8保存判定、cleanup/retentionへ接続していない。
- 実local catalog/artifact migrationは実行しておらず、testはtemporary synthetic artifactだけを使用した。

# 次PR仕様

次PRの実装仕様は未確定。今回PRでは次PR相当へ着手しない。

## 既存資料から確定している順序

次の候補は、current checkpointとcatalogのcomposite identity集合を保存前に照合する、明示opt-inの自動保存1件である。

## 未決事項

- opt-inをsession単位にするか端末settingへ記憶するか。
- catalogにidentityがないbackfill不能rowを通常画面でどう表示し、明示再収集をどの操作として許可するか。
- 保存前read、writer競合receipt、artifact publish、checkpoint updateの状態遷移と、partial failure/retry時の表示契約。
- 登録済みidentityで保存ボタンを無効化する範囲、manual saveとの関係、同じidentity表示中の再判定解除条件。
- 実capture fixtureでのfalse-positive 0件確認を自動保存開始の必須gateにする具体的な母数と手順。

これらを既存資料だけから一意に決められないため、次PR着手前に仕様と受入条件を固定する。
