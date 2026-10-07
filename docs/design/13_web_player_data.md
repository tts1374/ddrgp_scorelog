# Web Player Data設計

Issue #205で実装する、公開Player Dataページとread-only Public API V1の正本です。Player identityは[`11_web_player_identity.md`](11_web_player_identity.md)、公開Best集合は[`12_web_best_sync.md`](12_web_best_sync.md)を参照します。

#213の未実装の[マイプロフィール](15_web_my_profile.md)はGoogleログイン必須の管理画面である。本設計の公開ページ・Public APIは認証不要のまま維持する。後続の公開集合は[Web自己歴代Best](16_web_historical_best.md)として過去PCのBestも保持し、現在PCのDBとの一致を要求しない。既存Public APIのfield・集計・閲覧契約は維持する。

## 責務境界

- Cloudflare上のIdentity、Web Best同期、公開Player Dataは同一Worker、同一D1、same-originで提供する。
- 公開URLは`/player/{public_player_id}`とし、Viewer認証を要求しない。内部`player_id`とApp CredentialはHTML、URL、Public APIへ出さない。
- Playerが存在しない場合はWorkerからHTTP 404を返す。Playerが存在し公開Bestが0件の場合はHTTP 200のempty stateを返す。
- 公開データはread-onlyで、D1の`player_chart_bests`とshared masterから導出する。

## Public API V1

| Method | Route | 責務 |
|---|---|---|
| `GET` | `/api/v1/public/players/{public_player_id}` | display name、`public_bests_updated_at`、style別Best・Level・Flare Skill summary |
| `GET` | `/api/v1/public/players/{public_player_id}/bests` | Level / Version / Title探索、stable sort、keyset cursor pagination |
| `GET` | `/api/v1/public/players/{public_player_id}/flare-skill?style=SP\|DP` | 公開Best由来のカテゴリTop30、カテゴリ合計、TOTAL、rank |

Public APIは認証不要だがsame-origin UI用とし、wildcard CORSを付与しない。responseは`Cache-Control: no-store`とする。不正queryは`INVALID_QUERY`、不正cursorは`INVALID_CURSOR`、Player不在は`PLAYER_NOT_FOUND`の既存error envelopeで返す。

## Best表示集合

一覧の基本集合は、選択styleの現行chartと、収録終了後もPlayer Bestが残るchartである。現行chartはBestがなくても`best: null`として返す。収録終了かつBestなしのchartは返さない。Level / Version summaryの母数は現行chartだけを使う。

公開記録がないSCORE、EX SCORE、RANK、CLEAR、FLAREは`—`で表示する。記録の欠如は未プレーを意味しない。

RANKはD1へ保存せず、Desktopと同じscore境界からWorkerで導出する。SCORE、EX SCORE、CLEAR、FLAREは譜面ごとの独立Bestであり、1回のRESULTを表さない。

OverviewとBestの見出し・集計・説明・loading / empty表示・アクセシブル名では「自己ベスト」を使用する。公開された記録の有無と未プレーの違い、各指標が独立Bestである説明を維持する。RANK / CLEAR / FLAREのbadgeはDesktopの`ViewerModels.cs`の分類と`Components.xaml`・light themeの`Theme.xaml`の背景・枠・文字色に揃え、値を文字でも表示する。MFC / EXは同じ多色gradientを使い、BestとFlare Skill対象一覧の同じFLARE値は同じ配色にする。

条件指定のないBest初回表示は、選択styleの全譜面一覧とする。空のTitle検索と同じ表示集合・paginationを利用し、レベル・バージョン・曲名で絞り込まない。結果欄は「全譜面」、初期sortはSCORE高い順で、記録なしは末尾に置く。有効なURLの探索条件・sortを初期値より優先し、一覧からLevel / Version / Title探索へ切り替えられる。未知のversionをURLから復元した場合も、その値をselectの選択肢として表示し、結果欄・内部状態・API検索条件を一致させる。

sortは固定enumからD1の`ORDER BY`とcursor条件へ対応付け、title、difficulty固定順、`chart_id`までをsecondary keyにする。D1は`limit + 1`件だけ返し、次ページ判定に使う。score / EX SCORE sortでは`best: null`を常に末尾へ置く。cursorはversion、query scope、sort、最終rowの比較keyをbase64urlで表現し、frontendはopaque値として扱う。

Title部分一致はcanonical titleと検索用別名を対象とする。master DBの`song_aliases`にある曲名表記と、確認済みの`master/title_search_aliases.json`を別名として使う。検索語・canonical title・別名には、同じ小文字化、Unicode NFD分解、Latin文字のアクセント除去、`æ → ae`・`ø → o`変換、NFC再合成を適用する。日本語の濁点と記号は保持する。D1では`title_search_key`または`song_title_search_aliases`に`instr`で一致するsongを絞ってからkeyset paginationし、別名が複数一致してもchartを重複させない。Desktopの「曲名から」も同じ検索語・別名集合を使い、表示用titleとsort順は変えない。

## Flare Skill

