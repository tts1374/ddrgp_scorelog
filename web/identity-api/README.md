# GP Score Log Web application

Player identity、自己歴代Bestの追加・改善同期、公開Player Data、Google認証付きWeb管理を、同一Cloudflare Worker / D1 / originで提供します。React frontendはCloudflare Static Assetsとして同時にbuildします。認証・管理の正本は設計14/15/16とADR 0012です。

## TOPページ

`/`はアプリのダウンロード、記録・Web連携の始め方、公開Player Dataの使い方を案内します。右上の「マイページ」からログイン・プロフィール管理へ進めます。有効なWebログインと連携済みPlayerがある場合は、自分の公開ページ`/player/{public_player_id}`へ移動します。未ログイン、期限切れ、未連携の場合は案内ページを表示します。TOPの表示・移動は`no-store`で扱い、アカウントの作成や連携は開始しません。

## Local validation

```powershell
cd web\identity-api
npm ci
npm run check
```

`check`は型検査、Worker + local D1 test、React test、production buildを実行します。browser smokeはChromiumを準備したうえで`npm run test:e2e`を実行します。

## Local development

Windows Debugアプリの既定接続先はHTTPSのdev Workerです。local Webは画面・APIの検証用で、Google実認証は固定HTTPS callbackを持つdev Workerで行います。Windowsからlocalを選ぶ場合は`DDRGP_WEB_API_ORIGIN=http://127.0.0.1:5173/`を明示します。既存local環境で設定・migrationが古い場合も、次の`npm run dev`で準備をやり直して起動します。build済みのpreviewを使う場合は準備後の再build・再起動が必要です。

repository rootの`databases/ddrgp-master.sqlite`と`uv`を準備し、このdirectoryで次を実行します。

```powershell
npm run dev
```

開発用secretをGit対象外の`.dev.vars`へ生成し、再起動時は既存secretを再利用します。`PUBLIC_WEB_ORIGIN`はlocal用に設定します。local masterを`data/master/ddrgp-web-master.local.sql`へexportし、migrationとともに`.wrangler/development`配下のlocal D1へ適用します。Viteも同じ保存先を使用し、`http://127.0.0.1:5173`で起動します。準備だけを実行する場合は`npm run dev:prepare`を使用します。browser E2Eは`--mode e2e`で起動し、別の`.wrangler/e2e`を使用します。build済みfrontendを手動確認する`npm run preview`は開発用D1を使用します。

Windowsアプリの「アカウントを作成・引き継ぐ」からWebの新規登録/ログインを選びます。Googleアカウントと手元のアプリの照合コードを確認し、明示承認後にアプリが認証情報を保存・activationします。アプリで連携が完了すると同期ONになり、このPCの自己ベストをWebに送ります。公開名は`/my/profile`で編集します。Googleメールアドレスは本人向け管理画面にだけ表示されます。

Google実通信の手動確認には本番と分離したHTTPS開発Worker/D1、開発専用のGoogle Web client、当該HTTPS originの固定callback `/api/v1/auth/web/google/callback`、Googleテストユーザーを用意します。`GOOGLE_CLIENT_ID`、`GOOGLE_CLIENT_SECRET`、独立した32 byte以上の`APP_AUTHORIZATION_SECRET`を開発環境に設定します。本番client/secret/D1を流用しません。Desktop clientやWindows loopback callbackへ置き換えません。これらの設定とテストユーザーなしでは実OAuthを検証できません。ローカルHTTPはmock・fixtureの検証用です。自動UIテストは認証APIの応答をmockし、Google実通信を代替した成功とは扱いません。

`/my/profile`、`/my/app-connect`、`/my/account-delete`、`/my/public-data-delete`、`/my/auth-error`はWorker-firstのno-store管理画面です。Google選び直しで未保存入力と確認checkboxを破棄します。Google解除は利用できるアプリの「Web連携」から開始し、アカウント全削除はAppなしでもWebから同じGoogleを確認して行えます。どちらもGoogleの選択だけでは確定しません。`/player/{public_player_id}`はログイン不要です。公開Player Dataの右上にある「マイページ」から`/my/profile`へ移動できます。未ログイン時はGoogleログインの案内を表示します。ログイン済みで公開プロフィールがある場合は、マイページの右上にある「公開ページ」から自分の公開Player Dataへ移動できます。

