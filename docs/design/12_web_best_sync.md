# Web Best同期設計

Issue #204で実装するPlayer BestのLocalからWebへの同期契約の正本です。identityと認証は[`11_web_player_identity.md`](11_web_player_identity.md)、長期的な責務分離は[`ADR 0007`](../adr/0007-local-source-of-truth-and-stable-web-best-replica.md)を参照します。

## 責務境界

- 正式個人スコアDBがPlayer scoreのSource of Truthで、D1は公開用replicaである。
- 同期はLocalからWebへの一方向である。WebからLocal scoreを編集しない。
- Web失敗、停止、認証失敗は正式保存を失敗扱いにしない。
- Web Bestの母集団は`source_captures.source_kind = 'capture'`の正式保存playだけである。`manual`、`manifest`、`timestamped`と未知のsource kindは集計前に除外する。
- Recent play、全履歴、master metadata、capture・画像認識・診断情報は同期しない。

## PlayerChartBestProjectionV1

譜面ごとに次の5 fieldだけを公開する。

| field | 集計契約 |
|---|---|
| `chart_id` | M4 masterとWeb shared masterで共通の永続ID |
| `best_score` | capture由来playのscore最大値 |
| `best_ex_score` | capture由来playのEX SCORE最大値。`best_score`と別play由来でよい |
| `best_clear_type` | `MFC > PFC > GFC > FC > CLEAR > FAILED`。Localの`FULL COMBO`は`FC`へ正規化 |
| `best_flare_rank` | `clear_type != FAILED`のplayだけを対象に`EX > IX > VIII > VII > VI > V > IV > III > II > I`。実績なしは`null` |

field順を固定したcanonical JSONのSHA-256をProjection hashとする。title、artist、level、difficulty等はD1 shared masterから解決し、clientから送信しない。

## Local同期状態

`data/web-sync/web-best-sync.sqlite`は正式個人スコアDBと分離したapp-owned stateで、同期ON/OFF、full snapshot要求、status、最終成功時刻、retry情報、譜面ごとのdesired hash・synced hash・deferred errorを保持する。desiredだけがある状態はpending upsert、syncedだけがある状態はpending delete、両hashが一致する状態はsyncedを表す。

capture由来playの正式保存成功後に対象を含む現在Projection集合を再計算し、hashが変わった譜面だけを最大50件のdelta batchへ送る。Projectionが変わらない場合はBest rowと公開更新時刻を変更しない。状態DBは正式個人スコアDBのschema、backup、formal save transactionへ含めない。

## APIとD1

認証済みrouteはADR 0006のApp Credentialからserver側で`player_id`を決定する。clientがPlayer IDを指定するfieldは持たない。

| Method | Route | 契約 |
|---|---|---|
| `POST` | `/api/v1/me/bests/batch` | 最大50件のupsert/delete。request retryは冪等でitem単位結果を返し、`UNKNOWN_CHART`だけをrejectして他itemを継続 |
| `POST` | `/api/v1/me/bests/snapshots` | expected countとmaster versionを持つstaging snapshotを開始 |
| `PUT` | `/api/v1/me/bests/snapshots/{snapshotId}/items` | chunk IDとdigestで冪等upload。live集合は変更しない |
| `POST` | `/api/v1/me/bests/snapshots/{snapshotId}/commit` | count、重複、chart存在、revisionを検証し、transactionで集合を置換 |
| `DELETE` | `/api/v1/me/bests/snapshots/{snapshotId}` | stagingをabortしてitem/chunkを削除 |
| `DELETE` | `/api/v1/me/bests` | 公開Bestとpending snapshotを削除。Player、公開ID、Credentialは維持 |

D1は`songs`、`charts`、`web_master_metadata`を共有masterとして持ち、`player_chart_bests.chart_id`を外部キーで`charts`へ接続する。公開集合はPlayerごとの`best_sync_revision`で競合を検出する。snapshotは24時間でexpireし、各snapshot操作時にexpired/aborted stagingを遅延cleanupする。

