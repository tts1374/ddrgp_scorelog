---
name: ddrgp-pink-elephant-guard
description: DDRGP scorelogのIssue、設計docs、README、PR本文、Release notes、UI文言などを修正するとき、却下済みの案や訂正前の内容を完成面へ再登場させず、現在の契約だけから自然に書き直す。変更履歴、review、障害分析、必須のNon-scope・安全表示・検証記録には使わない。
---

# DDRGP Pink Elephant Guard

修正会話に残る旧案を完成物の主題へ戻さず、現在確定している状態だけで自立する成果物を作る。単語を伏せるのではなく、旧案中心の説明構造を捨てて現在の目的から組み直す。

ルートと対象directoryの`AGENTS.md`、指定Issue、関連docsの契約を常に優先する。このSkillは事実や必須記録を隠すために使わない。

## When To Apply

次の条件をすべて満たす修正で暗黙適用する。

1. 会話に却下、削除、訂正、禁止された案がある。
2. Issue、設計docs、README、利用手順、PR本文、Release notes、UI文言など、読者向けの完成物を作成または修正する。
3. 旧案や変更経緯を成果物へ記録する必要がない。

「その案はなし」「これは消して」「今の仕様では扱わない」「その話はもう関係ない」のような意味を、文字列一致ではなく依頼全体から判定する。

次では適用しない。

- 変更履歴、案比較、議事録、review指摘、障害分析、原因調査、再発防止
- Issue契約上必要なNon-scope、Acceptance criteria、Required tests、Validation
- PRや完了報告に必要な実装差分、未実施検証、Issue仕様との差異、別Issue候補
- 法令、安全、契約、アクセシビリティ、データ保護に必要な表示

非発動対象を含む文書でも、記録上必要な箇所だけを例外とし、利用者向けの説明へ旧案を広げない。

## Establish The Current Contract

編集前に会話と正本を内部で次へ分ける。

- `CURRENT_CONTRACT`: 現在伝えるべき目的、仕様、挙動、制約
- `REJECTED_HISTORY`: 取り下げた案、訂正前の値、不要になった論点と理由
- `REQUIRED_RECORD`: 比較、Non-scope、安全表示、差分報告など、成果物へ残す契約がある内容
- `SURFACE`: 読者が見る見出し、本文、表、注記、ラベル、例、CTA

優先順位は、必須の安全・法務表示、repository指示と成果物の記録契約、現在確定したIssue・docs・ユーザー判断、過去の会話の順とする。正本同士が矛盾する場合は、このSkillで片方を隠して解決せず、通常の契約確認へ戻る。

## Rewrite From A Clean Brief

1. `CURRENT_CONTRACT`、必要な読者、媒体、トーン、`REQUIRED_RECORD`だけで内部用briefを作る。
2. `REJECTED_HISTORY`をnegative promptや「書かないもの一覧」としてbriefへ再投入しない。
3. 旧稿から語句を削るだけで済ませず、現在の目的を主語にして見出しと説明構造から書き直す。
4. 削除後の空白は、現在必要な情報、操作、判断基準、構図で埋める。
5. `REQUIRED_RECORD`は指定された節と目的にだけ残す。

DDRGP scorelog固有の注意:

- 却下案を「今回はXをしない」「Xは対象外」とNon-scopeへ機械的に移さない。現実的な誤実装を防ぐ境界として必要な場合だけ、現在の契約を表す最小限のNon-scopeを書く。
- 既に不要な案を、将来Issue候補、互換性注記、設定項目、抽象化、状態名、用語集項目として復活させない。
- 省略名や訂正前の工程名を残さず、`docs/design/00_glossary.md`の正式呼称を使う。
- PR本文と完了報告では、実際に生じた差分と未解決事項は保持し、会話中だけで消えた案は説明しない。

## Pink Elephant Scan

完成前に`SURFACE`全体を確認する。

- **Literal leak**: 却下語、訂正前の名称・数値、表記揺れが残っていないか。
- **Semantic leak**: 同義語、上位語、否定形、婉曲表現で旧案を示していないか。
- **Rationale leak**: 「以前は」「代わりに」「検討したが」など、不要な変更理由を説明していないか。
- **Attention leak**: 見出し、冒頭、結論、注記が不在や旧案を主題にしていないか。
- **Structural leak**: 旧案があったからだけ存在する節、表の列、空ラベル、placeholder、設定、例が残っていないか。

漏れがあれば、その語だけを削除せず、Clean Briefから該当部分を再生成して再検査する。

## Completion Criteria

- 成果物が`CURRENT_CONTRACT`だけで自然に理解できる。
- `REQUIRED_RECORD`以外の旧案、言い換え、削除理由、不在の強調がない。
- Issueやdocsでは、必要なScopeとNon-scopeの境界が弱くなっていない。
- review、障害分析、検証、データ安全に必要な事実を隠していない。
- ユーザーが完成稿だけを求めた場合、旧案の検査報告を完成稿へ添えない。
