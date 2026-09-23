# Web Player Data設計

Issue #205で実装する、公開Player Dataページとread-only Public API V1の正本です。Player identityは[`11_web_player_identity.md`](11_web_player_identity.md)、公開Best集合は[`12_web_best_sync.md`](12_web_best_sync.md)を参照します。

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

RANKはD1へ保存せず、Desktopと同じscore境界からWorkerで導出する。SCORE、EX SCORE、CLEAR、FLAREは譜面ごとの独立Bestであり、1回のRESULTを表さない。

sortは固定enumからD1の`ORDER BY`とcursor条件へ対応付け、title、difficulty固定順、`chart_id`までをsecondary keyにする。D1は`limit + 1`件だけ返し、次ページ判定に使う。score / EX SCORE sortでは`best: null`を常に末尾へ置く。cursorはversion、query scope、sort、最終rowの比較keyをbase64urlで表現し、frontendはopaque値として扱う。Title部分一致はD1の`instr(lower(title), lower(q))`で評価し、ASCII英字は大小を区別せず、記号は文字どおり検索する。

## Flare Skill

Workerは現行・非removed chartの`best_flare_rank`、level、play style、versionだけを使う。Lv1〜19 × FLARE I〜EX完成値、CLASSIC / WHITE / GOLD分類、カテゴリTop30、tie-break、TOTAL rank閾値はDesktop #198と同じgolden fixtureで検証する。SP / DPは分離し、対象0件はTOTAL 0、rank NONEとして返す。

## HTML bootstrapとrouting

`/player/*`はStatic Assetsより先にWorkerを実行する。Player存在時はStatic AssetsのSPA shellへ次を注入する。

- Player固有のtitle、description、OGP、queryなしcanonical URL
- `noindex,follow`
- Public Player / Overview APIと同じDTOのbootstrap JSON

bootstrap JSONは`<`、`>`、`&`、U+2028、U+2029をescapeし、requestごとのnonceを付けた`application/json` scriptへ格納する。Overviewはbootstrapだけで初期描画し、BestとFlare Skillはview選択時にPublic APIから取得する。

Worker生成HTMLはstrict CSP、`nosniff`、`no-referrer`、frame拒否、Permissions Policy、`Cache-Control: no-store`を明示する。CSPはselfとrequest nonceだけを許可し、inline style、`unsafe-inline`、`unsafe-eval`、外部CDNを必要としない。

## URL stateとresponsive表示

pathはPlayer identity、queryは`style`、`view`、`mode`、`level`、`version`、`q`、`sort`を保持する。pagination cursorは共有URLへ含めない。Overviewはbootstrap、BestとFlare Skillはloading / empty / API errorを独立状態として扱う。

desktopではBest table、680px以下では同じ行をcard状に再配置する。390pxでは単一columnとし、主要操作と情報に横scrollを要求しない。

## Migrationとdeploy

Public browse用migrationは既存tableへのindex追加だけとし、旧Workerへ先に適用できる。productionは`ddrgp-scorelog` Workerと既存D1を使用し、`PUBLIC_WEB_ORIGIN`とWindows appの既定API originは`https://ddrgp-scorelog.tts1374.workers.dev`に揃える。Windows appの公開ページ導線も同じoriginの`/player/{public_player_id}`を開く。main更新時はCI成功後にD1 migration、Worker + Static Assets deployの順で行う。