## Cloudflare setup

### HTTPS開発Worker / D1

`wrangler.jsonc`の`dev`環境は、`ddrgp-scorelog-dev`と`ddrgp-scorelog-web-db-dev`を使用します。開発originは`https://ddrgp-scorelog-dev.tts1374.workers.dev`です。既定環境は本番のままです。開発用のmigration・master投入には`--env dev --config wrangler.jsonc`を指定します。

```powershell
cd web\identity-api
npx wrangler d1 migrations apply DB --remote --env dev --config wrangler.jsonc
npx wrangler d1 execute DB --remote --env dev --config wrangler.jsonc --file ../../data/master/ddrgp-web-master.dev.sql
```

開発master SQLはrepository rootで`uv run python -X utf8 -m master.d1_export --master-db databases/ddrgp-master.sqlite --output data/master/ddrgp-web-master.dev.sql`により生成します。

Cloudflare Vite pluginはbuild時に環境を選び、選択済みの設定を`dist/ddrgp_scorelog/wrangler.json`へ出力します。開発作業用PowerShellで次を実行し、出力設定のWorker名とD1 IDがdevであることを確認してからdeployします。別環境のbuildを行うと同じ出力設定が更新されるため、deploy直前にdev buildを行います。

```powershell
$env:CLOUDFLARE_ENV = 'dev'
npm run build
npx wrangler deploy --dry-run --config dist/ddrgp_scorelog/wrangler.json
npx wrangler deploy --config dist/ddrgp_scorelog/wrangler.json
```

開発Workerにも独立した`CREDENTIAL_PEPPER`、`APP_AUTHORIZATION_SECRET`、開発Google clientの`GOOGLE_CLIENT_ID`、`GOOGLE_CLIENT_SECRET`を設定します。Googleの承認済みredirect URIは`https://ddrgp-scorelog-dev.tts1374.workers.dev/api/v1/auth/web/google/callback`です。Windows Debugアプリは既定でこのdev Workerへ接続するため、起動時の設定は不要です。接続先を明示固定・変更する場合は`DDRGP_WEB_API_ORIGIN`を指定します。開発環境の準備・公開は明示依頼された場合だけ行います。

### 本番Worker / D1

