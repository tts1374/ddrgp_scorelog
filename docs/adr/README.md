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
| [`0006`](0006-stable-web-player-identity-and-app-credential-boundary.md) | Accepted | 不変Web Player identityと追加可能な認証手段、App Credential保護境界 | Web identity API、D1、Windows secure storage |
| [`0007`](0007-local-source-of-truth-and-stable-web-best-replica.md) | Superseded by ADR 0008 | ローカル正本、一方向Web Best replica、安定master identityの初期decision | Web Best同期、D1 shared master、M4 identity registry |
| [`0008`](0008-google-player-recovery-and-sync-consent.md) | Superseded by ADR 0010 | Google recoveryと確認済みsnapshotによる初期引き継ぎdecision | Google identity、App Credential発行、同期再開gate |
| [`0009`](0009-google-authenticated-web-profile-management.md) | Superseded by ADR 0011 | Google認証済みWeb管理へ公開プレーヤー名編集を集約し、公開閲覧とWindows同期を分離する後続実装契約 | Webマイプロフィール、Web session、Windows表示cache |
| [`0010`](0010-web-historical-best-and-independent-pc-authorization.md) | Superseded by ADR 0011 | Web自己歴代Bestを追加・改善で蓄積し、明示置換とPC Credential activationを分離する後続実装契約 | Web Best merge、App-Web承認、独立PC権限移行 |
| [`0011`](0011-web-account-registration-and-management.md) | Superseded by ADR 0012 | 未リリースの単一App入口、Google必須Web登録/管理、全Playerの歴代Best同期と独立PC権限移行 | Web登録/ログイン、マイプロフィール/削除、App Credential、Web Best |
| [`0012`](0012-purpose-bound-google-identity-confirmation.md) | Accepted | Google identity再確認と本サービスの短期操作許可を分離し、Web登録/管理・独立PC権限移行・歴代Best境界を継承 | purpose-bound OAuth、Web操作proof、App proof、アカウント削除 |
| [`0013`](0013-song-ac-history-reference-data.md) | Accepted | 曲版のAC収録歴を共有参照データで管理し、既存playを再集計する | マスタ更新、アプリ／Web FLARE SKILL |

## Status

- `Proposed`: decisionは提案中で、実装契約として確定していない。
- `Accepted`: decisionが確定し、現在または後続実装のarchitecture boundaryとして有効。
- `Superseded by ADR NNNN`: 後継ADRがdecisionを置換した。記録として本文を保持する。

## 作成対象

ADRは、複数componentまたは複数PRへ影響し、変更しにくい公開契約、永続化・データ保護・runtime・配布境界を固定するときに作成する。局所実装、UI詳細、threshold、fixture、作業手順、検証結果は、対象に近いIssue、design doc、component README、履歴資料へ記録する。
