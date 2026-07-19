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
- PR本文で対象Issueを参照し、完了時に自動closeできる関係を明示する。
- 長期的に参照する仕様や設計判断はIssueだけに閉じ込めず、必要に応じて関連docsまたはADRへ反映する。ADRは複数PRまたは複数componentへ影響し、後から変更しにくい公開契約や永続化境界の決定に限定する。
- 作業状態、受け入れ条件、追加の実装判断はIssueまたはPR上に残す。