Workerは現行・非removed chartの`best_flare_rank`、level、play styleと共有マスタの曲別`flare_category`を使う。GP表示用versionは維持し、現在GPでプレー可能な過去AC曲を含むGP用集計とする。確認済みAC収録歴なし・未解決・GP対象外のカテゴリNULLは正常な除外とする。Lv1〜19 × FLARE I〜EX完成値、CLASSIC / WHITE / GOLD分類、カテゴリTop30、tie-break、TOTAL rank閾値はDesktop #198と同じgolden fixtureで検証する。SP / DPは分離し、対象0件はTOTAL 0、rank NONEとして返す。

各カテゴリは初期状態で上位10件（10件以下なら全対象）を表示する。10件を超えるカテゴリに「全N件を見る」を設け、既存APIのTop30からそのカテゴリの全対象を独立して表示する。Nは実際の対象件数で最大30件。展開前後で順位・並び順・カテゴリ合計・TOTALを変えない。

## HTML bootstrapとrouting

`/player/*`はStatic Assetsより先にWorkerを実行する。Player存在時はStatic AssetsのSPA shellへ次を注入する。

- Player固有のtitle、description、OGP、queryなしcanonical URL
- `noindex,follow`
- Public Player / Overview APIと同じDTOのbootstrap JSON

bootstrap JSONは`<`、`>`、`&`、U+2028、U+2029をescapeし、requestごとのnonceを付けた`application/json` scriptへ格納する。Overviewはbootstrapだけで初期描画し、BestとFlare Skillはview選択時にPublic APIから取得する。

Worker生成HTMLはstrict CSP、`nosniff`、`no-referrer`、frame拒否、Permissions Policy、`Cache-Control: no-store`を明示する。CSPはselfとrequest nonceだけを許可し、`unsafe-inline`、`unsafe-eval`、外部CDNを必要としない。Viteが挿入するscript・style用nonce placeholderをWorkerでrequest nonceへ置換し、開発時のReact起動scriptと動的styleも同じnonceで許可する。

## URL stateとresponsive表示

pathはPlayer identity、queryは`style`、`view`、`mode`、`level`、`version`、`q`、`sort`を保持する。pagination cursorは共有URLへ含めない。Overviewはbootstrap、BestとFlare Skillはloading / empty / API errorを独立状態として扱う。

最上位タブ、SINGLE / DOUBLE、探索方法、レベル・バージョン・sortの選択変更を履歴に追加し、ブラウザの戻る・進むで条件と対応するデータを復元する。探索条件は別の最上位タブでもqueryに保持する。検索文字の入力は現在の履歴項目とURLを置換し、文字ごとの履歴は増やさない。同じ値の再選択と履歴復元では履歴を追加しない。

Bestの追加取得失敗は一覧の後ろに表示し、既存行と件数・選択条件を保持する。再試行は同じcursorの追加ページを取得し、成功時だけ一度追加する。条件変更時は追加取得の失敗状態を破棄し、古い条件の応答を新しい一覧へ混ぜない。

公開ページの「GP Score Log」ロゴはTOP `/`へ移動する。右上の「マイページ」から`/my/profile`へ進む導線も維持する。

desktopではBest table、680px以下では同じ行をcard状に再配置する。390pxでは単一columnとし、主要操作と情報に横scrollを要求しない。

## Migrationとdeploy

Public browse用migrationは既存tableへのindex・`title_search_key`追加と、検索用別名table追加で、旧Workerへ先に適用できる。既存songの検索keyはmigrationで補完し、以後のmaster exportがtitleと検索用別名を更新する。productionは`ddrgp-scorelog` Workerと既存D1を使用し、`PUBLIC_WEB_ORIGIN`とWindows appの既定API originは`https://ddrgp-scorelog.tts1374.workers.dev`に揃える。Windows appの公開ページ導線も同じoriginの`/player/{public_player_id}`を開く。

main更新時のdeploy workflowはWeb検証、master parser・identity registry test、同じcheckoutのregistryによるmaster生成・検査を実行する。D1 SQLは同じcheckoutのexporterと検索用別名から生成し、D1 migration、shared master SQL投入、Worker + Static Assets deployの順に進む。master sourceと検索用別名の変更も起動対象とし、master生成・検査・投入に失敗した場合はdeployを停止する。未登録の新曲・新表記は既存IDとの対応を確認してregistryへ追加する。初回公開前に新Workerへ既存productionと同じsecretを設定し、旧endpointを使う配布版のサポート期間中は旧WorkerのAPIを維持する。

開発環境のWindows appは既定で`https://ddrgp-scorelog-dev.tts1374.workers.dev/`へ登録・同期し、公開ページ導線も同じdev Worker originを使用する。local Webは画面・API検証用として、`DDRGP_WEB_API_ORIGIN=http://127.0.0.1:5173/`で明示選択できる。Google実認証は14/15の固定HTTPS callbackを使用し、local HTTPで登録・引き継ぎを完結させない。`npm run dev`は開発用secretを`.dev.vars`へ保持し、`.wrangler/development`配下のlocal D1へmigrationとlocal masterのexportを適用してからViteを起動する。本番D1、browser E2Eのlocal D1、Windows側の本番identity・同期状態とは保存先を分離する。
