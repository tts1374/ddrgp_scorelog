# ADR 0008: Googleによる既存Player recoveryと同期再開の明示許可

## Status

Superseded by ADR 0010

後継: [ADR 0010](0010-web-historical-best-and-independent-pc-authorization.md)。Web自己歴代Bestの蓄積とPC権限移行の独立化を採用する。下記本文は初期decisionの記録として保持する。

Date: 2026-10-01

Related issue: #210

Supersedes: [ADR 0007](0007-local-source-of-truth-and-stable-web-best-replica.md)

本decisionは仕様確定であり、Google連携とrecoveryは後続Issueで実装する。

## Context

Windowsユーザー単位で保護されたApp Credentialを新PCへ単純にコピーできないため、任意のGoogle認証を既存Playerの管理権限の回復へ使う。Player引き継ぎとローカル履歴の移行は異なり、空DBやmanifestバックアップ復元の後にcapture限定full snapshotを自動commitすると既存公開Bestを失い得る。

Worker/D1、Windows secure storage、同期coordinatorにまたがる所有者確認・Credential発行・公開集合の置換許可を固定する。ADR 0007 Decision 3の自動再整合条件にrecovery時の前提を追加するため後継ADRとする。その他のdecisionは継承する。

## Decision

1. [ADR 0006](0006-stable-web-player-identity-and-app-credential-boundary.md)の不変Player identity、認証手段分離、1:N App Credential、DPAPI CurrentUser、serverでのPlayer解決を維持する。既存CredentialとGoogle認証の両方で既存PlayerへGoogle identityを追加する。Google identityは検証済みissuer/subで一意に結び、emailや公開URLを所有証明にしない。
2. 新PCのGoogleログインは既存Playerに同じ形式の新App Credentialを発行する。Google tokenを通常のPlayer管理Credentialにしない。ログイン・連携失敗では新Playerを自動生成しない。旧Credentialの失効は新PCの確認済み初回snapshot commitまで延期し、公開集合置換と同じtransactionでhandoverを確定する。
3. ADR 0007 Decision 1、2、4、5を継承する。正式個人スコアDBがSource of Truth、Web Bestはcapture由来ProjectionだけのLocal→Web replica、認証済みPlayer contextを再利用し、LocalとWebのstable song/chart identityを共有する。
4. ADR 0007 Decision 3を次の条件付き再整合へ置換する。差分hashは正式DB外に保持する。初回ON、再開、bulk restore、repairではstagingを経たatomic replace-set snapshotで再整合するが、Google recovery由来Credentialは認証回復と同期許可を分離する。ログイン後に同期待機へ入り、現在のcapture由来集合が全既存公開Bestを保持できることへの本人確認、または欠落・低下を含む置換への明示確認を得てからcommitする。空snapshotの有効性は維持する。
5. 再開許可はserverで当該Credential、snapshot内容、公開revision、有効期限に固定する。通常同期・直接API・再起動・復元から待機を迂回できない。Web BestをLocalの履歴へ復元せず、比較は公開集合の損失確認に限定する。

## Consequences

- 同じPlayer・公開URLを引き継ぎながら、新PCの空DBや不完全な集合による意図しない公開Best置換を防げる。
- Googleログインは履歴移行・同期再開の完了を意味しない。履歴復元後もcapture由来集合が不足する場合は待機か明示置換を本人が選ぶ。
- 同期復旧に公開集合との比較・本人確認が必要となり、Google recovery由来Credentialにはserver側の書込みgateを追加する。
- 新PCの最初の再開成功で他App Credentialを失効し、同時PC運用は提供しない。旧端末は既存の認証無効状態へ接続する。
- 既存未連携Playerの通常利用と公開閲覧は維持する。Google identity・認証transaction・許可をscore実績や正式backupへ混入させない。

## Alternatives Considered

- Googleログイン直後の自動snapshotは、空/manifestのみの新PCから公開集合を消すため採用しない。
- 譜面数一致だけの再開判定は、異なる譜面集合や個別Best値の低下を見逃すため採用しない。
- Web BestからのLocal復元は、Local正本・capture限定・一方向同期の責務を変えるため採用しない。

## References

- [Issue #210](https://github.com/tts1374/ddrgp_scorelog/issues/210)
- [後続実装Issue #213](https://github.com/tts1374/ddrgp_scorelog/issues/213)
- [Google Account / Player recovery設計](../design/14_google_player_recovery.md)
- [Web Player identity設計](../design/11_web_player_identity.md)
- [Web Best同期設計](../design/12_web_best_sync.md)
- [ADR 0006](0006-stable-web-player-identity-and-app-credential-boundary.md)
- [ADR 0007](0007-local-source-of-truth-and-stable-web-best-replica.md)
