# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と保存境界Skillを読み、既存のローカルDB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

pure migration contractを副作用のないstatus/dry-run表示へ接続し、既存CLI排他と保存境界を維持するためです。

## 作業ブランチ

```powershell
codex/m8-personal-score-db-migration-status-dry-run-cli
```

## Goal

正式個人スコアDBのmigration可否をread-onlyで表示するstatus/dry-run CLIを追加します。

## Deliverables

- 既存schema inspectionとmigration pure contractを合成するread-only projectionを追加する。
- DB path、target version、明示backup pathを受ける専用status/dry-run CLIを追加する。
- status、reason、source/target version、backup path検査結果、予定step、終了コードをJSON/Markdownで表示する。
- CLI option排他、存在しない/非SQLite/directory、preview/unknown/identity mismatch/newer unsupported/partial stateをfixtureで固定する。
- README、設計docs、roadmapを同期する。

## Invariants

- DB、backup、`data/`、`logs/`を作成・変更しない。
- migration、backup、repairを実行せず、既存save/orchestration/diagnosticへ暗黙接続しない。
- preview/unknown/identity mismatch DBを正式DBへ昇格しない。
- source/play/analysis保存境界、duplicate transaction、既存CLI終了コードを変えない。

## Validation

対象テスト、全テスト、Ruff、compileall、`git diff --check` を実行してください。画像処理を変更しない限りVision PoCは省略します。

## Non-goals

- 実DB migration/backup writer、既存DBの変更、restore/repair
- schema version 2、migration SQL、backup retention
- save/orchestration/diagnosticからの自動実行
- OCR、ROI、画像分類、通常PoCへの接続

## Acceptance Criteria

- status/dry-runが同じpure contractを使い、副作用なしで再現可能な判定を返す。
- 現行/拒否/将来supported pathの表示と終了コードがfixtureで検証できる。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
