import { env, exports } from "cloudflare:workers";
import { applyD1Migrations, reset } from "cloudflare:test";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { validateGoogleToken } from "../src/google-oidc";
import { accountRequest } from "../src/client/management-api";
import worker from "../src/index";
import { appRequest, BrowserFixture, GoogleFixture, origin, seedPlayer, startApp } from "./account-fixture";

let google: GoogleFixture;
let browser: BrowserFixture;
beforeEach(async () => {
  vi.unstubAllGlobals();
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
  google = new GoogleFixture();
  await google.initialize();
  browser = new BrowserFixture();
});
afterEach(() => vi.unstubAllGlobals());
async function counts() {
  return env.DB.prepare(`SELECT (SELECT COUNT(*) FROM players) AS players,
    (SELECT COUNT(*) FROM google_identities) AS identities,
    (SELECT COUNT(*) FROM player_credentials) AS credentials`).first();
}
async function approve(id: string) {
  return browser.request(`/api/v1/auth/app-authorizations/${id}/approve`, { confirmed: true, app_compared: true });
}
async function seedBest(publicId: string) {
  await env.DB.batch([
    env.DB.prepare("INSERT INTO songs (song_id,title,artist,version,title_search_key) VALUES ('account-song','Song','Artist','DDR','song')"),
    env.DB.prepare("INSERT INTO charts (chart_id,song_id,play_style,difficulty,level,is_removed) VALUES ('account-chart','account-song','SINGLE','EXPERT',15,0)"),
    env.DB.prepare(`INSERT INTO player_chart_bests (player_id,chart_id,best_score,best_ex_score,best_clear_type,best_flare_rank,updated_at)
      SELECT id,'account-chart',900000,2000,'PFC','IX','before-transfer' FROM players WHERE public_player_id = ?1`).bind(publicId),
  ]);
  return (await env.DB.prepare("SELECT * FROM player_chart_bests").all()).results;
}
async function registered() {
  const app = await startApp();
  expect(app.response.status).toBe(201);
  await browser.operation(google, app.id, "register");
  expect((await approve(app.id)).status).toBe(200);
  const result = await appRequest(app.id, app.request_secret, "result");
  expect(result.status).toBe(200);
  const body = await result.json<{ credential: string; public_player_id: string }>();
  expect((await appRequest(app.id, app.request_secret, "activate", body.credential)).status).toBe(200);
  return { ...app, ...body };
}
async function deletionOAuth() {
  const start = await browser.request("/api/v1/account/deletion-start", {});
  expect(start.status).toBe(200);
  const { url } = await start.json<{ url: string }>();
  const redirect = await browser.request(url);
  const callback = await google.callback(browser, redirect.headers.get("Location")!);
  expect(callback.headers.get("Location")).toBe("/my/account-delete");
  const current = await browser.request("/api/v1/account/deletion-confirmations/current");
  return current.json<{ id: string; status: string }>();
}

async function publicDeletionOAuth(overrides: Record<string, unknown> = {}) {
  const start = await browser.request("/api/v1/account/bests/deletion-start", {});
  expect(start.status).toBe(200);
  const { url } = await start.json<{ url: string }>();
  const redirect = await browser.request(url);
  return google.callback(browser, redirect.headers.get("Location")!, overrides);
}

it("public-record deletion requires its own purpose proof, explicit consent and Origin/CSRF", async () => {
  const active = await registered();
  await seedBest(active.public_player_id);
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).status).toBe(403);
  await deletionOAuth();
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).status).toBe(403);
  expect((await publicDeletionOAuth()).headers.get("Location")).toBe("/my/public-data-delete");
  expect(await (await browser.request("/api/v1/account/bests/deletion-confirmation")).json()).toEqual({ status: "UNCONFIRMED" });
  expect((await browser.request("/api/v1/account/bests", { confirmed: false, sync_stopped: true }, "DELETE")).status).toBe(400);
  expect((await browser.request("/api/v1/account/bests", { confirmed: true }, "DELETE")).status).toBe(400);
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE", { Origin: "https://foreign.example" })).status).toBe(403);
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE", { "X-CSRF-Token": "bad" })).status).toBe(403);
  expect((await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests").first())?.count).toBe(1);
  expect(await env.DB.prepare("SELECT consumed_at FROM web_operation_proofs WHERE purpose = 'public-bests-delete'").first()).toEqual({ consumed_at: null });
});

