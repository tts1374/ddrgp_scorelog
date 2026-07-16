# 現在PR完了記録

developer-only jacket catalog collectorへ、current checkpointとcurrent catalogのcomposite identity集合を保存前に照合する、session単位・既定OFFの明示opt-in自動保存を追加した。既存local catalog、artifact、checkpoint、source/crop画像を移行、削除、上書き、repairせず、catalogに既存identityがある候補は新規artifact/checkpointを作らない。

## 今回の完了範囲

- current catalog schema version 1をstrict read-onlyで開き、catalog identity/schema/created-atを同じ接続で照合してから、`rejected`を含む全review状態のcomposite identity集合を返すversion付きJSON契約を追加した。
- 手動保存と自動保存のartifact publish前に、現在のsession checkpointと上記catalog identity集合を照合する。checkpointにあるidentityは既存receipt/retry経路へ留め、catalogだけにあるidentityは保存済み表示にして新規artifact/checkpointを作らない。
- 自動保存はfresh session、resume、stopの境界でOFFへ戻るsession単位opt-inとし、端末設定へ永続化しない。明示的に有効化したsessionだけ、stable composite候補をidentityごとに1回自動試行する。
- 自動保存は既存のartifact atomic publish、current catalog ingest、checkpoint receipt、明示catalog retryを再利用する。失敗後の暗黙再試行は行わず、明示保存またはcatalog retryへ送る。
- 保存前照合後の別processとの競合は、current catalogのcomposite identity一意制約と冪等ingestで既存referenceへ収束させる。forceful ownershipや別writerを追加していない。
- collector UIへ既定OFFのcheckboxを追加し、catalog/checkpoint保存済み候補では保存ボタンを無効化する。公開`DDRGpScoreViewer`、正式個人スコアDB、ゲーム操作へ接続していない。
- `docs/design/05_storage_io_spec.md`、`docs/design/06_regression_guard.md`、implementation roadmap、collector READMEを同じ契約へ同期した。

## 維持した境界

- artifact manifest/checkpoint v1/v2、observation ID、current ingest payload、catalog schema、manual review、resume/retry、catalog commit後checkpoint failureの状態機械を再採番・変更していない。
- title-line hashとcomposite identityをOCR文字列、master song/chart ID、正式個人スコアDBの保存値へ昇格していない。
- capture開始、window選択、ゲーム操作は自動化していない。自動保存opt-inだけでcapture開始や別window選択を起動しない。
- 既存local DB、artifact、checkpoint、source/crop画像、実入力JSON、生成物をtest入力やGit差分へ含めていない。testはtemporary synthetic DB/imageだけを使用した。

## 検証実績

- `python -m pytest -q tests`: passed。
- `python -m ruff check tools/vision_poc pyproject.toml tests`: passed。
- `python -m compileall -q master tools/vision_poc`: passed。
- `dotnet test tools/jacket_catalog_collector/tests/JacketCatalogCollector.Tests/JacketCatalogCollector.Tests.csproj --no-restore`: passed。
- `git diff --check`: passed。
- Python testの既知warningとして`pytest_chalice`経由の`pkg_resources` deprecated warningだけを確認した。
- 画像分類、ROI、OCR logicは変更していないため、local screenshot素材を使う`python -m tools.vision_poc`は実行条件外とした。

# 次PR状態

次PRの実装仕様は未確定。今回PR完了後に自動的に着手しない。

## 未決事項

- 実capture false-positive gateと、自動保存を実運用で有効化できる評価条件。
- 複数collector processが同じidentityを同時に観測した場合のUI上のownership、ordering、receipt表示。DB整合性は一意制約と冪等ingestで維持するが、process間調停は未実装。
- sessionを越えて自動保存opt-inを保持するproduct要件。今回の安全側契約はsession単位・既定OFF・非永続。
- grid自動巡回、ゲーム操作、公開app連携、正式個人スコアDB接続。

これらの受入条件が固定されるまでは推測実装しない。
