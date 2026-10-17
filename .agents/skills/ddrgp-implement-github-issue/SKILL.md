---
name: ddrgp-implement-github-issue
description: DDRGPの確定済みIssueを番号・URL指定で実装・修正・検証するときに使う。Issue作成、PR review指摘修正、CI失敗だけの修正は別workflow。
---

# DDRGP Implement GitHub Issue

確定済みIssueの実装・検証から、許可範囲に応じたPR作成・レビュー修正まで完了する。ルートと対象directoryの`AGENTS.md`を適用し、現在の明示的なユーザー指示をSkill guidanceより優先する。

## Execution Boundary

- Issue本文を今回の実装契約とし、Scope、Non-scope、Acceptance criteria、Required testsを上限にする。
- 小規模taskは単独で実行する。独立調査・並列検証で実益があり、実行環境とユーザーの許可範囲が認める場合はsubagentを利用できる。編集責務を分け、Issue契約・既存変更保護を共有し、結果を統合して完了判定する。
- ローカルのin-scope編集と非破壊検証は、実装依頼に含まれるものとして進める。
- commit、通常push、PR作成、レビュー修正・再レビュー依頼は、ルート`AGENTS.md`のIssue実行既定許可と現在の明示的な制限に従う。既に得た許可を再確認しない。Issue編集、merge、release、本番DB書込みなど既定許可外の操作には別途許可が必要。
- screenshot、実入力JSON、解析ログ、ローカルDB、`data/`、`logs/`の生成物をGit対象へ入れない。
- 既存の未コミット変更とローカル素材を保護し、今回差分へ混入させない。

## 1. Resolve The Contract

編集前に次を確認する。

1. 指定Issueの本文と状態を取得し、Scope、Non-scope、Acceptance criteria、Required testsを特定する。
2. `AGENTS.md`と対象directoryのnested `AGENTS.md`を確認する。既にcontextにある同一内容は再読込不要。
3. 現在branch、HEAD、worktree状態、既存未コミット変更を確認する。
4. 対象codeと既存testを調べ、最小の変更責務と影響範囲を特定する。

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

次の順で検証する。

1. 変更責務に直接対応するtest。
2. IssueのRequired testsとManual validation。
3. 変更した共通helper、schema、transaction、公開契約の影響範囲test。
4. repository既定のlint、型・構文検査、`git diff --check`。
5. `git status --short`とdiffを確認し、生成物、秘密情報、無関係な変更、encoding driftがないことを確認する。
6. ADR影響がある場合は、ADR番号・Status・index・関連design docsのlink、現在実装との整合、Supersedeの双方向参照を確認する。

repository既定CIは暗黙の検証対象とする。ローカルで再現できないCIや手動確認は、未実施理由と残るリスクを報告する。既存failureは今回差分との関係を確認し、無関係なら修正へ混ぜない。

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

次を簡潔に報告する。

- 実装概要。
- 変更file。
- 実行した検証と結果。
- 未実施の検証と理由。
- Issue仕様との差異。なければ`なし`。
- ADR判断と、作成・SupersedeしたADR。不要なら`NO_ADR`。
- 別Issue候補。なければ`なし`。
- 振り返り結果。観測した問題と再発防止の対応、未反映の候補と理由、または変更不要の理由を1〜3行で明記する。
- commit、push、PR作成を行っていない場合は、その状態。

完了報告だけを理由にIssueをclose、編集、commentしない。

報告する直前に末尾のCycle Retrospectiveを行う。レビュー待ちを作業完了やマージ可能と扱わず、今回確認できた範囲を報告する。

## Authorized GitHub Delivery

ルート`AGENTS.md`のIssue実行既定許可、または個別の明示依頼によりcommit、push、PR作成が許可されている場合に適用する。

- 原則1 Issueを1 PRで実装する。今回差分だけをstageして確認する。
- `.github/pull_request_template.md`を使い、不要な節・空節・重複を削除する。
- PR本文に`Closes #<number>`を置き、IssueのScopeや受け入れ条件の再掲を避け、実装差分・検証・未実施項目・仕様との差異・別Issue候補を書く。
- mergeには別途許可と対象PRの必須GitHub Actions成功が必要。失敗原因を確認せず再実行だけで通過扱いにしない。

### Review Follow-up

Issue実行の既定許可に含まれるPR、またはレビュー完了までの確認を個別依頼されたPRに適用する。単独のPR作成・review修正だけの依頼へ追跡の許可を追加せず、ローカル作業のみなど明示的に制限されたtaskでは監視やレビュー待ちを増やさない。

