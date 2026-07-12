# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と保存境界Skillを読み、既存のローカルDB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

正式DB version 1の複数入口と設計文書を、リリース前の保存境界として横断監査するためです。

## 作業ブランチ

```powershell
codex/m8-personal-score-db-v1-release-readiness
```

## Goal

初回リリースまで正式個人スコアDBをversion 1に固定し、既存の保存・backup・diagnostic・orchestration契約が同じv1境界を維持していることを回帰テストとリリース前チェックで固定します。

## Deliverables

- schema定数、metadata、`PRAGMA user_version`、migration historyが全入口でversion 1に固定されていることを横断テストで確認する。
- save、workflow、diagnostic、migration status、verified backupのCLI/APIがv1 compatible DBを同じ正式DBとして扱うことを確認する。
- preview/unknown/identity mismatch/newer unsupported/partial stateの拒否が入口間で一致することを確認する。
- v1の正式保存値、`source_captures`、`analysis_logs`、duplicate、失敗時原子性の既存不変条件をリリース前チェックリストへ整理する。
- README、設計docs、roadmapを同期する。

## Invariants

- 正式DB schema versionを1から変更しない。
- version 2 schema、supported transition、migration SQL、schema writerを設計・実装しない。
- 実DB、backup、`data/`、`logs/`を作成・変更・削除しない。
- 既存CLIのoption、出力語彙、終了コードを非互換変更しない。
- preview材料を正式保存値へ昇格せず、保存不可・duplicate・失敗を成功playへ丸めない。

## Validation

対象テスト、全テスト、Ruff、compileall、`git diff --check` を実行してください。画像処理を変更しない限りVision PoCは省略します。

## Non-goals

- version 2の検討、設計、実装
- 実DB migration、migration SQL、restore、repair
- backup retention、複数世代管理
- OCR、ROI、画像分類、Windows常駐アプリへの接続

## Acceptance Criteria

- 初回リリースまで正式DBがversion 1に固定されることをコード、テスト、docsから一意に確認できる。
- 全正式DB入口がv1 compatible/rejected状態について矛盾しない。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
