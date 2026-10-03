import type { Hono, Context } from "hono";
import { isProjection, type BestSyncEnvironment, type Projection } from "./best-sync";
import { randomId } from "./crypto";

export function canonicalProjection(item: Projection): Projection {
  return {
    chart_id: item.chart_id,
    best_score: item.best_score,
    best_ex_score: item.best_ex_score,
    best_clear_type: item.best_clear_type,
    best_flare_rank: item.best_flare_rank,
  };
}

export async function projectionDigest(value: unknown): Promise<string> {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)),
    byte => byte.toString(16).padStart(2, "0")).join("");
}

export function credentialWriteGuard(c: Context<BestSyncEnvironment>, id: string): D1PreparedStatement {
  const player = c.get("player");
  return c.env.DB.prepare(`INSERT INTO best_sync_write_guards (id, guard)
    VALUES (?1, CASE WHEN EXISTS (
      SELECT 1 FROM player_credentials WHERE id = ?2 AND player_id = ?3
      AND revoked_at IS NULL AND activation_state = 'active'
    ) THEN 1 ELSE 0 END)`).bind(id, player.credential_id, player.id);
}

const clearOrder = ["FAILED", "CLEAR", "FC", "GFC", "PFC", "MFC"];
const flareOrder = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"];

function rankSql(column: string, values: string[]): string {
  return `CASE ${column} ${values.map((value, index) => `WHEN '${value}' THEN ${index}`).join(" ")} ELSE -1 END`;
}

export function loweredFields(old: Projection, next: Projection): string[] {
  return [
    ...(next.best_score < old.best_score ? ["best_score"] : []),
    ...(next.best_ex_score < old.best_ex_score ? ["best_ex_score"] : []),
    ...(clearOrder.indexOf(next.best_clear_type) < clearOrder.indexOf(old.best_clear_type) ? ["best_clear_type"] : []),
    ...(flareOrder.indexOf(next.best_flare_rank ?? "") < flareOrder.indexOf(old.best_flare_rank ?? "") ? ["best_flare_rank"] : []),
  ];
}

export const snapshotCanonicalSql = `(SELECT json_group_array(json(item)) FROM (
  SELECT json_object('chart_id', chart_id, 'best_score', best_score,
    'best_ex_score', best_ex_score, 'best_clear_type', best_clear_type,
    'best_flare_rank', best_flare_rank) AS item
  FROM best_sync_snapshot_items WHERE snapshot_id = ?1 ORDER BY chart_id COLLATE BINARY
))`;

async function replacementReview(c: Context<BestSyncEnvironment>) {
  const id = c.req.param("snapshotId");
  const snapshot = await c.env.DB.prepare(`SELECT expected_item_count, base_sync_revision,
    ${snapshotCanonicalSql} AS content_json FROM best_sync_snapshots
    WHERE snapshot_id = ?1 AND player_id = ?2 AND credential_id = ?3
    AND status = 'PENDING' AND expires_at > ?4`).bind(id, c.get("player").id,
      c.get("player").credential_id, new Date().toISOString()).first<{
        expected_item_count: number; base_sync_revision: number; content_json: string;
      }>();
  if (snapshot === null) return null;
  const items = JSON.parse(snapshot.content_json) as Projection[];
  const revision = await c.env.DB.prepare("SELECT best_sync_revision FROM players WHERE id = ?1")
    .bind(c.get("player").id).first<{ best_sync_revision: number }>();
  if (items.length !== snapshot.expected_item_count || revision?.best_sync_revision !== snapshot.base_sync_revision) return null;
  const unknown = await c.env.DB.prepare(`SELECT i.chart_id FROM best_sync_snapshot_items i
    LEFT JOIN charts c ON c.chart_id = i.chart_id WHERE i.snapshot_id = ?1 AND c.chart_id IS NULL LIMIT 1`)
    .bind(id).first();
  if (unknown !== null) return null;
  const current = (await c.env.DB.prepare(`SELECT chart_id, best_score, best_ex_score,
    best_clear_type, best_flare_rank FROM player_chart_bests WHERE player_id = ?1 ORDER BY chart_id COLLATE BINARY`)
    .bind(c.get("player").id).all<Projection>()).results;
  const byChart = new Map(items.map(item => [item.chart_id, item]));
  return {
    ...snapshot,
    content_digest: await projectionDigest(items),
    public_count: current.length,
    eligible_count: items.length,
    removed: current.filter(item => !byChart.has(item.chart_id)).map(item => item.chart_id),
    lowered: current.flatMap(item => {
      const next = byChart.get(item.chart_id);
      const fields = next === undefined ? [] : loweredFields(item, next);
      return fields.length === 0 ? [] : [{ chart_id: item.chart_id, fields }];
    }),
  };
}

