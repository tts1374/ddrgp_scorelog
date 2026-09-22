# ADR 0007: ローカル正本と安定master identityによるWeb Best replica

## Status

Accepted

Date: 2026-09-21

Related issue: #204

## Context

Windows appの正式個人スコアDBからPlayer BestをWebへ公開するには、端末内の全履歴と公開用集合の責務を分離し、master更新をまたいでも同じ曲・譜面を参照できる必要がある。同期はWindows app、Cloudflare Worker、D1、M4 master生成にまたがり、公開集合と永続IDの変更は後続のWeb Player DataとRankingにも影響する。

## Decision

1. 正式個人スコアDBをPlayer scoreのSource of Truth、D1のPlayer Bestを公開用replicaとする。同期方向はLocalからWebへの一方向とし、Web障害を正式保存の成否へ伝播させない。
2. 公開集合は`source_captures.source_kind = 'capture'`のplayだけから`PlayerChartBestProjectionV1`を再計算する。Recent play、全履歴、master metadata、capture・認識情報は送らない。
3. 差分同期はProjectionのhashと同期済みhashを正式個人スコアDB外の専用SQLiteへ永続化する。初回ON、OFFからの再開、bulk restore、repairではstagingを経たatomic replace-set snapshotで現在集合へ再整合する。
4. Web側はApp CredentialからPlayerを決定し、D1のshared masterを`player_chart_bests.chart_id → charts → songs`で参照する。client payloadからPlayer IDとmaster metadataを受け取らない。
5. `song_id`と`chart_id`をLocalとWebで共通の永続identityとする。既存masterからSong Identity Registryをbootstrapしてfreezeし、canonical表記の修正では既存IDを変更しない。未登録の新しいpresentationは生成を失敗させ、registry reviewを要求する。

## Consequences

- Web同期が停止・失敗しても、ローカル保存と履歴閲覧は継続できる。
- OFF中の変更履歴を保持せず、再開時の現在集合snapshotだけで削除を含む整合を回復できる。
- 公開集合の差分状態は正式個人スコアDB schemaとbackup対象へ混入しない。
- master生成では新曲や新しい表記をregistryへ明示登録する作業が必要になる。
- 後続の公開Player DataとRankingは同じPlayer identity、Best projection、shared master identityを再利用する。

## Alternatives Considered

- Webをscoreの正本または双方向同期先にすると、Local正式保存境界と競合解決が必要になり、今回の一方向公開用途を超えるため採用しない。
- title、artist、levelをBest payloadへ含めると、clientごとのmaster差分が公開metadataへ混入するため採用しない。
- canonical titleとartistから毎回IDを再生成すると、表記修正で既存Bestとの参照が切れるため採用しない。

## References

- [Issue #204](https://github.com/tts1374/ddrgp_scorelog/issues/204)
- [`docs/design/12_web_best_sync.md`](../design/12_web_best_sync.md)
- [`docs/design/08_master_db_generation.md`](../design/08_master_db_generation.md)
- [`docs/adr/0006-stable-web-player-identity-and-app-credential-boundary.md`](0006-stable-web-player-identity-and-app-credential-boundary.md)
