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

## 正規化概要

初回リリース前のdeveloper-only catalogに公開互換性はないため、catalog schema v1/v2/v3の通常runtime互換を廃止する。現行composite identity schemaを唯一の初期schemaとしてversion 1へ再採番し、create、validation、projection、manual review、observation ingest、collector adapterをcurrent schemaだけへ統一する。既存local DB/artifactは削除・上書きせず、自動migrationも行わない。

## In scope

- 現行schema v3のtable、column、constraint、index、metadata契約を初回リリース向けcatalog schema version 1として定義し直す。
- `CATALOG_SCHEMA_VERSION`をcurrent 1件へ統一し、旧schema SQL、旧table column集合、v1/v2/v3分岐を削除する。
- 新規catalog作成をcurrent composite schemaへ変更する。composite fieldの全null/全非null、既知version、canonical hash、catalog全体の一意性は維持する。
- observation ingestをcurrent schema専用の1経路へ統一し、jacket/title-line/composite identity一式を必須にする。旧`ingest` / `ingest-v2`の通常互換入口とschema別payload分岐を削除する。
- projectionとC# loader/adapterをcurrent catalogだけへ統一し、`migration_required`、`read_only`、`manual_review_v2`など旧catalog互換のためだけのcapability分岐を整理する。
- v1→v2 migration UI/service、`migrate-v2`、v2→v3を通常運用として説明するREADME導線を削除する。
- title/artist evaluation、receipt validation、manual review、coverage、M5 feature loaderをcurrent catalogだけで検証する。
- 旧catalog fixture中心のtestをcurrent schemaのcreate、strict validation、manual review、identity lookup、duplicate、projectionへ置き換える。
- `docs/design/05_storage_io_spec.md`、`docs/design/06_regression_guard.md`、collector/vision PoC READMEをcurrent-only契約へ同期する。

## Fixed decisions

- catalogの唯一のcurrent schema versionは`1`とする。現行v3の内容を初期schemaへ畳み、`3`を公開済みversionとして維持しない。
- runtime、projection、collectorはcurrent catalogだけを受け入れ、旧catalogをread-only表示やlegacy ingestへfallbackしない。
- current catalogはcomposite identity未backfill rowを新規作成しない。通常ingestは完全なidentity一式を必須にする。
- manual review status/history、candidate、coverage、M5永続特徴量の現行責務は維持する。
- 既存local catalog、artifact、checkpoint、source/crop画像を削除、上書き、in-place repairしない。旧DBはunsupportedとして副作用なしで拒否する。
- local data救済を通常runtime互換の理由にしない。必要な観測はsource artifactから新current DBへ明示再構築し、current-only実装へlegacy importerを常設しない。既存`migrate-v3`が作るschema v3 DBもcurrent-only化後はunsupportedとする。
- schema整理をartifact manifest/checkpoint version整理と同時に行わない。`m5c-observation-manifest-v1/v2`、checkpoint v1/v2、resume/retry状態機械はこのPRの対象外とする。
- schema整理後も自動保存は開始しない。

## Acceptance criteria

- 新規catalogは`PRAGMA user_version=1`、metadata schema version 1、composite identityを含むcurrent exact schemaで作成される。
- current catalogのcreate、strict validation、projection、manual review、coverage、M5 feature load、observation ingest、receipt validationが成功する。
- observation ingestはidentity fieldのmissing/null/empty、unknown version、非lower-hex、canonical hash不一致をcatalog変更前に拒否する。
- 同じcomposite identityの別observation、retry、競合はreview statusが`unresolved`、review待ち、確定、再割当、`reopen`、`rejected`のいずれでも既存referenceへ収束し、2件目を作らない。
- 旧catalog schemaを指定すると、projection、manual review、ingest、collector preflightはDB byteを変更せずunsupportedとして拒否する。
- code、CLI help、projection fixture、C#表示、READMEにcatalog v2/v3をcurrent互換として扱う分岐や案内が残らない。
- catalog整理によってartifact/checkpointのversion、保存内容、resume/retry、atomic publish/rollbackを変更しない。
- test前後で既存local DB、artifact、checkpoint、source/crop画像が変更されず、生成物がGit差分へ混入しない。

## Out of scope

- artifact manifest/checkpoint v1/v2の再採番、migration、互換削除。
- 既存local DB/artifactの実migration、削除、移動、cleanup、retention。
- current checkpointとcatalog identity集合の保存前照合、保存ボタン無効化、opt-in自動保存。
- OCR文字列による曲ID確定、master song/chartへの自動割当。
- ゲーム操作、focus操作、grid自動巡回。
- 正式個人スコアDB、M7/M8保存判定、公開`DDRGpScoreViewer`への接続。

## Open risks / blockers

- current schemaへの再採番により、既存local catalogは意図どおりunsupportedになる。source artifactから観測を再構築できるが、既存manual review state/historyも保持する必要がある場合は、current-only PR着手前にその要否と一回限りの移送方法を決める必要がある。
- artifact manifest/checkpointには採用時のcatalog schema versionが保存されるため、旧catalog由来sessionをcurrent catalogへ暗黙resumeしない境界を維持する必要がある。
- migration/backfill testを削る際も、current schemaのcanonical identity、一意性、transaction rollback、target/source byte不変に相当する回帰guardを失わないよう置き換える。

## それ以降の順序（次PRでは着手しない）

catalog current-only化の後に、current checkpointとcatalogのcomposite identity集合を保存前に照合する明示opt-in自動保存を別PRとして扱う。opt-inの保存場所、backfill不能観測の表示、保存前readからwriter receipt/checkpoint updateまでの状態遷移、実capture false-positive gateは、そのPR着手前に固定する。
