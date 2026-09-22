import { Hono } from "hono";
import type { Context, MiddlewareHandler } from "hono";
import {
  deriveCredentialSecret,
  digestCredentialSecret,
  digestRegistrationRequest,
  randomId,
  verifyCredentialSecret,
} from "./crypto";
import { registerBestSyncRoutes } from "./best-sync";

export interface Bindings {
  DB: D1Database;
  CREDENTIAL_PEPPER: string;
  REGISTRATION_SECRET: string;
}

interface PlayerRow {
  id: string;
  public_player_id: string;
  display_name: string;
  created_at: string;
  updated_at: string;
}

interface RegistrationRow extends PlayerRow {
  credential_id: string;
  secret_digest: string;
}

interface CredentialPlayerRow extends PlayerRow {
  credential_id: string;
  secret_digest: string;
}

interface Variables {
  player: PlayerRow;
}

type AppEnvironment = {
  Bindings: Bindings;
  Variables: Variables;
};

const app = new Hono<AppEnvironment>();
const registrationRequestPattern = /^[A-Za-z0-9_-]{32,128}$/u;
const credentialIdPattern = /^ac_[A-Za-z0-9_-]{20,64}$/u;

function errorResponse(
  c: Context<AppEnvironment>,
  status: 400 | 401 | 404 | 409 | 500,
  code: string,
  message: string,
): Response {
  return c.json({ error: { code, message } }, status);
}

function playerResponse(player: PlayerRow) {
  return {
    public_player_id: player.public_player_id,
    display_name: player.display_name,
    created_at: player.created_at,
    updated_at: player.updated_at,
  };
}

function credentialToken(credentialId: string, secret: string): string {
  return `${credentialId}.${secret}`;
}

function parseCredentialToken(token: string): { id: string; secret: string } | null {
  const separator = token.indexOf(".");
  if (separator <= 0 || separator !== token.lastIndexOf(".")) {
    return null;
  }
  const id = token.slice(0, separator);
  const secret = token.slice(separator + 1);
  if (!credentialIdPattern.test(id) || !/^[A-Za-z0-9_-]{43}$/u.test(secret)) {
    return null;
  }
  return { id, secret };
}

function readBearerToken(header: string | undefined): string | null {
  if (header === undefined || !header.startsWith("Bearer ")) {
    return null;
  }
  const token = header.slice("Bearer ".length);
  return token.length > 0 && !token.includes(" ") ? token : null;
}

function normalizeDisplayName(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }
  const normalized = value.trim();
  return normalized.length >= 1 && normalized.length <= 64 ? normalized : null;
}

async function findRegistration(
  db: D1Database,
  requestDigest: string,
): Promise<RegistrationRow | null> {
  return db
    .prepare(
      `SELECT p.id,
              p.public_player_id,
              p.display_name,
              p.created_at,
              p.updated_at,
              c.id AS credential_id,
              c.secret_digest
       FROM player_registration_requests r
       JOIN players p ON p.id = r.player_id
       JOIN player_credentials c ON c.id = r.credential_id
       WHERE r.request_digest = ?1
         AND c.revoked_at IS NULL`,
    )
    .bind(requestDigest)
    .first<RegistrationRow>();
}

async function registrationResponse(
  c: Context<AppEnvironment>,
  row: RegistrationRow,
  requestId: string,
  status: 200 | 201,
): Promise<Response> {
  const secret = await deriveCredentialSecret(
    c.env.REGISTRATION_SECRET,
    requestId,
    row.credential_id,
  );
  const digest = await digestCredentialSecret(c.env.CREDENTIAL_PEPPER, secret);
  if (digest !== row.secret_digest) {
    return errorResponse(
      c,
      409,
      "REGISTRATION_RETRY_UNAVAILABLE",
      "The original registration response can no longer be reproduced.",
    );
  }
  return c.json(
    {
      ...playerResponse(row),
      credential: credentialToken(row.credential_id, secret),
    },
    status,
  );
}

const authenticate: MiddlewareHandler<AppEnvironment> = async (c, next) => {
  const bearer = readBearerToken(c.req.header("Authorization"));
  const credential = bearer === null ? null : parseCredentialToken(bearer);
  if (credential === null) {
    return errorResponse(c, 401, "UNAUTHORIZED", "The App Credential is invalid.");
  }

  const row = await c.env.DB.prepare(
    `SELECT p.id,
            p.public_player_id,
            p.display_name,
            p.created_at,
            p.updated_at,
            c.id AS credential_id,
            c.secret_digest
     FROM player_credentials c
     JOIN players p ON p.id = c.player_id
     WHERE c.id = ?1
       AND c.type = 'app'
       AND c.revoked_at IS NULL`,
  )
    .bind(credential.id)
    .first<CredentialPlayerRow>();

  const secretIsValid = await verifyCredentialSecret(
    c.env.CREDENTIAL_PEPPER,
    credential.secret,
    row?.secret_digest ?? "0".repeat(64),
  );
  if (row === null || !secretIsValid) {
    return errorResponse(c, 401, "UNAUTHORIZED", "The App Credential is invalid.");
  }

  await c.env.DB.prepare(
    "UPDATE player_credentials SET last_used_at = ?1 WHERE id = ?2",
  )
    .bind(new Date().toISOString(), row.credential_id)
    .run();
  c.set("player", row);
  await next();
};

