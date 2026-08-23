# ADR 0003: DB責務の分離と正式個人スコアDBの保護

## Status

Accepted

## Context

GP Score Logは、公式曲・譜面情報、画像照合用reference、利用者の正式play履歴、developer評価結果をSQLiteで扱う。それぞれは更新元、schema、writer、失敗時の実害、配布方法が異なる。

これらを同じDBまたは任意pathで扱うと、reference更新や評価再実行が正式playへ副作用を与え、preview・unknown schemaを正式DBとして開く危険が生じる。Releaseとdevelopmentのpath探索が混在すると、利用者データとcheckout内のDBを取り違える可能性もある。

## Decision

次の4責務を別file、別schema identity、別writerとして維持する。

- M4 master DB: 公式曲・譜面情報。runtimeはread-onlyで参照する。
- M5b jacket reference catalog: jacketとM7 result-text featureのreference。M4 master DBとは別fileでread-only検証する。
- 正式個人スコアDB: `source_captures`、`plays`、`analysis_logs`を持つ利用者データ。正式save workflow、登録済みmigration、確認済みbackup・restoreだけが変更する。
- 評価用DB: developer評価器が所有し、公開アプリと正式個人スコアDBから分離する。

Releaseはproduction固定pathだけを使用し、repository root探索やdevelopment pathへのfallbackを行わない。Debugは明示されたdevelopment rootまたは確認済みsource checkoutだけをdevelopmentとして扱う。UIから任意の正式DB pathへ切り替えない。

M4 master DBとM5b jacket reference catalogは保存開始前に別connectionでread-only検証する。どちらかが欠落または互換性不一致なら正式保存を開始しない。reference data、evaluation output、capture・analysis artifactは正式`plays`の代替にしない。

正式個人スコアDBのmissingまたは0 byte fileだけを現行schemaで初期化できる。既存の非空fileはidentity、schema、metadata、migration historyを検査し、preview、unknown、identity mismatch、未対応versionを自動修復しない。登録済みmigrationはsource変更前backup、transaction、post-migration再検証、失敗時restoreを必須とする。

利用者向けbackup・restoreは正式play履歴に必要な値だけを持つ明示形式とし、reference DB、settings、解析・診断情報、migration backupとは分離する。

## Consequences

- reference更新、developer評価、正式play保存の副作用範囲を分離できる。
- preview DBやunknown SQLiteを正式個人スコアDBとして誤って開かない。
- ReleaseとdevelopmentのDB取り違えを防げる。
- 正式playのmigration・restoreはbackupと再検証を伴い、失敗時に解析・保存を停止できる。
- 複数DBのpath解決、identity検査、互換性診断、reference整合を個別に維持する必要がある。
- DB間の整合はcross-file検査で保証し、単一SQLite transactionやforeign keyには依存できない。

## Alternatives Considered

### すべての責務を単一SQLite DBへ統合する

connectionと配布物を減らせる一方、reference data更新、developer評価、利用者playのwriterとmigration lifecycleが結合する。正式playへの実害を局所化できないため採用しない。

### Releaseでrepositoryやdevelopment DBへfallbackする

初期配置不足を補える一方、利用者が意図しないcheckoutや評価DBを開く可能性があり、production data rootを一意にできないため採用しない。

### schema不一致を起動時に自動repairまたは再作成する

利用開始を継続しやすい一方、unknown DBや既存playを不可逆に変更する危険があるため採用しない。既知migrationだけをbackup付きで実行し、それ以外は無変更で拒否する。

## References

- [概念data model](../design/04_data_model.md)
- [ストレージI/O仕様](../design/05_storage_io_spec.md)
- [M4 master DB生成契約](../design/08_master_db_generation.md)
- [M5 master match・参照catalog契約](../design/09_master_match_poc.md)
- [正式個人スコアDB schema](../design/10_personal_score_db_schema.md)
- [Windowsアプリ実行・保存契約](../../app/README.md)
