# 次PR作業仕様

`C:\work\ddrgp_scorelog` で作業してください。`AGENTS.md` と `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` に従い、このPRをレビュー可能かつmerge可能な状態まで完成させてください。

## 推奨モデル

GPT-5.6 Sol

## 推論レベル

medium

既存CLIと固定済み保存境界を使ったE2E回帰、README同期、検証が中心であり、新しいschemaや保存設計を含まないため `medium` を推奨します。

正式DB schema、migration、transaction境界の再設計、原因不明の重大な回帰など、現在のPRを完了できない複雑な問題が見つかった場合は、作業を無理に拡張せず、必要に応じて次の実行で `high` を検討してください。

## 作業ブランチ

前回完了ブランチ:

```powershell
codex/m8-personal-score-db-validation-receipt
```

merge済みなら最新 `main` から、未mergeなら前回完了ブランチの先端を取り込んでから、次のブランチを作成してください。

```powershell
codex/m8-personal-score-db-review-workflow-regression
```

開始時に確認:

```powershell
git status --short --branch
git log --oneline -5
git fetch --all --prune
```

既存の未コミット変更を上書き、削除、同梱しないでください。

## Goal

template生成、ローカルでの人手編集相当、validation receipt確認、明示DB保存という人手主導の操作を、既存CLIだけで再現できるE2E fixtureテストとコピー可能なREADME手順として固定します。

このPRでは、新しい保存機能、schema、自動連鎖、承認機構を追加しません。

## Context

現在はM8「個人スコアDB保存」の範囲です。

次は実装済みです。

- 正式個人スコアDB schemaと互換検査
- DB準備、diagnostic、transaction writer
- preview材料と正式値を分離するadapter
- 明示DB pathへの単発保存
- strict JSON loaderを使うsave CLI
- duplicate preflight
- 副作用のないvalidation CLI
- 空review template生成CLI
- validation receiptの明示出力

今回扱うロードマップ項目:

> template作成、手入力、validation receipt確認、明示保存を人手で順に実施する最小レビュー手順をCLI/README上で固定する。

M8内の次項目は今回のPRに含めません。

- 低信頼度analysis詳細JSON、失敗画像、保持期間、`analysis_logs.log_path` の契約
- migration方針

## Deliverables

### 1. E2E workflow fixture test

`tests/test_personal_score_db_cli_save.py` または責務を限定した専用テストで、利用者と同じCLI入口を使って次を固定してください。

1. 空templateを生成する。
2. 未編集templateをvalidationし、`unresolved` を確認する。
3. unresolved receiptを生成しても保存されないことを確認する。
4. fixture相当の正式値を人手編集相当としてtemplateへ設定する。
5. validation receiptへ `ready` を記録する。
6. receipt生成だけではDBや `logs/` が作成・変更されないことを確認する。
7. 明示save pairを別操作として実行する。
8. 新規正式DBへ `source_captures`、`plays`、`analysis_logs` が各1件記録されることを確認する。
9. save CLIがreceiptを要求または参照していないことを確認する。

### 2. Boundary regression

既存テストと重複しすぎない範囲で、次を固定してください。

- 未編集templateは `unresolved`。
- unresolvedまたはinvalid receiptから保存へ自動遷移しない。
- ready receipt生成だけではDBや `logs/` を作らない。
- receiptを正式save inputとして受理しない。
- validation、receipt、saveは独立した明示操作である。
- 既存status、終了コード、副作用順序を維持する。

### 3. README workflow

`tools/vision_poc/README.md` に、次をコピー可能なPowerShellコマンドで記載してください。

1. template生成
2. ローカルでの人手編集
3. validation receipt生成
4. receipt内容確認
5. 明示save
6. DB diagnostic確認

READMEには次を明記してください。

- 各段階は独立した明示操作である。
- receiptは承認、署名、認可token、保存成功証明ではない。
- `ready` はsave input構築可能だけを意味する。
- validationはDB互換性、DB内duplicate、並行writer、実保存成功を保証しない。
- receipt生成後に入力JSONが変更されていないことも保証しない。
- save CLIはreceiptを要求・消費しない。
- 実保存前に利用者が正式入力JSONを確認する。
- コマンド例はGit管理外の `data/` とローカルDBを使う。

### 4. Minimal documentation sync

既存の設計docsまたはDB保存境界Skillで今回のworkflowを正確に説明できない場合だけ、最小限更新してください。

新しいSkillやSubagentは作成しません。

## Invariants

詳細な正本は `.agents/skills/review-ddrgp-db-save-boundary/SKILL.md` と関連設計docsです。特に次を維持してください。

- templateへ候補値、preview値、metadata値、相対時刻、正式値を自動転記しない。
- 未編集templateは `unresolved`。
- validationはstrict loaderとadapterだけを再利用し、DBを開かない。
- receiptはvalidation投影だけを持ち、正式値、候補材料、DB情報を持たない。
- receiptは承認証明やsave入力ではない。
- ready receipt生成だけではDBや `logs/` を作らない。
- save CLIは正式入力JSONだけをstrict loadする。
- 実保存は明示save pairを指定した場合だけ行う。
- preview候補材料を正式値へ暗黙昇格させない。
- duplicate preflight、DB拒否、transaction rollbackの既存契約を変えない。
- M8 preview DBと正式個人スコアDBを相互に受け入れない。
- 画像、`metadata.csv`、`data/`、`logs/`、実入力JSON、ローカルDB、生成物をコミットしない。

