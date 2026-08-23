---
name: ddrgp-adr-authoring
description: DDRGP scorelogで、確定済みの変更しにくいarchitecture decisionを既存ADR・設計正本と照合し、ADRの新規作成、Supersede、index同期、検証まで行う。複数componentまたは複数PRへ影響する公開契約、永続化・データ保護・配布境界の決定を記録するときに使う。初期議論、Issue固有の局所実装、UI詳細、threshold調整、検証結果だけの記録には使わない。
---

# DDRGP ADR Authoring

確定したarchitecture decisionを、現在の詳細仕様とは分離した長期記録として残す。ルートと対象directoryの`AGENTS.md`、指定Issue、既存ADR、関連design docsを優先する。

## Eligibility Gate

次を両方満たす決定だけをADR対象にする。

1. 複数componentまたは複数PRへ影響する。
2. 後から変更しにくい公開契約、永続化境界、データ保護境界、runtime・配布・更新の責務分離を固定する。

次はdesign docs、Issue、review記録、testへ残し、ADRを作らない。

- 単一責務の局所実装や既存pattern内の選択
- UI配置、文言、mock、個別command、内部class構造
- OCR方式、ROI、threshold、fixture、実測値の調整
- 一時的なmigration手順、作業順、完了checklist
- 主要な判断が未確定の案比較

該当性が弱い場合はADRを増やさず、対象に最も近いdesign docを更新する。

## Establish The Record

編集前に次を確認する。

1. `docs/adr/README.md`と既存`docs/adr/*.md`。
2. 指定Issue、関連する親Issue、決定を実装したPR情報。
3. `docs/design/00_glossary.md`と決定対象のdesign docs。
4. 現在のコード、test、公開手順のうち決定を裏付ける箇所。

決定を次のいずれかに分類する。

- `CREATE`: 新しい独立decisionで、既存ADRが扱っていない。
- `SUPERSEDE`: 既存ADRのdecisionを置換する。既存本文は書き換えず、旧ADRのStatusと相互linkだけを更新する。
- `NO_ADR`: Eligibility Gateを満たさない、既存ADRで十分、または決定が未確定。

Accepted ADRのContext、Decision、Consequencesを現在仕様へ追従させるために書き換えない。詳細仕様の変更はdesign docsへ反映し、architecture decisionが変わった場合だけ後継ADRを作る。

## Author The ADR

連番は`docs/adr/README.md`と既存fileから次の未使用4桁番号を選ぶ。file名は`NNNN-short-kebab-title.md`とする。

本文は原則として次を置く。

- `# ADR NNNN: <decision title>`
- `## Status`: `Proposed`、`Accepted`、`Superseded by ADR NNNN`のいずれか
- `## Context`: 判断が必要になった制約と責務。Issueの時系列や実装詳細を再掲しない
- `## Decision`: 採用する責務分離、外部契約、不変条件
- `## Consequences`: 得られる性質、運用上のcost、変更時に必要な作業
- `## Alternatives Considered`: 実際に成立した主要案と採用しなかった理由。会話中だけの案を増やさない
- `## References`: Issue、design docs、component READMEなどの正本

ADRは「なぜこのarchitectureか」を保持し、table、column、status全件、CLI option、thresholdなど変化しやすい詳細を複製しない。詳細は相対linkでdesign docへ委譲する。工程名、field名、status名は`docs/design/00_glossary.md`の正式呼称を使う。

既に実装済みのdecisionを後から記録する場合は、Statusを`Accepted`とし、Contextで現在のarchitectureを固定する目的を簡潔に示す。過去のIssue本文を現在の要件として再構成しない。

## Synchronize

- `docs/adr/README.md`へ番号、title、Status、対象責務、主要正本を追加する。
- 関連design docの読者がdecision理由を必要とする場合だけ、ADRへの短いlinkを置く。
- `docs/README.md`と`docs/design/README.md`はADR indexを入口とし、個別ADR一覧を重複させない。
- Supersede時は旧ADRと新ADRを相互参照させる。
- ADR追加だけを理由に要求、schema、公開手順を変更しない。

## Validate

完了前に次を確認する。

- 番号とfile名が一意で、indexのStatusと一致する。
- ADRのDecisionが現在のコードとdesign docsに裏付けられている。
- ADRと正本に矛盾がなく、変化しやすい詳細をADRへ複製していない。
- 相対linkが存在し、Supersede linkが双方向である。
- UTF-8 BOMなし、LF、`git diff --check`を維持する。
- Issue実装の一部なら、ADR作成がIssue scopeと確定済みdecisionを越えていない。

完了報告では`CREATE`、`SUPERSEDE`、`NO_ADR`の判断、作成・更新file、参照した正本、検証結果を示す。