it("public-record reset deletes only Best, aborts staging and never re-deletes data on a lost-response retry", async () => {
  const active = await registered();
  await seedBest(active.public_player_id);
  const credentialId = active.credential.split(".")[0];
  const now = new Date().toISOString();
  await env.DB.prepare(`INSERT INTO best_sync_snapshots (snapshot_id, player_id, projection_version, master_version,
    expected_item_count, base_sync_revision, status, created_at, expires_at, credential_id)
    SELECT 'reset-pending',id,1,'fixture',0,0,'PENDING',?1,'2099-01-01T00:00:00.000Z',?2 FROM players`)
    .bind(now, credentialId).run();
  await publicDeletionOAuth();
  expect(await (await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).json()).toEqual({ status: "DELETED", retry: false });
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests").first()).toEqual({ count: 0 });
  expect(await env.DB.prepare("SELECT status FROM best_sync_snapshots").first()).toEqual({ status: "ABORTED" });
  const first = await env.DB.prepare("SELECT best_sync_revision, public_bests_updated_at FROM players").first();
  expect(first).toMatchObject({ best_sync_revision: 1 });
  expect(first?.public_bests_updated_at).not.toBeNull();
  expect((await browser.request("/api/v1/account/profile")).status).toBe(200);
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${active.credential}` } })).status).toBe(200);
  expect((await exports.default.fetch(`${origin}/player/${active.public_player_id}`)).status).toBe(200);
  await env.DB.prepare(`INSERT INTO player_chart_bests (player_id,chart_id,best_score,best_ex_score,best_clear_type,best_flare_rank,updated_at)
    SELECT id,'account-chart',950000,2100,'PFC','IX','new-sync' FROM players`).run();
  expect(await (await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).json()).toEqual({ status: "DELETED", retry: true });
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests").first()).toEqual({ count: 1 });
  expect(await env.DB.prepare("SELECT best_sync_revision, public_bests_updated_at FROM players").first()).toEqual(first);
  expect(await (await browser.request("/api/v1/account/bests/deletion-confirmation")).json()).toEqual({ status: "DELETED" });
});

it("empty public-record deletion leaves revision and public timestamp unchanged", async () => {
  await registered();
  await publicDeletionOAuth();
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).status).toBe(200);
  expect(await env.DB.prepare("SELECT best_sync_revision, public_bests_updated_at FROM players").first()).toEqual({ best_sync_revision: 0, public_bests_updated_at: null });
});

it("public-record proof rejects another Google identity and expired confirmation", async () => {
  const active = await registered();
  await seedBest(active.public_player_id);
  expect((await publicDeletionOAuth({ sub: "foreign-sub" })).headers.get("Location")).toContain("auth_error=identity");
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).status).toBe(403);
  await publicDeletionOAuth();
  await env.DB.prepare("UPDATE web_operation_proofs SET expires_at = '2000-01-01T00:00:00.000Z' WHERE purpose = 'public-bests-delete'").run();
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).status).toBe(403);
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests").first()).toEqual({ count: 1 });
});

it("public-record cancellation invalidates only its own pending OAuth and proof", async () => {
  const active = await registered();
  await seedBest(active.public_player_id);
  await deletionOAuth();
  await publicDeletionOAuth();
  expect((await browser.request("/api/v1/account/bests/deletion-cancel", {})).status).toBe(204);
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).status).toBe(403);
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM web_operation_proofs WHERE purpose = 'public-bests-delete'").first()).toEqual({ count: 0 });
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM web_oauth_requests WHERE purpose = 'public-bests-delete'").first()).toEqual({ count: 0 });
  expect((await browser.request("/api/v1/account/deletion-confirmations/current")).status).toBe(200);
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests").first()).toEqual({ count: 1 });
});

it("public-record migration preserves pending ordinary OAuth requests", async () => {
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS.slice(0, 7));
  await env.DB.prepare(`INSERT INTO web_oauth_requests (state_digest,nonce,browser_digest,purpose,created_at,expires_at)
    VALUES ('existing-state','existing-nonce','existing-browser','login','before','2099-01-01T00:00:00.000Z')`).run();
  const before = await env.DB.prepare("SELECT * FROM web_oauth_requests").first();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
  expect(await env.DB.prepare("SELECT * FROM web_oauth_requests").first()).toEqual(before);
});

it("public-record deletion rolls back a late failure and rejects an in-flight unlink", async () => {
  const active = await registered();
  await seedBest(active.public_player_id);
  await publicDeletionOAuth();
  await env.DB.prepare(`CREATE TRIGGER fail_public_delete BEFORE UPDATE OF consumed_at ON web_operation_proofs
    WHEN OLD.purpose = 'public-bests-delete' BEGIN SELECT RAISE(ABORT, 'fixture late failure'); END`).run();
  expect((await browser.request("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, "DELETE")).status).toBe(403);
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests").first()).toEqual({ count: 1 });
  expect(await env.DB.prepare("SELECT best_sync_revision FROM players").first()).toEqual({ best_sync_revision: 0 });
  expect(await env.DB.prepare("SELECT consumed_at FROM web_operation_proofs WHERE purpose = 'public-bests-delete'").first()).toEqual({ consumed_at: null });
  await env.DB.prepare("DROP TRIGGER fail_public_delete").run();
  const unlink = await startApp(active.credential, undefined, "unlink");
  await browser.operation(google, unlink.id, "login");
  const response = await interleavedRequest("/api/v1/account/bests", { confirmed: true, sync_stopped: true }, () => approve(unlink.id), 1, undefined, "DELETE");
  expect(response.status).toBe(401);
  expect(await env.DB.prepare("SELECT COUNT(*) AS count FROM player_chart_bests").first()).toEqual({ count: 1 });
});

it("P8 frontend request serialization reaches the real Worker deletion OAuth and atomic delete", async () => {
  const app = await registered();
  const googleFetch = globalThis.fetch;
  vi.stubGlobal("fetch", async (input: RequestInfo | URL, options?: RequestInit) => {
    if (typeof input === "string" && input.startsWith("/api/v1/")) {
      const headers = new Headers(options?.headers);
      headers.set("Origin", origin);
      headers.set("Cookie", [...browser.cookies].map(([key, value]) => `${key}=${value}`).join("; "));
      return exports.default.fetch(`${origin}${input}`, { ...options, headers });
    }
    return googleFetch(input, options);
  });
  const start = await accountRequest<{ url: string }>("account/deletion-start", "POST", browser.csrf, {});
  const oauth = await browser.request(start.url);
  expect(oauth.status).toBe(302);
  expect((await google.callback(browser, oauth.headers.get("Location")!)).headers.get("Location")).toBe("/my/account-delete");
  expect((await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true })).status).toBe(200);
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(200);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
  expect((await exports.default.fetch(`${origin}/player/${app.public_player_id}`)).status).toBe(404);
});

it("T1/P1 normal login issues only a short session for an unregistered Google", async () => {
  await browser.login(google);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
  expect((await browser.request("/api/v1/account/profile")).status).toBe(409);
  const session = await browser.session();
  expect(session).toMatchObject({ email: "private@example.test", google_linked: false });
  expect((await browser.request("/api/v1/auth/web/logout", {})).status).toBe(204);
  expect((await browser.request("/api/v1/account/session")).status).toBe(401);
});

it("T1/T3 requires operation proof and final App comparison, creates atomically and retries the same credential", async () => {
  await browser.login(google);
  const app = await startApp();
  expect((await approve(app.id)).status).toBe(403);
  await browser.operation(google, app.id, "register");
  expect((await browser.request(`/api/v1/auth/app-authorizations/${app.id}/approve`, { confirmed: true })).status).toBe(409);
  const before = await counts();
  expect(before).toEqual({ players: 0, identities: 0, credentials: 0 });
  expect((await approve(app.id)).status).toBe(200);
  const first = await (await appRequest(app.id, app.request_secret, "result")).json();
  expect(await (await appRequest(app.id, app.request_secret, "result")).json()).toEqual(first);
  expect(first).toMatchObject({ display_name: "Player", status: "APPROVED" });
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  expect((await approve(app.id)).status).toBe(200);
});

it("T1 registers no duplicate Player and never creates from unregistered login", async () => {
  await registered();
  const app = await startApp();
  await browser.operation(google, app.id, "register");
  expect(await (await approve(app.id)).json()).toMatchObject({ error: { code: "GOOGLE_ALREADY_REGISTERED" } });
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  expect((await browser.request("/api/v1/auth/web/logout", { app_authorization_id: app.id })).status).toBe(204);
  await browser.context();
  await browser.operation(google, app.id, "login", { sub: "unregistered-sub" });
  expect(await (await approve(app.id)).json()).toMatchObject({ error: { code: "PLAYER_NOT_LINKED" } });
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
});

it("unified App entry resolves a new Google to registration but waits for explicit approval and activation", async () => {
  const app = await startApp();
  const callback = await browser.operation(google, app.id);
  expect(callback.headers.get("Location")).toBe(`/my/app-connect?request=${app.id}`);
  expect(await (await browser.request(`/api/v1/auth/app-authorizations/${app.id}/confirmation`)).json())
    .toMatchObject({ status: "PENDING", intent: "register", player: null, registered_google: false });
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
  expect(await env.DB.prepare("SELECT purpose, consumed_at FROM web_operation_proofs").first())
    .toEqual({ purpose: "register", consumed_at: null });
  expect((await browser.request(`/api/v1/auth/app-authorizations/${app.id}/approve`, { confirmed: true, app_compared: false })).status).toBe(409);
  expect((await browser.request(`/api/v1/auth/app-authorizations/${app.id}/approve`, { confirmed: false, app_compared: true })).status).toBe(409);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
  expect((await approve(app.id)).status).toBe(200);
  const result = await (await appRequest(app.id, app.request_secret, "result"))
    .json<{ credential: string; public_player_id: string }>();
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${result.credential}` } })).status).toBe(401);
  expect((await appRequest(app.id, app.request_secret, "activate", result.credential)).status).toBe(200);
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${result.credential}` } })).status).toBe(200);
});

it("unified App entry resolves a registered Google to transfer and retains the Player and Best", async () => {
  const owner = await registered();
  const best = await seedBest(owner.public_player_id);
  const app = await startApp();
  await browser.operation(google, app.id);
  expect(await (await browser.request(`/api/v1/auth/app-authorizations/${app.id}/confirmation`)).json())
    .toMatchObject({ intent: "login", registered_google: true, player: { public_player_id: owner.public_player_id } });
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  expect((await approve(app.id)).status).toBe(200);
  const result = await (await appRequest(app.id, app.request_secret, "result"))
    .json<{ credential: string; public_player_id: string }>();
  expect(result.public_player_id).toBe(owner.public_player_id);
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${owner.credential}` } })).status).toBe(200);
  expect((await appRequest(app.id, app.request_secret, "activate", result.credential)).status).toBe(200);
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${owner.credential}` } })).status).toBe(401);
  expect((await env.DB.prepare("SELECT * FROM player_chart_bests").all()).results).toEqual(best);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 2 });
});

it("unified App recovery with an expected public ID never creates from an unregistered Google", async () => {
  const owner = await seedPlayer();
  const app = await startApp(undefined, owner.public_player_id);
  expect((await browser.operation(google, app.id)).headers.get("Location")).toContain("auth_error=player");
  expect((await approve(app.id)).status).toBe(401);
  expect(await counts()).toEqual({ players: 1, identities: 0, credentials: 1 });
});

it.each(["connect", "unlink"])("unified entry retains App credential binding for %s", async purpose => {
  const owner = purpose === "unlink" ? await registered() : await seedPlayer();
  const app = await startApp(owner.credential, owner.public_player_id, purpose);
  await browser.operation(google, app.id);
  expect(await (await browser.request(`/api/v1/auth/app-authorizations/${app.id}/confirmation`)).json())
    .toMatchObject({ intent: purpose === "unlink" ? "unlink" : "link", player: { public_player_id: owner.public_player_id } });
  expect(await (await approve(app.id)).json()).toMatchObject({ status: purpose === "unlink" ? "UNLINKED" : "LINKED" });
  expect(await counts()).toEqual({ players: 1, identities: purpose === "unlink" ? 0 : 1, credentials: 1 });
});

it("T2 links a development Player without changing name, identity, credential or Best", async () => {
  const legacy = await seedPlayer("Existing name");
  const app = await startApp(legacy.credential, legacy.public_player_id);
  const callback = await browser.operation(google, app.id, "login");
  expect(callback.headers.get("Location")).toBe(`/my/app-connect?request=${app.id}`);
  expect(await (await approve(app.id)).json()).toMatchObject({ status: "LINKED" });
  const result = await (await appRequest(app.id, app.request_secret, "result")).json();
  expect(result).toMatchObject({ public_player_id: legacy.public_player_id, display_name: "Existing name" });
  expect(result).not.toHaveProperty("credential");
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
});

it("T2/T4 rejects another local Player, invalid bearer and wrong expected public ID", async () => {
  const owner = await registered();
  const other = await seedPlayer();
  const foreign = await startApp(other.credential, other.public_player_id);
  const callback = await browser.operation(google, foreign.id, "login");
  expect(callback.headers.get("Location")).toContain("auth_error=conflict");
  expect((await approve(foreign.id)).status).toBe(403);
  expect((await startApp("invalid-token")).response.status).toBe(401);
  const mismatch = await startApp(undefined, other.public_player_id);
  const failed = await browser.operation(google, mismatch.id, "login");
  expect(failed.headers.get("Location")).toContain("auth_error=player");
  const same = await startApp(undefined, owner.public_player_id);
  await browser.operation(google, same.id, "login");
  expect((await approve(same.id)).status).toBe(200);
});

it("T3/P2 accepts absent azp, matching azp and absent or old auth_time", async () => {
  for (const overrides of [{}, { azp: env.GOOGLE_CLIENT_ID }, { auth_time: 1 }, { auth_time: "bad" }]) {
    const token = await google.token("nonce", overrides);
    expect(await validateGoogleToken(token, env.GOOGLE_CLIENT_ID!, "nonce")).toMatchObject({ sub: "google-fixture-sub" });
  }
});
it.each([
  { azp: "other-client" }, { azp: null }, { azp: [] }, { aud: "other-client" },
  { aud: ["test-client.apps.googleusercontent.com"] }, { iss: "https://attacker.test" },
  { exp: 1 }, { iat: 99999999999 }, { nonce: "wrong" }, { sub: "" }, { email_verified: false },
])("T3/P2 rejects invalid Google claims %j", async overrides => {
  await expect(validateGoogleToken(await google.token("nonce", overrides), env.GOOGLE_CLIENT_ID!, "nonce")).rejects.toThrow();
});

it("T3/P2 rejects invalid state, another browser, callback replay and arbitrary redirect", async () => {
  const start = await browser.request("/api/v1/auth/web/google/start");
  const url = start.headers.get("Location")!;
  expect((await new BrowserFixture().request(url)).status).toBe(400);
  expect((await browser.request("/api/v1/auth/web/google/start?redirect=https://attacker.test")).status).toBe(400);
  const auth = await browser.request(url);
  const callback = await google.callback(browser, auth.headers.get("Location")!);
  expect(callback.headers.get("Location")).toBe("/my/profile");
  expect((await google.callback(browser, auth.headers.get("Location")!)).headers.get("Location")).toBe("/my/auth-error");
  expect((await browser.request("/api/v1/auth/web/google/callback?state=bad&code=bad")).headers.get("Location")).toBe("/my/auth-error");
});

it("T3 keeps App-start TTL and operation-proof TTL independent and refuses expired confirmation", async () => {
  const app = await startApp();
  const initial = await app.response.json<{ expires_at: string }>();
  await browser.operation(google, app.id, "register", { auth_time: 1 });
  const proof = await env.DB.prepare("SELECT confirmed_at,expires_at FROM web_operation_proofs").first<{ confirmed_at: string; expires_at: string }>();
  expect(Date.parse(proof!.expires_at) - Date.parse(proof!.confirmed_at)).toBe(600_000);
  await env.DB.prepare("UPDATE web_operation_proofs SET expires_at = '2000-01-01T00:00:00Z'").run();
  expect((await approve(app.id)).status).toBe(403);
  await browser.operation(google, app.id, "register");
  expect(await env.DB.prepare("SELECT expires_at FROM app_authorizations").first()).toEqual({ expires_at: initial.expires_at });
  await env.DB.prepare("UPDATE app_authorizations SET expires_at = '2000-01-01T00:00:00Z'").run();
  expect((await approve(app.id)).status).toBe(409);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
});

it("T5/T6 activation preserves the old PC until saved, then revokes before first sync and allows result retry after TTL", async () => {
  const old = await registered();
  const beforeBests = await seedBest(old.public_player_id);
  const next = await startApp();
  await browser.operation(google, next.id, "login");
  await approve(next.id);
  const result = await (await appRequest(next.id, next.request_secret, "result")).json<{ credential: string; public_player_id: string }>();
  expect(result.public_player_id).toBe(old.public_player_id);
  const me = (token: string) => exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${token}` } });
  expect((await me(old.credential)).status).toBe(200);
  expect((await me(result.credential)).status).toBe(401);
  expect((await appRequest(next.id, next.request_secret, "activate", result.credential)).status).toBe(200);
  expect((await me(old.credential)).status).toBe(401);
  expect((await me(result.credential)).status).toBe(200);
  await env.DB.prepare("UPDATE app_authorizations SET expires_at = '2000-01-01T00:00:00Z'").run();
  expect((await appRequest(next.id, next.request_secret, "activate", result.credential)).status).toBe(200);
  expect((await appRequest(old.id, old.request_secret, "activate", old.credential)).status).toBe(401);
  expect((await env.DB.prepare("SELECT * FROM player_chart_bests").all()).results).toEqual(beforeBests);
});

