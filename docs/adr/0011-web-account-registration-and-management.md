# ADR 0011: WebでのGoogle必須アカウント作成と管理

## Status

Superseded by ADR 0012

後継: [ADR 0012](0012-purpose-bound-google-identity-confirmation.md)。機微操作のGoogle identity再確認とサービス側の操作許可を分離する。以下のAccepted時の本文は保持する。

Date: 2026-10-02

Related issues: #210 / #213

Supersedes: [ADR 0009](0009-google-authenticated-web-profile-management.md), [ADR 0010](0010-web-historical-best-and-independent-pc-authorization.md)

後続実装の契約でありruntimeは未変更。[ADR 0006](0006-stable-web-player-identity-and-app-credential-boundary.md)のPlayer/auth分離、1:N Credential、DPAPI境界を維持する。

## Context

サービスは未リリースであり、Google未連携の従来版利用者の互換導線を要求しない。Appの作成/引き継ぎ入口を一つにし、Google認証と新規登録/ログインの判断をWebに集約する。同期停止/再開は認証や登録から独立する。

公開名とアカウント全削除を本人向けWeb管理へまとめる。ログイン失敗による別Player作成と、新PCの空DBによる公開Best消失を防ぎ、公開閲覧にはログインを要求しない。

## Decision

1. App未登録時は「アカウントを作成・引き継ぐ」を単一入口とし、Webで新規登録/ログインを選ぶ。新規作成は検証済みGoogle identity、開始App proof、明示登録確認を要求する。Google loginだけではPlayerを作成しない。登録済みGoogleから別Playerを作らない。
2. 既存Playerへの認証追加は有効な既存App Credential＋Google認証で同じPlayerへ行う。不変Player/公開URL、Google issuer/subの1:1紐付けを維持する。認証手段の変更とPlayer作成を分離する。
3. Google認証はWeb clientの固定HTTPS callbackへ集約する。App専用proofでpending App Credentialを受領し、DPAPI保存/再読込後のactivationで他App Credentialをatomic失効する。1:N schemaを維持し、activationはBest/Localデータを変更しない。
4. ローカル正式DBはそのPCの履歴のSource of Truth。Webはcapture由来で公開された自己歴代Bestを追加・改善で蓄積する。空DB/manifest復元/不完全集合を通常同期の障害にせず、Web→Local復元を行わない。
5. full snapshotは空も有効なatomic replace-setのまま、全Playerで本人による別操作の置換確認を要求する。通常同期はmergeだけ。未リリースなのでPlayer別の旧版policy/自動置換互換を設けない。
6. 新規登録、公開名編集、アカウント全削除はGoogle認証済みWebへ集約する。公開閲覧は認証不要。Google解除は有効App proofも要求してAppの継続利用を確認する。全削除では再認証/確認後にPlayerと全権限/公開データをatomic削除し、Local履歴は保持する。

## Consequences

- Appでのアカウント操作、Web本人管理、PC内の同期停止/再開の境界が明確になる。
- 匿名登録、Appの名前更新/全アカウント削除、通常の置換batchを新契約で拒否する。既存開発データのIDや所有権をmigrationで再作成しない。
- Google解除後は現在のAppで同期を続けられるが、Web管理/引き継ぎには同じPlayerへの再連携が必要になる。
- 新PCへ権限を移しても公開BestとLocal DBの差を許容する。誤登録した高い値の訂正は明示置換/公開Best全削除を必要とする。
- 登録確定後のApp保存失敗ではPlayerを維持し、同transaction再照会または本人loginで回復する。アカウント削除は認証を消すため、短期proof付き完了結果の確認を設ける。

## References

- [Google Account / Player recovery](../design/14_google_player_recovery.md)
- [Webマイプロフィール](../design/15_web_my_profile.md)
- [Web自己歴代Best](../design/16_web_historical_best.md)
- [Issue #210](https://github.com/tts1374/ddrgp_scorelog/issues/210)
- [Issue #213](https://github.com/tts1374/ddrgp_scorelog/issues/213)
