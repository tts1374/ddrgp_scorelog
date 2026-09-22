import { env, exports } from "cloudflare:workers";
import { applyD1Migrations, reset } from "cloudflare:test";
import { beforeEach, describe, expect, it } from "vitest";

const baseUrl = "https://web.example.test";
const playerId = "pl_public_fixture";
const publicPlayerId = "p_aaaaaaaaaaaaaaaaaaaaaa";

async function seedPublicData(): Promise<void> {
  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO players
         (id, public_player_id, display_name, created_at, updated_at, public_bests_updated_at)
       VALUES (?1, ?2, '2TEN', '2026-09-20T00:00:00.000Z', '2026-09-20T00:00:00.000Z', '2026-09-21T01:02:03.000Z')`,
    ).bind(playerId, publicPlayerId),
    env.DB.prepare(
      `INSERT INTO songs (song_id, title, artist, version) VALUES
       ('song_a', 'Alpha MAX', 'Artist A', 'DanceDanceRevolution WORLD'),
       ('song_b', 'beta song', 'Artist B', 'DanceDanceRevolution WORLD'),
       ('song_c', 'Classic', 'Artist C', 'DDR X2'),
       ('song_d', 'Removed only', 'Artist D', 'DanceDanceRevolution A'),
       ('song_e', 'Removed none', 'Artist E', 'DanceDanceRevolution A')`,
    ),
    env.DB.prepare(
      `INSERT INTO charts (chart_id, song_id, play_style, difficulty, level, is_removed) VALUES
       ('chart_a', 'song_a', 'SINGLE', 'EXPERT', 17, 0),
       ('chart_b', 'song_b', 'SINGLE', 'BASIC', 17, 0),
       ('chart_c', 'song_c', 'SINGLE', 'CHALLENGE', 18, 0),
       ('chart_d', 'song_d', 'SINGLE', 'EXPERT', 17, 1),
       ('chart_e', 'song_e', 'SINGLE', 'EXPERT', 17, 1),
       ('chart_dp', 'song_a', 'DOUBLE', 'EXPERT', 18, 0)`,
    ),
    env.DB.prepare(
      `INSERT INTO player_chart_bests
         (player_id, chart_id, best_score, best_ex_score, best_clear_type, best_flare_rank, updated_at)
       VALUES
         (?1, 'chart_a', 990000, 1500, 'PFC', 'IX', '2026-09-21T01:00:00.000Z'),
         (?1, 'chart_c', 900000, 1200, 'FC', 'EX', '2026-09-21T01:00:00.000Z'),
         (?1, 'chart_d', 995000, 1400, 'MFC', 'EX', '2026-09-21T01:00:00.000Z')`,
    ).bind(playerId),
  ]);
}

async function get(path: string): Promise<Response> {
  return exports.default.fetch(`${baseUrl}${path}`);
}

beforeEach(async () => {
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
  await seedPublicData();
});

describe("Public Player API", () => {
  it("returns Overview without internal identity and uses public_bests_updated_at", async () => {
    const response = await get(`/api/v1/public/players/${publicPlayerId}`);
    expect(response.status).toBe(200);
    expect(response.headers.get("Cache-Control")).toBe("no-store");
    expect(response.headers.get("Access-Control-Allow-Origin")).toBeNull();
    const body = await response.json<Record<string, unknown>>();
    expect(JSON.stringify(body)).not.toContain(playerId);
    expect(body).toMatchObject({
      public_player_id: publicPlayerId,
      display_name: "2TEN",
      public_bests_updated_at: "2026-09-21T01:02:03.000Z",
      default_style: "SP",
      styles: {
        SP: {
          published_best_count: 3,
          active_chart_count: 3,
          active_published_best_count: 2,
        },
      },
    });
  });

  it("returns 200 for an existing Player with zero Best and chooses DP only when SP is empty", async () => {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO players (id, public_player_id, display_name, created_at, updated_at)
         VALUES ('pl_empty', 'p_bbbbbbbbbbbbbbbbbbbbbb', 'Empty', '2026-09-20T00:00:00Z', '2026-09-20T00:00:00Z')`,
      ),
      env.DB.prepare(
        `INSERT INTO players (id, public_player_id, display_name, created_at, updated_at)
         VALUES ('pl_dp', 'p_cccccccccccccccccccccc', 'DP', '2026-09-20T00:00:00Z', '2026-09-20T00:00:00Z')`,
      ),
      env.DB.prepare(
        `INSERT INTO player_chart_bests
           (player_id, chart_id, best_score, best_ex_score, best_clear_type, best_flare_rank, updated_at)
         VALUES ('pl_dp', 'chart_dp', 900000, 1000, 'CLEAR', NULL, '2026-09-20T00:00:00Z')`,
      ),
    ]);
    const empty = await (await get("/api/v1/public/players/p_bbbbbbbbbbbbbbbbbbbbbb")).json<Record<string, unknown>>();
    const dp = await (await get("/api/v1/public/players/p_cccccccccccccccccccccc")).json<Record<string, unknown>>();
    expect(empty).toMatchObject({ default_style: "SP", styles: { SP: { published_best_count: 0 }, DP: { published_best_count: 0 } } });
    expect(dp).toMatchObject({ default_style: "DP" });
  });

  it("enforces active/removed inclusion and represents missing Best as null", async () => {
    const response = await get(`/api/v1/public/players/${publicPlayerId}/bests?style=SP&mode=level&level=17&sort=title_asc`);
    expect(response.status).toBe(200);
    const body = await response.json<{ summary: { active_chart_count: number; active_published_best_count: number }; items: Array<{ chart_id: string; is_removed: boolean; best: unknown }> }>();
    expect(body.summary).toEqual({ active_chart_count: 2, active_published_best_count: 1 });
    expect(body.items.map((item) => item.chart_id)).toEqual(["chart_a", "chart_d", "chart_b"]);
    expect(body.items.find((item) => item.chart_id === "chart_b")?.best).toBeNull();
    expect(body.items.find((item) => item.chart_id === "chart_d")?.is_removed).toBe(true);
    expect(body.items.some((item) => item.chart_id === "chart_e")).toBe(false);
  });

  it("supports case-insensitive title substring search and V1 sort semantics", async () => {
    const title = await get(`/api/v1/public/players/${publicPlayerId}/bests?style=SP&mode=title&q=%20MAX%20&sort=score_desc`);
    const titleBody = await title.json<{ summary: null; items: Array<{ chart_id: string; best: { rank: string } | null }> }>();
    expect(titleBody.summary).toBeNull();
    expect(titleBody.items.map((item) => item.chart_id)).toEqual(["chart_a"]);
    expect(titleBody.items[0]?.best?.rank).toBe("AAA");

    const version = await get(`/api/v1/public/players/${publicPlayerId}/bests?style=SP&mode=version&version=${encodeURIComponent("DanceDanceRevolution WORLD")}&sort=score_asc`);
    const versionBody = await version.json<{ items: Array<{ chart_id: string }> }>();
    expect(versionBody.items.map((item) => item.chart_id)).toEqual(["chart_a", "chart_b"]);
  });

  it("uses opaque keyset cursors without duplicate or missing rows", async () => {
    const seen: string[] = [];
    let cursor: string | null = null;
    do {
      const suffix = cursor === null ? "" : `&cursor=${encodeURIComponent(cursor)}`;
      const response = await get(`/api/v1/public/players/${publicPlayerId}/bests?style=SP&mode=title&sort=score_desc&limit=1${suffix}`);
      expect(response.status).toBe(200);
      const body = await response.json<{ items: Array<{ chart_id: string }>; next_cursor: string | null }>();
      seen.push(...body.items.map((item) => item.chart_id));
      cursor = body.next_cursor;
    } while (cursor !== null);
    expect(seen).toEqual(["chart_d", "chart_a", "chart_c", "chart_b"]);
    expect(new Set(seen).size).toBe(seen.length);

    const invalid = await get(`/api/v1/public/players/${publicPlayerId}/bests?style=SP&mode=title&cursor=invalid`);
    expect(invalid.status).toBe(400);
    expect(await invalid.json()).toMatchObject({ error: { code: "INVALID_CURSOR" } });
  });

  it("calculates public Flare Skill from active Best and excludes removed charts", async () => {
    const response = await get(`/api/v1/public/players/${publicPlayerId}/flare-skill?style=SP`);
    expect(response.status).toBe(200);
    const body = await response.json<{ total: number; categories: Array<{ category: string; targets: Array<{ chart_id: string }> }> }>();
    expect(body.categories.flatMap((category) => category.targets).map((target) => target.chart_id).sort()).toEqual(["chart_a", "chart_c"]);
    expect(body.total).toBe(977 + 1040);
  });

  it("validates queries and hides unknown/deleted Player state behind one 404", async () => {
    expect((await get(`/api/v1/public/players/${publicPlayerId}/bests?style=XX&mode=title`)).status).toBe(400);
    expect((await get(`/api/v1/public/players/${publicPlayerId}/bests?style=SP&mode=level&level=20`)).status).toBe(400);
    expect((await get("/api/v1/public/players/p_zzzzzzzzzzzzzzzzzzzzzz")).status).toBe(404);
    await env.DB.prepare("DELETE FROM players WHERE id = ?1").bind(playerId).run();
    expect((await get(`/api/v1/public/players/${publicPlayerId}`)).status).toBe(404);
  });
});

