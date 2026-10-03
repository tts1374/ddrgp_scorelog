import { env, exports } from "cloudflare:workers";
import { applyD1Migrations, reset } from "cloudflare:test";
import { beforeEach, expect, it } from "vitest";
import { deriveCredentialSecret, digestCredentialSecret } from "../src/crypto";
import { canonicalProjection, projectionDigest } from "../src/historical-best";
import worker from "../src/index";

const origin = "https://identity.example.test";
let credential: string;
const item = { chart_id: "chart_1", best_score: 900000, best_ex_score: 1234,
  best_clear_type: "FC", best_flare_rank: "IX" };

async function request(path: string, body?: unknown, method = "POST", token = credential) {
  return exports.default.fetch(`${origin}/api/v1/me/bests/${path}`, {
    method, headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
}
async function merge(items: unknown[]) {
  return request("merge", { projection_version: 1, master_version: "fixture-v1", items });
}
async function state() {
  return { player: await env.DB.prepare("SELECT best_sync_revision, public_bests_updated_at FROM players").first(),
    bests: (await env.DB.prepare("SELECT * FROM player_chart_bests ORDER BY chart_id").all()).results };
}
async function snapshot(items: unknown[]) {
  const begin = await request("snapshots", { projection_version: 1, master_version: "fixture", expected_item_count: items.length });
  const { snapshot_id: id } = await begin.json<{ snapshot_id: string }>();
  if (items.length > 0) expect((await request(`snapshots/${id}/items`, { chunk_id: "one", items }, "PUT")).status).toBe(200);
  return id;
}
async function reviewAndAuthorize(id: string) {
  const review = await request(`snapshots/${id}/replacement-review`);
  expect(review.status).toBe(200);
  const body = await review.json<{ content_digest: string; base_sync_revision: number }>();
  expect((await request(`snapshots/${id}/replacement-authorize`, {
    confirmed: true, content_digest: body.content_digest, base_sync_revision: body.base_sync_revision,
  })).status).toBe(200);
  return body;
}

beforeEach(async () => {
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
  const id = "ac_fixturecredential0000001";
  const secret = await deriveCredentialSecret(env.REGISTRATION_SECRET, "historical-fixture", id);
  credential = `${id}.${secret}`;
  await env.DB.batch([
    env.DB.prepare("INSERT INTO players (id, public_player_id, display_name, created_at, updated_at) VALUES ('pl_fixture', 'p_fixture', 'Player', 'before', 'before')"),
    env.DB.prepare("INSERT INTO player_credentials (id, player_id, type, secret_digest, created_at) VALUES (?1, 'pl_fixture', 'app', ?2, 'before')")
      .bind(id, await digestCredentialSecret(env.CREDENTIAL_PEPPER, secret)),
    env.DB.prepare("INSERT INTO songs (song_id, title, artist, version, title_search_key) VALUES ('song_1', 'Song', 'Artist', 'DDR', 'song')"),
    env.DB.prepare("INSERT INTO charts (chart_id, song_id, play_style, difficulty, level, is_removed) VALUES ('chart_1','song_1','SINGLE','EXPERT',15,0),('chart_removed','song_1','DOUBLE','EXPERT',16,1)"),
  ]);
});

it("H1/H2 preserves absent, lower and null fields and receipts are for the sent projection", async () => {
  expect((await merge([item])).status).toBe(200);
  const initial = await state();
  const lower = { ...item, best_score: 0, best_ex_score: 0, best_clear_type: "FAILED", best_flare_rank: null };
  const response = await merge([lower]);
  expect(await response.json()).toEqual({ results: [{ index: 0, status: "accepted", changed: false,
    received_hash: await projectionDigest(canonicalProjection(lower)) }] });
  expect((await merge([])).status).toBe(200);
  expect(await state()).toEqual(initial);
  await merge([{ ...item, best_score: 800000, best_ex_score: 2000, best_clear_type: "PFC", best_flare_rank: "I" }]);
  expect((await state()).bests[0]).toMatchObject({ best_score: 900000, best_ex_score: 2000, best_clear_type: "PFC", best_flare_rank: "IX" });
  const improved = await state();
  await merge([lower]);
  expect(await state()).toEqual(improved);
});

it("H2 concurrent merges, reverse order and retries keep independent maxima", async () => {
  await Promise.all([
    merge([{ ...item, best_score: 950000, best_flare_rank: null }]),
    merge([{ ...item, best_ex_score: 3000, best_clear_type: "MFC", best_flare_rank: "EX" }]),
  ]);
  expect((await state()).bests[0]).toMatchObject({ best_score: 950000, best_ex_score: 3000, best_clear_type: "MFC", best_flare_rank: "EX" });
  const before = await state();
  await merge([item]);
  await merge([{ ...item, best_ex_score: 3000, best_clear_type: "MFC", best_flare_rank: "EX" }]);
  expect(await state()).toEqual(before);
});

it("H2/H3 accepts 50 projections with ordered receipts, no-op retry and item-local unknown results", async () => {
  const items = Array.from({ length: 50 }, (_, index) => ({ ...item, chart_id: `chart_batch_${index}` }));
  await env.DB.prepare(`INSERT INTO charts (chart_id, song_id, play_style, difficulty, level, is_removed)
    SELECT json_extract(value, '$.chart_id'), 'song_1', 'SINGLE', 'EXPERT', 15, 0 FROM json_each(?1)`)
    .bind(JSON.stringify(items)).run();
  const response = await merge(items);
  expect(response.status).toBe(200);
  const body = await response.json<{ results: Array<{ index: number; status: string; changed: boolean; received_hash: string }> }>();
  expect(body.results).toHaveLength(50);
  for (const [index, projection] of items.entries()) {
    expect(body.results[index]).toEqual({ index, status: "accepted", changed: true,
      received_hash: await projectionDigest(canonicalProjection(projection)) });
  }
  const before = await state();
  expect(before.bests).toHaveLength(50);
  expect(before.player).toMatchObject({ best_sync_revision: 1 });
  const retried = await merge([...items].reverse());
  expect(retried.status).toBe(200);
  expect((await retried.json<{ results: Array<{ changed: boolean }> }>()).results.every(result => !result.changed)).toBe(true);
  expect(await state()).toEqual(before);
  const partial = await merge([...items.slice(0, 49), { ...item, chart_id: "chart_unknown" }]);
  expect(partial.status).toBe(200);
  expect((await partial.json<{ results: unknown[] }>()).results[49]).toEqual({ index: 49, status: "rejected", code: "UNKNOWN_CHART" });
  expect((await merge([...items, { ...item, chart_id: "chart_extra" }])).status).toBe(400);
  expect(await state()).toEqual(before);
});

it("H4 rejects old batch and unconfirmed snapshot without changing live data", async () => {
  await merge([item]);
  const before = await state();
  expect((await request("batch", { operations: [{ type: "delete", chart_id: item.chart_id }] })).status).toBe(409);
  const id = await snapshot([]);
  expect(await (await request(`snapshots/${id}/commit`)).json()).toMatchObject({ error: { code: "REPLACEMENT_CONFIRMATION_REQUIRED" } });
  expect(await state()).toEqual(before);
});

it("H5 reviews all Web rows including removed charts, independent field lowering and empty deletion", async () => {
  await merge([item, { ...item, chart_id: "chart_removed" }]);
  const id = await snapshot([{ ...item, best_score: 0, best_ex_score: 3000, best_flare_rank: null }]);
  const review = await reviewAndAuthorize(id);
  expect(review).toMatchObject({ public_count: 2, eligible_count: 1, removed: ["chart_removed"],
    lowered: [{ chart_id: "chart_1", fields: ["best_score", "best_flare_rank"] }] });
  expect((await request(`snapshots/${id}/commit`)).status).toBe(200);
  const replaced = await state();
  expect((await request(`snapshots/${id}/commit`)).status).toBe(200);
  expect(await state()).toEqual(replaced);
  const empty = await snapshot([]);
  await reviewAndAuthorize(empty);
  expect((await request(`snapshots/${empty}/commit`)).status).toBe(200);
  expect((await state()).bests).toEqual([]);
});

it("H5 permission expires at 600 seconds and retry does not extend it", async () => {
  await merge([item]);
  const before = await state();
  const id = await snapshot([]);
  await reviewAndAuthorize(id);
  await env.DB.prepare("UPDATE best_replacement_authorizations SET expires_at = '2000-01-01T00:00:00.000Z'").run();
  await reviewAndAuthorize(id);
  expect((await request(`snapshots/${id}/commit`)).status).toBe(409);
  expect(await state()).toEqual(before);
});

it("H5 rejects changed chunks, revision conflicts and permission transfer", async () => {
  await merge([item]);
  const before = await state();
  const id = await snapshot([]);
  await reviewAndAuthorize(id);
  await env.DB.prepare(`UPDATE best_sync_snapshots SET expected_item_count = 1 WHERE snapshot_id = ?1`).bind(id).run();
  await request(`snapshots/${id}/items`, { chunk_id: "late", items: [{ ...item, best_score: 0 }] }, "PUT");
  expect((await request(`snapshots/${id}/commit`)).status).toBe(409);
  expect(await state()).toEqual(before);
  const other = await snapshot([]);
  expect((await request(`snapshots/${other}/commit`)).status).toBe(409);
  await reviewAndAuthorize(other);
  await merge([{ ...item, best_score: 999000 }]);
  expect(await (await request(`snapshots/${other}/commit`)).json()).toMatchObject({ error: { code: "SYNC_CONFLICT" } });
});

it("H5 unchanged replacement and simultaneous commit retry keep Best timestamps and revision", async () => {
  await merge([item]);
  const before = await state();
  const id = await snapshot([item]);
  await reviewAndAuthorize(id);
  const results = await Promise.all([request(`snapshots/${id}/commit`), request(`snapshots/${id}/commit`)]);
  for (const result of results) {
    expect(result.status).toBe(200);
    expect(await result.json()).toMatchObject({ changed: false, sync_revision: 1 });
  }
  expect(await state()).toEqual(before);
  expect(await env.DB.prepare("SELECT consumed_at FROM best_replacement_authorizations").first()).toMatchObject({ consumed_at: expect.any(String) });
});

it("H5 replacement digest uses chart ordinal order and fixed V1 field order regardless of upload order", async () => {
  const first = { ...item, chart_id: "chart_removed" }, second = { ...item, best_score: 1 };
  // Deliberately reorder object properties in addition to reversing chart order.
  const id = await snapshot([{ best_flare_rank: first.best_flare_rank, best_score: first.best_score,
    best_clear_type: first.best_clear_type, chart_id: first.chart_id, best_ex_score: first.best_ex_score }, second]);
  const review = await reviewAndAuthorize(id);
  expect(review.content_digest).toBe(await projectionDigest([canonicalProjection(second), canonicalProjection(first)]));
});

it("H5 unknown chart and aborted replacement leave live data and authorization unusable", async () => {
  await merge([item]);
  const before = await state();
  const unknown = await snapshot([{ ...item, chart_id: "unknown" }]);
  expect((await request(`snapshots/${unknown}/replacement-review`)).status).toBe(409);
  expect((await request(`snapshots/${unknown}/commit`)).status).toBe(400);
  expect(await state()).toEqual(before);
  const id = await snapshot([]);
  await reviewAndAuthorize(id);
  expect((await request(`snapshots/${id}`, undefined, "DELETE")).status).toBe(204);
  expect((await request(`snapshots/${id}/commit`)).status).toBe(409);
  expect((await env.DB.prepare("SELECT * FROM best_replacement_authorizations WHERE snapshot_id = ?1").bind(id).all()).results).toEqual([]);
  expect(await state()).toEqual(before);
});

it("H5 late transaction failure rolls back live replacement, proof consumption, revision and staging", async () => {
  await merge([item, { ...item, chart_id: "chart_removed" }]);
  const before = await state();
  const id = await snapshot([{ ...item, best_score: 1 }]);
  await reviewAndAuthorize(id);
  await env.DB.prepare(`CREATE TRIGGER fail_snapshot_commit BEFORE UPDATE OF status ON best_sync_snapshots
    WHEN NEW.status = 'COMMITTED' BEGIN SELECT RAISE(ABORT, 'injected failure'); END`).run();
  expect((await request(`snapshots/${id}/commit`)).status).toBe(409);
  expect(await state()).toEqual(before);
  expect(await env.DB.prepare("SELECT consumed_at FROM best_replacement_authorizations WHERE snapshot_id = ?1").bind(id).first()).toEqual({ consumed_at: null });
  expect((await env.DB.prepare("SELECT * FROM best_sync_snapshot_items WHERE snapshot_id = ?1").bind(id).all()).results).toHaveLength(1);
  await env.DB.prepare("DROP TRIGGER fail_snapshot_commit").run();
  expect((await request(`snapshots/${id}/commit`)).status).toBe(200);
  expect((await state()).bests).toHaveLength(1);
});

it("H4 rejects stale credentials at replacement commit", async () => {
  await merge([item]);
  const before = await state();
  const id = await snapshot([]);
  await reviewAndAuthorize(id);
  for (const [path, method, skip] of [[`snapshots/${id}/commit`, "POST", 1]] as const) {
    await env.DB.prepare("UPDATE player_credentials SET revoked_at = NULL").run();
    let batches = 0;
    const db = { prepare: env.DB.prepare.bind(env.DB), async batch(statements: D1PreparedStatement[]) {
      if (batches++ === skip) await env.DB.prepare("UPDATE player_credentials SET revoked_at = 'revoked'").run();
      return env.DB.batch(statements);
    } } as D1Database;
    const response = await worker.fetch(new Request(`${origin}/api/v1/me/bests/${path}`, {
      method, headers: { Authorization: `Bearer ${credential}` },
    }), { ...env, DB: db });
    expect(response.status).toBe(401);
    expect(await state()).toEqual(before);
  }
});

it("H4 all origins reject previous write routes and migration preserves development ownership", async () => {
  await merge([item]);
  const before = await state();
  for (const host of [origin, "https://old-worker.example.test"])
    expect((await exports.default.fetch(`${host}/api/v1/me/bests/batch`, {
      method: "POST", headers: { Authorization: `Bearer ${credential}`, "Content-Type": "application/json" },
      body: JSON.stringify({ operations: [{ type: "delete", chart_id: item.chart_id }] }),
    })).status).toBe(409);
  expect(await state()).toEqual(before);
  expect(await env.DB.prepare("SELECT id, public_player_id, display_name FROM players").first())
    .toEqual({ id: "pl_fixture", public_player_id: "p_fixture", display_name: "Player" });
  expect(await env.DB.prepare("SELECT id, player_id, activation_state FROM player_credentials").first())
    .toEqual({ id: credential.split(".")[0], player_id: "pl_fixture", activation_state: "active" });
});
