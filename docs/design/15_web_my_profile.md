# Webマイプロフィール設計

Issue [#210](https://github.com/tts1374/ddrgp_scorelog/issues/210)の追加決定を、後続実装[Issue #213](https://github.com/tts1374/ddrgp_scorelog/issues/213)の契約として固定する。2026-10-02時点では未実装であり、本変更は設計のみ。

Google identityと既存Playerの関係、App-Web承認、Windows App Credential発行は[Google Account / Player recovery](14_google_player_recovery.md)、自己歴代Bestの蓄積は[16](16_web_historical_best.md)を正本とする。公開ページは[Web Player Data](13_web_player_data.md)、現在の認証・同期・Web管理decisionは[ADR 0012](../adr/0012-purpose-bound-google-identity-confirmation.md)を参照する。画面状態は[wireframe](../wireframe/my-profile-mock.html)で確認する。

## 操作と責務

- Web管理・引き継ぎの認証はGoogleログインを必須とする。`/player/{public_player_id}`とPublic APIは従来どおりログイン不要。
- Webに`/my/profile`「マイプロフィール」を追加し、公開名、本人向けGoogleメールアドレス、連携状態、ログアウト/別アカウントでログイン、連携/解除の導線を表示する。名前編集は既存`players.display_name`だけ。公開ID/URLは変更しない。
- 新規登録は14のApp入口→Web新規登録→Google認証→明示確認だけ。通常Webログインで未登録Googleを自動登録しない。解除後の有効App Credentialによる同期は継続できる。
- Windows未登録画面は単一の「アカウントを作成・引き継ぐ」。完了後は同期ステータス、停止/再開、公開ページ、今すぐ同期、Web連携/プロフィール導線を持つ。名前はread-onlyで、設定保存や同期再開で名前を送信しない。
- Webの新規作成は既定名`Player`。既存Playerの名前は維持し、Googleの名前/emailから自動生成しない。本人がマイプロフィールで任意の公開名を保存する。
- 公開名は既存のtrim後1〜64文字の検証を継承し、空白のみ/上限超過は保存しない。Google identity、Credential、公開Best、正式個人スコアDBは名前編集で変更しない。

| 状態・操作 | 画面と結果 |
|---|---|
| 未ログインでマイプロフィールを開く | Googleログインを案内し、プロフィール取得/更新を拒否。公開ページは閲覧できる |
| Googleログイン済み、Player未登録/未連携 | 本人email、アカウントなし、アプリの単一入口からWeb新規登録する手順、ログアウト/選び直しを表示。名前編集不可。loginだけでは作成しない |
| 既存Playerへ連携済み | 本人email、現在の公開名、編集欄、保存、read-only公開URL、公開ページ、連携解除、ログアウト/選び直しを表示 |
| 公開名を変更して保存 | 同じPlayerのdisplay_nameだけ更新し、保存結果と新しい名前を表示。公開ページの名前へ反映 |
| 入力不正・通信失敗 | 入力とエラーを表示し、既存名を維持。通信結果不明時は現在値を取得し、保存済みか確認する |
| セッション期限切れ・別Googleアカウントへログイン | 編集を止め、再ログイン後にGoogle identityから対象Playerを再解決。未保存入力を別Playerへ自動送信しない |
| Player削除・連携なし | 既存Playerの編集を拒否。ログインだけでPlayerを再作成しない |
| Windowsからプロフィールを開く | 公開IDやApp Credentialを管理権限としてURLへ渡さず、同じoriginのマイプロフィールを標準ブラウザで開く。選択アカウントの現在の公開名/URLを本人が確認する |

App未登録/同期OFF/ON、Web新規登録/ログインの入口、登録確認、登録済みGoogle/未登録Google、名前保存成功/失敗、session期限切れ、Google認証cancel、App-Web承認/アプリ保存待ち/activation完了、解除、アカウント削除確認/完了をwireframeに含める。Googleのemailは本人確認用で公開名欄と分け、公開page/API/通常Windows設定へ出さない。「別のアカウントでログイン」は現Web sessionをログアウトしてGoogle選択をやり直す操作で、既存連携の変更ではない。未保存入力は切替時に破棄する。

連携/解除は14の有効App proof付きtransactionで行う。解除導線は手元アプリのWeb連携を案内し、操作専用のGoogle identity再確認とApp確認がそろった画面で最終確認する。アカウント全削除はWebへ集約する。公開Best全削除は別操作として16のWindows操作を維持する。自己紹介/アバター/公開URL変更/端末管理は含めない。

## Webログイン / session / API

Googleの[Web Server Authorization Code](https://developers.google.com/identity/protocols/oauth2/web-server)と[OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect)でGoogle identityを検証し、Workerが本サービス専用sessionを発行する。WindowsからのWeb連携も同じWeb clientを使い、Google認証と14のアプリproofを別に検証する。client IDは環境別に固定する。

- callbackは本番originの固定HTTPS route。開発用client/originは本番と分離。戻り先はserver保存purposeから`/my/profile`、14の当該App-Web確認画面、または当該アカウント削除確認画面へ限定し、任意redirectを許可しない。Google cancel/失敗も開始した操作に戻す。state不正は安全なエラー画面で拒否。
- Workerが開始時に独立のstate/nonceを生成し、短期browser cookieとのbindingでlogin CSRFとcallback replayを拒否する。state/nonce照合と下記のGoogle token検証は14の境界を共有し、認証待機は10分。Google client secretはWorker secretへ保存する。
- scopeは`openid email`、offline accessなし。code、Google ID/access/refresh tokenは検証後に破棄し、D1/session/cookie/Windowsへ永続化しない。
- JWKS署名と許可algorithm、Google issuerの許可された2表現（保存キーは`https://accounts.google.com`）、exp/iat、nonce、subを検証する。`aud`は当該環境の単一Web client IDとの一致を必須とし、別client/追加audienceを拒否する。`azp`は存在する場合だけ同じclient IDとの一致を検証し、不一致/不正な型を拒否する。`azp`の欠落だけでは拒否しない。emailは検証済みclaimから本人表示だけに使う。
- sessionは256 bit以上のopaque乱数。D1にはdigest、検証済みissuer/sub、本人表示用の検証済みemail、補助情報として取得・検証できた`auth_time`（未取得/不正な値はnull）、serverでの作成/expiryを保持する。`iat`/callback完了時刻/session作成時刻を`auth_time`として保存しない。emailは短期sessionだけ、期限/ログアウト/解除で削除しidentity判定に使わない。解除は下記のとおり同issuer/subの全sessionを対象とする。24時間absolute TTL、自動延長なし。Google identityから毎回既存Playerを解決する。未連携Googleにもsessionを発行してよいが、loginだけでPlayer/永続紐付け/長期Credentialは作らない。
- 本番cookieは`__Host-`prefix、Secure、HttpOnly、SameSite=Lax、Path=/、Domainなし。local HTTP開発だけ別cookie名・別保存先を使う。本番cookieを開発へ持ち込まない。Google tokenやApp Credentialをbrowser localStorageへ保存しない。
- profile更新とログアウトはsame-origin Origin検証とsessionにbindingしたCSRF tokenを必須とする。CORSによる外部originへの管理API開放を行わない。認証済み画面/APIはno-store、callbackのcode/state等を他assetへ渡さず、認証queryを除いて保存purposeの画面へ移動する。App transaction IDはGoogle認証queryと区別する。
- ログアウトはWeb sessionだけ失効する。Google連携、Windows Credential、同期状態、公開Best、Local履歴は維持する。session失効はWindowsの`AUTH_INVALID`に転用しない。

### Google identity再確認と操作許可

通常プロフィール取得/編集はWeb session＋Origin/CSRFで行う。登録/link/PC引き継ぎ/unlink/アカウント全削除は、通常loginとは独立したpurpose-bound Google OAuth transactionでidentityを再確認する。開始時にpurpose、対象Player（未登録は対象未確定）、開始session、browser binding、App transaction/登録・ログイン意図をserverへ固定し、独立state/nonceでAuthorization Code flowを行う。Google連携済みPlayerの操作は開始時のissuer/sub/Playerとcallback結果の一致を要求する。未連携既存PlayerへのlinkはApp CredentialでPlayerを固定し、Google identityはcallback結果をunique検証して追加する。未登録/新PCで対象未確定の場合だけ検証済みGoogleから対象を決め、14の旧公開ID制約も確認する。通常loginのcallbackを操作専用callback結果へ転用しない。

操作専用の認証requestは`prompt=select_account`でアカウント選択を表示し、通常sessionの有無にかかわらず新しいOAuthを行う。アカウント選択後も本人の最終確認を要求する。Google認証済みの既存SSOを利用でき、アカウント選択そのものを追加認証の証明にしない。

callbackでstate/nonce/browser bindingと上記token検証を完了したときだけ、Workerが256 bit以上の`Web operation confirmation proof`を発行する。session未発行のApp入口では、この検証済みGoogleからsessionも作成してproofをそのsessionへbindingする。raw proofは当該browserのSecure/HttpOnly cookieへ渡し、D1にはdigest、purpose、検証済みissuer/sub、対象Player/開始session/当該browserとApp transactionへのbinding、serverの`confirmed_at`/expiry/消費状態を保存する。Cookieは本番sessionと同じ属性、用途別の名前とし、query/JavaScript/localStorageへ出さない。通常session cookie・CSRF token・transaction IDだけでは取得/代用できず、各purpose-bound OAuth完了が必要。callback replayでも別proofを再発行しない。

操作許可のTTLはサービス側で成立した`confirmed_at`から10分、自動延長なし。App authorization自体の開始から10分TTLも維持し、実行期限は両方の早い方とする。最終承認/解除ではsession＋Origin/CSRF＋操作proofと未選択からの明示確認を要求し、14のApp proof/照合を併用する。同じ更新transaction内で未失効session、identity→Player、purpose/対象/App proof、`now < expires_at`、未消費を再検証し、proof消費と操作結果確定をatomicに行う。確定済み同操作retryは同じ結果照会だけとし、別操作への再使用は拒否する。

proof期限切れ、cancel、logout/アカウント選び直し、Player/identity/開始Credentialの失効は未完了の操作許可を無効にする。Google cancel/失敗、別Google/目的/Player、binding不一致では操作許可を作らず既存データ・権限を保持する。本人による新しいpurpose-bound OAuth開始から回復し、Google選択後に未選択確認と対象表示をやり直す。

Google identity再確認はGoogle Accountへのパスワード再入力/MFA等の再認証を保証しない。既存Google SSOを利用できる。盗まれた本サービスsession cookieだけでは当該OAuth callback・独立操作proofを成立させられない境界であり、Google session/端末まで奪取された場合の追加認証を提供するものではない。

Googleの[Security Bundle](https://developers.google.com/identity/siwg/security-bundle)はGoogle Account reauth requestをサポートしないと明記し、追加claim取得にIn production/Verified/Advanced Settingsを要求する。[OIDC API Reference](https://developers.google.com/identity/openid-connect/reference)に従う通常code flowを必須経路とする。`auth_time`は取得できた場合の補助的trust signalだけで、欠落/古い値だけを理由に登録・link・引き継ぎ・unlink・削除を拒否しない。操作許可TTLはサービス時刻で判定し、Googleの`iat`/`auth_time`に依存しない。追加claim有効化/telemetry/別方式のstep-upは今回の実装要件へ追加しない。本番OAuth設定変更は行わない。

### 連携解除のsession境界

14の`unlink`はGoogle identity、当該canonical issuer/subの**全Web session**（現在/別ブラウザ/別端末/未連携表示用を含む）、そのidentity/sessionにbindingされた認証待機・操作proof・未完了承認/削除確認を一つのtransactionで削除・失効する。全sessionのemail/CSRF/identity再確認情報も削除する。他Google identityのsessionは対象外。解除済み結果のApp proof付き確認は14の確定結果から行い、失効Web sessionを再使用しない。

各管理APIは失効sessionを401で拒否し、更新transaction内でもsession有効性と現在のidentity→Player対応を再確認する。解除と並行する旧session requestを後からcommitさせない。同じGoogleを後で同じ/別Playerへ明示連携しても、解除前のsessionからプロフィール取得/変更/承認/削除はできず、新たなloginが必要。App Credential/公開URL/Best/Localの維持は14のまま。

| Method / route | 契約 |
|---|---|
| `GET /api/v1/auth/web/google/start` | 通常login、またはserver保存のApp transaction/削除目的にbindingした独立認証待機を開始。既存sessionでの機微操作開始はOrigin/CSRF付きPOSTで目的を先に固定し、GETから任意Player/purposeを採用しない |
| `GET /api/v1/auth/web/google/callback` | 固定callbackでstate/browser bindingとGoogle code交換・token検証。通常loginはsession、機微操作は必要なsessionとbinding済み操作proofを発行。保存purposeの許可画面へredirectし、callbackだけでは登録/解除/削除しない |
| `GET /api/v1/account/session` | Web session必須。本人表示email、連携有無、session-bound CSRF token。未連携でも200としてlogout/選び直し可能 |
| `GET /api/v1/account/profile` | Web session必須。紐付け済みPlayerの公開名・公開ID/URL。内部player_id、Google sub、App Credentialを返さない |
| `PATCH /api/v1/account/profile` | Web session＋Origin＋CSRF。display_nameだけを受け付け、server側でGoogle identityからPlayerを決める。任意Player指定は拒否 |
| `POST /api/v1/auth/web/logout` | Web session＋Origin＋CSRF。当該sessionだけ失効 |

未認証/失効sessionは401、profileでPlayer未連携は409 `PLAYER_NOT_LINKED`、入力不正は400。session取得は未連携でも成功し、ログアウトのCSRF取得を妨げない。Web sessionはBest API認証に使わず、14の明示App-Web承認以外ではCredentialを発行しない。

## アカウント全削除

マイプロフィールに「アカウントを削除する」を置く。対象の公開名/URLを示し「公開Best、Google連携、すべてのPCの連携権限が削除され、公開URLは利用できなくなります。PC内のスコア履歴は残ります」と説明する。同じGoogle identityの操作専用OAuth確認、Origin/CSRF、未選択の確認と最終削除ボタンを要求する。Appを失った場合でも本人Googleから削除できる。Google解除とは別操作。

`POST /api/v1/account/deletion-start`: session＋Origin/CSRFで開始Google identity/Playerと削除purposeをserverへ固定し、上記の独立OAuth待機へ進む。callbackで同じissuer/subを再確認して操作proofを発行、対象を再表示し、未選択確認と最終削除ボタンを改めて表示する。別Googleなら拒否して入力済み確認を破棄する。Google選択操作そのものを最終削除にしない。結果照会用には独立256 bit以上の未成立deletion confirmation proofを同callbackでSecure/HttpOnly cookieへ先に保持する。未成立proofではDELETE/成功結果取得を許さず、次の本人確認操作だけで認可を成立させる。

- `POST /api/v1/account/deletion-confirmations`: session＋削除用操作proof＋先に保持した結果proof＋Origin/CSRF＋checkbox/最終確認で、固定した対象Playerの削除確認を成立させる。操作proofを消費し、`deletion confirmation proof`の認可を成立させる。serverはdigest、purpose/対象/issuer/sub/session binding、serverの`confirmed_at`とそこから10分TTL、確定結果を保存する。対象指定や通常sessionだけでの成立は不可。未確定の同確認retryでは同じ確認だけを再取得し、TTLを延ばさない。確認成立の応答喪失でも先に保持した結果proofで状態を照会し、確認済みか判断してからDELETEを行う。
- `DELETE /api/v1/account`: session＋Origin/CSRFと未失効・未消費のdeletion confirmation proofが必要。同じ更新transaction内でpurpose/対象/identity/session/expiryを再検証し、proof消費、Player、Best、Google identity、全App Credential、staging、未完了App authorization、Web session/操作proofの削除、完了結果確定を行う。リクエストに任意Player指定を許さない。期限切れは新しい削除専用OAuth確認からやり直す。
- `POST /api/v1/account/deletion-confirmations/{id}/result`: 結果proofだけで当該操作の未成立/確認済み/削除完了を取得できる。sessionは削除されるため応答喪失時にも完了確認できる。IDだけでは不可。未成立/期限切れの確認をproofだけで認可へ昇格せず、削除を実行しない。
- 短期結果には完了/期限とdigestだけを残し、PlayerへのFK cascade対象にしない。email/sub/公開名を残さず、期限後に結果とcookieを破棄する。成功済み同確認retryは同じ成功、別Playerを操作しない。
- 確認前cancel/Google identity再確認失敗/削除transaction失敗は既存状態を維持。応答不明では結果確認を行い、成功を推測しない。
- 完了後はログアウト状態/削除完了表示、公開URL404。各Appは次の認証requestで401→AUTH_INVALID、ローカル履歴は保持、Playerの自動再作成なし。未登録Googleへ戻っても新規登録は別の明示操作だけ。

## App Credential APIとの接続

公開名はGoogle認証sessionでのみ変更する。既存`PATCH /api/v1/me`のBearer名更新は全Playerで409 `WEB_PROFILE_REQUIRED`、既存`DELETE /api/v1/me`のアカウント全削除は409 `WEB_ACCOUNT_REQUIRED`で拒否する。Credentialは有効なまま、Windowsは認証エラーとして扱わない。匿名登録は14の410で拒否する。

未リリースなので旧版互換を設けない。App側の自動登録/名前PATCH/アカウント削除を外し、Web管理と同期操作を分離する。利用する全Worker originで同じ新契約を適用し、旧originから匿名登録や通常置換を迂回できる状態で公開しない。本検討では配布/設定/deployを行わない。

Windowsの名前cacheはWeb由来の非秘密表示cacheとして扱う。同期ON中の既存identity確認時、および「マイプロフィールを開く」後に本人が「情報を更新」した時、`GET /api/v1/me`で最新名を取得する。同期OFF中の自動requestは停止し、明示更新だけ許す。cacheを名前更新の入力に使わない。Webで変更した名前がWindows未更新でも公開ページはserverの名前を表示する。

名前変更は`players.updated_at`へ反映し、`public_bests_updated_at`は更新しない。Google login/session/プロフィール編集では同期ON/OFFを変えず、空DB/manifest復元から自動full snapshotを送らない。新PCは14のactivation後、本人の「連携を再開する」で16の追加・改善同期を開始できる。

## 運用

Cloudflare Workers/D1はFreeから開始し、必要に応じてPaidへ移行する。確認は既存Cloudflare dashboardのrequest、CPU、D1 read/write/storageを使う。独自の課金管理・telemetry・自動プラン変更は追加しない。料金とplan選択をプロフィール利用者の画面へ出さない。本検討/実装Issueでプランを変更しない。

## Scope / Non-scope

後続#213へ追加するScope:

- Googleログイン必須のマイプロフィール、本人email・連携状態・解除導線・ログアウト/選び直し、Web session APIと公開名更新/アカウント削除API、状態別wireframe。
- Windows名前編集をread-only表示とWeb導線へ移す。Web初回既定名、既存名保持、明示cache更新、Bearer名更新/アカウント削除拒否。
- 最小D1 session/認証待機保存、Worker-first管理page routing、既存公開ページ/同期との境界、対象テストと手動確認。

Non-scopeは14を継承し、追加プロフィール項目、WebからのBest編集/同期再開、管理機能の横展開、本番OAuth設定変更/deploy/有料プラン変更を含めない。

## Acceptance criteria / Required tests

| ID | 受け入れ条件と対応するRequired test |
|---|---|
| P1 | マイプロフィール/APIは未ログインで編集不可、公開page/APIはログイン不要。loginだけで未連携Playerを作成しない |
| P2 | 検証済みGoogle identityから同じPlayerを解決。正しいaudでazp欠落は成功、同clientのazpは成功、別client/型不正azp・別環境/client/aud/追加audienceは拒否。state/nonce/browser binding不正、認証待機期限切れ/replay、任意戻り先を拒否 |
| P3 | 自分の公開名だけ更新できる。入力境界、任意Player指定、未連携、別Google identity、HTML/JSONへの名前安全表示を検証。ID/URL/Credential/Best/公開Best日時/Local履歴を維持 |
| P4 | opaque sessionのdigest保存・24時間期限・cookie属性・Google token非保存・環境分離・no-store、Origin/CSRF拒否。emailは本人session画面だけ、expiry/logoutで当該sessionを削除、unlinkで同issuer/subの全session/email/認証待機・未完了承認をatomic失効。他identityは維持。別ブラウザの旧sessionとunlink並行requestを拒否し、後日同Googleを別Playerへ連携しても旧sessionへ権限を与えない。未連携でもCSRF取得/ログアウト可能 |
| P5 | Web新規登録は`Player`、既存名維持、App名前編集なし、設定保存/同期再開で名前PATCHなし。cache更新はserver→表示のみ、OFF中は自動通信なし |
| P6 | 全PlayerのBearer名更新/アカウント削除と匿名登録を拒否、データ不変/Credential維持。古いorigin/直接APIでも迂回なし |
| P7 | 未認証/未連携/session期限/logout/Player削除、保存応答喪失/アカウント切替で誤保存なし。login/logout/名前編集でCredential/連携/同期/Best維持。raw session/secret/tokenの保存・公開/log混入なし、emailの公開/Windows設定混入なし |
| P8 | 削除は独立OAuthで同issuer/subを再確認→対象再表示→未選択checkbox/最終確認→削除confirmation proof成立→DELETE。通常sessionだけ/通常login callback/CSRFだけではproof発行・削除不可、Appなしでも成立。目的/Player/session/browserの不一致・別Google・replay/消費済みproof拒否。auth_time欠落/古い値でも正しいフローは成功。期限はserver成立時刻から10分（600秒経過で不可）、Google時刻非依存、retry延長なし。全権限/Best/session/staging/操作proof削除がatomic、URL404、Local保持。cancel/認証失敗/transaction失敗は不変。確認成立/DELETE双方の応答喪失は先に保持した結果proofで未成立/確認済み/削除完了を区別し、未成立proofでDELETE不可、retryで別Player削除/自動再作成なし |

手動確認はテスト用Web clientと開発Worker/D1で行う。公開page未ログイン閲覧、App単一入口→Web登録/ログイン→プロフィール編集→公開page、Windows名cache更新、未登録/別Google/選び直し/cancel/session期限/logout、解除/再連携、空DB引き継ぎ後の追加・改善同期、アカウント削除cancel/完了とApp401、PC/390px表示を確認する。追加でSecurity Bundle未設定/既存Google SSOでも操作専用OAuth確認が成立しGoogle側の再認証を要求しないこと、通常sessionだけからの危険操作拒否、目的/別Google/期限/replay拒否、App紛失状態でのWeb削除、別ブラウザの解除前sessionから全管理APIが401、同Google再連携後も旧sessionが使えないことを確認する。repository既定CI、14のT1〜T10、16のH1〜H6は後続実装。設計変更は文書整合・wireframe・差分検証のみ。
