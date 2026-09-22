import { Hono } from "hono";
import type { Context } from "hono";

export interface BestSyncPlayer {
  id: string;
  public_player_id: string;
  display_name: string;
  created_at: string;
  updated_at: string;
}

export type BestSyncEnvironment = {
  Bindings: {
    DB: D1Database;
    CREDENTIAL_PEPPER: string;
    REGISTRATION_SECRET: string;
  };
  Variables: { player: BestSyncPlayer };
};

interface Projection {
  chart_id: string;
  best_score: number;
  best_ex_score: number;
  best_clear_type: string;
  best_flare_rank: string | null;
}

interface SnapshotRow {
  snapshot_id: string;
  player_id: string;
  expected_item_count: number;
  base_sync_revision: number;
  committed_revision: number | null;
  status: "PENDING" | "COMMITTED" | "ABORTED" | "EXPIRED";
  expires_at: string;
}

interface ParsedOperation {
  index: number;
  type: "upsert" | "delete";
  chart_id: string;
  item?: Projection;
}

interface CurrentOperationRow {
  operation_index: number;
  chart_known: number;
  current_chart_id: string | null;
  best_score: number | null;
  best_ex_score: number | null;
  best_clear_type: string | null;
  best_flare_rank: string | null;
}

const projectionVersion = 1;
const maximumBatchSize = 50;
const maximumSnapshotChunkSize = 250;
const snapshotLifetimeMilliseconds = 24 * 60 * 60 * 1000;
const snapshotIdPattern = /^bs_[A-Za-z0-9_-]{20,64}$/u;
const chunkIdPattern = /^[A-Za-z0-9_-]{1,64}$/u;
const clearTypes = new Set(["MFC", "PFC", "GFC", "FC", "CLEAR", "FAILED"]);
const flareRanks = new Set(["EX", "IX", "VIII", "VII", "VI", "V", "IV", "III", "II", "I"]);

