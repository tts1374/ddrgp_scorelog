# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と保存境界Skillを読み、既存のローカルDB、backup、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

正式DB schemaと互換遷移の設計を複数の保存契約・migration契約と整合させるためです。

## 作業ブランチ

```powershell
codex/m8-personal-score-db-v2-schema-transition-design
```

## Goal

実DBを変更せずに、正式個人スコアDB version 2の必要性とversion 1からの互換遷移を実装前の設計・pure contractとして固定します。

## Deliverables

- version 2で追加・変更する責務と、version 1のまま維持する責務を設計docsへ明記する。
- version 1からversion 2への前方遷移、拒否状態、履歴・metadata・`PRAGMA user_version` の期待状態をpure contractで固定する。
- supported transition登録とfixture matrixを同期する。
- backup検証完了後だけsource transactionへ進める既存順序を維持する。
- README、設計docs、roadmapを同期する。

## Invariants

- 実DB、backup、`data/`、`logs/`を作成・変更・削除しない。
- migration SQL、schema writer、実migration、restore、repairを実装しない。
- version 1のsave/orchestration/diagnostic/backup CLI契約と終了コードを変えない。
- preview/unknown/identity mismatch/newer unsupported/partial stateを正式遷移候補へ昇格しない。

## Validation

対象テスト、全テスト、Ruff、compileall、`git diff --check` を実行してください。画像処理を変更しない限りVision PoCは省略します。

## Non-goals

- 実DB migration、migration SQL、version 2 schema writer
- 実backup作成境界の変更、backup retention、自動restore/repair
- save/orchestration/diagnosticからの自動migration
- OCR、ROI、画像分類、通常PoCへの接続

## Acceptance Criteria

- version 2の目的とversion 1との差分がレビュー可能で、正式保存値・source capture・analysis logの責務境界を崩さない。
- pure contractとfixtureが同じsupported transitionと拒否語彙を表す。
- read-onlyレビューでmedium以上の未対応指摘がない。

完了後は次PR仕様へ更新し、今回変更だけをcommit、通常pushしてdraft PRを作成してください。
