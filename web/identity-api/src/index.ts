import { Hono } from "hono";
import type { Context, MiddlewareHandler } from "hono";
import {
  verifyCredentialSecret,
} from "./crypto";
import { registerBestSyncRoutes } from "./best-sync";
import { registerWebAccountRoutes, readWebSession } from "./web-account";
import { registerAppAuthorizationRoutes } from "./app-authorization";
import { registerAccountDeletionRoutes } from "./account-deletion";
import { registerPublicBestDeletionRoutes } from "./public-bests-deletion";
import { registerPublicPageRoute, randomNonce, securityHeaders } from "./public-page";
import { registerPublicPlayerRoutes } from "./public-player";

export interface Bindings {
  DB: D1Database;
  ASSETS: Fetcher;
  CREDENTIAL_PEPPER: string;
  PUBLIC_WEB_ORIGIN?: string;
  REGISTRATION_SECRET: string;
  APP_AUTHORIZATION_SECRET?: string;
  GOOGLE_CLIENT_ID?: string;
  GOOGLE_CLIENT_SECRET?: string;
  GOOGLE_ALLOW_LOCAL_HTTP?: string;
}

interface PlayerRow {
  id: string;
  public_player_id: string;
  display_name: string;
  created_at: string;
  updated_at: string;
  credential_id: string;
}

interface CredentialPlayerRow extends PlayerRow {
  credential_id: string;
  secret_digest: string;
}

export interface Variables {
  player: PlayerRow;
}

export type AppEnvironment = {
  Bindings: Bindings;
  Variables: Variables;
};

const app = new Hono<AppEnvironment>();
const credentialIdPattern = /^ac_[A-Za-z0-9_-]{20,64}$/u;

function errorResponse(
  c: Context<AppEnvironment>,
  status: 400 | 401 | 404 | 409 | 410 | 500,
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
       AND c.revoked_at IS NULL
       AND c.activation_state = 'active'`,
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
  if (!c.req.path.startsWith("/api/v1/public/")) c.header("Cache-Control", "no-store");
  const requestUrl = new URL(c.req.url);
  const localDevelopment = requestUrl.hostname === "127.0.0.1" || requestUrl.hostname === "localhost";
  if (requestUrl.protocol !== "https:" && !localDevelopment) {
    return errorResponse(c, 400, "HTTPS_REQUIRED", "HTTPS is required.");
  }
  await next();
});

app.use("/api/v1/public/*", async (c, next) => {
  await next();
  c.header("Cache-Control", "no-store");
});

app.post("/api/v1/players/register", c => errorResponse(c, 410,
  "REGISTRATION_MOVED_TO_WEB", "Create an account from the App's Web connection."));
registerWebAccountRoutes(app);
registerAppAuthorizationRoutes(app);
registerAccountDeletionRoutes(app);
registerPublicBestDeletionRoutes(app);

app.use("/api/v1/me", authenticate);
app.use("/api/v1/me/*", authenticate);

app.get("/api/v1/me", async c => {
  const player = c.get("player");
  const linked = await c.env.DB.prepare("SELECT 1 FROM google_identities WHERE player_id = ?1").bind(player.id).first();
  c.header("Cache-Control", "no-store");
  return c.json({ ...playerResponse(player), google_linked: linked !== null, activation_state: "active" });
});
app.patch("/api/v1/me", c => errorResponse(c, 409, "WEB_PROFILE_REQUIRED", "Edit the name on your Web profile."));
app.delete("/api/v1/me", c => errorResponse(c, 409, "WEB_ACCOUNT_REQUIRED", "Delete the account on your Web profile."));

registerBestSyncRoutes(app);
registerPublicPlayerRoutes(app);
registerPublicPageRoute(app);
app.get("/my/*", async c => {
  const response = await c.env.ASSETS.fetch(new Request(new URL("/index.html", c.req.url)));
  if (!response.ok) throw new Error("The account page could not be loaded.");
  const nonce = randomNonce();
  return new Response((await response.text()).replaceAll("__PLAYER_CSP_NONCE__", nonce), {
    headers: securityHeaders(nonce),
  });
});

app.notFound((c) => errorResponse(c, 404, "NOT_FOUND", "The route was not found."));

app.onError(async (_error, c) => {
  const player = c.get("player");
  if (player !== undefined) {
    const credential = await c.env.DB.prepare("SELECT 1 FROM player_credentials WHERE id = ?1 AND revoked_at IS NULL AND activation_state = 'active'")
      .bind(player.credential_id).first();
    if (credential === null) return errorResponse(c, 401, "UNAUTHORIZED", "The App Credential is invalid.");
  }
  if (c.req.path.startsWith("/api/v1/account/") && c.req.header("Cookie") !== undefined && await readWebSession(c) === null)
    return errorResponse(c, 401, "WEB_SESSION_REQUIRED", "Sign in again.");
  return errorResponse(c, 500, "INTERNAL_ERROR", "The request could not be completed.");
});

export default app;