1. `wrangler.jsonc`の既存D1 bindingと`PUBLIC_WEB_ORIGIN`をproduction環境に合わせる。初期production originは`https://ddrgp-scorelog.tts1374.workers.dev`である。
2. 公開用Workerへ`CREDENTIAL_PEPPER`、`APP_AUTHORIZATION_SECRET`、`GOOGLE_CLIENT_ID`、`GOOGLE_CLIENT_SECRET`を設定する。digest/認証用secretは互いに異なる32 byte以上の値にする。Google Web clientのcallbackは当該originの固定HTTPS callbackとする。利用する全Worker originに同じ新契約を適用し、匿名登録や通常置換を迂回できる旧APIを公開しない。本番OAuth設定、remote D1変更、deployは別途明示された公開作業で実施する。

   ```powershell
   npx wrangler secret put CREDENTIAL_PEPPER
   npx wrangler secret put APP_AUTHORIZATION_SECRET
   npx wrangler secret put GOOGLE_CLIENT_ID
   npx wrangler secret put GOOGLE_CLIENT_SECRET
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

`CREDENTIAL_PEPPER`はApp Credential/session/proofのdigest、`APP_AUTHORIZATION_SECRET`は開始Appへの冪等Credential受領とWeb CSRFを支えます。raw secretやGoogle tokenをDB、Git、log、browser localStorageへ保存しません。設定値を失う、または入れ替えると既存認証や未完了操作の回復に影響します。

## API

| Method | Route | Authentication |
| --- | --- | --- |
| `POST` | `/api/v1/players/register` | 410 `REGISTRATION_MOVED_TO_WEB` |
| `GET` | `/api/v1/me` | `Authorization: Bearer <credential_id.secret>` |
| `PATCH` | `/api/v1/me` | 409 `WEB_PROFILE_REQUIRED` |
| `DELETE` | `/api/v1/me` | 409 `WEB_ACCOUNT_REQUIRED` |
| `POST` | `/api/v1/me/bests/batch` | 409 `HISTORICAL_BEST_REQUIRED` |
| `POST` | `/api/v1/me/bests/merge` | active App Credential |
| `POST` | `/api/v1/me/bests/snapshots` | App Credential |
| `PUT` | `/api/v1/me/bests/snapshots/{snapshotId}/items` | App Credential |
| `POST` | `/api/v1/me/bests/snapshots/{snapshotId}/replacement-review` | active App Credential |
| `POST` | `/api/v1/me/bests/snapshots/{snapshotId}/replacement-authorize` | active App Credential＋明示確認 |
| `POST` | `/api/v1/me/bests/snapshots/{snapshotId}/commit` | active App Credential＋置換許可 |
| `DELETE` | `/api/v1/me/bests/snapshots/{snapshotId}` | App Credential |
| `DELETE` | `/api/v1/me/bests` | 409 `WEB_PUBLIC_BESTS_REQUIRED` |
| `GET` | `/api/v1/public/players/{public_player_id}` | 不要 |
| `GET` | `/api/v1/public/players/{public_player_id}/bests` | 不要 |
| `GET` | `/api/v1/public/players/{public_player_id}/flare-skill` | 不要 |
| `GET` | `/api/v1/account/session`、`/api/v1/account/profile` | Web session |
| `PATCH` | `/api/v1/account/profile` | Web session＋Origin/CSRF |
| `POST` | `/api/v1/account/bests/deletion-start` | Web session＋Origin/CSRF（公開記録削除専用OAuth開始） |
| `POST` | `/api/v1/account/bests/deletion-cancel` | Web session＋Origin/CSRF（当該目的の未完了操作を取消） |
| `GET` | `/api/v1/account/bests/deletion-confirmation` | Web session＋公開記録削除専用proof |
| `DELETE` | `/api/v1/account/bests` | Web session＋専用proof＋Origin/CSRF＋`confirmed: true`＋`sync_stopped: true` |
| `POST` | `/api/v1/auth/web/logout` | Web session＋Origin/CSRF |
| `POST` | `/api/v1/auth/app-authorizations/{id}/web-start` | browser/session＋Origin/CSRF |
| `POST` | `/api/v1/auth/app-authorizations/{id}/approve` | Web session＋操作proof＋Origin/CSRF＋明示照合 |
| `POST` | `/api/v1/account/deletion-start` | Web session＋Origin/CSRF |
| `POST` | `/api/v1/account/deletion-confirmations` | Web session＋削除proof＋Origin/CSRF＋明示確認 |
| `DELETE` | `/api/v1/account` | Web session＋削除確認proof＋Origin/CSRF |

App authorizationのID/request secretは開始Appで固定し、raw request secretはWebへ渡しません。Web承認からCredentialは取得できず、アプリの保存・独立activation完了まで引き継ぎ完了と表示しません。通常logへ認証header/queryや本人emailを出力しないでください。

Best payloadに`player_id`とmaster metadataは含めず、認証済みcontextとD1 shared masterから解決します。通常mergeは追加・fieldごとの改善だけ、空集合やLocal削除でWeb Bestを消しません。snapshotは明示比較・確認で取得した10分TTLの一回限り置換許可とrevision checkを使ったatomic replace-setです。公開記録の削除は`/my/profile`から開始します。先にアプリの「Webへの送信を止める」を押し、Webで同じGoogleアカウントを確認してから、公開記録の削除を確定します。削除後にアプリの「Webへの送信を再開する」を押すと、このPCのcapture由来の自己ベストをすべて送り直します。Webからアプリの送信設定は変更しません。公開記録の削除はPlayer、Google連携、App Credential、公開URL、PC内のスコア履歴を保持します。削除と専用proof消費、未完了snapshotの無効化はatomicで行い、同じ消費済みproofの再試行は成功結果を返すだけで、再送済み記録を削除しません。空集合の削除ではrevisionと公開更新日時を変えません。migration `0008_public_best_deletion.sql`は既存OAuth要求を保護し、公開記録削除専用purposeを追加します。詳細は[設計16](../../docs/design/16_web_historical_best.md)を参照してください。

公開URLは`/player/{public_player_id}`です。Player固有metadataとOverview bootstrapをWorkerで安全に注入し、BestとFlare Skillはsame-origin Public APIから取得します。詳細は[`docs/design/13_web_player_data.md`](../../docs/design/13_web_player_data.md)を参照してください。
