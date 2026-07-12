# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と保存境界Skillを読み、既存のローカルDB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

xhigh

正式DBの不可逆なversion遷移、backup整合、失敗時復旧境界を複数設計文書で固定するためです。

## 作業ブランチ

前回ブランチがmerge済みなら最新 `main` から、未mergeなら前回先端を取り込んで、次を作成してください。

```powershell
codex/m8-personal-score-db-migration-backup-version-design
```

## Goal

正式個人スコアDBのmigration、backup、schema version遷移を実装前の設計契約として固定します。

## Deliverables

- 現行version 1から将来versionへの遷移状態、互換範囲、拒否条件、所有責任を設計する。
- migration前backupの作成先、atomic性、検証、既存file保護、失敗時の扱いを定義する。
- migrationのpreflight、transaction、`PRAGMA user_version` / metadata / `schema_migrations` 更新順序とrollback境界を定義する。
- CLI/APIの明示確認、dry-run/diagnostic、status、終了コード、再実行契約を設計する。
- `docs/design/04_data_model.md`、`05_storage_io_spec.md`、`06_regression_guard.md`、`10_personal_score_db_schema.md`、README、roadmapを同期する。
- pure contractとfixture matrixを追加し、実DB migration writerは実装しない。

## Invariants

- preview/unknown/identity mismatch DBを正式DBへ昇格しない。
- migration、backup、repairを既存save/orchestration/diagnosticから暗黙実行しない。
- backup成功を検証する前に元DBを変更せず、元DBや既存backupを上書き・削除しない。
- source/play/analysisの保存境界、duplicate transaction、既存CLI終了コードを変えない。
- ローカルDB、実入力、backup生成物をGit管理しない。

## Fixture matrix

- compatible current / older supported / newer unsupported / unknown / preview / identity mismatch
- backup path安全性、新規作成、既存file conflict、write/flush/verification失敗
- migration各step失敗、transaction rollback、version/metadata/migration履歴の整合
- dry-run無変更、明示実行、再実行、partial state拒否
- 既存save/orchestration/diagnostic CLI互換

## Validation

設計・pure contractの対象テスト、全テスト、Ruff、compileall、`git diff --check` を実行してください。画像処理を変更しない限りVision PoCは省略します。

## Non-goals

- 実DB migration writer、既存DBの実migration、backup実生成
- 自動repair、cleanup、retention、scheduler
- schema version 2の列やproduct仕様の確定
- OCR、ROI、画像分類、通常PoCへの接続

## Acceptance Criteria

- version判定からbackup、migration、検証、失敗復旧までの順序と責務が一意である。
- destructive操作に必要な明示確認と人手境界が定義される。
- fixture matrixが将来実装のstatus、終了コード、副作用不変条件を検証可能にする。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
