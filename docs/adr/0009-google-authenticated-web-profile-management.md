# ADR 0009: Google認証済みWeb管理と公開プレーヤー名の編集責務

## Status

Superseded by [ADR 0011](0011-web-account-registration-and-management.md)

Date: 2026-10-01

Related issues: #210 / #213

本decisionは後続実装の確定契約である。[ADR 0006](0006-stable-web-player-identity-and-app-credential-boundary.md)、[ADR 0008](0008-google-player-recovery-and-sync-consent.md)を拡張し、両decisionを置換しない。

## Context

公開Playerの管理・PC引き継ぎはGoogle認証で本人を確認する一方、スコア同期はWindowsごとのApp Credentialを使い、公開ページは共有URLから閲覧できる。公開プレーヤー名をWebに集約するとき、Windowsの保存済み設定が後からWebの名前を上書きしない編集・認証境界が必要になる。

この境界はWeb管理UI、Worker認証/プロフィールAPI、Windows設定と非秘密cacheへ影響する。既存の不変Player identityと公開Best同期の責務は維持する。

## Decision

1. Web管理と引き継ぎの本人認証にはGoogle loginを要求し、公開ページとread-only Public APIはログイン不要とする。Web sessionとWindows App Credentialは別の認証手段として同じ既存Playerへ解決する。
2. 公開プレーヤー名の編集場所をWebのマイプロフィールへ集約する。Google identityに連携済みのPlayerはGoogle認証sessionでだけ名前を変更でき、Windows Credentialからの上書きを拒否する。Windowsは表示cacheとWebへの導線を持つ。
3. 新しいWindowsアプリの登録は既存のserver既定名を使い、既存Playerの名前と公開ID/URLを保持する。未連携の従来配布版に対する登録・名前更新互換を維持し、Google連携後の名前編集はWebへ移す。
4. Google loginとプロフィール編集ではPlayer新規作成、Credential発行、公開Best置換、同期待機解除を行わない。履歴移行と同期再開の契約はADR 0008を継承する。

## Consequences

- 名前編集の認証と正本をWebへ集約し、公開URLを維持したまま更新できる。
- マイプロフィールを使う本人はGoogle連携が必要だが、未連携の既存公開同期と認証不要の共有閲覧は継続できる。
- Web管理用の短期sessionとCSRF保護が必要になる。Google tokenをWindows Credentialや永続sessionへ流用しない。
- Windows表示はserver名のcacheとなり、Web編集後に更新する必要がある。連携済みPlayerを旧Windows版で名前変更する操作は拒否される。

## Alternatives Considered

- WebとWindowsの双方から連携済みPlayer名を変更すると、保存済みWindows設定による意図しない上書きが起きるため採用しない。
- 公開URLの閲覧にGoogle loginを要求すると、既存の共有閲覧契約を変更するため採用しない。

## References

- [マイプロフィール設計](../design/15_web_my_profile.md)
- [Google Account / Player recovery](../design/14_google_player_recovery.md)
- [Web Player identity](../design/11_web_player_identity.md)
- [公開Player Data](../design/13_web_player_data.md)
- [Issue #210](https://github.com/tts1374/ddrgp_scorelog/issues/210)
- [Issue #213](https://github.com/tts1374/ddrgp_scorelog/issues/213)
