---
name: ddrgp-implement-github-issue
description: DDRGPの確定済みIssueを番号・URL指定で実装・修正・検証するときに使う。Issue作成、PR review指摘修正、CI失敗だけの修正は別workflow。
---

# DDRGP Implement GitHub Issue

確定済みIssueの実装・検証から、許可範囲に応じたPR作成・レビュー修正まで完了する。ルートと対象directoryの`AGENTS.md`を適用し、現在の明示的なユーザー指示をSkill guidanceより優先する。

## Execution Boundary

素材保護・Issue契約・操作権限はルート`AGENTS.md`を正本とする。Issue番号・URLと目的から直接開始でき、開始指示文の生成を前提にしない。`ddrgp-issue-start-prompt`は別チャット用の文面を依頼された場合だけ使う。

Main Agentが実装・統合・完了判定を担当する。Subagentsは独立調査・検証・監査で実益があり、許可と実行環境が揃う場合だけ使う。分担時は境界・成果物を指定し、重複調査を避ける。利用不能な環境へ特定のagent構成を要求しない。

## 1. Resolve The Contract

編集前に次を確認する。

1. 指定Issueの本文と状態を取得し、Scope、Non-scope、Acceptance criteria、Required testsを特定する。
2. `AGENTS.md`と対象directoryのnested `AGENTS.md`を確認する。既にcontextにある同一内容は再読込不要。
3. 現在branch、HEAD、worktree状態、既存未コミット変更を確認する。
4. Issueの目的・変えてはいけない契約・完了条件・許可範囲を今回のGoalとして捉え、対象codeと既存testから最小の変更責務と影響境界を特定する。契約が揃っていれば人間に再記述を求めない。

局所修正はそのまま編集・対象検証へ進む。複数段階・横断契約・探索が必要な場合だけ、未確認事項と検証可能な実装単位を短く計画し、調査→実装→検証でGoalの不足を確認する。失敗や新しい証拠が出た責務へ戻り、契約確認済みの全工程を毎回やり直さない。専用Goalツール・多段計画の有無を開始条件にしない。

### Risk And Work Selection

変更行数・ファイル名ではなく、利用者・保存データへの実害と影響境界で選ぶ。複数に当たる場合は影響する経路に強い確認を適用し、無関係な領域へ広げない。判明した副作用・回帰に応じて再評価する。

| 区分 | 判断と例 | 検証・レビューの基本方針 |
| --- | --- | --- |
| Low | 文言、内部docs、通常表示の局所CSS。操作・判定・保存へ影響しない | 単独実行。差分・表示・link等と対応する既存test。無関係なdocs/ADR、全領域調査、全製品手動testは不要 |
| Medium | UI再構成、集計、既存機能の挙動変更 | 関連機能の正常系・代表操作・現実的な失敗を検証。UIは採用基準と主要操作を確認。独立作業に実益がある場合だけ分担 |
| High | 正式DB保存/削除/復元、schema/migration、認証・同期・外部送信、配布境界へ影響 | 変更する契約、既存データ互換性、障害/回復、副作用境界のtestと独立レビューを選ぶ。許可済みの安全なfixture・使い捨てDB等で検証 |

削除確認画面のCSSでも確認・キャンセル・無効化・危険操作の識別が影響すれば、その操作経路をHighとして確認する。見た目だけを理由に実認証・送信・削除を実行しない。nestedの安全条件は維持する。

Highの独立レビューは変更契約に焦点を合わせ、利用可能な別実行者またはPR reviewer等を使う。利用不能ならMain Agentの別passを補助とし、独立確認未実施を報告する。必須の独立確認を自己確認で完了扱いにしない。区分は追加権限や固定の全test一覧ではない。Required testsと必須CIはどの区分でも維持し、通常の方式選択や許可済み操作を再承認待ちにしない。

参照資料は次の条件で読む。資料一覧の存在だけを理由に全件読まない。

