import { env, exports } from "cloudflare:workers";
import { applyD1Migrations, reset } from "cloudflare:test";
import { beforeEach, expect, it } from "vitest";
import { seedPlayer, origin } from "./account-fixture";

beforeEach(async () => {
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
});
it("T10/P6 rejects anonymous registration on every origin without creating data", async () => {
  for (const host of [origin, "https://old-worker.example.test"]) {
    const response = await exports.default.fetch(`${host}/api/v1/players/register`, {
      method: "POST", headers: { "Content-Type": "application/json", "Idempotency-Key": "legacy-request-00000000000000000000" },
      body: JSON.stringify({ display_name: "Player" }),
    });
    expect(response.status).toBe(410);
    expect(await response.json()).toMatchObject({ error: { code: "REGISTRATION_MOVED_TO_WEB" } });
  }
  expect((await env.DB.prepare("SELECT COUNT(*) AS count FROM players").first())?.count).toBe(0);
});
it("T10/P6 rejects Bearer name changes and account deletion and preserves identity", async () => {
  const player = await seedPlayer("Existing name");
  for (const host of [origin, "https://old-worker.example.test"]) {
    for (const [method, code] of [["PATCH", "WEB_PROFILE_REQUIRED"], ["DELETE", "WEB_ACCOUNT_REQUIRED"]]) {
      const response = await exports.default.fetch(`${host}/api/v1/me`, {
        method, headers: { Authorization: `Bearer ${player.credential}`, "Content-Type": "application/json" },
        body: JSON.stringify({ display_name: "Other name" }),
      });
      expect(response.status).toBe(409);
      expect(await response.json()).toMatchObject({ error: { code } });
    }
  }
  const response = await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${player.credential}` } });
  expect(response.status).toBe(200);
  expect(await response.json()).toMatchObject({ public_player_id: player.public_player_id, display_name: player.display_name,
    google_linked: false, activation_state: "active" });
});
it("returns the same generic 401 for unknown, incorrect, revoked and pending credentials", async () => {
  const player = await seedPlayer();
  const [id] = player.credential.split(".");
  for (const token of [`ac_${"A".repeat(22)}.${"B".repeat(43)}`, `${id}.${"C".repeat(43)}`]) {
    const response = await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${token}` } });
    expect(response.status).toBe(401);
    expect(await response.json()).toEqual({ error: { code: "UNAUTHORIZED", message: "The App Credential is invalid." } });
  }
  await env.DB.prepare("UPDATE player_credentials SET activation_state = 'pending'").run();
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${player.credential}` } })).status).toBe(401);
  await env.DB.prepare("UPDATE player_credentials SET activation_state = 'active', revoked_at = 'revoked'").run();
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${player.credential}` } })).status).toBe(401);
});
it("stores digests and keeps Player:Credential 1:N", async () => {
  const player = await seedPlayer();
  const row = await env.DB.prepare("SELECT secret_digest FROM player_credentials").first<{ secret_digest: string }>();
  expect(row?.secret_digest).toMatch(/^[a-f0-9]{64}$/u);
  expect(row?.secret_digest).not.toContain(player.credential.split(".")[1]);
  await env.DB.prepare("INSERT INTO player_credentials (id,player_id,type,secret_digest,created_at) VALUES (?1,?2,'app',?3,'fixture')")
    .bind(`ac_${"Z".repeat(22)}`, player.id, "f".repeat(64)).run();
  expect((await env.DB.prepare("SELECT COUNT(*) AS count FROM player_credentials").first())?.count).toBe(2);
});
it("migrates development identity without deleting or recreating ownership", async () => {
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS.slice(0, 5));
  const player = await seedPlayer("Keep this name");
  const before = await env.DB.prepare("SELECT * FROM players").first();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
  expect(await env.DB.prepare("SELECT * FROM players").first()).toEqual(before);
  expect(await env.DB.prepare("SELECT player_id, activation_state FROM player_credentials").first()).toEqual({ player_id: player.id, activation_state: "active" });
});
it("rejects plaintext before processing credentials", async () => {
  const response = await exports.default.fetch("http://identity.example.test/api/v1/me");
  expect(response.status).toBe(400);
  expect(await response.json()).toMatchObject({ error: { code: "HTTPS_REQUIRED" } });
});
