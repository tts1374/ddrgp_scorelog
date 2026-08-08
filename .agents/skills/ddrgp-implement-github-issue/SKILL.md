---
name: ddrgp-implement-github-issue
description: DDRGP scorelogの確定済みGitHub Issueを実装契約として、関連Issue・設計docsの確認、局所実装、Required tests、Acceptance criteria突合、完了報告まで一気通貫で行う。Issue番号またはURLを指定して実装・修正・テストを依頼されたときに使う。アイデア整理、Issue新規作成、PR review指摘修正、CI失敗だけの修正、Issue未確定の実装には使わない。
---

# DDRGP Implement GitHub Issue

確定済みIssueからテスト済みローカル差分までを単一Leadで完了する。ルートと対象directoryの`AGENTS.md`を常に優先する。

## Execution Boundary

- Issue本文を今回の実装契約とし、Scope、Non-scope、Acceptance criteria、Required testsを上限にする。
- 通常はLead-onlyで実行する。Issueが大きい、reviewerのmodelを変えたい、またはtestを並列化したいだけではTeamを作らない。
- ローカルのin-scope編集と非破壊検証は、実装依頼に含まれるものとして進める。
- commit、push、PR作成、Issue編集、merge、releaseは、ユーザーが明示した場合だけ行う。
- screenshot、実入力JSON、解析ログ、ローカルDB、`data/`、`logs/`の生成物をGit対象へ入れない。
- 既存の未コミット変更とローカル素材を保護し、今回差分へ混入させない。

## 1. Resolve The Contract

編集前に次を確認する。

1. 指定Issueの本文、状態、親Issue、依存Issueを取得する。
2. `AGENTS.md`、対象directoryのnested `AGENTS.md`、関連docs、`docs/design/00_glossary.md`を読む。
3. 現在branch、HEAD、worktree状態、既存未コミット変更を確認する。
4. IssueのScope、Non-scope、Acceptance criteria、Required testsを短く内部整理する。
5. コードと既存testを調べ、最小の変更責務と影響範囲を特定する。

親Issueは背景、依存関係、全体のNon-scope確認にだけ使う。子Issueが明示していない親Issueの項目を実装へ追加しない。

## 2. Contract Gate

次のいずれかなら、編集せず`NEEDS_DECISION`で停止する。

- Issue本文とコードまたは設計docsが矛盾し、最小判断でも外部挙動が変わる。
- Scope、保存データ、公開CLI、永続化形式、ユーザー手順を追加または変更する必要がある。
- 複数の成立案があり、選択でAcceptance criteriaまたはNon-scopeが変わる。
- 必須検証を実行できず、代替検証だけで完了扱いにする判断が必要である。
- Issueが未確定、closed、別Issueへ置換済み、または実装対象を一意に特定できない。

`NEEDS_DECISION`では次だけを返す。

- 人間が決める必要のある質問。原則1問、最大3問。
- 推奨案と理由。
- 各選択肢がScope、互換性、保存データ、検証へ与える実際の影響。
- 判断に依存しない範囲で確認済みの事実。

回答後は同じIssueとworktree状態を再確認し、契約が確定した場合だけ続行する。Issue外の別課題は混入させず、別Issue候補として記録する。

## 3. Implement Minimally

1. 既存コードと既存patternに沿う局所変更を選ぶ。
2. 新規file、型、状態、設定、依存関係を必要最小限にする。
3. 変更する責務の主要正常系、現実的な失敗、今回の回帰だけをtestへ固定する。
4. 公開操作、CLI、永続化形式、ユーザー手順、判定契約を変えた場合だけ関連docsを同期する。
5. 新しい工程名または内部コード名を追加した場合は、同じ変更で`docs/design/00_glossary.md`へ追記する。
6. 既存の安全機構を、今回不要という理由だけで削除・簡略化しない。

作業中に契約判断が必要になった場合は、推測で進めずContract Gateへ戻る。

## 4. Validate

次の順で検証する。

1. 変更責務に直接対応するtest。
2. IssueのRequired testsとManual validation。
3. 変更した共通helper、schema、transaction、公開契約の影響範囲test。
4. repository既定のlint、型・構文検査、`git diff --check`。
5. `git status --short`とdiffを確認し、生成物、秘密情報、無関係な変更、encoding driftがないことを確認する。

repository既定CIは暗黙の検証対象とする。ローカルで再現できないCIや手動確認は、未実施理由と残るリスクを報告する。既存failureは今回差分との関係を確認し、無関係なら修正へ混ぜない。

正式個人スコアDB、正式保存可否、duplicate、schema、transaction、`source_captures`、`plays`、`analysis_logs`へ影響する場合は、`review-ddrgp-db-save-boundary`を使って境界checklistと対象testを追加確認する。独立reviewerを追加することだけを理由にTeamを作らない。

## 5. Acceptance Check

完了前に各Acceptance criterionを、実装箇所または検証結果へ対応付ける。次の場合は完了扱いにしない。

- Acceptance criterionが未実装または検証不能である。
- Required testが失敗している。
- Issue外の仕様追加がないと成立しない。
- 既存変更と今回差分を安全に分離できない。
- 生成物やローカル入力がGit差分へ混入している。

## 6. Report

次を簡潔に報告する。

- 実装概要。
- 変更file。
- 実行した検証と結果。
- 未実施の検証と理由。
- Issue仕様との差異。なければ`なし`。
- 別Issue候補。なければ`なし`。
- commit、push、PR作成を行っていない場合は、その状態。

完了報告だけを理由にIssueをclose、編集、commentしない。