1. PR作成またはreview修正のpush後、利用可能なautomation toolで同じchatのheartbeat監視を開始する。既存の同じPRの監視は再利用し、対象repository、PR URL、Issue実行既定許可と現在の明示的な制限を反映した操作範囲をpromptへ保存する。追跡のみの依頼と、指摘修正・push・再レビュー依頼まで許可された依頼を区別する。定期実行を利用できない場合は未設定と報告し、監視中と偽らない。
2. 各回、現在のhead SHA、Codex reviewerの結果・対象commit・完了状態、現在有効な未対応指摘、必須CIの対象と成否、merge阻害の有無を取得する。レビュー結果は現在headへ照合し、古い結果や👍、コメントがないこと、取得不能をレビューOKの根拠にしない。head更新後は旧headの判定を使わない。
3. 現在headへのレビュー完了を確認した時点で、下記Cycle Retrospectiveへ新しく確認できた事実を反映する。指摘なしでも変更不要の理由を残す。レビューOKでもCI待ちやmerge阻害があれば、マージ可能とは通知しない。
4. 通知直前にheadと上記条件を再確認し、現在headへのレビューOK、必須CI成功、現在有効な未対応指摘なし、merge阻害なしを確認できた場合だけ、振り返り結果を添えてこのchatへマージ可能と1回通知し、監視を終了する。PRのclose・mergeでも監視を終了し、マージ可能とは通知しない。
5. 要対応のreview指摘があり、Issue実行既定許可または個別依頼でその修正が許可されている場合は、[レビュー修正Skill](../ddrgp-github-review-fix/SKILL.md)のAuthorization Gateを確認して、同Skillに従い修正・検証・commit・通常pushを進める。同Skillの[原因クラス監査と契約確認](../ddrgp-github-review-fix/SKILL.md#fix-and-validate)を適用し、同じ契約不一致が原因の互換性指摘が続く場合も個別修正を重ねる前に契約照合へ戻る。指摘件数やレビュー回数だけを理由に停止しない。再レビュー依頼まで許可されていれば依頼後に同じ監視へ戻る。
6. 追加許可、未確定の契約判断、レビュー修正SkillのStop Conditionsへの該当などで人間の対応が必要な場合だけ、確認できた事実と必要な対応を報告し、同じ回で監視を停止する。既に同じ状態を通知済みなら再通知せず停止する。レビューやCIの実行完了を待つだけなら、定期報告を出さず監視を継続する。停止した監視は、明示依頼された修正のpush・再レビュー依頼などが完了し、再び外部の処理待ちになった時点で同じ監視を再利用して再開する。人間の対応待ちだけを理由に再開しない。
7. 追跡のみの依頼から、指摘修正、再レビュー依頼、CI再実行、Skill編集、GitHub comment、mergeの許可を追加しない。状態確認はread-onlyで行い、変更操作は保存した許可範囲内でだけ行う。Issue実行既定許可を含め、既に許可された修正まで監視promptで一律に禁止しない。

## 7. Cycle Retrospective

ローカルの実装・検証またはreview修正の結果を報告する直前に、現在までの事実で短く振り返る。PRを追跡する場合は、現在headへのGitHub Codex review完了を確認した時点でも見直し、マージ可能通知より先に結果を報告する。初回の振り返りは将来のreviewやmergeを待たない。後続のreview・修正で新しい事実がなければ、前回から変更不要の理由を短く記載し、同じ振り返りを繰り返し列挙しない。

1. 今回のミス、ユーザーの訂正、独立確認したreview指摘、不要な手戻りを、会話・diff・検証結果などの事実から拾う。観測していない失敗を想定して規則を増やさない。
2. 原因を、手順の不足・曖昧さ・矛盾、既存指示の不履行、コードの不具合、仕様不足へ切り分ける。原因が未確認なら推測でSkillを書き換えない。
3. 次のIssueでも起こりうる作業判断の問題だけをSkill更新候補にする。既存指示で十分なら重複追加せず、必要に応じて配置・表現・参照導線を直す。コードの不具合は実装と回帰test、仕様不足はIssueまたは設計docsへ戻し、今回のscope外なら別課題として報告する。
4. Skillを更新する場合は`skill-creator`を使い、判断する場面と取る行動を具体化した最小差分にする。今回だけの事情を一般規則にせず、権限・Issue契約・既存の安全機構を維持する。
5. 更新したSkillの形式・参照を検証し、観測した失敗場面と通常の作業で、再発を防ぐ判断になり、不要な停止やscope拡張を招かないか確認する。重要な判断を変える更新で実行による確認が必要なら`empirical-prompt-tuning`を使い、未検証の効果を確認済みと報告しない。

Skill更新やIssue・PRへの記録は、今回の明示的な許可範囲でだけ行う。通常のIssue実装・review修正の許可だけで、契約外のSkill変更を今回diffへ混入させない。更新が許可されていなければ具体的な候補をtask報告に残し、振り返りだけを理由に完了した作業を止めたり、追加確認を要求したりしない。

報告は「再発防止の変更と根拠・検証結果」、未反映なら「更新候補と未反映理由」、更新不要なら「不要と判断した理由」を簡潔に記載する。振り返り用の空commit、定型文書、GitHub commentを新規作成しない。
