# Architecture Decision Records

このdirectoryは、GP Score Logで複数componentまたは複数PRへ影響し、後から変更しにくい公開契約、永続化、データ保護、runtime、配布のarchitecture decisionを記録する。

現在の詳細仕様は[`../design/README.md`](../design/README.md)を正本とする。ADRは判断理由と責務分離を保持し、Accepted後に詳細仕様へ追従させるため本文を書き換えない。decisionを置換するときは後継ADRを作り、旧ADRと新ADRを相互参照する。

## 一覧

| ADR | Status | Decision | 主要正本 |
|---|---|---|---|
| [`0001`](0001-foundational-poc-boundaries.md) | Accepted | 初期PoCのFrameInput、confirmed event、local生成物境界 | FrameInput、event・保存境界 |
| [`0002`](0002-app-owned-formal-save-boundary.md) | Accepted | app-owned recognitionとformal evidenceによる正式保存境界 | pipeline、event・保存境界、正式個人スコアDB |
| [`0003`](0003-database-responsibility-and-protection.md) | Accepted | DB責務の分離と正式個人スコアDBの保護 | data model、storage、正式個人スコアDB |
| [`0004`](0004-separate-application-and-reference-data-updates.md) | Accepted | application packageとreference data setの更新分離 | storage、app package・更新 |
| [`0005`](0005-application-owned-user-theme-and-runtime-tokens.md) | Accepted | app-owned user theme設定とXAML・コード描画のsemantic token境界 | user settings、UI resources、runtime theme適用 |

## Status

- `Proposed`: decisionは提案中で、実装契約として確定していない。
- `Accepted`: decisionが確定し、現在または後続実装のarchitecture boundaryとして有効。
- `Superseded by ADR NNNN`: 後継ADRがdecisionを置換した。記録として本文を保持する。

## 作成対象

ADRは、複数componentまたは複数PRへ影響し、変更しにくい公開契約、永続化・データ保護・runtime・配布境界を固定するときに作成する。局所実装、UI詳細、threshold、fixture、作業手順、検証結果は、対象に近いIssue、design doc、component README、履歴資料へ記録する。