it("T5/T9 keeps approval separate from activation and status never discloses secrets", async () => {
  const app = await startApp();
  await browser.operation(google, app.id, "register");
  await approve(app.id);
  expect(await (await browser.request(`/api/v1/auth/app-authorizations/${app.id}/status`)).json()).toEqual({ status: "APPROVED" });
  expect((await appRequest(app.id, "x".repeat(43), "result")).status).toBe(401);
  expect(await (await appRequest(app.id, app.request_secret, "cancel")).json()).toEqual({ status: "APPROVED" });
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
});

it.each(["google-fixture-sub", "replacement-google-sub"])("T8 unlink preserves active App and permits relink to %s with the expected public ID", async sub => {
  const active = await registered();
  const beforeBests = await seedBest(active.public_player_id);
  const otherBrowser = new BrowserFixture();
  await otherBrowser.login(google);
  const pending = await startApp();
  await browser.operation(google, pending.id, "login");
  await approve(pending.id);
  const pendingCredential = await (await appRequest(pending.id, pending.request_secret, "result")).json<{ credential: string }>();
  const unlink = await startApp(active.credential, active.public_player_id, "unlink");
  await browser.operation(google, unlink.id, "login");
  expect(await (await approve(unlink.id)).json()).toEqual({ status: "UNLINKED" });
  expect((await otherBrowser.request("/api/v1/account/profile")).status).toBe(401);
  expect((await appRequest(pending.id, pending.request_secret, "activate", pendingCredential.credential)).status).toBe(401);
  expect(await (await appRequest(unlink.id, unlink.request_secret, "result")).json()).toMatchObject({ status: "UNLINKED" });
  expect(await counts()).toEqual({ players: 1, identities: 0, credentials: 2 });
  const relink = await startApp(active.credential, active.public_player_id);
  // The old Web session is gone; create a browser-bound CSRF context.
  browser.cookies.delete("__Host-ddrgp-session");
  await browser.context();
  const callback = await browser.operation(google, relink.id, "login", { sub });
  expect(callback.headers.get("Location")).toBe(`/my/app-connect?request=${relink.id}`);
  expect(await (await approve(relink.id)).json()).toEqual({ status: "LINKED" });
  const result = await (await appRequest(relink.id, relink.request_secret, "result")).json();
  expect(result).toMatchObject({ status: "LINKED", public_player_id: active.public_player_id });
  expect(result).not.toHaveProperty("credential");
  expect((await otherBrowser.request("/api/v1/account/profile")).status).toBe(401);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 2 });
  expect((await env.DB.prepare("SELECT * FROM player_chart_bests").all()).results).toEqual(beforeBests);
});