describe("Public Player page", () => {
  it("injects escaped bootstrap, Player metadata, canonical URL, and security headers", async () => {
    await env.DB.prepare("UPDATE players SET display_name = ?1 WHERE id = ?2")
      .bind('</script><script>alert("x")</script>', playerId).run();
    const response = await get(`/player/${publicPlayerId}?style=DP&view=best`);
    expect(response.status).toBe(200);
    expect(response.headers.get("Cache-Control")).toBe("no-store");
    expect(response.headers.get("Content-Security-Policy")).toContain("script-src 'self' 'nonce-");
    expect(response.headers.get("X-Content-Type-Options")).toBe("nosniff");
    expect(response.headers.get("X-Frame-Options")).toBe("DENY");
    const html = await response.text();
    expect(html).toContain("&lt;/script&gt;&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; - GP Score Log");
    expect(html).toContain("\\u003c/script\\u003e\\u003cscript\\u003ealert");
    expect(html).not.toContain("pl_public_fixture");
    expect(html).toContain(`<link rel="canonical" href="https://web.example.test/player/${publicPlayerId}">`);
    expect(html).toContain('<meta name="robots" content="noindex,follow">');
  });

  it("returns a real HTTP 404 instead of the SPA shell", async () => {
    const response = await get("/player/p_zzzzzzzzzzzzzzzzzzzzzz");
    expect(response.status).toBe(404);
    expect(await response.text()).toContain("Playerが見つかりません");
  });
});
