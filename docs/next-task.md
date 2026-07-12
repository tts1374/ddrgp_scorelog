# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と保存境界Skillを読み、既存のローカルDB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

source DBを変更せずに、exclusive createとreadback検証を伴うbackup境界を実装するためです。

## 作業ブランチ

```powershell
codex/m8-personal-score-db-verified-backup-cli
```

## Goal

正式個人スコアDBから検証済みbackupを明示的に1件作成する専用API/CLIを追加します。

## Deliverables

- compatibleな正式DBだけをsourceとして受けるverified backup APIを追加する。
- source DB pathと新規backup pathを必須とする専用CLIを追加する。
- exclusive create、flush、再open、SQLite integrity、formal identity/version/history、source snapshot対応を検証する。
- 成功・source拒否・backup conflict・copy/readback失敗をfixtureで固定する。
- README、設計docs、roadmapを同期する。

## Invariants

- source DB、既存backup、`data/`、`logs/`を変更・削除しない。
- migration、schema変更、restore、repairを実行しない。
- preview/unknown/identity mismatch/newer unsupported/partial stateをbackup元として受け入れない。
- save/orchestration/diagnostic/migration statusの既存CLI契約と終了コードを変えない。

## Validation

対象テスト、全テスト、Ruff、compileall、`git diff --check` を実行してください。画像処理を変更しない限りVision PoCは省略します。

## Non-goals

- 実DB migration、version 2 schema、migration SQL
- backup retention、複数世代管理、自動restore/repair
- save/orchestration/diagnosticからの自動実行
- OCR、ROI、画像分類、通常PoCへの接続

## Acceptance Criteria

- 成功したbackupがsourceのformal identity/version/historyと対応し、SQLite integrity検査を通る。
- 失敗時にsourceと既存backupを変更せず、不完全な新規backupを残さない。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
