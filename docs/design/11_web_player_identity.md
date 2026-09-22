# Web Player identity設計

Issue #203で実装するWeb Player identity、App Credential、Windows側identity stateの正本です。Player Best同期、Web Player Data、Rankingは扱いません。長期的な責務分離の理由は[`ADR 0006`](../adr/0006-stable-web-player-identity-and-app-credential-boundary.md)に記録します。

## 責務境界

- Cloudflare WorkerはD1上の内部`player_id`を認証済みrequest contextへ解決する。
- `public_player_id`は公開URL用の不変IDで、display nameから生成しない。
- Playerと認証手段を分離し、`Player : PlayerCredential = 1:N`とする。初期実装のCredential typeは`app`だけである。
- Windows appは`public_player_id`とApp Credentialを保持する。Credential secretを正式個人スコアDB、通常設定JSON、Release logへ入れない。
- 後続のPlayer Best同期は、このserviceが返す認証済みcontextとBearer Credentialを再利用する。clientから任意の内部`player_id`を送らない。

Account、Login、OAuth、Credential再発行、複数PC管理、Player Best同期は本設計の対象外である。将来Accountを追加する場合は、新しいPlayerへ置換せず既存`player_id`へ別の認証手段として紐付ける。

## D1 schema

migrationの正本は[`web/identity-api/migrations/0001_player_identity.sql`](../../web/identity-api/migrations/0001_player_identity.sql)である。

| table | 主な責務 |
| --- | --- |
| `players` | 内部`id`、不変`public_player_id`、可変`display_name`、作成・更新時刻 |
| `player_credentials` | 検索用Credential ID、`player_id`、type、secretのHMAC-SHA-256 digest、利用・失効時刻 |
| `player_registration_requests` | registration request digestから作成済みPlayerとCredentialを一意に解決する冪等化記録 |

`player_credentials.player_id`は外部キーで、Player削除時にcascade削除する。Credential IDをprimary key検索してからsecretを検証するため、Credential全件scanは行わない。Account tableは未実装だが、Playerと認証手段が分離されているため既存Player identityを維持したまま追加できる。

## IDとCredential

- 内部`player_id`: `pl_`＋128 bitの暗号学的乱数。
- `public_player_id`: `p_`＋128 bitの暗号学的乱数。
- Credential ID: `ac_`＋128 bitの暗号学的乱数。
- Credential secret: registration secret、registration request ID、Credential IDを入力とするHMAC-SHA-256の256 bit出力。
- Bearer表現: `<credential_id>.<base64url-secret>`。

D1にはCredential secretを保存せず、別のCloudflare secretで計算したHMAC-SHA-256 digestだけを保存する。`CREDENTIAL_PEPPER`と`REGISTRATION_SECRET`は32 UTF-8 byte以上の互いに異なるCloudflare secretとし、Wrangler設定やGitへ保存しない。Workerはrequest header、response body、raw secretを通常logへ出力しない。認証失敗はPlayerやCredentialの存在状態に関係なく同じ`401 UNAUTHORIZED`を返す。

## Registration idempotency

Windows appは初回送信前に256 bit乱数のregistration request IDを生成し、DPAPI CurrentUserで保護してからmetadataへ保存し、`Idempotency-Key`として送信する。request ID単体で同じApp Credentialを再取得できるためsecretとして扱う。Workerはその値自体をD1へ保存せず、`REGISTRATION_SECRET`によるdigestをprimary keyとして保存する。

初回登録はPlayer、Credential、registration requestをD1 batch transactionで作成する。同じrequest IDの再送では保存済みPlayerとCredential IDを取得し、決定的に同じCredential secretを再構成して同じ応答を返す。並行requestのunique競合はbatch全体をrollbackし、既存registrationを再読込する。clientがresponseを受信できなかった場合も新しいPlayerを追加しない。

## API

すべてHTTPSで利用する。

| Method | Route | 契約 |
| --- | --- | --- |
| `POST` | `/api/v1/players/register` | `Idempotency-Key`と任意の`display_name`からPlayerとApp Credentialを作成または再取得 |
| `GET` | `/api/v1/me` | Bearer Credentialからcurrent Playerを取得 |
| `PATCH` | `/api/v1/me` | `display_name`だけを更新し、内部IDと`public_player_id`は維持 |
| `DELETE` | `/api/v1/me` | 認証済みPlayerを削除し、外部キーcascadeでCredentialとregistration記録も削除 |

認証済み応答は`public_player_id`、`display_name`、`created_at`、`updated_at`を返す。内部`player_id`はclientへ返さない。

## Windows secure storageとidentity state

非秘密の`public_player_id`、`display_name`、identity stateは既存設定pathと同じdirectoryの`web-player-identity.json`へ保存する。未完了registration request IDは同じJSON内のDPAPI CurrentUser保護blob、Credentialは`web-player-credential.bin`へDPAPI CurrentUser保護blobとして保存する。用途ごとに異なる追加entropyを使い、復号できるのは同じWindowsユーザーcontextである。metadata JSON、正式個人スコアDB、Release logにはraw secretを保存しない。

| state | local条件 | request失敗時の遷移 |
| --- | --- | --- |
| `UNREGISTERED` | `public_player_id`と利用可能なCredentialがない | registrationのNetwork Error / 5xxでは保存済みrequest IDを維持 |
| `REGISTERED` | `public_player_id`とDPAPI保護Credentialがある | 401/403だけ`AUTH_INVALID`へ遷移 |
| `AUTH_INVALID` | local Credentialはあるがserverに拒否された | Network Error / 5xxでは維持し、自動registrationしない |

認証済みoperationのNetwork Error、timeout、5xxは`NetworkError`または`ServerError`として返し、Credential、`public_player_id`、stateを変更しない。Player削除の2xx成功時だけlocal metadataとCredential fileを削除して`UNREGISTERED`へ戻す。認証失敗を受けたserviceはregistration APIを自動で呼ばない。

`AUTH_INVALID`から新しいPlayerを登録する場合は、ユーザーの明示操作に対応する`ForgetInvalidIdentity`だけがlocal identityを破棄して`UNREGISTERED`へ戻す。`RegisterAsync`は`AUTH_INVALID`を直接受け付けない。local identity削除はmetadataを先に削除し、その後Credential fileを削除する。Credential file削除が中断しても、次回読込では残存blobをidentityとして扱わず`UNREGISTERED`へ回復する。

## 通常機能との接続境界

Web Best同期は同じ`WebPlayerIdentityService`境界を明示的に注入して利用し、独自CredentialやPlayer IDを追加しない。設定保存で同期ONを確定した時点に`UNREGISTERED`の場合だけ、設定画面の公開プレイヤー名で既存registrationを開始する。登録済みPlayerの公開プレイヤー名は同じ設定保存操作から`PATCH /api/v1/me`で更新する。`AUTH_INVALID`や同期失敗からregistrationを自動実行しない。既存の監視、画像認識、正式保存、履歴、Personal ProgressはWeb identityの成否に依存しない。
