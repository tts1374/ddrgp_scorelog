# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。

最初に `AGENTS.md` と `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` を読み、Git状態と関連設計docsを確認してください。既存の未コミット変更、ローカル素材、DB、`data/`、`logs/`、生成物を保護してください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

medium

既存のpure analysis artifact contractに沿った明示生成入口、atomic file write、fixtureテスト、docs同期が中心であり、正式DB schema、migration、transaction writerを変更しないため `medium` を推奨します。保存境界やschemaの再設計が必要と判明した場合は現在PRを拡張せず、次の実行で `high` を検討してください。

## 作業ブランチ

前回完了ブランチ:

```powershell
codex/m8-personal-score-db-low-confidence-log-contract
```

前回ブランチがmerge済みなら最新 `main` から、未mergeなら前回ブランチの先端を取り込んでから、次を作成してください。

```powershell
codex/m8-personal-score-db-analysis-artifact-file-output
```

## Goal

version 1 analysis詳細JSON contractを使い、利用者が明示した新規pathへ1件だけ安全に生成するpure-adjacent file output入口を実装します。既存save CLIや通常PoCへ自動接続せず、failure imageは参照検査に留めて画像自体を生成・コピーしません。

## Context

実装済み:

- 正式個人スコアDB schema、互換検査、transaction writer、明示単発save
- strict save input、validation、review template、validation receipt、E2E人手workflow
- version 1 analysis詳細JSON strict contract
- `analysis_logs.log_path` の `logs/analysis_details/**/*.json` 相対path境界
- failure imageの `logs/analysis_failures/` 別参照境界
- `short=7日`、`standard=30日`、`indefinite` のretention metadata

今回扱うのは明示ファイル生成入口だけです。migration/backup設計はM8後続候補として残します。

## Deliverables

### 1. Explicit file output API

- 検査済みversion 1 payloadと明示output pathを受け取るAPIを追加する。
- outputはrepository root基準の `logs/analysis_details/**/*.json` に限定する。
- UTF-8 BOMなし、LF、決定的key順、末尾改行で保存する。
- 親directoryは明示実行時だけ作成してよい。
- 既存ファイルを上書きしない。
- contract/path検査をdirectory/file作成より前に完了する。
- 一時ファイルとatomic replace等で部分JSONを残さない。

### 2. Explicit CLI or equivalent user entry

- 通常PoCやsaveとは独立した明示optionとして1件だけ生成できるようにする。
- 入力JSON pathとoutput pathを必須ペアにする。
- save、diagnostic、validation、template、receipt、通常PoC optionとの混在を副作用前に拒否する。
- statusと終了コードをfixtureで固定する。
- DBを開かず、`data/`、画像、failure imageを作成・変更しない。

### 3. Regression tests

- valid low-confidence/error fixtureを明示pathへ生成しreadback検証できる。
- invalid schema、unknown/forbidden key、unsafe path、拡張子不正、既存outputを副作用前に拒否する。
- UTF-8 BOMなし、LF、末尾改行、決定的出力を確認する。
- 途中失敗で一時/部分ファイルを残さない。
- failure image pathは検査するが画像を生成・コピーしない。
- 既存ready/excluded/duplicate save、人手workflow、analysis pure contractが回帰しない。

### 4. Documentation sync

最低限、次を同期してください。

- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/10_personal_score_db_schema.md`
- `tools/vision_poc/README.md`
- `docs/implementation-roadmap.md`

必要な場合だけ保存境界Skillを更新し、更新時はvalidatorを実行してください。新しいSkillやSubagentは作成しません。

## Invariants

- `confirmed_result=true` かつ `duplicate=false` の保存候補境界を変えない。
- 低信頼度/error/skipを成功playへ丸めない。
- 正式play値、receipt、DB diagnostic payloadをanalysis詳細へ混入させない。
- `analysis_logs.log_path`、failure image、source capture、DB diagnosticの責務を分離する。
- `log_path` 空文字を許す既存save契約を維持する。
- artifact生成だけでDBへinsertせず、saveだけでartifactを暗黙生成しない。
- retention期限を根拠に削除しない。
- M8 preview DBと正式DBを相互に受け入れない。
- duplicate preflight、transaction rollback、既存CLI終了コードを変えない。

## Non-goals

- save workflowへの自動連鎖
- failure imageの生成、copy、capture
- cleanup、scheduler、保持期限による削除
- 正式DB schema変更、列追加、version 2、migration、backup、DB修復
- duplicate key差し替え、並行writer、lock戦略
- 通常PoC、OCR、ROI、画像分類の変更
- 次PR相当の作業

## Validation

実装中:

```powershell
python -m pytest tests\test_personal_score_db_analysis_artifacts.py
```

完了前:

```powershell
python -m pytest tests\test_personal_score_db_analysis_artifacts.py tests\test_personal_score_db_cli_save.py tests\test_personal_score_db_file_save.py tests\test_personal_score_db_save_adapter.py tests\test_personal_score_db_save.py tests\test_personal_score_db_schema.py
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
git diff --check
git status --short
```

CLI option解析またはrunnerを変更するため、PR完成前に全テストを1回実行してください。

```powershell
python -m pytest tests
```

画像処理やPoC集計を変更しない限り `python -m tools.vision_poc` は省略し、理由と残るリスクを報告してください。

## Review

実装後は `main` との差分をread-onlyでレビューし、保存境界Skillの観点を適用してください。特に副作用順序、path traversal、atomic write、責務混在、既存workflowの終了コード、Git管理外生成物の混入を確認し、medium以上の範囲内指摘は修正・再検証してください。

## Acceptance Criteria

- 明示指定した新規analysis detail pathだけへversion 1 JSONをatomicに生成できる。
- 不正入力/path/既存outputでファイルやdirectoryを作らない。
- failure image、DB、`data/`、通常PoC生成物を作成・変更しない。
- 対象テスト、全テスト、Ruff、compileall、`git diff --check` が通る。
- docs/README/code/fixtureの語彙が一致する。
- read-onlyレビューでmedium以上の未対応指摘がない。
- 今回変更だけをcommit、通常pushし、draft PRを作成する。

## Required Completion Report

`AGENTS.md` のCompletion Report項目に加え、atomic file output、副作用前検査、既存ファイル保護の結果を記載してください。

## Next Task Update

完了後、`docs/next-task.md` を次PR仕様へ更新し、同じcommitへ含めてください。後続候補は次です。

1. analysis artifact明示生成とsave workflowを独立操作のまま接続する可否の設計
2. 正式個人スコアDBのmigration方針、backup前提、互換version遷移の設計

優先順位を既存資料から一意に決められない場合だけ、`ユーザー対応が必要` として比較を提示してください。更新後の作業には着手しません。
