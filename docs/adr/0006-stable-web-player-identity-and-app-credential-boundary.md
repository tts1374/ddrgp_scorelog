# ADR 0006: 不変Web Player identityとApp Credential境界

## Status

Accepted

Date: 2026-09-20

Related issue: #203

## Context

Web公開用のPlayer Bestを後続componentから安全に更新するには、公開表示名とは独立したPlayer identityと、Windows appがそのPlayerを管理できることを証明する認証手段が必要になる。初期段階ではAccount登録やLoginを要求しない一方、将来Accountを導入したときに公開URL、Best、Ranking上のidentityを新しいPlayerへ移し替えない境界が必要である。

この境界はCloudflare Worker、D1、Windows app、後続のPlayer Best同期と公開Player Dataにまたがり、D1の永続キーとCredential保護を固定するため、局所実装ではなくarchitecture decisionとして記録する。

## Decision

1. PlayerをWeb上の不変identityとし、内部`player_id`と公開用`public_player_id`をdisplay nameや端末情報から独立して生成する。公開URLと後続データはPlayerへ紐付ける。
2. 認証手段をPlayerから分離し、Playerが複数Credentialを持てるschemaにする。初期実装はopaque App Credentialだけを提供し、将来Accountを既存Playerへ別の認証手段として追加する。
3. App Credentialは検索用IDと十分なentropyを持つsecretへ分離する。serverは検索用IDとsecretのdigestだけを保存し、raw secretをDBまたは通常logへ残さない。
4. Windows appは非秘密identity metadataとCredentialを分離し、CredentialをWindowsユーザー単位のDPAPI CurrentUserで保護する。認証拒否、通信障害、server障害を別状態として扱い、認証拒否から新しいPlayerを自動作成しない。
5. 認証済みAPIはCredentialからserver側で内部`player_id`を決定する。Player Best等のclient payloadへ任意の内部`player_id`を含めない。

## Consequences

- display name変更、将来Account追加、認証手段追加後も`player_id`、`public_player_id`、公開URL、Player Best、Ranking identityを維持できる。
- Credential漏えい時の影響は当該Playerの管理権限に及ぶため、Cloudflare secretとWindows DPAPI保護fileを継続して管理する必要がある。
- Account recoveryを実装するまで、Credentialを失ったPlayerの復旧は保証できない。
- registration retry用secretを維持する運用が必要になるが、response lossで不要なPlayerを増やさず、raw Credentialをserverへ保存せずに同じ応答を再現できる。
- 後続の同期・公開機能はこのidentity/authentication境界を再利用し、独自identityやCredentialを導入できない。

## Alternatives Considered

- display nameを公開identityとして使う案は、名称変更と重複を許容しながら公開URLとBest identityを維持できないため採用しない。
- 端末ID、Windows Machine ID、IP addressからPlayer identityを作る案は、移行性とprivacyを損ない、正当な所有証明にもならないため採用しない。
- Player rowへ単一Credentialを直接持たせる案は、将来Accountや追加Credentialを既存Playerへ紐付ける際にidentity移行が必要になるため採用しない。
- raw CredentialをD1へ保存する案は、DB漏えい時にそのまま認証へ使用できるため採用しない。

## References

- [Issue #203](https://github.com/tts1374/ddrgp_scorelog/issues/203)
- [Issue #143](https://github.com/tts1374/ddrgp_scorelog/issues/143)
- [`docs/design/11_web_player_identity.md`](../design/11_web_player_identity.md)
- [`web/identity-api/README.md`](../../web/identity-api/README.md)
- [`app/README.md`](../../app/README.md)
