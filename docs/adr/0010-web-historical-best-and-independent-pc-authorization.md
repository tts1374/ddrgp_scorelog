# ADR 0010: Web自己歴代Bestの蓄積とPC権限移行の分離

## Status

Superseded by [ADR 0011](0011-web-account-registration-and-management.md)

Date: 2026-10-01

Related issues: #210 / #213

Supersedes: [ADR 0008](0008-google-player-recovery-and-sync-consent.md)

後続実装の契約であり、現在のruntimeは未変更。[ADR 0006](0006-stable-web-player-identity-and-app-credential-boundary.md)と[ADR 0009](0009-google-authenticated-web-profile-management.md)のPlayer identityと公開名編集の境界を維持する。

## Context

AppはそのPCの正式個人スコアDBにある履歴からBestを算出する。Webは同じ公開Playerの自己歴代Bestを保持する。PC変更やcaptureに昇格しないmanifest復元による差は通常状態であり、公開ユーザーの引き継ぎをローカル集合の完全再構築へ依存させない。

既存full snapshotはatomic replace-setで空集合も有効。この契約を通常同期に使うと過去の公開Bestを消し得るため、改善だけの蓄積と明示置換の責務を分ける。また、PCの管理権限移行をBest置換transactionから独立させる必要がある。

## Decision

1. ローカル正式DBはそのPCの履歴のSource of Truth。Webはcapture由来で公開された自己歴代Bestの蓄積とする。現在のPCとの一致は保証せず、同期方向はLocal → Webを維持する。WebをLocal履歴の復元元にしない。
2. 通常同期は譜面の追加と各Best fieldの改善だけを受け付ける専用merge契約を使う。空集合、欠落、低下、Local削除は公開集合を損失させない。再ON・復元・repairもこの契約を使う。
3. 既存full snapshotのatomic replace-setと空集合有効性は維持し、歴代Best policyのPlayerには本人による別操作の置換確認を要求する。旧版の自動置換から蓄積を守り、既存legacy Playerは移行まで既存契約を維持する。
4. Google認証をWebに集約し、既存App Credentialの証明とGoogle認証で既存Playerへ連携する。Webで本人が承認した新PC用App Credentialを開始アプリだけへ渡し、DPAPI保存成功後のactivationで他App Credentialを失効する。activationとBest同期は別transactionで、activationはBestを変更しない。
5. 新PCの権限移行完了後、利用者は空DB/manifest復元でも追加・改善同期をONにできる。WebとLocalの完全一致を条件とする同期待機は設けない。未保存/未activation Credentialでは同期しない。
6. 不変Player/公開URL、Google issuer/sub identity、1:N App Credential、Windowsユーザー単位DPAPI、Google tokenとApp Credentialの分離、stable song/chart identityを維持する。公開閲覧はログイン不要、名前編集はGoogle認証済みマイプロフィールを使う。

## Consequences

- PC変更後も過去の公開Bestを保持して、新PCの記録を追加できる。空DBを公開全削除として解釈しない。
- WebのBestが現在のLocalより良い・譜面数が多い状態を許容する。Local履歴の移行と公開Playerの移行は独立する。
- 誤登録の高い値は改善だけの同期では訂正できず、明示置換または公開全削除が必要になる。
- Credential発行/保存/activationの失敗回復と、旧版の自動置換を拒否する移行境界が必要になる。
- 旧PC停止は新PC Credential activation時点で成立し、最初のスコア同期を待たない。端末一覧や複数PC同時同期は初期範囲に含めない。

## Alternatives Considered

- 現在のLocal集合との一致を同期再開条件にすると、Webが保持する過去の自己Bestと現在PCの履歴という責務を混同する。
- 既存snapshotを暗黙mergeへ変更すると、明示的な訂正・削除に必要なreplace-set契約を壊す。
- WebからLocalへBestを復元して一致させると、履歴移行と認証の責務を拡張する。

## References

- [Web自己歴代Best設計](../design/16_web_historical_best.md)
- [Google Account / Player recovery](../design/14_google_player_recovery.md)
- [Webマイプロフィール](../design/15_web_my_profile.md)
- [現行Web同期設計](../design/12_web_best_sync.md)
- [ADR 0008](0008-google-player-recovery-and-sync-consent.md)
- [Issue #210](https://github.com/tts1374/ddrgp_scorelog/issues/210)
- [Issue #213](https://github.com/tts1374/ddrgp_scorelog/issues/213)
