# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。

最初に `AGENTS.md` と `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` を読み、Git状態と関連設計docsを確認してください。既存の未コミット変更、ローカル素材、DB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

analysis artifact生成と正式saveの責務・失敗順序・整合性を設計し、複数設計文書の境界を揃える作業です。正式DB schemaやmigrationは変更しませんが、transaction外artifactとDB transactionの接続可否を比較するため `high` を推奨します。

## 作業ブランチ

前回完了ブランチ:

```powershell
codex/m8-personal-score-db-analysis-artifact-file-output
```

前回ブランチがmerge済みなら最新 `main` から、未mergeなら前回ブランチの先端を取り込んでから、次を作成してください。

```powershell
codex/m8-personal-score-db-analysis-artifact-save-connection-design
```

## Goal

version 1 analysis artifactの明示生成と正式save workflowを独立操作のまま接続する可否を設計し、順序、入力共有、失敗時の扱い、再実行、利用者workflowを実装可能な契約へ固定します。

## Context

実装済み:

- 正式個人スコアDB schema、transaction writer、duplicate preflight、明示単発save
- strict save input、validation、review template、receipt、E2E人手workflow
- version 1 analysis詳細JSON strict contract
- 新規 `logs/analysis_details/**/*.json` だけへ1件をatomic生成する明示API/CLI
- failure imageは参照検査だけで、画像生成・copyなし

artifact fileとDB transactionは別の永続化単位です。今回の設計では、片方だけ成功した場合を成功playへ丸めず、暗黙生成や自動補償を導入しない前提で接続可否を決めます。

## Deliverables

### 1. Responsibility and ordering design

- artifact生成、save input validation、DB compatibility、duplicate preflight、transaction writeの順序候補を比較する。
- artifact必須/任意、ready/excluded/duplicate/error別の適用範囲を固定する。
- artifact pathと `analysis_logs.log_path` の一致を誰が保証するか固定する。
- file成功/DB失敗、file失敗/DB未実行、再実行、既存artifactの扱いを状態表で定義する。

### 2. User workflow contract

- 現行の独立CLIを維持した手順、または新しい明示orchestration入口の必要性を決める。
- 入力JSONの共有範囲と、正式play値・candidate material・analysis detail間の投影禁止を固定する。
- status、終了コード、再試行手順、利用者が確認する生成物を定義する。

### 3. Verifiable specification

- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/10_personal_score_db_schema.md`
- `tools/vision_poc/README.md`
- `docs/implementation-roadmap.md`

設計判断を上記へ同期し、後続実装のfixture行列とacceptance criteriaを記載してください。設計だけで安全に固定できる範囲を超えてproduction orchestrationを実装しないでください。

## Invariants

- `confirmed_result=true` かつ `duplicate=false` の保存候補境界を変えない。
- 低信頼度/error/skip/duplicateを成功playへ丸めない。
- 正式play値、receipt、DB diagnostic payloadをanalysis詳細へ混入させない。
- artifact生成だけでDBへinsertせず、saveだけでartifactを暗黙生成しない現行挙動を、設計決定前に変更しない。
- `analysis_logs.log_path` 空文字互換、failure image、source capture、DB diagnosticの責務分離を維持する。
- 既存ファイル上書き、retentionによる削除、自動migration、自動修復を行わない。
- duplicate preflight、transaction rollback、既存CLI終了コードを変えない。

## Non-goals

- 正式DB schema変更、migration、backup実装、DB修復
- cleanup、scheduler、保持期限による削除
- failure imageの生成、copy、capture
- duplicate key差し替え、並行writer、lock戦略
- 通常PoC、OCR、ROI、画像分類の変更
- 設計で決めた後続production orchestrationの先行実装

## Validation

文書変更が中心なら、関連fixtureテストと次を実行してください。

```powershell
python -m pytest tests\test_personal_score_db_analysis_artifacts.py tests\test_personal_score_db_cli_save.py tests\test_personal_score_db_file_save.py tests\test_personal_score_db_save.py
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
git diff --check
```

コード、CLI option解析、共通runnerを変更した場合は `python -m pytest tests` も実行してください。画像処理を変更しない限り `python -m tools.vision_poc` は省略し、理由と残るリスクを報告してください。

## Review

実装後は `main` との差分をread-onlyでレビューし、保存境界Skillの観点を適用してください。特に二重書込みのpartial success、責務混在、正式値の暗黙投影、既存CLI互換、再実行時の既存artifact保護を確認してください。

## Acceptance Criteria

- artifactとDBの責務、順序、partial success、再実行が状態表または同等の検証可能な形で固定される。
- `analysis_logs.log_path` とartifact output pathの整合責任が明記される。
- 後続実装のfixture行列、status、終了コード、非目標が一意に読める。
- docs/READMEの語彙が一致し、read-onlyレビューでmedium以上の未対応指摘がない。
- 今回変更だけをcommit、通常pushし、draft PRを作成する。

## Next Task Update

完了後、`docs/next-task.md` を設計結果に基づく実装PR、または正式個人スコアDBのmigration/backup/version遷移設計のうち、既存資料から優先順位を一意に決められる方へ更新してください。更新後の作業には着手しません。
