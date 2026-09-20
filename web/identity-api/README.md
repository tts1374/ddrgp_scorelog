# Player identity API

Issue #203のPlayer registration / identity専用Cloudflare Workerです。Player Best同期、公開Player Data、Rankingは含みません。

## Local validation

```powershell
cd web\identity-api
npm ci
npm run check
```

testはCloudflare Workers runtimeとlocal D1へmigrationを適用して実行します。

## Cloudflare setup

1. D1 databaseを作成し、`wrangler.jsonc`の`database_id`を実際のIDへ置き換える。
2. 互いに異なる32 byte以上の値をsecretとして登録する。

   ```powershell
   npx wrangler secret put CREDENTIAL_PEPPER
   npx wrangler secret put REGISTRATION_SECRET
   ```

3. migrationを適用してからdeployする。

   ```powershell
   npm run migrate:remote
   npm run deploy
   ```

`CREDENTIAL_PEPPER`はApp Credential secretのHMAC digest、`REGISTRATION_SECRET`は登録requestのdigestとretry時に同じCredentialを再構成するために使用します。どちらもDBやWrangler設定fileへ保存しません。値を失う、または入れ替えると既存Credentialの検証や未完了registration retryができなくなるため、Cloudflare secretとして保持してください。

## API

| Method | Route | Authentication |
| --- | --- | --- |
| `POST` | `/api/v1/players/register` | `Idempotency-Key` header |
| `GET` | `/api/v1/me` | `Authorization: Bearer <credential_id.secret>` |
| `PATCH` | `/api/v1/me` | App Credential |
| `DELETE` | `/api/v1/me` | App Credential |

登録の`Idempotency-Key`はclientが生成した32〜128文字のbase64url値です。同じ値のretryは同じPlayer、`public_player_id`、App Credentialを返します。通常logへrequest headerやresponse bodyを出力しないでください。