## Non-goals

- template、validation、receipt、save、diagnosticの自動連鎖
- validation成功後の自動save
- receiptの承認証明、署名、認可token化
- receiptへの正式値、候補材料、input hash、DB情報の追加
- save CLIによるreceiptの検証または消費
- templateへの候補値や正式値の自動入力
- schema version 2
- 既定入力、出力、DB path
- duplicate key方式の差し替え
- 冪等化、並行writer、ロック戦略
- migration方針またはmigration実装
- 低信頼度ログ、失敗画像、保持期間の新規契約
- 実キャプチャ、常駐監視、非同期処理、Windows UI
- OCR方式刷新、ROI座標の大変更
- 無関係なリファクタリングや別領域の修正
- 次PR相当の作業

## Validation

変更に近いテストから実行し、完了前に全体検証を行ってください。

```powershell
python -m pytest tests\test_personal_score_db_cli_save.py
python -m pytest tests\test_personal_score_db_file_save.py
python -m pytest tests\test_personal_score_db_save_adapter.py
python -m pytest tests\test_personal_score_db_save.py
python -m pytest tests\test_personal_score_db_schema.py
python -m pytest tests\test_vision_poc_ocr.py -k "m7_save_decision or m7_save_readiness or m7a or m8"
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall master tools\vision_poc
python -m pytest tests
python -m tools.vision_poc
python -X utf8 "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\review-ddrgp-db-save-boundary"
git diff --check
git status --short
```

READMEに記載したコマンドも、Git管理外の一時pathを使って実行可能性を確認してください。

既知の `pytest_chalice` / `pkg_resources` deprecated warningは、テスト失敗と区別してください。

## Review

実装後、`main` との差分をread-onlyでレビューしてください。利用可能なら `/review` を使い、DB保存境界Skillの観点を適用します。

少なくとも次を確認してください。

- receiptを承認証明またはsave入力として扱っていない。
- template、validation、receipt、saveが独立操作のままである。
- ready receipt生成だけでDBや `logs/` を作らない。
- E2EテストがCLI利用経路を表している。
- READMEコマンドと実CLI optionが一致する。
- READMEの保証範囲が実装より強くない。
- 不要なschema、公開CLI、migration、低信頼度ログ契約を追加していない。
- Git管理外ファイルがdiffやstaged filesへ混入していない。

重大度medium以上の指摘は、このPR範囲内なら修正して再検証してください。範囲外なら実装せず、次PR候補へ送ってください。

## Authorization

`AGENTS.md` の事前許可に基づき、次まで実施してください。

- 今回の変更だけをcommit
- 指定した `codex/*` ブランチへ通常push
- draft PR作成

次は実施しません。

- `main` への直接push
- force-push
- PR merge
- tag、release作成
- issueや既存PRへの書込み
- migration、データ削除、既存DB修復

## Acceptance Criteria

### Functionality

- templateから明示保存までの人手主導workflowを、既存CLIだけで再現するE2E fixtureテストが通る。
- 未編集templateは `unresolved`。
- unresolvedまたはinvalid receiptから保存へ進まない。
- ready receipt生成だけではDBや `logs/` を作成・変更しない。
- 明示save pairを別途実行した場合だけ正式DBへsource、play、analysisが各1件記録される。
- save CLIがreceiptを要求、検証、消費、参照しない。
- 既存status、終了コード、duplicate preflight、DB拒否、rollbackを壊していない。

### Documentation

- READMEにコピー可能な6段階のworkflowが記載されている。
- READMEが操作の独立性とreceiptの非保証範囲を説明している。
- README、E2Eテスト、実CLIの操作順と副作用境界が一致する。

### PR Quality

- 対象テストと全体検証が通る。
- Ruff、compileall、PoC実行、Skill validator、`git diff --check` が通る。
- read-onlyレビューでmedium以上の未対応指摘がない。
- staged diffがこのPR目的に限定されている。
- Git管理外ファイルをコミットしていない。
- commit、通常push、draft PR作成が完了している。
- 次PR相当の作業へ着手していない。

## Required Completion Report

完了報告には次を含めてください。

- 変更したファイルと責務
- 固定したworkflow
- 維持した保存境界
- 実行した検証と結果
- read-onlyレビュー結果
- コミットSHA
- push先branch
- draft PR番号またはURL
- 未解決事項
- 次PR候補
- `ユーザー対応が必要` または `現時点でユーザー対応はありません`

## Next Task Update

このPR完了後、`docs/next-task.md` は次PRの作業仕様へ更新してください。

現時点のM8後続候補:

1. 低信頼度analysis詳細JSON、失敗画像、保持期間、`analysis_logs.log_path` の参照契約
2. migration方針

優先順位を既存資料から一意に決められない場合は、候補を比較して `ユーザー対応が必要` としてください。今回のPRでは実装しません。