full snapshotは`BEGIN → staging upload → validation → atomic replace → cleanup`で実行する。途中upload、invalid item、`UNKNOWN_CHART`、件数不一致、stale revisionではlive集合を変更しない。空snapshotは有効で、commit時に公開集合を空へ置換する。同一chunk ID・同一内容のretryとcommit response loss後のretryは冪等である。

## master identityと配布

`master/song_identity_registry.json`は既存配布masterのcanonical表記とalias表記を同じ既存`song_id`へ固定する。M4 masterの通常生成はregistryに未登録のpresentationを推測採用せず停止する。`chart_id`は固定済み`song_id + play_style + difficulty`の`stable_identity_id_v1`から生成する。contractはUnicodeをそのままUTF-8でNUL区切りし、SHA-1先頭16桁を既存prefixへ付ける現行互換方式で、golden testを持つ。

CIはmaster生成・inspection後に`master.d1_export`で`songs`、`charts`、`master_version`の冪等upsert SQLを生成し、同じartifactへ含める。Web専用のsong/chart IDは作らない。

## 同期操作と状態

- OFFではローカル保存を継続し、Web requestを停止する。Player identity、Credential、公開済みBestを維持する。
- 初回ON、OFFからON、bulk restore、repairは現在のcapture由来集合をfull snapshotで再整合する。
- network error、timeout、429、5xxは5秒、15秒、30秒、1分、5分を基準に±20% jitterで最大5回自動retryする。
- 401/403は`AUTH_INVALID`へ接続し、自動retryと自動Player再登録を行わない。
- 設定画面の同期ON/OFFと公開プレイヤー名は設定保存時に確定する。未登録時は公開プレイヤー名をregistrationへ渡し、登録済みでは`PATCH /api/v1/me`で名前だけを更新する。
- `AUTH_INVALID`からの再登録は、確認付き操作で無効なlocal identityを破棄し、改めて同期ONを保存した場合だけ行う。
- deltaの`UNKNOWN_CHART`は該当譜面をdeferredに保ち、他itemを同期する。ユーザー操作を要求せず、対象がそれだけなら「今すぐ同期」を無効にする。
- 公開Best削除成功後は同期をOFFにし、同じPlayerへ再度ONにしたときfull snapshotで再公開する。
- Windowsアプリはproduction Worker originを既定接続先とし、`DDRGP_WEB_API_ORIGIN`はHTTPSのdevelopment / staging overrideとして扱う。

## D1 Free枠の確認

client chunkは50件、server上限は250件である。snapshot chunk uploadは、重複確認1 queryと、chunk metadata・全itemをJSONから投入する2 statementのtransactionで、件数に比例したD1 statementを発行しない。deltaも全itemのmaster/current Best確認を1 query、変更分upsert・delete・revision更新を3 statementのtransactionで処理する。

2026-09-21のcurrent master 9,801譜面とwireframe代表値862譜面について、認証、lazy cleanup、snapshot状態確認、transaction内statementを含む実装経路を数えた結果は次のとおり。D1 planの具体上限値はappへhard-codeしない。

| 操作 | HTTP request | D1 statement実行 | 変更集合を空から公開するときのrow write/delete上限 |
|---|---:|---:|---:|
| 通常差分1〜50譜面 | 1 | 6 | 認証利用時刻1 + Best最大50 + Player公開時刻1 |
| Projection不変 | 0 | 0 | 0 |
| 862譜面full snapshot | 20（begin + 18 chunk + commit） | 206 | 2,647 |
| 9,801譜面full snapshot | 199（begin + 197 chunk + commit） | 1,996 | 30,001 |

full snapshotのrow数はrequestごとのCredential利用時刻、snapshot 1、chunk metadata、staging item、live Best、Player公開時刻、commit guard/status、staging cleanupを合算した上限である。既存live集合と内容が同じcommitではlive BestとPlayer公開時刻を書き換えないため減少する。通常利用はプレー回数ではなく、実際にProjectionが変わったchart数へ比例する。