it("T4 an expected public ID alone cannot link an unregistered Google to an existing Player", async () => {
  const existing = await seedPlayer();
  expect((await startApp(existing.credential, `p_${"x".repeat(22)}`)).response.status).toBe(409);
  const app = await startApp(undefined, existing.public_player_id);
  const callback = await browser.operation(google, app.id, "login");
  expect(callback.headers.get("Location")).toContain("auth_error=player");
  expect((await approve(app.id)).status).toBe(401);
  expect(await counts()).toEqual({ players: 1, identities: 0, credentials: 1 });
});

it("T2 relink with the expected public ID rejects an App credential revoked before approval", async () => {
  const existing = await seedPlayer();
  const app = await startApp(existing.credential, existing.public_player_id);
  const callback = await browser.operation(google, app.id, "login");
  expect(callback.headers.get("Location")).toBe(`/my/app-connect?request=${app.id}`);
  await env.DB.prepare("UPDATE player_credentials SET revoked_at = ?1 WHERE player_id = ?2")
    .bind(new Date().toISOString(), existing.id).run();
  expect((await approve(app.id)).status).toBe(409);
  expect(await counts()).toEqual({ players: 1, identities: 0, credentials: 1 });
  expect(await env.DB.prepare("SELECT consumed_at FROM web_operation_proofs WHERE app_authorization_id = ?1")
    .bind(app.id).first()).toEqual({ consumed_at: null });
});

