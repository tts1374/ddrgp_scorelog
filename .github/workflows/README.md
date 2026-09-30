# GitHub Actions

## `deploy-web.yml`

公開Web applicationをmain merge後にproductionへ反映するworkflow。

- `web/identity-api/**`、`master/**`、`pyproject.toml`、`uv.lock`またはworkflow自身がmainで更新された場合に実行する。
- `CLOUDFLARE_ACCOUNT_ID`と`CLOUDFLARE_API_TOKEN`をproduction environment secretから使用する。
- 型検査、Worker + local D1 test、React test、production buildを再実行する。
- mainの直近の成功した`build-master-db.yml`から検査済みmaster DBを取得し、再検査する。D1 SQLはcheckoutしたcommitで再exportし、同じcommitの曲名検索用別名を含める。
- additive D1 migration、shared master SQL投入、`ddrgp-scorelog` WorkerとStatic Assetsのdeployを順に実行する。artifact取得・master検査・投入の失敗時はdeployへ進まない。artifactが期限切れの場合はmaster buildを成功させてからdeployを再実行する。
- 初回deploy前に、公開用Workerへ既存production Workerと同じ`CREDENTIAL_PEPPER`と`REGISTRATION_SECRET`をCloudflare secretとして登録する。既存配布版が参照する旧WorkerのAPIは、旧endpointを利用する配布版のサポート期間中維持する。

## `build-master-db.yml`

M4マスタDBを生成する手動・定期実行workflow。

- `workflow_dispatch` で手動実行できる。
- 毎週土曜 03:17 UTC に定期実行する。
- ネットワークに依存しない `tests/test_master_builder.py` を先に実行する。
- `python -X utf8 -m master --output data/master/ddrgp-master.sqlite` でWiki譜面表、公式収録曲一覧、DDR WORLD公式楽曲一覧の実HTMLからSQLiteを生成する。DDR WORLD公式楽曲一覧は固定queryの全ページを空ページ終端まで取得する。
- `python -X utf8 -m master.inspect` で必須metadata、実テーブル件数、`source_snapshots` 件数、各source hash、source URL、chart ID重複、chart identity重複、外部キー整合性、DDR WORLD差分reportのstatus・件数・最終レベルを検査する。`unmatchable_gp_candidate`または`ambiguous_gp_candidate`が1件以上なら失敗する。
- `ddrgp-master-<run_number>` artifact として `ddrgp-master.sqlite`、`master-summary.json`、`ddrworld-merge-report.json`、`ddrgp-web-master.sql` をアップロードする。
- `master-summary.json` にはテーブル件数、snapshot件数、Wiki/公式/DDR WORLD source hash、snapshot側source URL、parser version、公式プレー可否の突合件数、DDR WORLD差分件数を含める。

生成DBはGit管理しない。Releases配布は、artifact運用で生成結果の確認が安定してから別フェーズで追加する。