app.use("/api/*", async (c, next) => {
  if (new URL(c.req.url).protocol !== "https:") {
    return errorResponse(c, 400, "HTTPS_REQUIRED", "HTTPS is required.");
  }
  await next();
});

app.post("/api/v1/players/register", async (c) => {
  const requestId = c.req.header("Idempotency-Key");
  if (requestId === undefined || !registrationRequestPattern.test(requestId)) {
    return errorResponse(
      c,
      400,
      "INVALID_IDEMPOTENCY_KEY",
      "Idempotency-Key must be a 32 to 128 character base64url value.",
    );
  }

  let body: unknown;
  try {
    body = await c.req.json();
  } catch {
    return errorResponse(c, 400, "INVALID_REQUEST", "A JSON request body is required.");
  }
  const candidate = body as { display_name?: unknown } | null;
  const displayName = normalizeDisplayName(candidate?.display_name ?? "Player");
  if (displayName === null) {
    return errorResponse(
      c,
      400,
      "INVALID_DISPLAY_NAME",
      "display_name must contain between 1 and 64 characters.",
    );
  }

  const requestDigest = await digestRegistrationRequest(
    c.env.REGISTRATION_SECRET,
    requestId,
  );
  const existing = await findRegistration(c.env.DB, requestDigest);
  if (existing !== null) {
    return registrationResponse(c, existing, requestId, 200);
  }

  const now = new Date().toISOString();
  const playerId = randomId("pl_");
  const publicPlayerId = randomId("p_");
  const credentialId = randomId("ac_");
  const credentialSecret = await deriveCredentialSecret(
    c.env.REGISTRATION_SECRET,
    requestId,
    credentialId,
  );
  const credentialDigest = await digestCredentialSecret(
    c.env.CREDENTIAL_PEPPER,
    credentialSecret,
  );

  try {
    await c.env.DB.batch([
      c.env.DB.prepare(
        `INSERT INTO players
           (id, public_player_id, display_name, created_at, updated_at)
         VALUES (?1, ?2, ?3, ?4, ?4)`,
      ).bind(playerId, publicPlayerId, displayName, now),
      c.env.DB.prepare(
        `INSERT INTO player_credentials
           (id, player_id, type, secret_digest, created_at)
         VALUES (?1, ?2, 'app', ?3, ?4)`,
      ).bind(credentialId, playerId, credentialDigest, now),
      c.env.DB.prepare(
        `INSERT INTO player_registration_requests
           (request_digest, player_id, credential_id, created_at)
         VALUES (?1, ?2, ?3, ?4)`,
      ).bind(requestDigest, playerId, credentialId, now),
    ]);
  } catch {
    const concurrentlyCreated = await findRegistration(c.env.DB, requestDigest);
    if (concurrentlyCreated !== null) {
      return registrationResponse(c, concurrentlyCreated, requestId, 200);
    }
    throw new Error("Player registration could not be committed.");
  }

  return c.json(
    {
      public_player_id: publicPlayerId,
      display_name: displayName,
      created_at: now,
      updated_at: now,
      credential: credentialToken(credentialId, credentialSecret),
    },
    201,
  );
});

app.use("/api/v1/me", authenticate);
app.use("/api/v1/me/*", authenticate);

app.get("/api/v1/me", (c) => c.json(playerResponse(c.get("player"))));

app.patch("/api/v1/me", async (c) => {
  let body: unknown;
  try {
    body = await c.req.json();
  } catch {
    return errorResponse(c, 400, "INVALID_REQUEST", "A JSON request body is required.");
  }
  const displayName = normalizeDisplayName(
    (body as { display_name?: unknown } | null)?.display_name,
  );
  if (displayName === null) {
    return errorResponse(
      c,
      400,
      "INVALID_DISPLAY_NAME",
      "display_name must contain between 1 and 64 characters.",
    );
  }

  const player = c.get("player");
  const updatedAt = new Date().toISOString();
  await c.env.DB.prepare(
    "UPDATE players SET display_name = ?1, updated_at = ?2 WHERE id = ?3",
  )
    .bind(displayName, updatedAt, player.id)
    .run();
  return c.json(
    playerResponse({ ...player, display_name: displayName, updated_at: updatedAt }),
  );
});

app.delete("/api/v1/me", async (c) => {
  await c.env.DB.prepare("DELETE FROM players WHERE id = ?1")
    .bind(c.get("player").id)
    .run();
  return c.body(null, 204);
});

registerBestSyncRoutes(app);

app.notFound((c) => errorResponse(c, 404, "NOT_FOUND", "The route was not found."));

app.onError((_error, c) =>
  errorResponse(c, 500, "INTERNAL_ERROR", "The request could not be completed."),
);

export default app;
