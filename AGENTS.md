# AGENTS.md

このリポジトリで常に適用する共通規則です。対象ディレクトリにnested `AGENTS.md` がある場合は、その追加規則にも従ってください。

## Project Rules

- スクリーンショット、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、実入力JSON、ローカルDBをGit管理しない。
- 生成物は原則 `data/` または `logs/` 配下へ出力する。
- 既存の未コミット変更、ローカル素材、生成物を保護し、今回の変更へ混入させない。
- 画像解析PoCは軽量に保ち、まずローカルで再現できる1コマンド実行を優先する。
- 公開操作、CLI、永続化形式、ユーザー手順または判定契約を変えた場合だけ、関連docsを同期する。内部実装だけの変更ではdocs更新を必須にしない。

## Implementation Proportionality

- 利用者、保存データ、障害時の実害に比例した必要十分な実装を選ぶ。商用サービス、複数組織運用、機密情報処理を前提とした設計を持ち込まない。
- 複数案がAcceptance criteriaを満たす場合は、既存コードと既存パターンの局所変更で済み、新規ファイル、型、状態、設定、依存関係が最も少ない案を優先する。
- Issueまたはnested `AGENTS.md`で要求されていないenterprise向けの堅牢性、将来拡張用の抽象化、汎用frameworkを追加しない。
- 親Issue、設計docs、nested `AGENTS.md`、Skillのchecklistは制約と確認観点であり、それ自体を追加実装や追加testのbacklogとして扱わない。変更していない責務をchecklistだけを理由にrefactorしない。
- 既存の安全機構は今回の目的に不要でも壊さない。簡略化や削除は明示scopeがある場合だけ行う。

## Issue Authoring

- Issueは、現在確認できているユーザー価値、不具合、または実測で必要と判明した作業を、実装可能な粒度へ固定するために作成する。
- Objective、Scope、Non-scope、Acceptance criteria、Required testsには今回必要な内容だけを書く。未観測の将来要件、将来consumer、将来version、理論上だけのedge caseを要件へ追加しない。
- 複数の実装方式が成立する場合は、必要な外部挙動と制約だけを固定し、内部構造、抽象化方式、汎用frameworkを指定しない。
- Required testsは主要正常系、現実に起こり得る失敗、今回修正する回帰へ限定する。網羅的な組合せ試験や全失敗点へのfailure injectionを慣例だけで要求しない。
- 安全性と検証水準は、対象ディレクトリのnested `AGENTS.md`、実際の利用者、保存データ、障害時の実害に合わせる。
- 「必要なら」「場合によっては」「将来を考慮して」など、実装者へ不要な選択肢を残す文言を避ける。必要性が未確定ならNon-scopeまたは別Issue候補とする。
- 親Issueは背景、依存関係、保証範囲を示す。子Issueへ明示していない親Issueの項目を暗黙の実装要件にしない。
- Issueには原則として `Objective`、必要な場合の `Product / implementation level`、`Scope`、`Non-scope`、`Acceptance criteria`、`Required tests`、`Validation`、`Deliverable` を置く。項目が不要なら形式維持のためだけに空節を作らない。
- 対象directoryのnested `AGENTS.md`に既定の実装水準がある場合、Issueにはその全文を再掲せず、今回の例外または追加制約だけを書く。
- repository既定のCIは暗黙に実行対象とし、IssueのRequired testsには変更責務に固有の検証と手動確認だけを書く。CIを一部省略する場合は理由を明示する。

## Task Scope

- 作業開始時に、指定されたGitHub Issue、関連する親Issue、関連docsを確認する。
- 指定Issueの本文を今回の実装契約とし、Scope、Non-scope、Acceptance criteria、Required testsに従う。
- 親Issueは背景、依存関係、全体のNon-scopeを確認するために参照する。子Issueが明示的に取り込んでいない親Issueの項目を今回の成果物へ追加しない。
- Issueに含まれない追加機能やリファクタリングを、明示的な必要性なく今回の変更へ含めない。
- 実装中に判明した別課題は今回へ混入させず、別Issue候補として完了報告へ記載する。
- Issue本文とコードまたは既存仕様が矛盾する場合は、推測で仕様を拡張せず、矛盾内容と採用した最小限の判断を報告する。
- 作業完了時に、実装概要、変更ファイル、実行した検証と結果、未実施の検証、Issue仕様との差異、別Issue候補を報告する。

## GitHub Workflow

- 原則として1 Issueを1 PRで実装する。
- IssueとPRはrepositoryのtemplateを使い、不要な節は削除する。形式維持のためだけの空節や本文の重複を残さない。
- PR本文で対象Issueを参照し、完了時に自動closeできる関係を明示する。
- PR本文ではIssueのScopeやAcceptance criteriaを再掲せず、実装差分、検証結果、未実施項目、Issue仕様との差異、別Issue候補を記載する。
- GitHub Actionsが設定されている場合は、対象PRの必須jobが成功してからmergeする。失敗を未確認のまま再実行だけで通過扱いにしない。
- 長期的に参照する仕様や設計判断はIssueだけに閉じ込めず、必要に応じて関連docsまたはADRへ反映する。ADRは複数PRまたは複数componentへ影響し、後から変更しにくい公開契約や永続化境界の決定に限定する。
- 作業状態、受け入れ条件、追加の実装判断はIssueまたはPR上に残す。