function errorResponse(
  c: Context<BestSyncEnvironment>,
  status: 400 | 404 | 409 | 500,
  code: string,
  message: string,
): Response {
  return c.json({ error: { code, message } }, status);
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isProjection(value: unknown): value is Projection {
  if (!isPlainObject(value)) {
    return false;
  }
  return (
    typeof value.chart_id === "string" &&
    value.chart_id.length > 0 &&
    value.chart_id.length <= 128 &&
    Number.isInteger(value.best_score) &&
    (value.best_score as number) >= 0 &&
    (value.best_score as number) <= 1_000_000 &&
    Number.isSafeInteger(value.best_ex_score) &&
    (value.best_ex_score as number) >= 0 &&
    typeof value.best_clear_type === "string" &&
    clearTypes.has(value.best_clear_type) &&
    (value.best_flare_rank === null ||
      (typeof value.best_flare_rank === "string" && flareRanks.has(value.best_flare_rank))) &&
    Object.keys(value).every((key) =>
      ["chart_id", "best_score", "best_ex_score", "best_clear_type", "best_flare_rank"].includes(key),
    )
  );
}

function sameProjection(left: Projection, right: Projection): boolean {
  return (
    left.chart_id === right.chart_id &&
    left.best_score === right.best_score &&
    left.best_ex_score === right.best_ex_score &&
    left.best_clear_type === right.best_clear_type &&
    left.best_flare_rank === right.best_flare_rank
  );
}

async function readJson(c: Context<BestSyncEnvironment>): Promise<unknown | null> {
  try {
    return await c.req.json();
  } catch {
    return null;
  }
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

async function contentDigest(value: unknown): Promise<string> {
  const encoded = new TextEncoder().encode(JSON.stringify(value));
  return bytesToHex(new Uint8Array(await crypto.subtle.digest("SHA-256", encoded)));
}

function randomSnapshotId(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return `bs_${btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "")}`;
}

async function cleanupSnapshotStaging(
  db: D1Database,
  playerId: string,
  now: string,
): Promise<void> {
  await db.batch([
    db.prepare(
      `UPDATE best_sync_snapshots
       SET status = 'EXPIRED'
       WHERE player_id = ?1 AND status = 'PENDING' AND expires_at <= ?2`,
    ).bind(playerId, now),
    db.prepare(
      `DELETE FROM best_sync_snapshot_items
       WHERE snapshot_id IN (
         SELECT snapshot_id FROM best_sync_snapshots
         WHERE player_id = ?1 AND status IN ('EXPIRED', 'ABORTED')
       )`,
    ).bind(playerId),
    db.prepare(
      `DELETE FROM best_sync_snapshot_chunks
       WHERE snapshot_id IN (
         SELECT snapshot_id FROM best_sync_snapshots
         WHERE player_id = ?1 AND status IN ('EXPIRED', 'ABORTED')
       )`,
    ).bind(playerId),
  ]);
}

async function readSnapshot(
  db: D1Database,
  snapshotId: string,
  playerId: string,
): Promise<SnapshotRow | null> {
  return db.prepare(
    `SELECT snapshot_id, player_id, expected_item_count, base_sync_revision,
            committed_revision, status, expires_at
     FROM best_sync_snapshots
     WHERE snapshot_id = ?1 AND player_id = ?2`,
  ).bind(snapshotId, playerId).first<SnapshotRow>();
}

export function registerBestSyncRoutes(app: Hono<BestSyncEnvironment>): void {
  app.post("/api/v1/me/bests/batch", async (c) => {
    const body = await readJson(c);
    if (!isPlainObject(body) || body.projection_version !== projectionVersion ||
        typeof body.master_version !== "string" || !Array.isArray(body.operations) ||
        body.operations.length === 0 || body.operations.length > maximumBatchSize) {
      return errorResponse(c, 400, "INVALID_REQUEST", "A valid projection batch is required.");
    }

    const player = c.get("player");
    const results: Array<Record<string, unknown>> = [];
    const parsed: ParsedOperation[] = [];
    for (let index = 0; index < body.operations.length; index += 1) {
      const operation = body.operations[index];
      if (!isPlainObject(operation) || (operation.type !== "upsert" && operation.type !== "delete")) {
        results.push({ index, status: "rejected", code: "INVALID_OPERATION" });
        continue;
      }

      if (operation.type === "upsert") {
        if (!isProjection(operation.item)) {
          results.push({ index, status: "rejected", code: "INVALID_PROJECTION" });
          continue;
        }
        parsed.push({
          index,
          type: "upsert",
          chart_id: operation.item.chart_id,
          item: operation.item,
        });
        continue;
      }

      if (typeof operation.chart_id !== "string" || operation.chart_id.length === 0 ||
          Object.keys(operation).some((key) => !["type", "chart_id"].includes(key))) {
        results.push({ index, status: "rejected", code: "INVALID_OPERATION" });
        continue;
      }
      parsed.push({ index, type: "delete", chart_id: operation.chart_id });
    }

    const duplicateChart = parsed.find(
      (operation, index) =>
        parsed.findIndex((candidate) => candidate.chart_id === operation.chart_id) !== index,
    );
    if (duplicateChart !== undefined) {
      return errorResponse(c, 400, "DUPLICATE_CHART", "A chart may occur only once in a batch.");
    }

    const operationJson = JSON.stringify(parsed.map((operation) => ({
      index: operation.index,
      type: operation.type,
      chart_id: operation.chart_id,
      ...(operation.item === undefined ? {} : operation.item),
    })));
    const currentRows = parsed.length === 0
      ? []
      : (await c.env.DB.prepare(
        `WITH operations AS (
           SELECT CAST(json_extract(value, '$.index') AS INTEGER) AS operation_index,
                  json_extract(value, '$.chart_id') AS chart_id
           FROM json_each(?1)
         )
         SELECT o.operation_index,
                CASE WHEN c.chart_id IS NULL THEN 0 ELSE 1 END AS chart_known,
                b.chart_id AS current_chart_id,
                b.best_score,
                b.best_ex_score,
                b.best_clear_type,
                b.best_flare_rank
         FROM operations o
         LEFT JOIN charts c ON c.chart_id = o.chart_id
         LEFT JOIN player_chart_bests b
           ON b.player_id = ?2 AND b.chart_id = o.chart_id`,
      ).bind(operationJson, player.id).all<CurrentOperationRow>()).results;
    const currentByIndex = new Map(currentRows.map((row) => [row.operation_index, row]));
    const changedOperations: ParsedOperation[] = [];
    for (const operation of parsed) {
      const row = currentByIndex.get(operation.index);
      if (operation.type === "upsert" && row?.chart_known !== 1) {
        results.push({ index: operation.index, status: "rejected", code: "UNKNOWN_CHART" });
        continue;
      }
      const changed = operation.type === "delete"
        ? row?.current_chart_id !== null && row?.current_chart_id !== undefined
        : row?.current_chart_id === null || row?.current_chart_id === undefined ||
          !sameProjection(
            {
              chart_id: operation.chart_id,
              best_score: row.best_score!,
              best_ex_score: row.best_ex_score!,
              best_clear_type: row.best_clear_type!,
              best_flare_rank: row.best_flare_rank,
            },
            operation.item!,
          );
      results.push({ index: operation.index, status: "accepted", changed });
      if (changed) {
        changedOperations.push(operation);
      }
    }

    if (changedOperations.length > 0) {
      const now = new Date().toISOString();
      const changedJson = JSON.stringify(changedOperations.map((operation) => ({
        type: operation.type,
        chart_id: operation.chart_id,
        ...(operation.item === undefined ? {} : operation.item),
      })));
      await c.env.DB.batch([
        c.env.DB.prepare(
          `INSERT INTO player_chart_bests (
             player_id, chart_id, best_score, best_ex_score,
             best_clear_type, best_flare_rank, updated_at
           )
           SELECT ?1,
                  json_extract(value, '$.chart_id'),
                  CAST(json_extract(value, '$.best_score') AS INTEGER),
                  CAST(json_extract(value, '$.best_ex_score') AS INTEGER),
                  json_extract(value, '$.best_clear_type'),
                  json_extract(value, '$.best_flare_rank'),
                  ?3
           FROM json_each(?2)
           WHERE json_extract(value, '$.type') = 'upsert'
           ON CONFLICT(player_id, chart_id) DO UPDATE SET
             best_score = excluded.best_score,
             best_ex_score = excluded.best_ex_score,
             best_clear_type = excluded.best_clear_type,
             best_flare_rank = excluded.best_flare_rank,
             updated_at = excluded.updated_at`,
        ).bind(player.id, changedJson, now),
        c.env.DB.prepare(
          `DELETE FROM player_chart_bests
           WHERE player_id = ?1 AND chart_id IN (
             SELECT json_extract(value, '$.chart_id')
             FROM json_each(?2)
             WHERE json_extract(value, '$.type') = 'delete'
           )`,
        ).bind(player.id, changedJson),
        c.env.DB.prepare(
          `UPDATE players SET best_sync_revision = best_sync_revision + 1,
                              public_bests_updated_at = ?1
           WHERE id = ?2`,
        ).bind(now, player.id),
      ]);
    }

    return c.json({ results: results.sort((left, right) =>
      (left.index as number) - (right.index as number)) });
  });

  app.post("/api/v1/me/bests/snapshots", async (c) => {
    const body = await readJson(c);
    if (!isPlainObject(body) || body.projection_version !== projectionVersion ||
        typeof body.master_version !== "string" || body.master_version.length > 128 ||
        !Number.isInteger(body.expected_item_count) ||
        (body.expected_item_count as number) < 0 ||
        (body.expected_item_count as number) > 20_000) {
      return errorResponse(c, 400, "INVALID_REQUEST", "A valid snapshot request is required.");
    }

    const player = c.get("player");
    const now = new Date();
    const nowText = now.toISOString();
    await cleanupSnapshotStaging(c.env.DB, player.id, nowText);
    const existing = await c.env.DB.prepare(
      "SELECT snapshot_id FROM best_sync_snapshots WHERE player_id = ?1 AND status = 'PENDING'",
    ).bind(player.id).first<{ snapshot_id: string }>();
    const snapshotId = randomSnapshotId();
    const revision = await c.env.DB.prepare(
      "SELECT best_sync_revision FROM players WHERE id = ?1",
    ).bind(player.id).first<{ best_sync_revision: number }>();
    const statements: D1PreparedStatement[] = [];
    if (existing !== null) {
      statements.push(
        c.env.DB.prepare(
          "UPDATE best_sync_snapshots SET status = 'ABORTED' WHERE snapshot_id = ?1",
        ).bind(existing.snapshot_id),
        c.env.DB.prepare(
          "DELETE FROM best_sync_snapshot_items WHERE snapshot_id = ?1",
        ).bind(existing.snapshot_id),
        c.env.DB.prepare(
          "DELETE FROM best_sync_snapshot_chunks WHERE snapshot_id = ?1",
        ).bind(existing.snapshot_id),
      );
    }
    statements.push(c.env.DB.prepare(
      `INSERT INTO best_sync_snapshots (
         snapshot_id, player_id, projection_version, master_version,
         expected_item_count, base_sync_revision, status, created_at, expires_at
       ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'PENDING', ?7, ?8)`,
    ).bind(
      snapshotId,
      player.id,
      projectionVersion,
      body.master_version,
      body.expected_item_count,
      revision?.best_sync_revision ?? 0,
      nowText,
      new Date(now.getTime() + snapshotLifetimeMilliseconds).toISOString(),
    ));
    await c.env.DB.batch(statements);
    return c.json({
      snapshot_id: snapshotId,
      base_sync_revision: revision?.best_sync_revision ?? 0,
      expires_at: new Date(now.getTime() + snapshotLifetimeMilliseconds).toISOString(),
    }, 201);
  });

  app.put("/api/v1/me/bests/snapshots/:snapshotId/items", async (c) => {
    const snapshotId = c.req.param("snapshotId");
    if (!snapshotIdPattern.test(snapshotId)) {
      return errorResponse(c, 404, "SNAPSHOT_NOT_FOUND", "The snapshot was not found.");
    }
    const player = c.get("player");
    const now = new Date().toISOString();
    await cleanupSnapshotStaging(c.env.DB, player.id, now);
    const snapshot = await readSnapshot(c.env.DB, snapshotId, player.id);
    if (snapshot === null) {
      return errorResponse(c, 404, "SNAPSHOT_NOT_FOUND", "The snapshot was not found.");
    }
    if (snapshot.status === "EXPIRED" || snapshot.expires_at <= now) {
      return errorResponse(c, 409, "SNAPSHOT_EXPIRED", "The snapshot has expired.");
    }
    if (snapshot.status !== "PENDING") {
      return errorResponse(c, 409, "SNAPSHOT_NOT_PENDING", "The snapshot is not pending.");
    }

    const body = await readJson(c);
    if (!isPlainObject(body) || typeof body.chunk_id !== "string" ||
        !chunkIdPattern.test(body.chunk_id) || !Array.isArray(body.items) ||
        body.items.length === 0 || body.items.length > maximumSnapshotChunkSize ||
        !body.items.every(isProjection)) {
      return errorResponse(c, 400, "INVALID_REQUEST", "A valid snapshot chunk is required.");
    }
    const chartIds = body.items.map((item) => item.chart_id);
    if (new Set(chartIds).size !== chartIds.length) {
      return errorResponse(c, 400, "DUPLICATE_CHART", "A snapshot chunk contains duplicate charts.");
    }
    const digest = await contentDigest(body.items);
    const existingChunk = await c.env.DB.prepare(
      `SELECT content_digest FROM best_sync_snapshot_chunks
       WHERE snapshot_id = ?1 AND chunk_id = ?2`,
    ).bind(snapshotId, body.chunk_id).first<{ content_digest: string }>();
    if (existingChunk !== null) {
      if (existingChunk.content_digest !== digest) {
        return errorResponse(c, 409, "CHUNK_CONFLICT", "The chunk ID was already used with different content.");
      }
      return c.json({ accepted: body.items.length, retry: true });
    }

    const duplicate = await c.env.DB.prepare(
      `SELECT chunk_id FROM best_sync_snapshot_items
       WHERE snapshot_id = ?1 AND chart_id IN (
         SELECT value FROM json_each(?2)
       )
       LIMIT 1`,
    ).bind(snapshotId, JSON.stringify(chartIds)).first<{ chunk_id: string }>();
    if (duplicate !== null) {
      return errorResponse(c, 400, "DUPLICATE_CHART", "A chart was uploaded in more than one chunk.");
    }

    const itemJson = JSON.stringify(body.items);
    await c.env.DB.batch([
      c.env.DB.prepare(
        `INSERT INTO best_sync_snapshot_chunks (
           snapshot_id, chunk_id, content_digest, item_count, created_at
         ) VALUES (?1, ?2, ?3, ?4, ?5)`,
      ).bind(snapshotId, body.chunk_id, digest, body.items.length, now),
      c.env.DB.prepare(
        `INSERT INTO best_sync_snapshot_items (
           snapshot_id, chunk_id, chart_id, best_score, best_ex_score,
           best_clear_type, best_flare_rank
         )
         SELECT ?1,
                ?2,
                json_extract(value, '$.chart_id'),
                CAST(json_extract(value, '$.best_score') AS INTEGER),
                CAST(json_extract(value, '$.best_ex_score') AS INTEGER),
                json_extract(value, '$.best_clear_type'),
                json_extract(value, '$.best_flare_rank')
         FROM json_each(?3)`,
      ).bind(
        snapshotId,
        body.chunk_id,
        itemJson,
      ),
    ]);
    return c.json({ accepted: body.items.length, retry: false });
  });

  app.post("/api/v1/me/bests/snapshots/:snapshotId/commit", async (c) => {
    const snapshotId = c.req.param("snapshotId");
    if (!snapshotIdPattern.test(snapshotId)) {
      return errorResponse(c, 404, "SNAPSHOT_NOT_FOUND", "The snapshot was not found.");
    }
    const player = c.get("player");
    const now = new Date().toISOString();
    await cleanupSnapshotStaging(c.env.DB, player.id, now);
    const snapshot = await readSnapshot(c.env.DB, snapshotId, player.id);
    if (snapshot === null) {
      return errorResponse(c, 404, "SNAPSHOT_NOT_FOUND", "The snapshot was not found.");
    }
    if (snapshot.status === "COMMITTED") {
      return c.json({ sync_revision: snapshot.committed_revision, changed: false, retry: true });
    }
    if (snapshot.status === "EXPIRED" || snapshot.expires_at <= now) {
      return errorResponse(c, 409, "SNAPSHOT_EXPIRED", "The snapshot has expired.");
    }
    if (snapshot.status !== "PENDING") {
      return errorResponse(c, 409, "SNAPSHOT_NOT_PENDING", "The snapshot is not pending.");
    }

    const counts = await c.env.DB.prepare(
      `SELECT COUNT(*) AS item_count,
              COUNT(DISTINCT chart_id) AS distinct_count
       FROM best_sync_snapshot_items WHERE snapshot_id = ?1`,
    ).bind(snapshotId).first<{ item_count: number; distinct_count: number }>();
    if (counts?.item_count !== snapshot.expected_item_count ||
        counts.distinct_count !== snapshot.expected_item_count) {
      return errorResponse(c, 400, "SNAPSHOT_COUNT_MISMATCH", "The snapshot item count is invalid.");
    }
    const unknown = await c.env.DB.prepare(
      `SELECT i.chart_id
       FROM best_sync_snapshot_items i
       LEFT JOIN charts c ON c.chart_id = i.chart_id
       WHERE i.snapshot_id = ?1 AND c.chart_id IS NULL
       LIMIT 1`,
    ).bind(snapshotId).first<{ chart_id: string }>();
    if (unknown !== null) {
      return errorResponse(c, 400, "UNKNOWN_CHART", "The snapshot contains an unknown chart.");
    }
    const currentRevision = await c.env.DB.prepare(
      "SELECT best_sync_revision FROM players WHERE id = ?1",
    ).bind(player.id).first<{ best_sync_revision: number }>();
    if (currentRevision?.best_sync_revision !== snapshot.base_sync_revision) {
      return errorResponse(c, 409, "SYNC_CONFLICT", "The published Best set changed after snapshot begin.");
    }
    const difference = await c.env.DB.prepare(
      `SELECT 1 AS changed
       FROM player_chart_bests b
       LEFT JOIN best_sync_snapshot_items i
         ON i.snapshot_id = ?2 AND i.chart_id = b.chart_id
       WHERE b.player_id = ?1 AND (
         i.chart_id IS NULL OR
         b.best_score != i.best_score OR
         b.best_ex_score != i.best_ex_score OR
         b.best_clear_type != i.best_clear_type OR
         b.best_flare_rank IS NOT i.best_flare_rank
       )
       UNION ALL
       SELECT 1 AS changed
       FROM best_sync_snapshot_items i
       LEFT JOIN player_chart_bests b
         ON b.player_id = ?1 AND b.chart_id = i.chart_id
       WHERE i.snapshot_id = ?2 AND b.chart_id IS NULL
       LIMIT 1`,
    ).bind(player.id, snapshotId).first<{ changed: number }>();
    const changed = difference !== null;
    const committedRevision = snapshot.base_sync_revision + (changed ? 1 : 0);

    const statements: D1PreparedStatement[] = [
      c.env.DB.prepare(
        `INSERT INTO best_sync_commit_guards (snapshot_id, player_id, guard)
         VALUES (?1, ?2, CASE
           WHEN (SELECT best_sync_revision FROM players WHERE id = ?2) = ?3 THEN 1
           ELSE 0 END)`,
      ).bind(snapshotId, player.id, snapshot.base_sync_revision),
    ];
    if (changed) {
      statements.push(
        c.env.DB.prepare(
          `DELETE FROM player_chart_bests
           WHERE player_id = ?1 AND chart_id NOT IN (
             SELECT chart_id FROM best_sync_snapshot_items WHERE snapshot_id = ?2
           )`,
        ).bind(player.id, snapshotId),
        c.env.DB.prepare(
          `INSERT INTO player_chart_bests (
             player_id, chart_id, best_score, best_ex_score,
             best_clear_type, best_flare_rank, updated_at
           )
           SELECT ?1, chart_id, best_score, best_ex_score,
                  best_clear_type, best_flare_rank, ?3
           FROM best_sync_snapshot_items WHERE snapshot_id = ?2
           ON CONFLICT(player_id, chart_id) DO UPDATE SET
             best_score = excluded.best_score,
             best_ex_score = excluded.best_ex_score,
             best_clear_type = excluded.best_clear_type,
             best_flare_rank = excluded.best_flare_rank,
             updated_at = excluded.updated_at`,
        ).bind(player.id, snapshotId, now),
        c.env.DB.prepare(
          `UPDATE players SET best_sync_revision = best_sync_revision + 1,
                              public_bests_updated_at = ?1
           WHERE id = ?2`,
        ).bind(now, player.id),
      );
    }
    statements.push(
      c.env.DB.prepare(
        `UPDATE best_sync_snapshots
         SET status = 'COMMITTED', committed_revision = ?1
         WHERE snapshot_id = ?2`,
      ).bind(committedRevision, snapshotId),
      c.env.DB.prepare(
        "DELETE FROM best_sync_snapshot_items WHERE snapshot_id = ?1",
      ).bind(snapshotId),
      c.env.DB.prepare(
        "DELETE FROM best_sync_snapshot_chunks WHERE snapshot_id = ?1",
      ).bind(snapshotId),
      c.env.DB.prepare(
        "DELETE FROM best_sync_commit_guards WHERE snapshot_id = ?1",
      ).bind(snapshotId),
    );
    try {
      await c.env.DB.batch(statements);
    } catch {
      return errorResponse(c, 409, "SYNC_CONFLICT", "The published Best set changed after snapshot begin.");
    }
    return c.json({ sync_revision: committedRevision, changed, retry: false });
  });

  app.delete("/api/v1/me/bests/snapshots/:snapshotId", async (c) => {
    const snapshotId = c.req.param("snapshotId");
    const player = c.get("player");
    const snapshot = await readSnapshot(c.env.DB, snapshotId, player.id);
    if (snapshot === null) {
      return errorResponse(c, 404, "SNAPSHOT_NOT_FOUND", "The snapshot was not found.");
    }
    if (snapshot.status === "PENDING") {
      await c.env.DB.batch([
        c.env.DB.prepare(
          "UPDATE best_sync_snapshots SET status = 'ABORTED' WHERE snapshot_id = ?1",
        ).bind(snapshotId),
        c.env.DB.prepare(
          "DELETE FROM best_sync_snapshot_items WHERE snapshot_id = ?1",
        ).bind(snapshotId),
        c.env.DB.prepare(
          "DELETE FROM best_sync_snapshot_chunks WHERE snapshot_id = ?1",
        ).bind(snapshotId),
      ]);
    }
    return c.body(null, 204);
  });

  app.delete("/api/v1/me/bests", async (c) => {
    const player = c.get("player");
    const count = await c.env.DB.prepare(
      "SELECT COUNT(*) AS count FROM player_chart_bests WHERE player_id = ?1",
    ).bind(player.id).first<{ count: number }>();
    const now = new Date().toISOString();
    const statements: D1PreparedStatement[] = [
      c.env.DB.prepare(
        `UPDATE best_sync_snapshots
         SET status = 'ABORTED'
         WHERE player_id = ?1 AND status = 'PENDING'`,
      ).bind(player.id),
      c.env.DB.prepare(
        `DELETE FROM best_sync_snapshot_items
         WHERE snapshot_id IN (
           SELECT snapshot_id FROM best_sync_snapshots
           WHERE player_id = ?1 AND status = 'ABORTED'
         )`,
      ).bind(player.id),
      c.env.DB.prepare(
        `DELETE FROM best_sync_snapshot_chunks
         WHERE snapshot_id IN (
           SELECT snapshot_id FROM best_sync_snapshots
           WHERE player_id = ?1 AND status = 'ABORTED'
         )`,
      ).bind(player.id),
    ];
    if ((count?.count ?? 0) > 0) {
      statements.push(
        c.env.DB.prepare(
          "DELETE FROM player_chart_bests WHERE player_id = ?1",
        ).bind(player.id),
        c.env.DB.prepare(
          `UPDATE players SET best_sync_revision = best_sync_revision + 1,
                              public_bests_updated_at = ?1
           WHERE id = ?2`,
        ).bind(now, player.id),
      );
    }
    await c.env.DB.batch(statements);
    return c.body(null, 204);
  });
}
