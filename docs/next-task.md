# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。

最初に `AGENTS.md` と `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` を読み、Git状態と関連設計docsを確認してください。既存の未コミット変更、ローカル素材、DB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

transaction外artifactと正式DB transactionを接続し、partial success、duplicate race、既存artifact再利用を同時に守る実装です。既存CLI互換と副作用順序の監査が必要なため `high` を推奨します。

## 作業ブランチ

前回完了ブランチ:

```powershell
codex/m8-personal-score-db-analysis-artifact-save-connection-design
```

前回ブランチがmerge済みなら最新 `main` から、未mergeなら前回ブランチの先端を取り込んでから、次を作成してください。

```powershell
codex/m8-personal-score-db-analysis-artifact-save-orchestration
```

## Goal

設計済みのanalysis artifact / 正式save接続契約を、既存の独立CLIを変えず、単発の明示orchestration API/CLIとして実装します。

## Source of truth

- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md` の「Analysis artifactと正式saveの接続契約」
- `docs/design/06_regression_guard.md`
- `docs/design/10_personal_score_db_schema.md`
- `tools/vision_poc/README.md`

## Deliverables

### 1. Pure workflow validation

- artifact payloadとstrict save inputを別objectとしてload/validateするversion 1 workflow inputを追加する。
- `analysis_id`、`source_capture_id`、保存境界status、artifact output path / `analysis_logs.log_path` の一致を副作用前に検査する。
- candidate material、正式play値、analysis detail本文を相互投影しない。
- ready、低信頼度/error、その他skip、unresolvedについてartifact必須/任意/禁止を設計表どおり判定する。

### 2. Explicit orchestration API

- 入力/adapter、共有値、DB互換性、早期duplicate、artifact publish/reuse、既存file saveの順で1回だけ実行する。
- 早期duplicateは予告分類だけに使い、衝突時も既存file saveへ進めてsource/analysisを記録する。transaction内preflightとUNIQUE制約を置き換えない。
- artifact失敗時はDBを呼ばず、artifact成功後のDB失敗はDB rowをrollbackしてartifactを保持する。
- 既存artifactはstrict load後の正規化payloadが完全一致するときだけ再利用し、不一致fileを上書き・削除しない。
- `workflow_status`、`artifact_status`、adapter/DB status、ID、理由、artifact/DB pathだけを結果へ投影する。

### 3. Explicit CLI and docs

- workflow input、artifact output、正式DB pathの明示optionだけで動く単独modeを追加する。
- 現行artifact、save、validation、template、diagnostic、通常PoC optionとの混在を副作用前に拒否する。
- 設計済みstatusと終了コードを実装し、既存CLIのstatus/終了コードを変更しない。
- README、設計docs、roadmapへ実装済み契約と利用者の確認手順を同期する。

## Invariants

- `confirmed_result=true` かつ `duplicate=false` の保存候補境界を変えない。
- 低信頼度/error/skip/duplicateを成功playへ丸めない。
- 正式play値、receipt、DB diagnostic payloadをanalysis詳細へ混入させない。
- artifact writerとDB writerは互いを暗黙実行しない。
- `analysis_logs.log_path` 空文字互換、failure image、source capture、DB diagnosticの責務分離を維持する。
- 既存file上書き、artifact自動削除、自動補償、自動migration、自動修復を行わない。
- duplicate preflight、transaction rollback、既存CLI終了コードを変えない。

## Fixture matrix

- ready: artifactなし / 新規生成 / 同一artifact再利用
- excluded: 低信頼度 / errorのartifact必須、その他skipのartifact任意
- duplicate: 早期衝突 / transaction時衝突のどちらもsource/analysisを記録し、`play_id=null`
- rejection: invalid / unresolved / DB非互換 / unsafe path / 共有ID・status・path不一致
- failure: artifact publish失敗 / artifact後DB失敗 / 既存artifact conflict
- side effects: 各失敗点でartifact、DB、親directory、既存fileが設計表どおり不変
- compatibility: 既存artifact/save/validation/template/diagnostic CLIの結果と終了コードが不変

## Non-goals

- 正式DB schema変更、migration、backup、DB修復
- cleanup、scheduler、retentionによる削除、自動補償
- failure image生成、copy、capture
- duplicate key差し替え、並行writer、lock戦略
- 通常PoC、OCR、ROI、画像分類、常駐監視への接続

## Validation

対象テストを先に実行し、共通CLI option解析を変更するため全テストも実行してください。

```powershell
python -m pytest tests\test_personal_score_db_analysis_artifacts.py tests\test_personal_score_db_cli_save.py tests\test_personal_score_db_file_save.py tests\test_personal_score_db_save.py
python -m pytest tests
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
git diff --check
```

画像処理を変更しない限り `python -m tools.vision_poc` は省略し、理由と残るリスクを報告してください。

## Review

実装後は `main` との差分をread-onlyでレビューし、保存境界Skillの観点を適用してください。特に副作用前validation、二重書込みのpartial success、transaction内duplicate再検査、正式値の暗黙投影、既存CLI互換、既存artifact保護を確認してください。

## Acceptance Criteria

- 設計済み順序、status、終了コード、artifact必須/任意、partial success、再実行がfixtureで固定される。
- `analysis_logs.log_path` とartifact output pathの整合をorchestration入口が保証する。
- artifact後のDB失敗を成功へ丸めず、同一payloadの既存artifactだけ安全に再利用できる。
- 現行CLI互換と責務分離を維持し、read-onlyレビューでmedium以上の未対応指摘がない。
- 今回変更だけをcommit、通常pushし、draft PRを作成する。

## Next Task Update

完了後、`docs/next-task.md` を正式個人スコアDBのmigration/backup/version遷移設計へ更新してください。更新後の作業には着手しません。
