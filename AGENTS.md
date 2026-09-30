# AGENTS.md

このリポジトリの全taskに適用する不変条件です。対象directoryのnested `AGENTS.md`を追加適用します。現在の明示的なユーザー指示はSkill guidanceより優先し、既に許可されたscope内の局所編集・非破壊検証は追加確認なしで進めます。

## Project Rules

- スクリーンショット、`samples/screenshots/metadata.csv`、PoC出力、解析ログ、実入力JSON、ローカルDBをGit管理しない。
- 生成物は原則 `data/` または `logs/` 配下へ出力する。
- 既存の未コミット変更、ローカル素材、生成物を保護し、今回の変更へ混入させない。
- 画像解析PoCは軽量に保ち、まずローカルで再現できる1コマンド実行を優先する。
- 公開操作、CLI、永続化形式、ユーザー手順または判定契約を変えた場合だけ、関連docsを同期する。内部実装だけの変更ではdocs更新を必須にしない。
- milestoneコード、field名、status名を扱うときは `docs/design/00_glossary.md` を正本とし、対象と工程を付けた正式呼称を使う。新しい工程名や内部コード名を追加した場合は、同じ変更で用語集へ追記する。

## Implementation Proportionality

- 利用者、保存データ、障害時の実害に比例した必要十分な実装を選ぶ。商用サービス、複数組織運用、機密情報処理を前提とした設計を持ち込まない。
- 複数案がAcceptance criteriaを満たす場合は、既存コードと既存パターンの局所変更で済み、新規ファイル、型、状態、設定、依存関係が最も少ない案を優先する。
- Issueまたはnested `AGENTS.md`で要求されていないenterprise向けの堅牢性、将来拡張用の抽象化、汎用frameworkを追加しない。
- 親Issue、設計docs、nested `AGENTS.md`、Skillのchecklistは制約と確認観点であり、それ自体を追加実装や追加testのbacklogとして扱わない。変更していない責務をchecklistだけを理由にrefactorしない。
- 既存の安全機構は今回の目的に不要でも壊さない。簡略化や削除は明示scopeがある場合だけ行う。

## Contract And Safety

- 指定Issueの本文を実装契約とし、Scope、Non-scope、Acceptance criteria、Required testsに従う。親Issueの項目は子Issueが明示的に取り込んだ範囲だけ実装する。
- Issue外の追加機能やrefactorを混入させない。別課題は別Issue候補として報告する。
- 正本の矛盾は推測で仕様を拡張せず、矛盾内容と採用した最小判断を報告する。契約を越える挙動・保存データの変更や、未許可の公開操作は人間判断を求める。
- repository既定CIを検証対象とする。未実施・失敗と残るリスクを報告し、対象PRの必須GitHub Actionsが成功してからmergeする。再実行だけで成功扱いにしない。
- 長期仕様は関連docsへ残す。ADRは複数componentまたは複数PRへ影響する、変更しにくい公開契約・永続化・データ保護・配布境界の確定decisionに限定する。
- 作業状態、受け入れ条件、追加の実装判断はIssueまたはPR上に残す。投稿・更新は許可範囲内で行う。

Issue作成・仕様固定は`ddrgp-issue-authoring`、指定Issueの実装・検証は`ddrgp-implement-github-issue`を使う。workflow詳細と参照資料は、そのtaskに必要なSkillを選択してから読む。
