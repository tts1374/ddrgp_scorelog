# GitHub Actions

## `build-master-db.yml`

M4マスタDBを生成する手動・定期実行workflow。

- `workflow_dispatch` で手動実行できる。
- 毎週土曜 03:17 UTC に定期実行する。
- ネットワークに依存しない `tests/test_master_builder.py` を先に実行する。
- `python -m master --output data/master/ddrgp-master.sqlite` で実HTMLからSQLiteを生成する。
- `python -m master.inspect` で必須metadata、実テーブル件数、`source_snapshots` 件数、source hash、source URLを検査する。
- `ddrgp-master-<run_number>` artifact として `ddrgp-master.sqlite` と `master-summary.json` をアップロードする。
- `master-summary.json` にはテーブル件数、snapshot件数、source hash、snapshot側source URL、parser versionを含める。

生成DBはGit管理しない。Releases配布は、artifact運用で生成結果の確認が安定してから別フェーズで追加する。