it("P3/P4 profile edits only public name, validates boundaries and keeps private claims out of App/public responses", async () => {
  const app = await registered();
  const invalid = [{ display_name: " " }, { display_name: "x".repeat(65) }, { display_name: "X", player_id: "other" }];
  for (const body of invalid) expect((await browser.request("/api/v1/account/profile", body, "PATCH")).status).toBe(400);
  expect((await browser.request("/api/v1/account/profile", { display_name: "X" }, "PATCH", { Origin: "https://attacker.test" })).status).toBe(403);
  expect((await browser.request("/api/v1/account/profile", { display_name: "X" }, "PATCH", { "X-CSRF-Token": "bad" })).status).toBe(403);
  expect((await browser.request("/api/v1/account/profile", { display_name: " <script>name</script> " }, "PATCH")).status).toBe(200);
  const me = await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${app.credential}` } });
  const body = await me.text();
  expect(body).not.toContain("private@example.test");
  expect(body).not.toContain("google-fixture-sub");
  expect(await env.DB.prepare("SELECT best_sync_revision, public_bests_updated_at FROM players").first()).toEqual({ best_sync_revision: 0, public_bests_updated_at: null });
});

it("P8 rejects ordinary session deletion and recovers both lost confirmation and DELETE responses using the preheld proof", async () => {
  const active = await registered();
  await seedBest(active.public_player_id);
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(403);
  expect((await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true })).status).toBe(403);
  const pending = await deletionOAuth();
  expect(pending.status).toBe("UNCONFIRMED");
  expect(await (await browser.request(`/api/v1/account/deletion-confirmations/${pending.id}/result`, {})).json()).toMatchObject({ status: "UNCONFIRMED" });
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(403);
  expect((await browser.request("/api/v1/account/deletion-confirmations", { confirmed: false })).status).toBe(409);
  expect((await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true })).status).toBe(200);
  const confirmed = await (await browser.request(`/api/v1/account/deletion-confirmations/${pending.id}/result`, {})).json();
  expect(confirmed).toMatchObject({ status: "CONFIRMED" });
  expect(await (await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true })).json()).toMatchObject({ status: "CONFIRMED", retry: true });
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(200);
  expect(await (await browser.request(`/api/v1/account/deletion-confirmations/${pending.id}/result`, {})).json()).toMatchObject({ status: "DELETED" });
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
  expect((await env.DB.prepare("SELECT * FROM player_chart_bests").all()).results).toEqual([]);
  const privateResult = await env.DB.prepare("SELECT * FROM account_deletion_confirmations").first();
  expect(privateResult).toMatchObject({ player_id: null, issuer: null, sub: null, session_digest: null, browser_digest: null, operation_digest: null });
  expect((await exports.default.fetch(`${origin}/api/v1/me`, { headers: { Authorization: `Bearer ${active.credential}` } })).status).toBe(401);
});

it("P8 expiry is service time and confirmation retries never renew it", async () => {
  await registered();
  const pending = await deletionOAuth();
  await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true });
  const row = await env.DB.prepare("SELECT confirmed_at,expires_at FROM account_deletion_confirmations").first<{ confirmed_at: string; expires_at: string }>();
  expect(Date.parse(row!.expires_at) - Date.parse(row!.confirmed_at)).toBe(600_000);
  await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true });
  expect(await env.DB.prepare("SELECT confirmed_at,expires_at FROM account_deletion_confirmations").first()).toEqual(row);
  await env.DB.prepare("UPDATE account_deletion_confirmations SET expires_at = '2000-01-01T00:00:00Z'").run();
  expect((await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true })).status).toBe(403);
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(403);
  expect((await browser.request(`/api/v1/account/deletion-confirmations/${pending.id}/result`, {})).status).toBe(403);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
});

it("T3 verifies the RSA signature and algorithm and canonicalizes both Google issuer forms", async () => {
  const token = await google.token("nonce");
  const parts = token.split(".");
  const tampered = `${parts[0]}.${parts[1]}.${parts[2][0] === "A" ? "B" : "A"}${parts[2].slice(1)}`;
  await expect(validateGoogleToken(tampered, env.GOOGLE_CLIENT_ID!, "nonce")).rejects.toThrow();
  for (const alg of ["none", "HS256", "RS512"])
    await expect(validateGoogleToken(await google.token("nonce", {}, { alg }), env.GOOGLE_CLIENT_ID!, "nonce")).rejects.toThrow();
  await expect(validateGoogleToken(await google.token("nonce", {}, { kid: "missing" }), env.GOOGLE_CLIENT_ID!, "nonce")).rejects.toThrow();
  expect(await validateGoogleToken(await google.token("nonce", { iss: "accounts.google.com" }), env.GOOGLE_CLIENT_ID!, "nonce"))
    .toMatchObject({ issuer: "https://accounts.google.com", auth_time: null });
});

it("T1/T3 failed or cancelled OAuth returns to the initiating screen, consumes state and creates nothing", async () => {
  for (const query of ["error=access_denied", "code=unknown"]) {
    const app = await startApp();
    await browser.context();
    const start = await browser.request(`/api/v1/auth/app-authorizations/${app.id}/web-start`, { intent: "register" });
    const oauth = await browser.request((await start.json<{ url: string }>()).url);
    const state = new URL(oauth.headers.get("Location")!).searchParams.get("state");
    const response = await browser.request(`/api/v1/auth/web/google/callback?state=${state}&${query}`);
    expect(response.headers.get("Location")).toContain(`/my/app-connect?request=${app.id}&auth_error=`);
    expect((await google.callback(browser, oauth.headers.get("Location")!)).headers.get("Location")).toBe("/my/auth-error");
    expect((await approve(app.id)).status).toBe(401);
  }
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
});

it("T3/P8 wrong Google consumes the purpose-bound flow without granting proof or deletion", async () => {
  await registered();
  const start = await browser.request("/api/v1/account/deletion-start", {});
  const oauth = await browser.request((await start.json<{ url: string }>()).url);
  const wrong = await google.callback(browser, oauth.headers.get("Location")!, { sub: "another-google" });
  expect(wrong.headers.get("Location")).toBe("/my/account-delete?auth_error=identity");
  expect((await google.callback(browser, oauth.headers.get("Location")!)).headers.get("Location")).toBe("/my/auth-error");
  expect((await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true })).status).toBe(403);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
});

it.each([
  ["purpose", "delete"], ["issuer", "https://other.test"], ["sub", "other-google"],
  ["session_digest", "other-session"], ["browser_digest", "other-browser"],
  ["app_authorization_id", null], ["consumed_at", "already-consumed"],
])("T3 refuses operation proof binding mismatch %s", async (field, value) => {
  const app = await startApp();
  await browser.operation(google, app.id, "register");
  if (field === "session_digest") await env.DB.prepare(`INSERT INTO web_sessions
    SELECT 'other-session', issuer, sub, email, auth_time, browser_digest, csrf_digest, created_at, expires_at FROM web_sessions LIMIT 1`).run();
  await env.DB.prepare(`UPDATE web_operation_proofs SET ${field} = ?1`).bind(value).run();
  expect((await approve(app.id)).status).toBe(403);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
});

it("T3 rejects a proof for a different Player, a second transaction and another browser", async () => {
  const legacy = await seedPlayer();
  const other = await seedPlayer();
  const app = await startApp(legacy.credential);
  await browser.operation(google, app.id, "login");
  await env.DB.prepare("UPDATE web_operation_proofs SET player_id = ?1").bind(other.id).run();
  expect((await approve(app.id)).status).toBe(403);
  await browser.operation(google, app.id, "login");
  const anotherBrowser = new BrowserFixture();
  await anotherBrowser.login(google);
  expect((await anotherBrowser.request(`/api/v1/auth/app-authorizations/${app.id}/approve`, { confirmed: true, app_compared: true })).status).toBe(403);
  const second = await startApp(legacy.credential);
  await browser.operation(google, second.id, "login");
  expect((await approve(app.id)).status).toBe(403);
  expect((await approve(second.id)).status).toBe(200);
  expect(await counts()).toEqual({ players: 2, identities: 1, credentials: 2 });
});

it("T1/T2 simultaneous starts and approvals preserve one identity and one result", async () => {
  const app = await startApp();
  const initial = await app.response.json<{ expires_at: string }>();
  const retry = () => exports.default.fetch(`${origin}/api/v1/auth/app-authorizations`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id: app.id, request_secret: app.request_secret, purpose: "connect" }),
  });
  for (const response of await Promise.all([retry(), retry()])) {
    expect(response.status).toBe(200);
    expect(await response.json()).toMatchObject({ authorization_id: app.id, expires_at: initial.expires_at });
  }
  await browser.operation(google, app.id, "register");
  for (const response of await Promise.all([approve(app.id), approve(app.id)])) expect(response.status).toBe(200);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  const otherBrowser = new BrowserFixture(), second = await startApp();
  await otherBrowser.operation(google, second.id, "register");
  expect((await otherBrowser.request(`/api/v1/auth/app-authorizations/${second.id}/approve`, { confirmed: true, app_compared: true })).status).toBe(409);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
});

it("T1 first App start is idempotent under a real insert race and refuses a conflicting secret", async () => {
  const app = await startApp();
  await env.DB.prepare("DELETE FROM app_authorizations WHERE id = ?1").bind(app.id).run();
  const start = (secret: string) => exports.default.fetch(`${origin}/api/v1/auth/app-authorizations`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id: app.id, request_secret: secret, purpose: "connect" }),
  });
  const responses = await Promise.all([start(app.request_secret), start(app.request_secret)]);
  for (const response of responses) expect([200, 201]).toContain(response.status);
  const bodies = await Promise.all(responses.map(response => response.json()));
  expect(bodies[0]).toEqual(bodies[1]);
  expect((await env.DB.prepare("SELECT * FROM app_authorizations").all()).results).toHaveLength(1);
  expect((await start("X".repeat(43))).status).toBe(409);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
});

it("T1 atomic creation failure leaves no Player/identity/credential and an unconsumed operation proof", async () => {
  const app = await startApp();
  await browser.operation(google, app.id, "register");
  await env.DB.prepare(`CREATE TRIGGER fail_pending_credential BEFORE INSERT ON player_credentials
    BEGIN SELECT RAISE(ABORT, 'injected failure'); END`).run();
  expect((await approve(app.id)).status).toBe(409);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
  expect(await env.DB.prepare("SELECT consumed_at FROM web_operation_proofs").first()).toEqual({ consumed_at: null });
  expect(await env.DB.prepare("SELECT status FROM app_authorizations").first()).toEqual({ status: "PENDING" });
  await env.DB.prepare("DROP TRIGGER fail_pending_credential").run();
  expect((await approve(app.id)).status).toBe(200);
});

it("T2 rejects the second Google for a linked Player and concurrent identity unique conflicts", async () => {
  const active = await registered(), another = new BrowserFixture();
  await another.login(google, { sub: "second-google" });
  const relink = await startApp(active.credential, active.public_player_id);
  const mismatch = await another.operation(google, relink.id, "login", { sub: "second-google" });
  expect(mismatch.headers.get("Location")).toContain("auth_error=identity");
  expect((await another.request(`/api/v1/auth/app-authorizations/${relink.id}/approve`, { confirmed: true, app_compared: true })).status).toBe(403);
  const first = await seedPlayer(), second = await seedPlayer();
  const one = await startApp(first.credential, first.public_player_id), two = await startApp(second.credential, second.public_player_id);
  const firstBrowser = new BrowserFixture(), secondBrowser = new BrowserFixture();
  await firstBrowser.operation(google, one.id, "login", { sub: "third-google" });
  await secondBrowser.operation(google, two.id, "login", { sub: "third-google" });
  const approveIn = (b: BrowserFixture, id: string) => b.request(`/api/v1/auth/app-authorizations/${id}/approve`, { confirmed: true, app_compared: true });
  expect((await Promise.all([approveIn(firstBrowser, one.id), approveIn(secondBrowser, two.id)])).map(r => r.status).sort()).toEqual([200, 409]);
  expect(await counts()).toEqual({ players: 3, identities: 2, credentials: 3 });
});

it("T5/T6 simultaneous activations choose one PC and the revoked retry never restores it", async () => {
  const old = await registered();
  const pending = [];
  for (let index = 0; index < 2; index++) {
    const app = await startApp();
    await browser.operation(google, app.id, "login");
    await approve(app.id);
    pending.push({ ...app, ...(await (await appRequest(app.id, app.request_secret, "result")).json<{ credential: string }>()) });
  }
  const responses = await Promise.all(pending.map(app => appRequest(app.id, app.request_secret, "activate", app.credential)));
  expect(responses.map(r => r.status).sort()).toEqual([200, 401]);
  expect((await env.DB.prepare("SELECT id FROM player_credentials WHERE revoked_at IS NULL AND activation_state = 'active'").all()).results).toHaveLength(1);
  const loser = pending[responses.findIndex(r => r.status === 401)];
  expect((await appRequest(loser.id, loser.request_secret, "activate", loser.credential)).status).toBe(401);
  expect((await appRequest(old.id, old.request_secret, "activate", old.credential)).status).toBe(401);
});

it("T6 activation failure rolls back revocation and staging and leaves the old PC active", async () => {
  const old = await registered(), next = await startApp();
  await browser.operation(google, next.id, "login");
  await approve(next.id);
  const pending = await (await appRequest(next.id, next.request_secret, "result")).json<{ credential: string }>();
  await env.DB.prepare(`CREATE TRIGGER fail_activation BEFORE UPDATE OF status ON app_authorizations
    WHEN NEW.status = 'ACTIVATED' BEGIN SELECT RAISE(ABORT, 'injected failure'); END`).run();
  expect((await appRequest(next.id, next.request_secret, "activate", pending.credential)).status).toBe(409);
  expect(await env.DB.prepare("SELECT activation_state, revoked_at FROM player_credentials WHERE id = ?1").bind(old.credential.split(".")[0]).first())
    .toEqual({ activation_state: "active", revoked_at: null });
  expect(await env.DB.prepare("SELECT activation_state, revoked_at FROM player_credentials WHERE id = ?1").bind(pending.credential.split(".")[0]).first())
    .toEqual({ activation_state: "pending", revoked_at: null });
  await env.DB.prepare("DROP TRIGGER fail_activation").run();
  expect((await appRequest(next.id, next.request_secret, "activate", pending.credential)).status).toBe(200);
});

it("P4 cookies/session are opaque, absolute, no-store and expiry removes identity display material", async () => {
  const start = await browser.request("/api/v1/auth/web/google/start");
  const redirect = await browser.request(start.headers.get("Location")!);
  const callback = await google.callback(browser, redirect.headers.get("Location")!, { auth_time: 1 });
  const sessionCookie = callback.headers.getSetCookie().find(value => value.startsWith("__Host-ddrgp-session="))!;
  expect(sessionCookie).toMatch(/Secure/u);
  expect(sessionCookie).toMatch(/HttpOnly/u);
  expect(sessionCookie).toMatch(/SameSite=Lax/u);
  expect(sessionCookie).toMatch(/Path=\//u);
  expect(sessionCookie).not.toContain("Domain=");
  expect(callback.headers.get("Cache-Control")).toBe("no-store");
  expect(callback.headers.get("Referrer-Policy")).toBe("no-referrer");
  const raw = browser.cookies.get("__Host-ddrgp-session")!;
  expect(raw).toMatch(/^[A-Za-z0-9_-]{43}$/u);
  const row = await env.DB.prepare("SELECT * FROM web_sessions").first<{ digest: string; created_at: string; expires_at: string; auth_time: number }>();
  expect(row!.digest).not.toBe(raw);
  expect(row!.auth_time).toBe(1);
  expect(Date.parse(row!.expires_at) - Date.parse(row!.created_at)).toBe(86400_000);
  await browser.session();
  expect(await env.DB.prepare("SELECT expires_at FROM web_sessions").first()).toEqual({ expires_at: row!.expires_at });
  await env.DB.prepare("UPDATE web_sessions SET expires_at = '2000-01-01T00:00:00.000Z'").run();
  expect((await browser.request("/api/v1/account/session")).status).toBe(401);
  expect((await env.DB.prepare("SELECT * FROM web_sessions").all()).results).toEqual([]);
});

it("home redirects an active linked session to its own public page and stops after session expiry", async () => {
  const player = await registered();
  const response = await browser.request("/?player=p_other");
  expect(response.status).toBe(302);
  expect(response.headers.get("Location")).toBe(`/player/${player.public_player_id}`);
  expect(response.headers.get("Cache-Control")).toBe("no-store");
  expect(await response.text()).not.toContain("private@example.test");

  await env.DB.prepare("UPDATE web_sessions SET expires_at = '2000-01-01T00:00:00Z'").run();
  const expired = await browser.request("/");
  expect(expired.status).toBe(200);
  expect(expired.headers.get("Location")).toBeNull();
  expect(await expired.text()).toContain("<title>GP Score Log</title>");
});

it("home keeps the getting-started page for a Google session without a linked Player", async () => {
  await browser.login(google);
  const response = await browser.request("/");
  expect(response.status).toBe(200);
  expect(response.headers.get("Location")).toBeNull();
  expect(response.headers.get("Cache-Control")).toBe("no-store");
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
});

it("P1/P4 Worker-first account screens load without exposing identity or proof and share the page safety policy", async () => {
  for (const route of ["/my/profile", "/my/app-connect?request=opaque-id", "/my/account-delete", "/my/auth-error"]) {
    const response = await exports.default.fetch(`${origin}${route}`);
    expect(response.status).toBe(200);
    expect(response.headers.get("Cache-Control")).toBe("no-store");
    expect(response.headers.get("Referrer-Policy")).toBe("no-referrer");
    expect(response.headers.get("Content-Security-Policy")).toContain("frame-ancestors 'none'");
    const html = await response.text();
    expect(html).not.toContain("__PLAYER_CSP_NONCE__");
    expect(html).not.toContain("private@example.test");
    expect(html).not.toContain("google-fixture-sub");
  }
});

it("T3 local HTTP UI configuration cannot initiate Google OAuth or create an HTTP callback", async () => {
  const request = new Request("http://localhost:4173/api/v1/auth/web/google/start");
  const response = await worker.fetch(request, { ...env, PUBLIC_WEB_ORIGIN: "http://localhost:4173", GOOGLE_ALLOW_LOCAL_HTTP: "true" });
  expect(response.status).toBe(500);
  expect(response.headers.get("Location")).toBeNull();
  expect((await env.DB.prepare("SELECT * FROM web_oauth_requests").all()).results).toEqual([]);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
});

it("T3/T9 expired OAuth waits and App cancellation cannot create data or reuse a finished transaction", async () => {
  const app = await startApp();
  await browser.context();
  const start = await browser.request(`/api/v1/auth/app-authorizations/${app.id}/web-start`, { intent: "register" });
  const flow = (await start.json<{ url: string }>()).url;
  const oauth = await browser.request(flow);
  await env.DB.prepare("UPDATE web_oauth_requests SET expires_at = '2000-01-01T00:00:00.000Z'").run();
  expect((await browser.request(flow)).status).toBe(400);
  expect((await google.callback(browser, oauth.headers.get("Location")!)).headers.get("Location")).toBe("/my/auth-error");
  expect(await (await appRequest(app.id, app.request_secret, "cancel")).json()).toEqual({ status: "CANCELLED" });
  expect((await browser.request(`/api/v1/auth/app-authorizations/${app.id}/web-start`, { intent: "register" })).status).toBe(409);
  expect(await counts()).toEqual({ players: 0, identities: 0, credentials: 0 });
});

it("T9 choosing Google again preserves the App/start Player but discards old confirmation", async () => {
  const active = await registered(), app = await startApp(active.credential);
  await browser.operation(google, app.id, "login");
  expect((await browser.request("/api/v1/auth/web/logout", { app_authorization_id: app.id })).status).toBe(204);
  const row = await env.DB.prepare("SELECT status, start_player_id, session_digest, player_id FROM app_authorizations WHERE id = ?1").bind(app.id).first();
  expect(row).toMatchObject({ status: "PENDING", session_digest: null, player_id: null });
  expect((await approve(app.id)).status).toBe(401);
  await browser.context();
  await browser.operation(google, app.id, "login");
  expect((await approve(app.id)).status).toBe(200);
  const finished = await browser.request(`/api/v1/auth/app-authorizations/${app.id}/web-start`, { intent: "login" });
  expect(finished.status).toBe(409);
});

it("P8 rejects arbitrary target/body and confirmation proof mismatch, with atomic rollback on DELETE failure", async () => {
  await registered();
  for (const body of [{ player_id: "other" }, { redirect: "https://attacker.test" }, []])
    expect((await browser.request("/api/v1/account/deletion-start", body)).status).toBe(400);
  const deletion = await deletionOAuth();
  expect((await browser.request(`/api/v1/account/deletion-confirmations/${deletion.id}/result`, {}, "POST", { Cookie: "" })).status).toBe(403);
  await env.DB.prepare("UPDATE account_deletion_confirmations SET sub = 'wrong-google'").run();
  expect((await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true })).status).toBe(403);
  await env.DB.prepare("UPDATE account_deletion_confirmations SET sub = 'google-fixture-sub'").run();
  for (const response of await Promise.all([
    browser.request("/api/v1/account/deletion-confirmations", { confirmed: true }),
    browser.request("/api/v1/account/deletion-confirmations", { confirmed: true }),
  ])) expect(response.status).toBe(200);
  const before = await env.DB.prepare("SELECT * FROM account_deletion_confirmations").first();
  await env.DB.prepare(`CREATE TRIGGER fail_delete_player BEFORE DELETE ON players
    BEGIN SELECT RAISE(ABORT, 'injected failure'); END`).run();
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(500);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  expect(await env.DB.prepare("SELECT * FROM account_deletion_confirmations").first()).toEqual(before);
  expect((await browser.request("/api/v1/account/profile")).status).toBe(200);
  await env.DB.prepare("DROP TRIGGER fail_delete_player").run();
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(200);
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(401);
});

// Insert a real D1 interleaving after the request has read its identity and before its
// write transaction. Both the interleaving and the guarded update use migrated D1.
async function interleavedRequest(path: string, body: unknown, beforeWrite: () => Promise<unknown>, afterBatches = 0, token?: string, method = "POST") {
  let batches = 0;
  const db = {
    prepare: env.DB.prepare.bind(env.DB),
    async batch(statements: D1PreparedStatement[]) {
      if (batches++ === afterBatches) await beforeWrite();
      return env.DB.batch(statements);
    },
  } as D1Database;
  return worker.fetch(new Request(`${origin}${path}`, {
    method, headers: { "Content-Type": "application/json", Cookie: [...browser.cookies].map(([key, value]) => `${key}=${value}`).join("; "),
      Origin: origin, "X-CSRF-Token": browser.csrf, ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify(body),
  }), { ...env, DB: db });
}

it("T6/H4 a previously authenticated old-PC merge cannot commit after activation", async () => {
  const old = await registered(), next = await startApp();
  await browser.operation(google, next.id, "login");
  await approve(next.id);
  const pending = await (await appRequest(next.id, next.request_secret, "result")).json<{ credential: string }>();
  const response = await interleavedRequest("/api/v1/me/bests/merge", { projection_version: 1, master_version: "fixture", items: [] },
    () => appRequest(next.id, next.request_secret, "activate", pending.credential), 0, old.credential);
  expect(response.status).toBe(401);
  expect(await env.DB.prepare("SELECT best_sync_revision FROM players").first()).toEqual({ best_sync_revision: 0 });
});

it("T8/P4 unlink rejects in-flight old session writes and invalidates uncompleted operations atomically", async () => {
  const active = await registered();
  const foreign = new BrowserFixture();
  await foreign.login(google, { sub: "foreign-sub", email: "foreign@example.test" });
  const deletion = await deletionOAuth();
  const unlink = await startApp(active.credential, undefined, "unlink");
  await browser.operation(google, unlink.id, "login");
  const response = await interleavedRequest("/api/v1/account/deletion-confirmations", { confirmed: true }, () => approve(unlink.id), 1);
  expect(response.status).toBe(401);
  expect((await foreign.request("/api/v1/account/session")).status).toBe(200);
  expect((await browser.request(`/api/v1/account/deletion-confirmations/${deletion.id}/result`, {})).status).toBe(403);
  expect((await env.DB.prepare("SELECT * FROM web_operation_proofs").all()).results).toEqual([]);
  expect((await env.DB.prepare("SELECT sub FROM web_sessions").all()).results).toEqual([{ sub: "foreign-sub" }]);
  expect(await counts()).toEqual({ players: 1, identities: 0, credentials: 1 });
});

it("T8 unlink late failure restores all sessions, proofs and pending deletion confirmation", async () => {
  const active = await registered();
  const otherBrowser = new BrowserFixture();
  await otherBrowser.login(google);
  const deletion = await deletionOAuth();
  const unlink = await startApp(active.credential, undefined, "unlink");
  await browser.operation(google, unlink.id, "login");
  await env.DB.prepare(`CREATE TRIGGER fail_unlink_session BEFORE DELETE ON web_sessions
    BEGIN SELECT RAISE(ABORT, 'injected failure'); END`).run();
  expect((await approve(unlink.id)).status).toBe(409);
  expect(await counts()).toEqual({ players: 1, identities: 1, credentials: 1 });
  expect((await otherBrowser.request("/api/v1/account/profile")).status).toBe(200);
  expect((await browser.request("/api/v1/account/profile")).status).toBe(200);
  expect(await (await browser.request(`/api/v1/account/deletion-confirmations/${deletion.id}/result`, {})).json()).toMatchObject({ status: "UNCONFIRMED" });
  expect(await env.DB.prepare("SELECT consumed_at FROM web_operation_proofs WHERE purpose = 'unlink'").first()).toEqual({ consumed_at: null });
  await env.DB.prepare("DROP TRIGGER fail_unlink_session").run();
  expect((await approve(unlink.id)).status).toBe(200);
});

it("P8 full deletion removes bound pre-callback App/OAuth requests and all Best staging", async () => {
  const active = await registered();
  await seedBest(active.public_player_id);
  const unfinished = await startApp();
  expect((await browser.request(`/api/v1/auth/app-authorizations/${unfinished.id}/web-start`, { intent: "login" })).status).toBe(200);
  const snapshot = await exports.default.fetch(`${origin}/api/v1/me/bests/snapshots`, {
    method: "POST", headers: { Authorization: `Bearer ${active.credential}`, "Content-Type": "application/json" },
    body: JSON.stringify({ projection_version: 1, master_version: "fixture", expected_item_count: 0 }),
  });
  expect(snapshot.status).toBe(201);
  await deletionOAuth();
  await browser.request("/api/v1/account/deletion-confirmations", { confirmed: true });
  expect((await browser.request("/api/v1/account", undefined, "DELETE")).status).toBe(200);
  for (const table of ["web_sessions", "web_oauth_requests", "web_operation_proofs", "app_authorizations",
    "best_sync_snapshots", "best_sync_snapshot_items", "best_sync_snapshot_chunks", "best_replacement_authorizations", "player_chart_bests"])
    expect((await env.DB.prepare(`SELECT * FROM ${table}`).all()).results).toEqual([]);
  expect((await exports.default.fetch(`${origin}/api/v1/public/players/${active.public_player_id}`)).status).toBe(404);
});
