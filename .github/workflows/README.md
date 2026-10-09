# GitHub Actions

## `deploy-web.yml`

公開Web applicationをmain merge後にproductionへ反映するworkflow。

- `web/identity-api/**`、`master/**`、`.github/web-master.json`、`pyproject.toml`、`uv.lock`またはworkflow自身がmainで更新された場合に実行する。
- `CLOUDFLARE_ACCOUNT_ID`と`CLOUDFLARE_API_TOKEN`をproduction environment secretから使用する。
- 型検査、Worker + local D1 test、React test、production buildを再実行する。
- 固定入力・export・identity・カテゴリのtestを実行し、`.github/web-master.json`の明示Release tagから既存`reference-set.json`とM4 master DBを取得する。manifestとDBのSHA-256、固定metadata、DB整合性、`master_version`を検査し、checkoutの`master.d1_export`で検索用別名・フレアカテゴリを含むSQLを出力する。SQLはcheckoutの実migrationを適用したメモリDBで検証する。
- 取得・検証・exportがすべて成功した後、additive D1 migration、shared master SQL投入、`ddrgp-scorelog` WorkerとStatic Assetsのdeployを順に実行する。各段階の失敗で後続を停止する。
- 本番Webの固定Release、content version、master versionは[`.github/web-master.json`](../web-master.json)を正本として参照する。[固定入力の検証・更新手順](../../master/README.md#本番webの固定入力)に従ってpinをreviewする。通常の`ci.yml`は維持する。
- 本番Workerの認証用secret・Google OAuth設定は[Web README](../../web/identity-api/README.md#本番worker--d1)を正本として確認する。既存配布版が参照する旧WorkerのAPIは、旧endpointを利用する配布版のサポート期間中維持する。

## `build-master-db.yml`

M4マスタDBを生成する手動・定期実行workflow。

- `workflow_dispatch` で手動実行できる。
- 毎週土曜 03:17 UTC に定期実行する。
- ネットワークに依存しない `tests/test_master_builder.py` を先に実行する。
- `python -X utf8 -m master --output data/master/ddrgp-master.sqlite` でWiki譜面表、公式収録曲一覧、DDR WORLD公式楽曲一覧の実HTMLからSQLiteを生成する。DDR WORLD公式楽曲一覧は固定queryの全ページを空ページ終端まで取得する。
- `python -X utf8 -m master.inspect` で必須metadata、実テーブル件数、`source_snapshots` 件数、各source hash、source URL、chart ID重複、chart identity重複、外部キー整合性、DDR WORLD差分reportのstatus・件数・最終レベルを検査する。`unmatchable_gp_candidate`または`ambiguous_gp_candidate`が1件以上なら失敗する。
- `ddrgp-master-<run_number>` artifact として `ddrgp-master.sqlite`、`master-summary.json`、`ddrworld-merge-report.json`、`ddrgp-web-master.sql` をアップロードする。
- `master-summary.json` にはテーブル件数、snapshot件数、Wiki/公式/DDR WORLD source hash、snapshot側source URL、parser version、公式プレー可否の突合件数、DDR WORLD差分件数を含める。

生成DBはGit管理しない。このworkflowの成功は本番入力を更新しない。artifactは調査・候補生成用で、検証済みreference data setの公開・Web固定入力の更新は別操作で行う。