- 親・依存Issue: 指定Issueが契約を参照している、前提や依存の完了状態が実装判断へ影響する場合。
- docs: Issueが指定した契約資料と、変更する責務の仕様・公開手順を確かめる箇所。
- `docs/design/00_glossary.md`: milestone、field、statusの呼称を扱う、新しい工程名・内部コード名を追加する場合。
- ADR: 下記の適格性を満たす境界の変更、またはIssueがADRとの照合を要求する場合。まず`docs/adr/README.md`から関連ADRを選び、`CREATE`、`SUPERSEDE`、既存ADRで十分な`NO_ADR`を判定する。適格性を満たさない局所変更はADRを読まず`NO_ADR`とする。

親Issueは背景、依存関係、全体のNon-scope確認にだけ使う。子Issueが明示していない親Issueの項目を実装へ追加しない。

既存データの受理条件を狭める前に、変更する境界と関連する生成・保存経路について、検証前の変換・暗黙補完、実保存値、読み込み側の受理条件を対応表で契約と照合し、保存→読み込みの往復を検証する。現在のwriter入力条件と、過去の保存済み値の互換性を区別する。調査は変更境界と関連経路に限り、全形式・repository全体の調査や、互換性に影響しない局所修正への追加調査は求めない。対応表・検証記録はtask報告またはignoredな`logs/`配下でよく、常設文書は追加しない。

入出力境界の拒否testの期待値は、形式の見た目だけで決めず、既存経路での実際の受理結果と契約の両方を根拠にする。保存できることだけで正当とは扱わない。食い違いは下記のContract Gateで扱い、Issueとユーザー指示で確定済みの範囲は追加確認なしで進める。

ADRは、複数componentまたは複数PRへ影響し、後から変更しにくい公開契約、永続化・データ保護・runtime・配布境界を固定する場合だけ対象にする。Issue固有の局所実装、UI詳細、threshold、test条件はADR化しない。

## 2. Contract Gate

現在のIssueと明示的なユーザー指示で未確定の契約判断が必要な場合だけ、依存する編集を止めて`NEEDS_DECISION`とする。

- Issue本文とコードまたは設計docsが矛盾し、最小判断でも外部挙動が変わる。
- 許可されたScopeを越えて、保存データ、公開CLI、永続化形式、ユーザー手順などの契約を追加・変更する必要がある。
- 複数の成立案があり、選択でAcceptance criteriaまたはNon-scopeが変わる。
- 必須検証を実行できず、代替検証だけで完了扱いにする判断が必要である。
- Issueが未確定、closed、別Issueへ置換済み、または実装対象を一意に特定できない。

Issueに明示された契約変更の実装、同じ受け入れ条件を満たす局所的な方式選択、非破壊検証は停止理由にしない。未許可の公開操作は実行せず、許可済みのローカル成果物と検証を完了してから必要な判断を求める。必須検証が実行できない場合も、実行可能な独立作業は進め、代替検証で完了扱いにする承認を推測しない。

`NEEDS_DECISION`では次を簡潔に返す。

- 人間が決める必要のある質問。原則1問、最大3問。
- 推奨案と理由。
- 各選択肢がScope、互換性、保存データ、検証へ与える実際の影響。
- 判断に依存しない範囲で確認済みの事実。

Skillが停止を要求した場合は、読んだ`SKILL.md`のlinkと該当指示を引用し、未確定判断または未許可操作との関係を説明する。

回答後は同じIssueとworktree状態を再確認し、契約が確定した場合だけ続行する。Issue外の別課題は混入させず、別Issue候補として記録する。

## 3. Implement Minimally

1. 既存コードと既存patternに沿う局所変更を選ぶ。
2. 新規file、型、状態、設定、依存関係を必要最小限にする。
3. 変更する責務の主要正常系、現実的な失敗、今回の回帰だけをtestへ固定する。
4. 公開操作、CLI、永続化形式、ユーザー手順、判定契約を変えた場合だけ関連docsを同期する。
5. 新しい工程名または内部コード名を追加した場合は、同じ変更で`docs/design/00_glossary.md`へ追記する。
6. 既存の安全機構を、今回不要という理由だけで削除・簡略化しない。

作業中に契約判断が必要になった場合は、推測で進めずContract Gateへ戻る。

