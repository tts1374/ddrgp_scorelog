---
name: ddrgp-issue-authoring
description: DDRGPの観測済み課題・確定した仕様を実装可能なIssue本文へ固定するときに使う。指定Issueの実装や未確定のアイデア整理は対象外。
---

# DDRGP Issue Authoring

現在必要な作業を、実装者が判断・検証できる契約へ固定する。ルートと対象directoryの`AGENTS.md`を適用し、現在の明示的なユーザー指示をSkill guidanceより優先する。

## Establish The Scope

- 現在確認できているユーザー価値、不具合、または実測で必要と判明した作業を対象にする。
- 関連する対象code・docsとnested `AGENTS.md`を確認する。親Issueがある場合は背景、依存、保証範囲を確認し、子Issueへ取り込む内容を明示する。
- 未観測の将来要件、将来consumer/version、理論上だけのedge caseを要件へ追加しない。
- 複数の実装方式が成立する場合は、必要な外部挙動と制約だけを固定し、内部構造、抽象化、汎用frameworkを指定しない。
- 主要な仕様判断が未確定なら、確定した仕様としてIssueへ書かず、必要な判断を確認する。

## Write The Contract

`.github/ISSUE_TEMPLATE/implementation-task.md`を使い、不要な節・空節・重複を削除する。今回必要な内容だけを次へ記載する。

- `Objective`: 達成する価値または観測した不具合。
- `Implementation level`: nestedの既定水準に対する今回の例外・追加制約だけ。全文を再掲しない。
- `Scope` / `Non-scope`: 今回の成果物と、現実的な誤実装を防ぐ境界。
- `Acceptance criteria`: 外部挙動と制約から判定できる完了条件。
- `Required tests`: 主要正常系、現実的な失敗、今回の回帰に固有の検証。網羅的な組合せ試験や全失敗点へのfailure injectionを慣例で要求しない。
- `Validation` / `Manual validation`: 比較条件や実機・GUI等の必要な確認。
- `Deliverable`: 実装差分と報告する結果。

repository既定CIは暗黙に実行対象とし、Required testsへ重複列挙しない。CIを一部省略する場合は理由を明示する。安全性と検証水準はnested、実際の利用者、保存データ、障害時の実害に合わせる。

「必要なら」「場合によっては」「将来を考慮して」など、不要な選択肢を実装者へ残さない。必要性が未確定の項目はNon-scopeまたは別Issue候補にする。親Issueの項目を暗黙の子Issue要件にしない。

## Validate And Deliver

- Scope、完了条件、検証が対応し、Issue外の機能・refactorを要求していないことを確認する。
- milestone、field、statusを扱う場合は`docs/design/00_glossary.md`の正式呼称と照合する。
- 却下済み案・訂正前内容が再混入し得る場合は`ddrgp-pink-elephant-guard`を使い、必須Non-scopeや安全・検証記録を保持する。
- 長期仕様が確定した場合は関連docsへ同期する。広範囲で変更困難なdecisionだけ`ddrgp-adr-authoring`の対象にする。
- GitHubへの作成・更新は明示的な許可範囲で行う。本文作成の依頼だけなら、reviewできる本文を返す。
