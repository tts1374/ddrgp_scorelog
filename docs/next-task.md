# 現在PR完了記録

初回リリース前のdeveloper-only jacket catalogをcurrent composite schema 1件へ統一し、旧catalog schemaの通常runtime互換、migration、legacy ingest、read-only projection fallbackを削除した。既存local DB/artifact/checkpoint/source/crop画像は変更せず、current catalogとexact一致しないDBを副作用なしでunsupportedとして拒否する。

## 今回の完了範囲

- 現行composite identity付きschemaを唯一のcatalog schema version 1として定義した。新規catalogは`PRAGMA user_version=1`、metadata schema version 1、manual review state/history、jacket/title-line/composite identity列・constraint・一意indexを持つ。
- `CATALOG_SCHEMA_VERSION`を1件へ統一し、旧schema SQL、v1/v2/v3分岐、v1→v2/v2→v3 migration API/CLI、CSV legacy build/ingestを削除した。`create` CLIで`data/`配下の未作成pathへcurrent catalogを作成できる。
- current `ingest`は非空observation ID、artifact image hash、current master/catalog/extractor identity、jacket/title-line/composite identity一式を必須にする。missing/null、unknown version、非lower SHA-256、canonical hash不一致をcatalog変更前に拒否する。
- 同一observation ID・同一payloadは冪等、異payloadは副作用なしで拒否する。異なるobservation IDでも同じcomposite identityなら、`unresolved`、`auto_confirmed`、`manual_confirmed`、`needs_review`、`rejected`を含む全review状態で既存reference receiptへtransaction内で収束する。
- create、strict validation、manual review、coverage、M5 feature load、read-only identity lookup、observation session/receipt validationをcurrent schemaだけへ統一した。
- projectionをcurrent-only version 3へ更新し、`migration_required`、`read_only`、`manual_review_v2`など旧compatibility fieldを削除した。C# loader、model、ViewModel、fixture、UIから旧projection/catalog互換とmigration操作を削除した。
- collector adapterはcatalog schema version 1のcurrent `ingest`だけを呼び、manifest v2の完全なcomposite identityを渡す。unsupported catalog sessionをPython実行前またはstrict preflightで拒否する。
- title/artist evaluationはcurrent catalog schema version 1だけを受け入れ、旧catalog schema identityへのfallbackを削除した。artifact manifest v1/v2自体の検査契約は維持した。
- `docs/design/05_storage_io_spec.md`、`docs/design/06_regression_guard.md`、`docs/design/09_master_match_poc.md`、implementation roadmap、vision PoC/collector READMEをcurrent-only契約へ同期した。

## 維持した境界

- 既存local catalog、artifact、checkpoint、source/crop画像をmigration、削除、移動、上書き、in-place repairしていない。
- artifact manifest/checkpoint v1/v2のversion、保存内容、atomic publish、resume/retry状態機械をcatalog schema versionと同時に再採番していない。
- backfill不能観測を別identityへ推測せず、title-line hashをOCR文字列、master song/chart ID、正式保存値へ昇格していない。
- manual review revision/history、candidate、coverage、M5永続特徴量の責務を維持した。
- current checkpointとcatalog identity集合の保存前照合、保存ボタン無効化、opt-in自動保存、ゲーム操作、正式個人スコアDB、公開`DDRGpScoreViewer`へ進んでいない。
- 実local DB/artifactを使用するmigrationやrepairは実行していない。testはtemporary synthetic DB/imageだけを使用した。

## 検証実績

- `python -m pytest -q`: 538 passed。
- `python -m ruff check tools/vision_poc pyproject.toml tests`: passed。
- `python -m compileall -q master tools/vision_poc`: passed。
- `dotnet test tools/jacket_catalog_collector/tests/JacketCatalogCollector.Tests/JacketCatalogCollector.Tests.csproj --no-restore`: 131 passed。
- Python testの既知warningとして`pytest_chalice`経由の`pkg_resources` deprecated warningだけを確認した。
- 画像分類、ROI、OCR logicは変更していないため、local screenshot素材を使う`python -m tools.vision_poc`は実行条件外とした。

# 次PR状態

次PRの実装仕様は未確定。今回PR完了後に自動的に着手しない。

既存資料上の後続候補は、current checkpointとcurrent catalogのcomposite identity集合を保存前に照合する明示opt-in自動保存だが、次のproduct/状態遷移判断が固定されていないため、この記録では受入条件を推測しない。

## 未決事項

- opt-in設定の保存場所、既定値、session単位か端末単位か。
- current catalogに存在しないidentityだけを保存可能とするUI表示と、backfill不能/unsupported旧sessionの表示。
- 保存前read、writer receipt、checkpoint update、retry/replay、catalog commit後checkpoint failureの状態遷移。
- 保存ボタン無効化と明示手動保存の優先関係。
- 実capture false-positive gateと、auto-saveを有効化できる評価条件。
- concurrent session/processが同じidentityを保存する場合のownership、ordering、UI receipt表示。

これらを固定するまでは、opt-in自動保存、保存前照合UI、公開app/正式個人スコアDB接続、ゲーム操作へ進まない。
