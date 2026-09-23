# GP Score Log Web application

Issue #203のPlayer identity、Issue #204のPlayer Best同期、Issue #205の公開Player Dataを、同一Cloudflare Worker / D1 / originで提供します。React frontendはCloudflare Static Assetsとして同時にbuild・deployします。

## Local validation

```powershell
cd web\identity-api
npm ci
npm run check
```

`check`は型検査、Worker + local D1 test、React test、production buildを実行します。browser smokeはChromiumを準備したうえで`npm run test:e2e`を実行します。

## Cloudflare setup

1. `wrangler.jsonc`の既存D1 bindingと`PUBLIC_WEB_ORIGIN`をproduction環境に合わせる。初期production originは`https://ddrgp-scorelog.tts1374.workers.dev`である。
2. 公開用Worker `ddrgp-scorelog`へ、互いに異なる32 byte以上の値をsecretとして登録する。既存Workerからraw secretは取得できないため、管理元の値を使用する。

   ```powershell
   npx wrangler secret put CREDENTIAL_PEPPER
   npx wrangler secret put REGISTRATION_SECRET
   ```

3. migrationを適用してからdeployする。

   ```powershell
   npm run migrate:remote
   npm run deploy
   ```

4. master build artifactの`ddrgp-web-master.sql`を同じD1へ適用する。migrationはtableを作成し、artifact SQLがLocal masterと同じsong/chart identityと`master_version`を冪等upsertする。

   ```powershell
   npx wrangler d1 execute DB --remote --file ..\..\data\master\ddrgp-web-master.sql
   ```

`CREDENTIAL_PEPPER`はApp Credential secretのHMAC digest、`REGISTRATION_SECRET`は登録requestのdigestとretry時に同じCredentialを再構成するために使用します。どちらもDBやWrangler設定fileへ保存しません。値を失う、または入れ替えると既存Credentialの検証や未完了registration retryができなくなるため、Cloudflare secretとして保持してください。

## API

| Method | Route | Authentication |
| --- | --- | --- |
| `POST` | `/api/v1/players/register` | `Idempotency-Key` header |
| `GET` | `/api/v1/me` | `Authorization: Bearer <credential_id.secret>` |
| `PATCH` | `/api/v1/me` | App Credential |
| `DELETE` | `/api/v1/me` | App Credential |
| `POST` | `/api/v1/me/bests/batch` | App Credential |
| `POST` | `/api/v1/me/bests/snapshots` | App Credential |
| `PUT` | `/api/v1/me/bests/snapshots/{snapshotId}/items` | App Credential |
| `POST` | `/api/v1/me/bests/snapshots/{snapshotId}/commit` | App Credential |
| `DELETE` | `/api/v1/me/bests/snapshots/{snapshotId}` | App Credential |
| `DELETE` | `/api/v1/me/bests` | App Credential |
| `GET` | `/api/v1/public/players/{public_player_id}` | 不要 |
| `GET` | `/api/v1/public/players/{public_player_id}/bests` | 不要 |
| `GET` | `/api/v1/public/players/{public_player_id}/flare-skill` | 不要 |

登録の`Idempotency-Key`はclientが生成した32〜128文字のbase64url値です。同じ値のretryは同じPlayer、`public_player_id`、App Credentialを返します。通常logへrequest headerやresponse bodyを出力しないでください。

Best payloadに`player_id`とmaster metadataは含めず、認証済みcontextとD1 shared masterから解決します。deltaはitem単位partial success、snapshotは24時間TTLのstagingとrevision checkを使ったatomic replace-setです。公開Best削除はPlayer identityとCredentialを削除しません。詳細は[`docs/design/12_web_best_sync.md`](../../docs/design/12_web_best_sync.md)を参照してください。

公開URLは`/player/{public_player_id}`です。Player固有metadataとOverview bootstrapをWorkerで安全に注入し、BestとFlare Skillはsame-origin Public APIから取得します。詳細は[`docs/design/13_web_player_data.md`](../../docs/design/13_web_player_data.md)を参照してください。
