import { env, exports } from "cloudflare:workers";
import { applyD1Migrations, reset } from "cloudflare:test";
import { beforeEach, describe, expect, it } from "vitest";

interface RegistrationResponse {
  public_player_id: string;
  credential: string;
}

const baseUrl = "https://identity.example.test";

async function register(suffix = "0001"): Promise<RegistrationResponse> {
  const response = await exports.default.fetch(`${baseUrl}/api/v1/players/register`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": `best-sync-registration-request-${suffix.padStart(8, "0")}`,
    },
    body: JSON.stringify({ display_name: "Player" }),
  });
  expect(response.status).toBe(201);
  return response.json<RegistrationResponse>();
}

function headers(credential: string): HeadersInit {
  return {
    Authorization: `Bearer ${credential}`,
    "Content-Type": "application/json",
  };
}

async function seedMaster(): Promise<void> {
  await env.DB.batch([
    env.DB.prepare(
      "INSERT INTO songs (song_id, title, artist, version) VALUES ('song_1', 'Song 1', 'Artist', 'DDR')",
    ),
    env.DB.prepare(
      `INSERT INTO charts (chart_id, song_id, play_style, difficulty, level, is_removed)
       VALUES ('chart_1', 'song_1', 'SINGLE', 'EXPERT', 15, 0),
              ('chart_2', 'song_1', 'DOUBLE', 'EXPERT', 16, 0),
              ('chart_3', 'song_1', 'SINGLE', 'CHALLENGE', 18, 0)`,
    ),
  ]);
}

function projection(chartId: string, score = 900_000) {
  return {
    chart_id: chartId,
    best_score: score,
    best_ex_score: 1234,
    best_clear_type: "FC",
    best_flare_rank: "IX",
  };
}

async function delta(
  credential: string,
  operations: unknown[],
): Promise<Response> {
  return exports.default.fetch(`${baseUrl}/api/v1/me/bests/batch`, {
    method: "POST",
    headers: headers(credential),
    body: JSON.stringify({
      projection_version: 1,
      master_version: "fixture-v1",
      operations,
    }),
  });
}

async function beginSnapshot(
  credential: string,
  expectedItemCount: number,
): Promise<{ response: Response; snapshot_id: string; base_sync_revision: number }> {
  const response = await exports.default.fetch(`${baseUrl}/api/v1/me/bests/snapshots`, {
    method: "POST",
    headers: headers(credential),
    body: JSON.stringify({
      projection_version: 1,
      master_version: "fixture-v1",
      expected_item_count: expectedItemCount,
    }),
  });
  return { response, ...(await response.json<Record<string, never>>()) } as never;
}

async function uploadChunk(
  credential: string,
  snapshotId: string,
  chunkId: string,
  items: unknown[],
): Promise<Response> {
  return exports.default.fetch(
    `${baseUrl}/api/v1/me/bests/snapshots/${snapshotId}/items`,
    {
      method: "PUT",
      headers: headers(credential),
      body: JSON.stringify({ chunk_id: chunkId, items }),
    },
  );
}

async function commitSnapshot(credential: string, snapshotId: string): Promise<Response> {
  return exports.default.fetch(
    `${baseUrl}/api/v1/me/bests/snapshots/${snapshotId}/commit`,
    { method: "POST", headers: headers(credential) },
  );
}

beforeEach(async () => {
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
  await seedMaster();
});

