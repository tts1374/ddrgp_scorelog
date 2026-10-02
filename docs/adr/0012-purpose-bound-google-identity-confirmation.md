# ADR 0012: Google identity再確認と本サービスの操作許可を分離する

## Status

Accepted

Date: 2026-10-02

Related issues: #210 / #213

Supersedes: [ADR 0011](0011-web-account-registration-and-management.md)

後続実装の契約でありruntimeは未変更。0011のWeb登録/管理、Local/Web Best責務、独立PC権限移行を継承し、機微操作の本人確認と操作認可の境界を確定する。

## Context

GoogleはGoogle Accountへの再認証要求をサポートしない。Google identity確認と、Google自身へのパスワード/MFA等による認証は別のイベントである。サービスの登録/引き継ぎ/解除/削除は、利用可能なGoogle OIDCとサービスが検証できる操作proofにより成立させる必要がある。

アカウント全削除はAppを失っていても本人Googleで行える責務を維持する。一方、通常Web session cookieだけでは機微操作を認可しない。PC移行とローカル履歴/同期責務は変更しない。

## Decision

1. 0011の単一App入口、Google必須Web登録/管理、Player/auth分離、不変Player/公開URL、DPAPI保護、自己歴代Bestの追加・改善、明示atomic置換、独立Credential activationを継承する。公開閲覧はログイン不要。
2. 通常プロフィール操作はGoogle認証済みWeb sessionとOrigin/CSRFで行う。新規登録/link/PC引き継ぎ/unlink/全削除は、通常loginとは独立したpurpose-bound OAuthでGoogle identityを再確認する。対象/目的/browser/App transactionをserverへ固定し、callbackでstate/nonceとissuer/sub等を検証する。
3. 機微操作の最終認可は、本サービスが発行する独立の短期confirmation proofと本人の明示確認で行う。proofは対象Player/目的/identity/session/browser/App proof等へbindingし、サービス側の成立時刻から期限を数える。消費と操作確定をatomicに行い、通常sessionによる代用や他操作への転用を拒否する。
4. linkは既存Playerの有効App Credential、unlinkは同Google identity再確認と有効App Credentialも要求する。unlinkで当該identityの全Web session/未完了操作許可をatomic失効し、active App Credential/公開URL/Best/Localを維持する。
5. アカウント全削除は同Google identityの操作専用OAuth確認、対象再表示と最終確認、サービス側の削除confirmation proofを要求する。App proofは要求しない。全権限/公開データをatomic削除し、Local履歴を保持する。応答喪失は短期結果proofで確認する。
6. OAuth round-tripはGoogle Accountへの再認証や追加要素を保証しない。既存Google SSOを利用できる。auth_timeは取得できた場合の補助的trust signalに限定し、取得不能/古い値だけで機微操作を禁止しない。

## Consequences

- Google追加claimの公開/審査/設定条件に依存せず、登録・PC引き継ぎとApp紛失後のWeb削除を実現できる。
- 通常Web sessionの奪取だけでは操作用OAuthと独立proofを完了できない。Google sessionや端末まで奪取された場合の追加本人確認は保証しない。
- Workerは通常loginと操作専用OAuth/短期proofを区別し、Windowsは従来のApp proof/照合/DPAPI保存/activation境界を維持する。実際のparameterと失敗/期限/replayは設計14/15に固定する。

## References

- [Google Account / Player recovery](../design/14_google_player_recovery.md)
- [Webマイプロフィール](../design/15_web_my_profile.md)
- [Web自己歴代Best](../design/16_web_historical_best.md)
- [Google Security Bundle](https://developers.google.com/identity/siwg/security-bundle)
- [Google OIDC API Reference](https://developers.google.com/identity/openid-connect/reference)
- [PR #214の本人判断を反映したレビュー方針](https://github.com/tts1374/ddrgp_scorelog/pull/214#discussion_r4164587129)
