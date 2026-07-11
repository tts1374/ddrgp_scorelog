# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。

最初に `AGENTS.md` と `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` を読み、現在のGit状態と関連設計docsを確認してください。既存の未コミット変更、ローカル素材、生成物を保護し、このPRへ混入させないでください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

high

低信頼度analysis詳細JSON、失敗画像、保持期間、`analysis_logs.log_path` の責務を分離し、複数設計文書と正式DB保存境界を同期する必要があるため `high` を推奨します。推論レベルを理由にmigrationや自動保存へ範囲を広げないでください。

## 作業ブランチ

前回完了ブランチ:

```powershell
codex/m8-personal-score-db-review-workflow-regression
```

前回ブランチがmerge済みなら最新 `main` から、未mergeなら前回ブランチの先端を取り込んでから、次のブランチを作成してください。

```powershell
codex/m8-personal-score-db-low-confidence-log-contract
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

## Goal

低信頼度または解析失敗を再調査できるよう、version付きanalysis詳細JSON、任意の失敗画像参照、保持期間metadata、正式DB `analysis_logs.log_path` の参照境界を、pure contractとfixtureテスト、設計docsで固定します。

このPRではログや画像を自動生成・削除せず、既存save CLIへ自動連鎖しません。

## Context

現在はM8「個人スコアDB保存」の範囲です。人手主導のtemplate、validation receipt、明示save、diagnostic workflowまでは固定済みです。

既存資料では次が確定しています。

- `analysis_logs` は保存判断と再調査の入口で、正式play値を二重管理しない。
- `analysis_logs.log_path` は本番解析詳細JSONへの参照であり、DB diagnostic JSONLやsource capture画像を指さない。
- `source_captures` は元フレーム参照を持ち、解析ログ本文やDB診断ログを持たない。
- 低信頼度、error、skipは成功playへ丸めず、playなしanalysisとして扱う。
- 詳細JSON、失敗画像、ローカルDB、実入力、`data/`、`logs/` はGit管理しない。

ロードマップ上、この契約固定をmigration方針より先に扱います。migration方針は次々PR候補へ残します。

## Deliverables

### 1. Versioned analysis detail contract

責務を限定した新規moduleまたは既存保存module内の独立したpure境界として、version 1のanalysis詳細JSON contractを定義してください。

最低限、次を明示的に分離してください。

- schema versionと生成元識別
- analysis IDとsource capture ID
- `analysis_status`、`save_boundary_status`、`skip_reason`
- event確定、duplicate、confirmation mode、時刻情報
- identity/digit review statusとanalysis confidence
- 再調査用の候補材料または診断summary
- 任意の失敗画像参照
- retention classまたは保持期限を説明するmetadata

詳細JSONは正式 `plays` の値を正本として複製せず、validation receiptやDB diagnostic projectionとも共有しないでください。unknown key、型、schema version、status整合をpure validationで拒否できるようにします。

### 2. Path and reference boundary

次の参照契約をコード定数、validator、fixtureテストのいずれかで固定してください。

- `analysis_logs.log_path` はanalysis詳細JSONだけを参照する。
- failure image pathは詳細JSON内の別fieldで参照し、`log_path` や `source_captures.source_path` と混同しない。
- 本番解析詳細と失敗画像の生成先候補は `logs/` 配下とし、DB diagnostic JSONLとは別namespaceにする。
- path traversal、`logs/` 外、想定外拡張子を副作用前に拒否する。
- `log_path` が空でよい既存analysis契約を維持し、既存保存を壊さない。

pathのDB格納形式を相対pathにする場合は、基準directoryと解決規則をdocsとテストで一意にしてください。絶対pathを採用する場合は、移動可能性と個人環境情報の扱いを明記してください。

### 3. Retention contract without deletion

保持期間の語彙、期限計算の基準時刻、期限なしの扱い、失敗画像と詳細JSONの関係をpure contractとして固定してください。

このPRでは既存ファイル削除、cleanup実行、scheduler、起動時掃除を実装しません。将来cleanupが参照できる期限・分類を定義するところまでに留めます。

### 4. Regression tests

最低限、次をfixtureで確認してください。

- 低信頼度とerrorの有効な詳細JSONを構築・検査できる。
- 正式play値、receipt key、DB diagnostic payloadを詳細JSONへ混入させない。
- `log_path`、failure image path、source capture pathの責務が分離されている。
- schema version、未知key、型、status/理由不整合、unsafe pathを拒否する。
- retention metadataを決定的に計算・投影できる。
- pure contractの検査だけではDB、`data/`、`logs/`、画像を作成・変更しない。
- 既存のready/excluded/duplicate保存と人手主導workflowが回帰しない。

### 5. Documentation sync

最低限、次を同期してください。

- `docs/design/04_data_model.md`
- `docs/design/05_storage_io_spec.md`
- `docs/design/06_regression_guard.md`
- `docs/design/10_personal_score_db_schema.md`
- `tools/vision_poc/README.md`
- 必要なら `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md`
- `docs/implementation-roadmap.md`

新しいSkillやSubagentは作成しません。

## Invariants

- `confirmed_result=true` かつ `duplicate=false` の保存候補境界を変えない。
- 候補値、preview値、metadata値、相対時刻を正式play値へ暗黙昇格させない。
- 低信頼度/error/skipを成功play rowへ丸めない。
- `analysis_logs` に正式play値を二重管理しない。
- `analysis_logs.log_path` はDB diagnostic JSONL、validation receipt、source capture画像を参照しない。
- `source_captures` は元フレーム参照の責務を維持する。
- validation receiptは承認、認可token、save入力、保存成功証明にしない。
- template、validation、receipt、save、diagnosticを独立した明示操作のまま維持する。
- ready receipt生成だけでDBや `logs/` を作らない。
- M8 preview DBと正式個人スコアDBを相互に受け入れない。
- duplicate preflight、transaction rollback、DB拒否の既存契約を変えない。
- ローカル画像、詳細JSON、実入力、DB、`data/`、`logs/`、生成物をコミットしない。

## Non-goals

- save CLIまたは通常PoCからのanalysis artifact自動生成
- validation、receipt、save、artifact生成の自動連鎖
- 既存ログや画像の削除、cleanup実行、scheduler
- retention policyによる破壊的操作
- 正式DB schema version 2またはschema migration
- migration方針、backup実装、既存DB修復
- `analysis_logs` tableへの列追加
- duplicate key方式、冪等化、並行writer、ロック戦略
- 実キャプチャ、常駐監視、非同期処理、Windows UI
- OCR方式刷新、ROI座標変更
- 次PR相当の実装

## Validation

実装中は新規contractの対象テストを先に実行してください。PR完成前は、今回追加したテストと既存保存境界をまとめて確認します。

```powershell
python -m pytest tests\test_personal_score_db_analysis_artifacts.py
python -m pytest tests\test_personal_score_db_cli_save.py tests\test_personal_score_db_file_save.py tests\test_personal_score_db_save_adapter.py tests\test_personal_score_db_save.py tests\test_personal_score_db_schema.py
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
git diff --check
git status --short
```

テストファイル名は実装責務に合わせて変更して構いません。共通loader、正式DB schema、writer、transaction、duplicate処理、CLI option解析を変更した場合、または対象テストで予期しない回帰が出た場合は全テストも実行してください。

```powershell
python -m pytest tests
```

画像分類、ROI、OCR、profile、confirmed-events、PoC runner、report生成を変更しない場合、`python -m tools.vision_poc` は省略できます。Skillを変更した場合だけSkill validatorを実行してください。

```powershell
python -X utf8 "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\review-ddrgp-db-save-boundary"
```

## Review

実装後、`main` との差分をread-onlyでレビューし、保存境界Skillの観点を適用してください。少なくとも次を確認します。

- detail JSON、failure image、source capture、diagnostic log、receiptの責務が混ざっていない。
- 正式play値をanalysis artifactへ二重管理していない。
- retention contractが暗黙削除を開始しない。
- path validationが副作用より先に行われる。
- 既存save/validation workflowのstatus、終了コード、副作用順序を変えていない。
- Git管理外ファイルがdiffやstaged filesへ混入していない。

重大度medium以上の指摘は、現在のPR範囲内なら修正して再検証してください。範囲外なら次PR候補へ送ってください。

## Authorization

`AGENTS.md` の事前許可に基づき、今回分だけのcommit、指定 `codex/*` branchへの通常push、draft PR作成まで実施します。

mainへの直接push、force-push、PR merge、tag/release、issue書込み、既存ファイル削除、migration、DB修復は実施しません。

## Acceptance Criteria

- version付きanalysis詳細JSON contractとstrict validationがfixtureで固定されている。
- `analysis_logs.log_path`、failure image、source capture、DB diagnostic JSONL、receiptの参照境界が明確である。
- retention metadataは決定的だが、ファイル削除を実行しない。
- pure contract検査だけではDB、`data/`、`logs/`、画像を作成・変更しない。
- 既存の保存workflowと保存境界テストが通る。
- 関連docs、README、必要なSkillが実装と一致する。
- read-onlyレビューでmedium以上の未対応指摘がない。
- 今回分だけをcommit、通常pushし、draft PRを作成している。

## Required Completion Report

- 変更した責務と主要ファイル
- 固定したanalysis artifact / path / retention contract
- 維持した保存境界
- 実行した検証と結果
- 省略した条件付き検証、その理由、残るリスク
- read-onlyレビュー結果
- コミットSHA、push先branch、draft PR
- 未解決事項と次PR候補
- `ユーザー対応が必要` または `現時点でユーザー対応はありません`

## Next Task Update

このPR完了後、`docs/next-task.md` は次PRの作業仕様へ更新してください。

現時点の後続候補:

1. 正式個人スコアDBのmigration方針、backup前提、互換version遷移の設計
2. analysis artifact contractを使う明示ファイル生成入口と、save workflowへの接続可否の検討

優先順位や破壊的操作の要否を既存資料から一意に決められない場合だけ、`AGENTS.md` の形式で `ユーザー対応が必要` としてください。更新後の次PR作業には着手しません。