describe("Player Best sync API", () => {
  it("keeps batch retries idempotent and isolates UNKNOWN_CHART items", async () => {
    const player = await register();
    const operations = [
      { type: "upsert", item: projection("chart_1") },
      { type: "upsert", item: projection("chart_unknown") },
      { type: "upsert", item: projection("chart_2", 850_000) },
    ];
    const first = await delta(player.credential, operations);
    expect(first.status).toBe(200);
    expect(await first.json()).toEqual({
      results: [
        { index: 0, status: "accepted", changed: true },
        { index: 1, status: "rejected", code: "UNKNOWN_CHART" },
        { index: 2, status: "accepted", changed: true },
      ],
    });
    const stateAfterFirst = await env.DB.prepare(
      `SELECT best_sync_revision, public_bests_updated_at,
              (SELECT COUNT(*) FROM player_chart_bests) AS best_count
       FROM players`,
    ).first<{ best_sync_revision: number; public_bests_updated_at: string; best_count: number }>();
    expect(stateAfterFirst?.best_sync_revision).toBe(1);
    expect(stateAfterFirst?.best_count).toBe(2);

    const retry = await delta(player.credential, operations);
    expect(await retry.json()).toEqual({
      results: [
        { index: 0, status: "accepted", changed: false },
        { index: 1, status: "rejected", code: "UNKNOWN_CHART" },
        { index: 2, status: "accepted", changed: false },
      ],
    });
    const stateAfterRetry = await env.DB.prepare(
      "SELECT best_sync_revision, public_bests_updated_at FROM players",
    ).first<{ best_sync_revision: number; public_bests_updated_at: string }>();
    expect(stateAfterRetry).toEqual({
      best_sync_revision: 1,
      public_bests_updated_at: stateAfterFirst?.public_bests_updated_at,
    });
  });

  it("authenticates from the credential and never accepts a client player_id", async () => {
    const left = await register("left");
    const right = await register("right");
    const response = await delta(left.credential, [{
      type: "upsert",
      player_id: right.public_player_id,
      item: projection("chart_1"),
    }]);
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({
      results: [{ index: 0, status: "accepted", changed: true }],
    });
    const owner = await env.DB.prepare(
      `SELECT p.public_player_id
       FROM player_chart_bests b JOIN players p ON p.id = b.player_id`,
    ).first<{ public_player_id: string }>();
    expect(owner?.public_player_id).toBe(left.public_player_id);
  });

  it("uploads chunks idempotently without changing the live set", async () => {
    const player = await register();
    await delta(player.credential, [{ type: "upsert", item: projection("chart_1") }]);
    const snapshot = await beginSnapshot(player.credential, 1);
    expect(snapshot.response.status).toBe(201);
    const first = await uploadChunk(
      player.credential,
      snapshot.snapshot_id,
      "chunk-1",
      [projection("chart_2")],
    );
    expect(await first.json()).toEqual({ accepted: 1, retry: false });
    const retry = await uploadChunk(
      player.credential,
      snapshot.snapshot_id,
      "chunk-1",
      [projection("chart_2")],
    );
    expect(await retry.json()).toEqual({ accepted: 1, retry: true });
    const live = await env.DB.prepare(
      "SELECT chart_id FROM player_chart_bests ORDER BY chart_id",
    ).all<{ chart_id: string }>();
    expect(live.results).toEqual([{ chart_id: "chart_1" }]);
  });

  it("validates snapshot count and unknown charts before atomic replace", async () => {
    const player = await register();
    await delta(player.credential, [{ type: "upsert", item: projection("chart_1") }]);

    const countMismatch = await beginSnapshot(player.credential, 2);
    await uploadChunk(
      player.credential,
      countMismatch.snapshot_id,
      "chunk-1",
      [projection("chart_2")],
    );
    const countCommit = await commitSnapshot(player.credential, countMismatch.snapshot_id);
    expect(countCommit.status).toBe(400);
    expect(await countCommit.json()).toMatchObject({ error: { code: "SNAPSHOT_COUNT_MISMATCH" } });

    const unknown = await beginSnapshot(player.credential, 1);
    await uploadChunk(
      player.credential,
      unknown.snapshot_id,
      "chunk-1",
      [projection("chart_unknown")],
    );
    const unknownCommit = await commitSnapshot(player.credential, unknown.snapshot_id);
    expect(unknownCommit.status).toBe(400);
    expect(await unknownCommit.json()).toMatchObject({ error: { code: "UNKNOWN_CHART" } });
    expect((await env.DB.prepare("SELECT chart_id FROM player_chart_bests").all()).results)
      .toEqual([{ chart_id: "chart_1" }]);
  });

  it("rejects invalid and duplicate snapshot items without changing the live set", async () => {
    const player = await register();
    await delta(player.credential, [{ type: "upsert", item: projection("chart_1") }]);
    const invalid = await beginSnapshot(player.credential, 1);
    const invalidUpload = await uploadChunk(
      player.credential,
      invalid.snapshot_id,
      "invalid",
      [{ ...projection("chart_2"), best_score: 1_000_001 }],
    );
    expect(invalidUpload.status).toBe(400);
    expect(await invalidUpload.json()).toMatchObject({ error: { code: "INVALID_REQUEST" } });

    const duplicate = await beginSnapshot(player.credential, 2);
    const duplicateInChunk = await uploadChunk(
      player.credential,
      duplicate.snapshot_id,
      "duplicate",
      [projection("chart_2"), projection("chart_2")],
    );
    expect(duplicateInChunk.status).toBe(400);
    expect(await duplicateInChunk.json()).toMatchObject({ error: { code: "DUPLICATE_CHART" } });

    const acrossChunks = await beginSnapshot(player.credential, 2);
    expect((await uploadChunk(
      player.credential,
      acrossChunks.snapshot_id,
      "chunk-1",
      [projection("chart_2")],
    )).status).toBe(200);
    const duplicateAcrossChunks = await uploadChunk(
      player.credential,
      acrossChunks.snapshot_id,
      "chunk-2",
      [projection("chart_2")],
    );
    expect(duplicateAcrossChunks.status).toBe(400);
    expect(await duplicateAcrossChunks.json()).toMatchObject({
      error: { code: "DUPLICATE_CHART" },
    });
    expect((await env.DB.prepare("SELECT chart_id FROM player_chart_bests").all()).results)
      .toEqual([{ chart_id: "chart_1" }]);
  });

  it("atomically replaces the live set and accepts an empty snapshot", async () => {
    const player = await register();
    await delta(player.credential, [
      { type: "upsert", item: projection("chart_1") },
      { type: "upsert", item: projection("chart_2") },
    ]);
    const replacement = await beginSnapshot(player.credential, 1);
    await uploadChunk(
      player.credential,
      replacement.snapshot_id,
      "chunk-1",
      [projection("chart_3", 999_000)],
    );
    const commit = await commitSnapshot(player.credential, replacement.snapshot_id);
    expect(commit.status).toBe(200);
    expect(await commit.json()).toMatchObject({ changed: true, retry: false });
    expect((await env.DB.prepare(
      "SELECT chart_id FROM player_chart_bests ORDER BY chart_id",
    ).all()).results).toEqual([{ chart_id: "chart_3" }]);

    const empty = await beginSnapshot(player.credential, 0);
    const emptyCommit = await commitSnapshot(player.credential, empty.snapshot_id);
    expect(emptyCommit.status).toBe(200);
    expect(await emptyCommit.json()).toMatchObject({ changed: true });
    expect((await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests")
      .first<{ count: number }>())?.count).toBe(0);
  });

  it("rejects stale, expired, and aborted snapshots without changing live Best", async () => {
    const player = await register();
    const stale = await beginSnapshot(player.credential, 1);
    await uploadChunk(
      player.credential,
      stale.snapshot_id,
      "chunk-1",
      [projection("chart_1")],
    );
    await delta(player.credential, [{ type: "upsert", item: projection("chart_2") }]);
    const staleCommit = await commitSnapshot(player.credential, stale.snapshot_id);
    expect(staleCommit.status).toBe(409);
    expect(await staleCommit.json()).toMatchObject({ error: { code: "SYNC_CONFLICT" } });

    const expired = await beginSnapshot(player.credential, 0);
    await env.DB.prepare(
      "UPDATE best_sync_snapshots SET expires_at = '2000-01-01T00:00:00.000Z' WHERE snapshot_id = ?1",
    ).bind(expired.snapshot_id).run();
    const expiredCommit = await commitSnapshot(player.credential, expired.snapshot_id);
    expect(expiredCommit.status).toBe(409);
    expect(await expiredCommit.json()).toMatchObject({ error: { code: "SNAPSHOT_EXPIRED" } });

    const expiringWithItems = await beginSnapshot(player.credential, 1);
    await uploadChunk(
      player.credential,
      expiringWithItems.snapshot_id,
      "chunk-1",
      [projection("chart_1")],
    );
    await env.DB.prepare(
      "UPDATE best_sync_snapshots SET expires_at = '2000-01-01T00:00:00.000Z' WHERE snapshot_id = ?1",
    ).bind(expiringWithItems.snapshot_id).run();
    await beginSnapshot(player.credential, 0);
    expect((await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM best_sync_snapshot_items WHERE snapshot_id = ?1",
    ).bind(expiringWithItems.snapshot_id).first<{ count: number }>())?.count).toBe(0);
    expect((await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM best_sync_snapshot_chunks WHERE snapshot_id = ?1",
    ).bind(expiringWithItems.snapshot_id).first<{ count: number }>())?.count).toBe(0);

    const aborted = await beginSnapshot(player.credential, 0);
    const abort = await exports.default.fetch(
      `${baseUrl}/api/v1/me/bests/snapshots/${aborted.snapshot_id}`,
      { method: "DELETE", headers: headers(player.credential) },
    );
    expect(abort.status).toBe(204);
    const abortedCommit = await commitSnapshot(player.credential, aborted.snapshot_id);
    expect(abortedCommit.status).toBe(409);
    expect(await abortedCommit.json()).toMatchObject({ error: { code: "SNAPSHOT_NOT_PENDING" } });
    expect((await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM best_sync_snapshot_items WHERE snapshot_id = ?1",
    ).bind(aborted.snapshot_id).first<{ count: number }>())?.count).toBe(0);
  });

  it("deletes only public Best and preserves Player identity and credential", async () => {
    const player = await register();
    await delta(player.credential, [{ type: "upsert", item: projection("chart_1") }]);
    const pending = await beginSnapshot(player.credential, 1);
    await uploadChunk(
      player.credential,
      pending.snapshot_id,
      "chunk-1",
      [projection("chart_2")],
    );
    const deletion = await exports.default.fetch(`${baseUrl}/api/v1/me/bests`, {
      method: "DELETE",
      headers: headers(player.credential),
    });
    expect(deletion.status).toBe(204);
    expect((await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests")
      .first<{ count: number }>())?.count).toBe(0);
    const me = await exports.default.fetch(`${baseUrl}/api/v1/me`, {
      headers: headers(player.credential),
    });
    expect(me.status).toBe(200);
    expect(await me.json()).toMatchObject({ public_player_id: player.public_player_id });
    const staleCommit = await commitSnapshot(player.credential, pending.snapshot_id);
    expect(staleCommit.status).toBe(409);
    expect(await staleCommit.json()).toMatchObject({ error: { code: "SNAPSHOT_NOT_PENDING" } });
  });

  it("uses the shared master lookup and ranking indexes", async () => {
    const joined = await env.DB.prepare(
      `SELECT c.chart_id, s.title
       FROM charts c JOIN songs s ON s.song_id = c.song_id
       WHERE c.chart_id = 'chart_1'`,
    ).first<{ chart_id: string; title: string }>();
    expect(joined).toEqual({ chart_id: "chart_1", title: "Song 1" });
    const scorePlan = await env.DB.prepare(
      "EXPLAIN QUERY PLAN SELECT * FROM player_chart_bests WHERE chart_id = 'chart_1' ORDER BY best_score DESC",
    ).all<{ detail: string }>();
    const exPlan = await env.DB.prepare(
      "EXPLAIN QUERY PLAN SELECT * FROM player_chart_bests WHERE chart_id = 'chart_1' ORDER BY best_ex_score DESC",
    ).all<{ detail: string }>();
    expect(scorePlan.results.some((row) => row.detail.includes("idx_player_chart_bests_chart_score"))).toBe(true);
    expect(exPlan.results.some((row) => row.detail.includes("idx_player_chart_bests_chart_ex_score"))).toBe(true);
  });
});