export function registerHistoricalBestRoutes(app: Hono<BestSyncEnvironment>): void {
  app.post("/api/v1/me/bests/snapshots/:snapshotId/replacement-review", async c => {
    const review = await replacementReview(c);
    if (review === null) return c.json({ error: { code: "REPLACEMENT_REVIEW_INVALID" } }, 409);
    const { content_json: _content, expected_item_count: _count, ...result } = review;
    return c.json(result);
  });
  app.post("/api/v1/me/bests/snapshots/:snapshotId/replacement-authorize", async c => {
    const body = await c.req.json().catch(() => null);
    const review = await replacementReview(c);
    if (review === null || body?.confirmed !== true || body.content_digest !== review.content_digest ||
        body.base_sync_revision !== review.base_sync_revision ||
        Object.keys(body).some(key => !["confirmed", "content_digest", "base_sync_revision"].includes(key))) {
      return c.json({ error: { code: "REPLACEMENT_CONFIRMATION_REQUIRED" } }, 409);
    }
    const id = c.req.param("snapshotId");
    const expires = new Date(Date.now() + 600_000).toISOString();
    const guard = randomId("ra_");
    await c.env.DB.batch([
      credentialWriteGuard(c, guard),
      c.env.DB.prepare(`INSERT INTO best_replacement_authorizations
        (snapshot_id, credential_id, content_digest, content_json, base_revision, expires_at)
        SELECT ?1, ?2, ?3, ?4, ?5, ?6 WHERE EXISTS (
          SELECT 1 FROM best_sync_snapshots s JOIN players p ON p.id = s.player_id
          WHERE s.snapshot_id = ?1 AND s.credential_id = ?2 AND s.status = 'PENDING'
          AND s.expires_at > ?7 AND p.best_sync_revision = ?5
          AND ${snapshotCanonicalSql} = ?4)
        ON CONFLICT(snapshot_id) DO NOTHING`).bind(id, c.get("player").credential_id,
          review.content_digest, review.content_json, review.base_sync_revision, expires, new Date().toISOString()),
      c.env.DB.prepare("DELETE FROM best_sync_write_guards WHERE id = ?1").bind(guard),
    ]);
    const authorization = await c.env.DB.prepare(`SELECT content_digest, base_revision, expires_at
      FROM best_replacement_authorizations WHERE snapshot_id = ?1`).bind(id).first();
    if (authorization === null || authorization.content_digest !== review.content_digest ||
        authorization.base_revision !== review.base_sync_revision) return c.json({ error: { code: "SYNC_CONFLICT" } }, 409);
    return c.json(authorization);
  });
  app.post("/api/v1/me/bests/merge", async c => {
    const body = await c.req.json().catch(() => null);
    if (body === null || body.projection_version !== 1 || typeof body.master_version !== "string" ||
        !Array.isArray(body.items) || body.items.length > 50 ||
        Object.keys(body).some(key => !["projection_version", "master_version", "items"].includes(key))) {
      return c.json({ error: { code: "INVALID_REQUEST" } }, 400);
    }
    const items: unknown[] = body.items;
    const results: Array<Record<string, unknown>> = [];
    const valid: Array<{ index: number; item: Projection; hash: string }> = [];
    for (const [index, item] of items.entries()) {
      if (!isProjection(item)) {
        results.push({ index, status: "rejected", code: "INVALID_PROJECTION" });
      } else {
        valid.push({ index, item: canonicalProjection(item), hash: await projectionDigest(canonicalProjection(item)) });
      }
    }
    if (new Set(valid.map(value => value.item.chart_id)).size !== valid.length) {
      return c.json({ error: { code: "DUPLICATE_CHART" } }, 400);
    }
    const guard = randomId("mg_");
    const now = new Date().toISOString();
    const statements = [credentialWriteGuard(c, guard)];
    const clearBetter = `${rankSql("excluded.best_clear_type", clearOrder)} > ${rankSql("player_chart_bests.best_clear_type", clearOrder)}`;
    const flareBetter = `${rankSql("excluded.best_flare_rank", flareOrder)} > ${rankSql("player_chart_bests.best_flare_rank", flareOrder)}`;
    for (const { item } of valid) {
      statements.push(c.env.DB.prepare(`INSERT INTO player_chart_bests
        (player_id, chart_id, best_score, best_ex_score, best_clear_type, best_flare_rank, updated_at)
        SELECT ?1, ?2, ?3, ?4, ?5, ?6, ?7 WHERE EXISTS (SELECT 1 FROM charts WHERE chart_id = ?2)
        ON CONFLICT(player_id, chart_id) DO UPDATE SET
          best_score = max(player_chart_bests.best_score, excluded.best_score),
          best_ex_score = max(player_chart_bests.best_ex_score, excluded.best_ex_score),
          best_clear_type = CASE WHEN ${clearBetter} THEN excluded.best_clear_type ELSE player_chart_bests.best_clear_type END,
          best_flare_rank = CASE WHEN ${flareBetter} THEN excluded.best_flare_rank ELSE player_chart_bests.best_flare_rank END,
          updated_at = excluded.updated_at
        WHERE excluded.best_score > player_chart_bests.best_score OR
          excluded.best_ex_score > player_chart_bests.best_ex_score OR ${clearBetter} OR ${flareBetter}
        RETURNING chart_id`).bind(c.get("player").id, item.chart_id, item.best_score,
          item.best_ex_score, item.best_clear_type, item.best_flare_rank, now));
      statements.push(c.env.DB.prepare(`UPDATE best_sync_write_guards
        SET changed_count = changed_count + changes() WHERE id = ?1`).bind(guard));
      statements.push(c.env.DB.prepare("SELECT chart_id FROM charts WHERE chart_id = ?1").bind(item.chart_id));
    }
    statements.push(c.env.DB.prepare(`UPDATE players SET best_sync_revision = best_sync_revision + 1,
      public_bests_updated_at = ?1 WHERE id = ?2 AND EXISTS (
        SELECT 1 FROM best_sync_write_guards WHERE id = ?3 AND changed_count > 0
      )`).bind(now, c.get("player").id, guard));
    statements.push(c.env.DB.prepare("DELETE FROM best_sync_write_guards WHERE id = ?1").bind(guard));
    const committed = await c.env.DB.batch(statements);
    for (const [offset, { index, hash }] of valid.entries()) {
      const known = committed[3 + offset * 3].results.length > 0;
      results.push(known ? { index, status: "accepted", changed: committed[1 + offset * 3].results.length > 0,
        received_hash: hash } : { index, status: "rejected", code: "UNKNOWN_CHART" });
    }
    return c.json({ results: results.sort((a, b) => (a.index as number) - (b.index as number)) });
  });
}
