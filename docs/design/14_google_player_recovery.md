# Google Account連携と公開Player引き継ぎ設計

Issue [#210](https://github.com/tts1374/ddrgp_scorelog/issues/210)の後続実装契約。2026-10-02時点では未実装。本変更は仕様整理であり、OAuth設定変更・機能実装・deployを含まない。後続は[Issue #213](https://github.com/tts1374/ddrgp_scorelog/issues/213)。設計をレビューしてrepositoryへ取り込んだ後に実装する。

identityは[11](11_web_player_identity.md)、現行同期は[12](12_web_best_sync.md)、公開閲覧は[13](13_web_player_data.md)。Web管理は[15](15_web_my_profile.md)、追加・改善同期は[16](16_web_historical_best.md)、現在のdecisionは[ADR 0012](../adr/0012-purpose-bound-google-identity-confirmation.md)を正本とする。

## 現行実装との照合

基準はmain `697de5d6e0a016a58932b2afca6cc52bbb041aea`と完了済み#203 / #204 / #205。#143のPhase 3 Account/Login対象外と分離し、#205へ要件を追加しない。

| 既存契約 | 後続との接続 |
|---|---|
| `players`と`player_credentials`、Player : Credential = 1:N | Google identityを別tableに追加。既存Playerへ同形式の新App Credentialを発行 |
| [identity service](../../app/src/DDRGpScoreViewer/WebIdentity/WebPlayerIdentityService.cs)と[DPAPI store](../../app/src/DDRGpScoreViewer/WebIdentity/WebPlayerIdentityStore.cs) | CurrentUser保護を維持。正式DB/backupへ認証情報を含めない |
| 未登録の同期ON→自動登録 | 未登録では単一の「アカウントを作成・引き継ぐ」だけ。Webの「Googleで続ける」から認証後に作成/引き継ぎを確認する |
| [同期coordinator](../../app/src/DDRGpScoreViewer/WebBestSync/WebBestSyncCoordinator.cs)の再ON/復元→snapshot | 通常は16のmerge。snapshotは本人の別操作による明示置換 |
| [snapshot commit](../../web/identity-api/src/best-sync.ts)のatomic replace、空集合有効 | 意味を維持し、全Playerで置換確認許可を要求 |
| [backup restore](../../app/src/DDRGpScoreViewer/Data/PersonalScoreDataBackupService.cs)の`manifest` | Local Bestへ含むが、Web同期対象へ昇格しない |
| #205の公開URLと認証不要Public API | Player ID/URL・閲覧条件を維持。登録以外の認証/activationでBestを変更しない |

未リリースのため旧版・Google未連携利用者向け互換導線は設けない。開発用の既存Playerをmigrationで削除・再作成せず、既存Credentialによる同じPlayerへの連携は維持する。

## 不変条件

- `player_id` / `public_player_id` / 公開URLは不変。Playerと認証手段を分離する。
- Google identityは検証済みcanonical issuer＋case-sensitive `sub`。Player : Google identity = 1:1。email/公開名/URLを所有証明にしない。
- サービスの新規登録・Web管理・引き継ぎはGoogle認証必須。公開閲覧は不要。
- 既存PlayerへのGoogle連携は、そのPlayerの有効App Credential＋Google認証＋本人確認で行う。
- 公開ユーザーの引き継ぎとLocal履歴移行は別責務。Web/Local不一致は正常。Web→Local復元は行わない。
- login失敗、Credential紛失、未登録Google、削除済みPlayerから新Playerを自動作成しない。新規登録の明示確認だけが作成を許可する。

## 入口と登録判断

未登録アプリには「アカウントを作成・引き継ぐ」を一つだけ表示する。押下でApp-Web transactionを開始し、標準ブラウザで`/my/app-connect`を開く。Webには「Googleで続ける」を一つ表示する。Googleアカウントと操作意図を認証後に確認し、登録/引き継ぎの最終確認を行う。

| Web操作と認証結果 | 結果 |
|---|---|
| Googleで続ける＋未登録Google＋開始Playerなし | Google email、既定公開名`Player`、アプリ照合を示し「このアカウントを作成する」でPlayer・Google identity・pending App Credentialをatomic作成 |
| Googleで続ける＋登録済みGoogle | 同じPlayerの公開名/URLを確認して「このPCへ引き継ぐ」を承認 |
| 有効な未連携PlayerのAppから開始 | 対象を開始Playerに固定。「現在の公開ユーザーにGoogleを連携」で追加。Player/現在Credentialを維持 |
| 有効な連携済みPlayerのAppから開始 | 同じGoogleならプロフィールへ案内。Credential更新は別の引き継ぎ確認が必要 |
| 有効な別local Player、Playerの別Google、別Playerへ連携済みGoogle | 409競合。上書き/移動/Player mergeなし。他Playerの情報を開示しない |

Webの単一入口は空bodyで開始し、`intent = null`のApp transactionと操作専用OAuth `login-connect`をbindingする。callbackでGoogle identityを検証後、未登録は`register`、登録済みは`login`へ確定し、対応する操作proofと確認画面へ進む。OAuth callbackだけではPlayerやCredentialを作成しない。明示intentを指定したrequestはその操作に固定し、不一致から他操作へ自動変更しない。Google選び直しでは対象と確認を破棄する。Webの通常ログインだけではApp Credentialを発行しない。アプリ開始proofのないWeb画面は登録ボタンを実行できず、アプリから開始する手順を案内する。

読める旧公開IDがあるAUTH_INVALID/復号不能アプリは、別公開IDの作成・引き継ぎを拒否する。公開IDは誤操作防止だけに使い権限にはしない。認証失敗から新規登録へfallbackしない。既存Playerへ追加する操作は有効App Credentialで権限を再確認する。

AUTH_INVALID/復号不能からの本人による再ログインは、失効/読めないBearerを送らずに`connect`を開始し、読める旧公開IDを`expected_public_player_id`として開始transactionへ固定する。serverがGoogle由来の対象と一致を確認したときだけ新Credentialを発行する。この条件を持つtransactionでは未登録Googleからの新規作成を拒否する。このIDだけで権限を認めず、既存PlayerへのGoogle追加にも使わない。REGISTEREDからの開始で401になった場合はAUTH_INVALIDを表示して一度終了し、本人の再試行でこの経路へ進む。serverが受けた無効Bearerを無認証登録へ切り替えない。

アカウント全削除後など同じPlayerへ戻れない場合は、既存`ForgetInvalidIdentity`の責務を継承する「このPCの連携情報を削除」をAUTH_INVALID/復号不能状態だけに設ける。旧公開URLと「このPCの認証情報だけを消します。Webの公開ユーザー・公開BestとPC内のスコア履歴は削除しません」を示し、本人確認後にlocal metadata/Credential/未完了認証を破棄して同期OFF＋UNREGISTEREDへ戻す。削除失敗は未登録成功として扱わない。新規登録はその後の別の本人操作だけ。有効なREGISTEREDのPlayer switch導線にはしない。

## キャンセル・選び直し・完了確認

- 認証前にアプリ接続transaction、開始Player、browser、戻り先を固定する。操作未確定の単一入口は検証済みGoogleから作成/引き継ぎを決め、最終確認前にserverへ固定する。Google cancel/失敗時は「ログインを中止しました/失敗しました」と、開始した操作に戻る導線を表示する。通常プロフィールloginからならプロフィール、App接続からなら当該接続画面へ戻す。
- callbackのstate/browser bindingが不正なら処理を拒否し、未検証のqueryから戻り先を採用しない。安全なエラー画面からAppで再開始する手順を示す。
- Google選び直しは確定前だけ。App transaction/proofと開始Playerを維持し、Google session/対象/操作proof/未選択確認だけを破棄してGoogle identityを再確認する。登録→login等の意図変更もserverへ再固定し、当該purpose-bound OAuthをやり直す。別transaction/別Playerへの無断遷移なし。
- Appの取消/Google cancelでtransactionを終了した場合と10分期限切れは、本人がAppから新transactionを開始する。同じ終了transactionを再使用しない。Web承認/登録が既に確定していたら取消済みと表示せず確定結果を確認する。
- Webの「アプリ保存待ち」は`GET /api/v1/auth/app-authorizations/{id}/status`で、承認済みGoogle session＋browser bindingを検証して5秒間隔/元の10分TTLまで確認する。登録/承認済み、activation待機、完了、期限/失敗だけを返し、Credential/request secretは返さない。App用`result`のproof境界を迂回しない。
- Webはserverのactivation成功を確認してから「このPCへの連携が完了」を表示する。承認成功だけで完了扱いにしない。session失効/期限/結果不明は「アプリで状態を確認してください」と表示し、Appが保存済みCredentialで結果確認する。Webから別Credential発行を自動開始しない。

## 利用者操作と結果

| シナリオ | 利用者操作 | identity / Credential | 公開URL / Best | Localデータ | 同期 |
|---|---|---|---|---|---|
| 未登録→新規作成 | 単一ボタン→WebのGoogleで続ける→Google選択→作成確認→App保存/activation | 新Player＋Google、active App Credential | 新URL、初期Best空 | 維持 | activation成功後にONとなりmergeを開始。送信開始をWeb最終確認で示す |
| 未連携既存Player→Google連携 | 設定のWeb連携→Google選択→現在Playerへ連携確認 | 同じPlayerへ追加、現在Credential維持 | 同じURL/Best | 維持 | 元のON/OFF維持 |
| 利用開始後→後日連携 | Google解除後等に設定から同じ連携操作 | 同じPlayer/現在Credential | 維持 | 維持 | OFFでも明示認証可、自動ONなし |
| 新PCの空DB→Googleログイン | 単一ボタン→WebのGoogleで続ける→Google選択→既存Player確認→引き継ぎ→App保存/activation | 同じPlayerに新Credential、activationで他App Credential失効 | 同じURL/全Bestを維持 | 空のまま | activation成功後にON。空集合no-op、新capture追加可 |
| 正式backup復元後→ログイン | 復元と上記引き継ぎを別操作 | 同じPlayer、新Credential | 維持 | manifest履歴を保持 | 再開可。manifestだけなら同期対象0件no-op |
| 不完全/十分なcapture集合 | 本人が同期再開 | 同じPlayer/active Credential | 新譜面・改善fieldだけ追加、既存Best保持 | 維持 | 完全性に依存せず可 |
| Google認証cancel/失敗、最終承認前cancel | Web/アプリで中止、本人が再試行 | 操作前の状態を維持。未登録は未登録 | 維持 | 維持 | 自動作成/ONなし |
| Web登録確定後の受領/保存失敗 | エラー表示、同transaction再照会 | 作成済みPlayer＋Googleは保持、pending Credentialは通常API不可 | 作成済みURL/空Best保持 | 維持 | 新PC不可。期限後は本人loginで同じPlayerへ新transaction |
| 引き継ぎ承認後の受領/保存失敗 | 同transaction再照会 | 新Credentialは未activation、旧PC有効 | 維持 | 維持 | 新PC不可 |
| activation応答喪失 | 保存済みCredentialで結果確認 | 成功なら新Credentialのみ有効、再発行/失効取消なし | 維持 | 維持 | 結果確定まで停止。activation成功確認後にON |

「アカウントを作成する」は公開ユーザーの作成、「このPCへ引き継ぐ」は管理権限の移行。どちらもスコア履歴を移さない。新規登録確定後の遅いcancelは登録を巻き戻さず、確定結果を表示する。登録完了とCredential activation完了を別に示す。

## Web認証とアプリへの受け渡し

Googleは15のWeb application clientを使う。Authorization Codeは固定HTTPS callbackでWorkerが交換・検証する。WindowsはGoogleのcode/tokenを受け取らず、Google側state/nonce/browser bindingと以下のApp proofを分離する。Desktop OAuth client、loopback/custom schemeは使わない。

1. アプリは独立128 bit以上のtransaction IDと256 bit以上の`app authorization request secret`を生成し、開始API送信前に用途別DPAPI CurrentUserで保存する。
2. `connect`は登録/ログイン共通。現在の有効Bearerがあれば対象Playerを固定し、なければGoogle認証後に対象を決める。送信されたBearerが無効なら401で拒否し、Bearerなしの新規登録へfallbackしない。`unlink`は有効Bearer必須。serverはpurpose/開始Player・Credentialを固定しsecret digestを保存。retryは同じID/proof/purposeだけ。
3. serverが返す同じoriginの`/my/app-connect?request={id}`を開く。URLにはIDだけ。ID単体で受領・承認不可、App Credential/request secretをURLへ出さない。
4. Webは単一入口からGoogle認証を要求し、登録状況から作成/引き継ぎを確定して、検証済みemail、対象の公開名/URL、操作結果を表示する。アプリとWebの短い照合コードの一致を本人が確認する。コードは表示用で所有証明ではない。
5. 承認はWeb session＋Origin＋CSRF＋当該purpose-bound OAuthで発行した独立操作proof＋本人の最終確認を要求する。[15の操作許可](15_web_my_profile.md#google-identity再確認と操作許可)に従い、検証済みGoogle identityとApp transaction/目的/対象/browserを結び、通常sessionだけでは承認しない。操作proofの10分TTLはserver成立時刻から数え、App transactionの開始から10分TTLも維持する。既存Player連携/解除は開始Credentialの有効性を再検証。新規登録は上記atomic作成、引き継ぎは既存Playerへpending Credentialを発行し、proof消費と結果確定をatomicに行う。
6. アプリはHTTPSでproof付き結果を5秒間隔、開始から最大10分までpollする。429はRetry-Afterを尊重。Credentialをbrowser response/cookie/localStorageへ渡さない。
7. 新CredentialをDPAPI保存・再読込検証後にactivationする。成功確認後だけ通常identityへ採用する。途中起動は結果確定まで送信しない。

Google callbackは固定Web route。戻り先は保存purpose/transactionからプロフィールまたは当該確認画面に限定し、任意redirectを禁止する。Web clientのsecretをWindowsへ含めない。

transaction TTLは開始から10分。確定登録/link/unlink/activationとcancel/expiryを排他的に処理する。確定後の遅いcancelは確定結果を返す。未activation Credentialは期限後に利用不可。成功済みactivationはTTL後も保存済みCredentialで結果確認でき、古いretryで失効済みCredentialを復活させない。

## Credential activationと旧PC

- 既存`type = app`、256 bit以上のopaque secret、server digest、DPAPI CurrentUserを維持。Google tokenをBearerへ流用しない。
- 新Credentialは`activation_state = pending`。通常API不可、当該結果/activationだけに使える。既存Credential migrationはactiveを既定とする。
- 同transaction retryは同Player/同Credential。既存registrationと用途を分離したWorker secretとrequest secret/transaction ID/Credential IDからsecretを再構成しD1にraw secretを置かない。
- activationは新Credential proof＋request secret＋確定Web承認を検証し、active化、他App Credential全失効、他Credentialの未完了snapshot無効化を同じtransactionで行う。Best/日時・Local DBは変更しない。
- 保存/activation未成功なら旧PC維持。成功後は旧PC401→AUTH_INVALID。最初の同期を失効条件にしない。serverは端末保存を直接検証できないため対応Appが保存/再読込後だけactivationする。
- Best write transaction内で未失効を再検証し、middleware通過済みの旧requestもactivation後は拒否する。
- activation後のDB復元/再起動/同期停止・再開では再発行しない。1:N schemaを維持し、今回の操作は1台への権限移行。端末一覧/個別失効/同時複数PC同期UIは含めない。

## 連携解除とアカウント削除

同じPlayer＋同じGoogleの連携は冪等、別Google/別Playerの競合は409。並行unique競合、開始Credential失効、Player削除は部分更新なしで拒否する。

AppのWeb連携から開始した「Google連携を解除」のWeb確認画面は15のpurpose-bound OAuthによる同Google identity再確認/短期操作proof＋そのPlayerの有効App proof＋最終確認を要求する。手元アプリのWeb連携から`unlink` transactionを開始する。Google identityと当該canonical issuer/subの全Web session（別ブラウザ/別端末を含む）、そのidentity/sessionにbindingされた認証待機・操作proof・未完了承認/削除確認をatomic失効する。全sessionのemailも削除し、後日同Googleが同じ/別Playerへ連携しても旧sessionへ権限を与えない。並行する旧sessionの管理更新はtransaction内の再検証で拒否する。既にactiveなApp Credential/URL/Best/Localは維持するが、失効した未完了承認からのpending Credential activationは許可しない。確認文は「Googleでのログイン・PC引き継ぎができなくなります。現在のアプリから再連携できます」。応答喪失はWeb sessionではなくApp secret proof付き同transaction確定結果で確認する。

解除後のGoogle再連携は有効Appから開始し、同じPlayerを固定する。初回登録のGoogle必須と、本人操作で後日解除したPlayerの継続利用を区別する。Webログアウトは解除ではない。Googleと全App Credentialの喪失の救済は保証しない。

アカウント全削除は15の同Google identity再確認/削除confirmation proof付きWeb画面だけで行う。公開記録だけの削除もWebへ集約し、16の送信停止→Web削除→送信再開の手順で現在PCの記録から作り直す。アカウント全削除とは目的と操作proofを分離する。

## API / 永続化 / Windows状態

| Route | 契約 |
|---|---|
| `POST /api/v1/auth/app-authorizations` | purpose=`connect`/`unlink`、ID/request secret固定。connectは現在Bearerがあれば固定、旧公開IDが読める場合はexpected_public_player_idも固定。unlinkはBearer必須 |
| `POST /api/v1/auth/app-authorizations/{id}/approve` | Web session/操作専用OAuth proof/Origin/CSRF/browser binding＋最終確認。server保存の登録/ログイン意図と対象、開始Credentialを再検証しproofを消費 |
| `POST /api/v1/auth/app-authorizations/{id}/result` | App secret proofで確定結果取得。pending Credentialは開始Appだけへ返す |
| `GET /api/v1/auth/app-authorizations/{id}/status` | 承認済みGoogle session＋browser bindingでWeb完了待ちを確認。結果状態だけ、Credential/secretなし |
| `POST /api/v1/auth/app-authorizations/{id}/activate` | 新規発行したpending Credential＋secretでactivate。冪等 |
| `POST /api/v1/auth/app-authorizations/{id}/cancel` | App proofまたはbinding済みWeb session/Origin/CSRF。未確定だけcancel |
| 既存`GET /api/v1/me` | 連携有無とactivation state追加。email/sub/内部ID/他Credential一覧なし |
| 既存`POST /api/v1/players/register` | 410 `REGISTRATION_MOVED_TO_WEB`、新規作成なし |

D1はGoogle issuer/sub/Player unique、15の短期session/認証待機/操作proof digest・binding・server成立時刻・expiry・消費状態、App authorizationのpurpose/登録・ログイン意図/開始Player・Credential/expected_public_player_id/secret digest/承認Google/expiry/確定結果、Credential activation stateを保存する。新規登録のPlayer・identity・pending Credential・操作proof消費・確定結果は一つのtransaction。raw Google token/code/secretは保存しない。emailは15の短期session以外へ保存しない。

WindowsはID/secret/pending Credential/結果確認状態を用途別DPAPIで保護し正式DB/backupへ含めない。UNREGISTERED/REGISTERED/AUTH_INVALIDを維持し、独立状態は`AppAuthorizationPending`と`CredentialActivationPending`。既存Playerのlink/unlink中は既存同期維持、新Credential発行・activation待機は新PC送信禁止。新規登録・引き継ぎのactivation成功後はREGISTERED＋同期ONとなり、このPCのcapture集合をmergeで送信する。Webの最終確認で送信開始を示す。承認だけ、DPAPI保存失敗、activation結果不明では送信を開始しない。link/unlinkだけなら元の同期状態を維持する。

| アプリ状態 | 主操作と結果 |
|---|---|
| UNREGISTERED | 「アカウントを作成・引き継ぐ」のみ。同期checkbox/自動登録なし |
| 認証・activation待機 | 照合コード/進行/中止または結果確認を表示。重複開始と新PC同期を禁止 |
| REGISTERED＋同期OFF | ステータス「Webへの送信を停止中」、「Webへの送信を再開する」「公開ページを開く」。今すぐ同期は無効 |
| REGISTERED＋同期ON | ステータスと「Webへの送信を止める」「公開ページを開く」「今すぐWebに送る」。送信中は重複同期を禁止 |
| AUTH_INVALID | 権限失効/削除等を表示。失効Bearerなしの「アカウントを作成・引き継ぐ」で同じPlayerへ明示再ログイン。戻れない場合は確認付き「このPCの連携情報を削除」。自動作成なし |

停止/再開はボタン押下で直ちに設定へ反映し、別の設定保存を待たない。停止はこのPCの送信停止であり、Google解除/Credential失効/公開削除ではない。OFFでも本人のプロフィール/認証操作は許す。Google/session失敗はAppのAUTH_INVALIDに変換せず、真のBearer401だけAUTH_INVALID。network/timeout/5xx/契約409をCredential失効として扱わない。

## Scope / Non-scope

Scope: 単一App入口とWeb新規登録/ログイン、明示登録、同じPlayerへの連携/解除、App専用承認/受領/独立activation、DPAPI失敗回復、同期停止/再開ボタン、16の歴代Best同期、15のプロフィール/アカウント削除、wireframe、対象migration/test/手動確認。

Non-scope: 本番OAuth設定/D1変更/deploy、Google loginだけの自動Player作成、Player merge/switch、複数Google identity、端末管理/同時PC同期、Web→Local復元、backup形式変更/manifest昇格、表示名/URLだけの復旧、画像認識/正式保存/master refactor。Free開始、プラン変更なし。

## Acceptance criteria / Required tests

| ID | 条件と検証 |
|---|---|
| T1 | 新規登録はGoogle＋App proof＋明示確認。Player/identity/pending Credential作成atomic、同transaction retry同一結果。登録済みGoogleで二重作成なし、login未登録/cancel/失敗から自動作成なし |
| T2 | 既存Credential＋Google＋確認で同じPlayerへlink、同一冪等。別Google/別Player/並行unique/失効/削除拒否。ID/URL/Best/Local維持 |
| T3 | Web state/nonce/署名/issuer/aud/expiry/browser binding、azpはpresent時だけ一致検証（欠落を許可）。登録/link/引き継ぎ/unlinkは独立purpose-bound OAuth＋App proof＋操作proof/最終確認でのみ承認。通常session/通常login callback/IDだけでは不可。auth_time欠落/古い値でも正常フローは成功。操作proofのserver成立から10分TTL/未消費/目的・identity・Player・session・browser・App transaction bindingを検証し、App開始TTLも延長しない。別目的/別ブラウザ/replay/期限/任意redirect拒否、proof消費と操作結果はatomic、retryは同結果のみ |
| T4 | 別local Player/旧公開ID不一致で上書きなし。AUTH_INVALIDは失効Bearerなしで同Playerへ戻れ、linkには使えない。確認付きlocal情報削除だけで旧ID制約解除、Web/Local履歴不変。有効REGISTEREDのswitchにはしない |
| T5 | 受領/DPAPI失敗・途中終了・cancel/expiryで旧PC維持、新PC書込み不可。確定登録は保持。activation応答喪失/再起動から保存済みCredentialで結果回復。Webは承認とactivation完了を区別、statusで秘密を返さない |
| T6 | activation/他Credential失効/staging無効化がatomic、Best不変。並行activation/旧PC in-flight write/古いretryで失効取消なし。初回同期前に旧PC停止 |
| T7 | 空DB/manifest復元/不完全captureでも本人再開後merge可、既存Best保持。16のH1〜H6を検証 |
| T8 | unlinkの同Google identity再確認/操作proof＋有効App proof＋確認、App proof付き確定結果retry、同issuer/subの全session/email/認証待機・操作proof・未完了承認/削除確認のatomic失効を検証。別ブラウザの旧session/並行更新/未完了activationを拒否、再連携で旧sessionを復活させず、他Google session/active App Credential/URL/Best/Localは維持。同じPlayerへの再連携/別Googleを検証 |
| T9 | 単一App入口→Web新規登録/ログイン→activation成功後ON。承認/DPAPI失敗/結果不明では送信しない。Web最終確認に送信開始を表示。停止/再開は即反映、OFF中今すぐ同期無効/自動通信なし。選び直しは目的/開始Appを維持、cancel/失敗は正しい入口へ戻り、終了/timeout後は本人による新transaction。自動fallbackなし |
| T10 | 匿名登録/Bearer名前更新/Bearerアカウント削除拒否、秘密/email/subの公開/log混入なし、DPAPI/URL/認証不要閲覧/正式DB/backup維持。Web障害でも正式保存継続 |

手動確認はテストGoogle Web client、開発Worker/D1、別Windowsユーザー/PCで行う。新規登録/登録済みGoogleの再登録、login未登録、旧PC→新PC空DB→保存/activation成功→旧PC401→同期ON→新capture公開、manifest復元、DPAPI失敗、ブラウザ閉鎖、Google違い、解除/再連携、Web削除を確認。実装時にrepository既定CI、15のP1〜P9、16のH1〜H6が必要。現在は文書/wireframe確認のみ。

## セキュリティと失敗時

本番API/Google通信はHTTPS、開発origin/client/D1/DPAPI保存先は分離。Web sessionとApp Credentialを相互流用しない。callback query/Bearer/request secret/token/email/subをlog・例外・telemetryへ出さず認証responseはno-store。Google/JWKS/D1/DPAPI障害を成功扱いにしない。

Googleまたは有効App Credentialの奪取は管理権限奪取になり得る。DPAPIは同じWindowsユーザーの悪意あるプログラムへの保護ではない。Web承認は手元Appとの照合を要求し、第三者のURLだけで承認させない。

## Issue #210との対応

同じPlayer/URLの引き継ぎ、登録/連携/ログインの操作・失敗、認証/API/永続化、履歴移行との分離、Windows状態・同期境界、security/testを本書・15・16へ固定。0006のidentity/DPAPIを維持し、0007以降の現在の拡張を0012へ記録。#213へ実装を分離する。初期full snapshot前提は本人判断で歴代Best蓄積へ拡張し、空snapshotのatomic置換は明示操作へ維持する。
