# GP Score Log Web application

Issue #203のPlayer identity、Issue #204のPlayer Best同期、Issue #205の公開Player Dataを、同一Cloudflare Worker / D1 / originで提供します。React frontendはCloudflare Static Assetsとして同時にbuild・deployします。

## Local validation

```powershell
cd web\identity-api
npm ci
npm run check
```

`check`は型検査、Worker + local D1 test、React test、production buildを実行します。browser smokeはChromiumを準備したうえで`npm run test:e2e`を実行します。

## Local development

repository rootの`databases/ddrgp-master.sqlite`と`uv`を準備し、このdirectoryで次を実行します。

```powershell
npm run dev
```

開発用secretをGit対象外の`.dev.vars`へ生成し、再起動時は既存secretを再利用します。`PUBLIC_WEB_ORIGIN`はlocal用に設定します。local masterを`data/master/ddrgp-web-master.local.sql`へexportし、migrationとともに`.wrangler/development`配下のlocal D1へ適用します。Viteも同じ保存先を使用し、`http://127.0.0.1:5173`で起動します。準備だけを実行する場合は`npm run dev:prepare`を使用します。browser E2Eは`--mode e2e`で起動し、別の`.wrangler/e2e`を使用します。build済みfrontendを手動確認する`npm run preview`は開発用D1を使用します。

Windowsアプリをrepository rootからDebug起動し、設定画面で「公開データの同期」をONにして保存すると、開発用Playerの登録と同期が行われます。`公開ページを開く`もlocal Webを開きます。開発用のidentity・Credential・同期状態は本番用と分離しています。公開ページは登録後の`/player/{public_player_id}`で確認します。

## Cloudflare setup

1. `wrangler.jsonc`の既存D1 bindingと`PUBLIC_WEB_ORIGIN`をproduction環境に合わせる。初期production originは`https://ddrgp-scorelog.tts1374.workers.dev`である。
2. 公開用Worker `ddrgp-scorelog`へ、既存production Workerと同じ`CREDENTIAL_PEPPER`と`REGISTRATION_SECRET`を登録する。両者は互いに異なる32 byte以上の値である。既存Workerからraw secretは取得できないため、管理元の値を使用する。旧endpointを利用する配布版のサポート期間中は、旧WorkerのAPIも同じD1とsecretで維持する。

   ```powershell
   npx wrangler secret put CREDENTIAL_PEPPER
   npx wrangler secret put REGISTRATION_SECRET
   ```

3. repository rootでmaster DBを生成・検査し、同じcheckoutの曲名検索用別名を含むshared master SQLをexportする。

   ```powershell
   uv sync --frozen --extra dev
   uv run pytest tests/test_master_builder.py tests/test_master_identity_registry.py
   uv run python -X utf8 -m master --output data/master/ddrgp-master.sqlite
   uv run python -X utf8 -m master.inspect data/master/ddrgp-master.sqlite --summary data/master/master-summary.json --merge-report data/master/ddrworld-merge-report.json
   uv run python -X utf8 -m master.d1_export --master-db data/master/ddrgp-master.sqlite --output data/master/ddrgp-web-master.sql
   ```

4. このdirectoryでmigration、shared master SQL投入、Worker deployを順に実行する。migrationはtableと公開検索用columnを作成し、SQLがLocal masterと同じsong/chart identity、曲名の検索key・別名、`master_version`を反映する。各commandの成功を確認してから次へ進み、投入失敗時はdeployしない。

   ```powershell
   npm run migrate:remote
   npx wrangler d1 execute DB --remote --file ..\..\data\master\ddrgp-web-master.sql
   npm run deploy
   ```

main更新時の`deploy-web.yml`も同じcommitからmasterを生成・検査・exportし、上記の適用順を実行する。master生成・検査・投入に失敗した場合はdeployへ進まない。未登録の新曲・新表記は、既存IDとの対応を確認して`master/song_identity_registry.json`へ追加してから再実行する。

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