ADR影響が`CREATE`または`SUPERSEDE`の場合は`ddrgp-adr-authoring`を使う。Issueと関連docsがdecisionを確定しており、ADRがそのdecisionの記録に留まる場合は同じ変更で同期する。ADR作成に追加のarchitecture判断やIssue外の責務が必要な場合は実装へ混入させず、Contract Gateへ戻すか別Issue候補として報告する。Accepted ADRの本文を現在仕様へ追従させるために書き換えない。

## 4. Validate

選んだリスクと影響する責務に対して検証する。IssueのRequired tests・Manual validationは区分で弱めない。

1. 変更責務に直接対応するtest。
2. IssueのRequired testsとManual validation。
3. 変更した共通helper、schema、transaction、公開契約の影響範囲test。
4. 変更言語・責務のlint、型・構文検査と`git diff --check`。docs/Skillsだけなら形式・trigger/body・参照・UTF-8/改行を確認し、無関係な製品検査をローカルで一律実行しない。
5. `git status --short`とdiffを確認し、生成物、秘密情報、無関係な変更、encoding driftがないことを確認する。
6. ADR影響がある場合は、ADR番号・Status・index・関連design docsのlink、現在実装との整合、Supersedeの双方向参照を確認する。

広いローカルtestは共通境界への影響、影響範囲が限定不能、予期しない回帰、または明示要求がある場合に選ぶ。repository既定の必須CIはローカル検証の選択と独立して全件確認する。ローカルで再現できないCIや手動確認は、未実施理由と残るリスクを報告する。既存failureは今回差分との関係を確認し、無関係なら修正へ混ぜない。

正式個人スコアDB、正式保存可否、duplicate、schema、transaction、`source_captures`、`plays`、`analysis_logs`へ影響する場合は、`review-ddrgp-db-save-boundary`を使って境界checklistと対象testを追加確認する。

## 5. Acceptance Check

完了前に各Acceptance criterionを、実装箇所または検証結果へ対応付ける。次の場合は完了扱いにしない。

- Acceptance criterionが未実装または検証不能である。
- Required testが失敗している。
- Issue外の仕様追加がないと成立しない。
- Issue scope内で必要と判定したADRの作成・Supersede・index同期が完了していない。
- 既存変更と今回差分を安全に分離できない。
- 生成物やローカル入力がGit差分へ混入している。

## 6. Report

報告直前に[Cycle Retrospective](#7-cycle-retrospective)を行う。Goalの達成結果、変更箇所、実行した検証と未実施理由・残るリスク、仕様との差異、ADR判断、別Issue候補、commit/PRとレビュー・CIの状態を簡潔に報告する。リスク判断の根拠は検証を選んだ理由として必要な範囲で記す。空の節や同じ受け入れ条件を重複列挙しない。

完了報告だけを理由にIssueをclose、編集、commentしない。レビュー待ちを完了・マージ可能と扱わない。

## Authorized GitHub Delivery

ルート`AGENTS.md`のIssue実行既定許可、または個別の明示依頼によりcommit、push、PR作成が許可されている場合に適用する。

- 原則1 Issueを1 PRで実装する。今回差分だけをstageして確認する。
- `.github/pull_request_template.md`を使い、不要な節・空節・重複を削除する。
- PR本文に`Closes #<number>`を置き、IssueのScopeや受け入れ条件の再掲を避け、実装差分・検証・未実施項目・仕様との差異・別Issue候補を書く。
- mergeには別途許可と対象PRの必須GitHub Actions成功が必要。失敗原因を確認せず再実行だけで通過扱いにしない。

### Review Follow-up

PR準備は上記、指摘の修正・検証・再レビュー依頼は`ddrgp-github-review-fix`が担当する。追跡が許可されている場合だけ[review-follow-up.md](references/review-follow-up.md)を読み、PR作成・push後の状態確認、外部処理待ち、最終判定を引き継ぐ。修正から戻った際は取得済みのhead・指摘・CIを共有し、変化がなければ重複取得しない。

## 7. Cycle Retrospective

結果報告前と現在headのレビュー完了時に、観測した問題・再発防止・未反映候補または変更不要の理由を短く振り返る。新事実がなければ前回との差だけを報告する。Skill更新を検討する場合だけ[retrospective.md](references/retrospective.md)を読み、通常実装の許可を契約外のSkill編集へ拡張しない。
