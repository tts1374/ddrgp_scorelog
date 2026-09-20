import { env, exports } from "cloudflare:workers";
import { applyD1Migrations, reset } from "cloudflare:test";
import { beforeEach, describe, expect, it, vi } from "vitest";

interface RegistrationResponse {
  public_player_id: string;
  display_name: string;
  created_at: string;
  updated_at: string;
  credential: string;
}

const baseUrl = "https://identity.example.test";

async function register(
  idempotencyKey = "registration-request-0000000000000001",
  displayName = "Player",
): Promise<{ response: Response; body: RegistrationResponse }> {
  const response = await exports.default.fetch(`${baseUrl}/api/v1/players/register`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": idempotencyKey,
    },
    body: JSON.stringify({ display_name: displayName }),
  });
  return { response, body: await response.json<RegistrationResponse>() };
}

function authenticatedHeaders(credential: string): HeadersInit {
  return { Authorization: `Bearer ${credential}` };
}

beforeEach(async () => {
  await reset();
  await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
});

describe("Player identity API", () => {
  it("registers one Player and makes registration retries idempotent", async () => {
    const first = await register(undefined, "2ten");
    const retry = await register(undefined, "ignored on retry");

    expect(first.response.status).toBe(201);
    expect(retry.response.status).toBe(200);
    expect(retry.body).toEqual(first.body);
    expect(first.body.public_player_id).toMatch(/^p_[A-Za-z0-9_-]{22}$/u);
    expect(first.body.credential).toMatch(/^ac_[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}$/u);

    const playerCount = await env.DB.prepare("SELECT COUNT(*) AS count FROM players")
      .first<{ count: number }>();
    const credentialCount = await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM player_credentials",
    ).first<{ count: number }>();
    expect(playerCount?.count).toBe(1);
    expect(credentialCount?.count).toBe(1);
  });

  it("serializes concurrent registration retries without orphan Players", async () => {
    const requestId = "concurrent-registration-request-000001";
    const [left, right] = await Promise.all([
      register(requestId),
      register(requestId),
    ]);

    expect([left.response.status, right.response.status].sort()).toEqual([200, 201]);
    expect(left.body).toEqual(right.body);
    const counts = await env.DB.prepare(
      `SELECT
         (SELECT COUNT(*) FROM players) AS players,
         (SELECT COUNT(*) FROM player_credentials) AS credentials,
         (SELECT COUNT(*) FROM player_registration_requests) AS requests`,
    ).first<{ players: number; credentials: number; requests: number }>();
    expect(counts).toEqual({ players: 1, credentials: 1, requests: 1 });
  });

  it("authenticates, returns the current Player, and preserves public identity on update", async () => {
    const registration = await register();

    const current = await exports.default.fetch(`${baseUrl}/api/v1/me`, {
      headers: authenticatedHeaders(registration.body.credential),
    });
    expect(current.status).toBe(200);
    expect(await current.json()).toMatchObject({
      public_player_id: registration.body.public_player_id,
      display_name: "Player",
    });

    const update = await exports.default.fetch(`${baseUrl}/api/v1/me`, {
      method: "PATCH",
      headers: {
        ...authenticatedHeaders(registration.body.credential),
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ display_name: "Updated player" }),
    });
    expect(update.status).toBe(200);
    expect(await update.json()).toMatchObject({
      public_player_id: registration.body.public_player_id,
      display_name: "Updated player",
    });
  });

  it("returns the same generic 401 for unknown and incorrect credentials", async () => {
    const registration = await register();
    const [credentialId] = registration.body.credential.split(".");
    const unknown = `ac_AAAAAAAAAAAAAAAAAAAAAA.${"B".repeat(43)}`;
    const incorrect = `${credentialId}.${"C".repeat(43)}`;

    for (const credential of [unknown, incorrect]) {
      const response = await exports.default.fetch(`${baseUrl}/api/v1/me`, {
        headers: authenticatedHeaders(credential),
      });
      expect(response.status).toBe(401);
      expect(await response.json()).toEqual({
        error: {
          code: "UNAUTHORIZED",
          message: "The App Credential is invalid.",
        },
      });
    }
  });

  it("fully deletes the Player and invalidates every associated credential", async () => {
    const registration = await register();
    const deletion = await exports.default.fetch(`${baseUrl}/api/v1/me`, {
      method: "DELETE",
      headers: authenticatedHeaders(registration.body.credential),
    });
    expect(deletion.status).toBe(204);

    const retry = await exports.default.fetch(`${baseUrl}/api/v1/me`, {
      headers: authenticatedHeaders(registration.body.credential),
    });
    expect(retry.status).toBe(401);
    const counts = await env.DB.prepare(
      `SELECT
         (SELECT COUNT(*) FROM players) AS players,
         (SELECT COUNT(*) FROM player_credentials) AS credentials,
         (SELECT COUNT(*) FROM player_registration_requests) AS requests`,
    ).first<{ players: number; credentials: number; requests: number }>();
    expect(counts).toEqual({ players: 0, credentials: 0, requests: 0 });
  });

  it("stores only digests and permits multiple credentials for one Player", async () => {
    const log = vi.spyOn(console, "log").mockImplementation(() => undefined);
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const registration = await register();
    const [credentialId, secret] = registration.body.credential.split(".");

    const row = await env.DB.prepare(
      "SELECT player_id, secret_digest FROM player_credentials WHERE id = ?1",
    )
      .bind(credentialId)
      .first<{ player_id: string; secret_digest: string }>();
    expect(row?.secret_digest).toMatch(/^[0-9a-f]{64}$/u);
    expect(row?.secret_digest).not.toContain(secret);

    await env.DB.prepare(
      `INSERT INTO player_credentials
         (id, player_id, type, secret_digest, created_at)
       VALUES (?1, ?2, 'app', ?3, ?4)`,
    )
      .bind(`ac_${"Z".repeat(22)}`, row?.player_id, "f".repeat(64), new Date().toISOString())
      .run();
    const count = await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM player_credentials WHERE player_id = ?1",
    )
      .bind(row?.player_id)
      .first<{ count: number }>();
    expect(count?.count).toBe(2);
    expect(log).not.toHaveBeenCalledWith(expect.stringContaining(secret));
    expect(error).not.toHaveBeenCalledWith(expect.stringContaining(secret));
    log.mockRestore();
    error.mockRestore();
  });

  it("rejects plaintext transport before processing credentials", async () => {
    const response = await exports.default.fetch(
      "http://identity.example.test/api/v1/me",
      { headers: authenticatedHeaders(`ac_${"A".repeat(22)}.${"B".repeat(43)}`) },
    );
    expect(response.status).toBe(400);
    expect(await response.json()).toMatchObject({
      error: { code: "HTTPS_REQUIRED" },
    });
  });
});
