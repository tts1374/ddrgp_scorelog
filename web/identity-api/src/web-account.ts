import type { Hono, Context } from "hono";
import { deleteCookie, getCookie, setCookie } from "hono/cookie";
import type { AppEnvironment } from "./index";
import { deriveCredentialSecret, digestCredentialSecret, randomId } from "./crypto";
import { exchangeGoogleCode, type GoogleClaims } from "./google-oidc";

export type AccountContext = Context<AppEnvironment>;
export interface WebSession {
  digest: string; issuer: string; sub: string; email: string; auth_time: number | null;
  browser_digest: string; csrf_digest: string; expires_at: string;
}
export interface AppAuthorization {
  id: string; secret_digest: string; purpose: "connect" | "unlink";
  intent: "register" | "login" | "link" | "unlink" | null;
  start_player_id: string | null; start_credential_id: string | null;
  expected_public_player_id: string | null; player_id: string | null; credential_id: string | null;
  session_digest: string | null; browser_digest: string | null; issuer: string | null; sub: string | null;
  status: string; created_at: string; expires_at: string;
}
export interface OperationProof {
  digest: string; purpose: string; player_id: string | null; issuer: string; sub: string;
  session_digest: string; browser_digest: string; app_authorization_id: string | null;
  confirmed_at: string; expires_at: string; consumed_at: string | null;
}
interface OAuthRequest {
  state_digest: string; nonce: string; browser_digest: string; purpose: string;
  app_authorization_id: string | null; intent: string | null; start_session_digest: string | null;
  player_id: string | null; issuer: string | null; sub: string | null;
  expires_at: string; consumed_at: string | null;
}
export function accountError(c: AccountContext, code: string, status: 400 | 401 | 403 | 404 | 409 | 410 = 409) {
  return c.json({ error: { code } }, status);
}
export const serverNow = () => new Date().toISOString();
export const expiresIn = (milliseconds: number) => new Date(Date.now() + milliseconds).toISOString();
export const accountDigest = (c: AccountContext, purpose: string, value: string) =>
  digestCredentialSecret(c.env.CREDENTIAL_PEPPER, `${purpose}\0${value}`);
export function authorizationKey(c: AccountContext): string {
  if (!c.env.APP_AUTHORIZATION_SECRET) throw new Error("App authorization configuration is unavailable.");
  return c.env.APP_AUTHORIZATION_SECRET;
}
export function configuredOrigin(c: AccountContext): string {
  const value = c.env.PUBLIC_WEB_ORIGIN;
  if (!value) throw new Error("Web origin configuration is unavailable.");
  const url = new URL(value);
  if (url.origin !== value || (url.protocol !== "https:" && !(c.env.GOOGLE_ALLOW_LOCAL_HTTP === "true" &&
      url.protocol === "http:" && ["localhost", "127.0.0.1"].includes(url.hostname)))) {
    throw new Error("Web origin configuration is invalid.");
  }
  return value;
}
function googleCallback(c: AccountContext): string {
  const origin = configuredOrigin(c);
  if (!origin.startsWith("https://")) throw new Error("Google requires a fixed HTTPS callback.");
  return `${origin}/api/v1/auth/web/google/callback`;
}
function local(c: AccountContext) { return configuredOrigin(c).startsWith("http:"); }
export function cookieName(c: AccountContext, purpose: string) {
  return `${local(c) ? "ddrgp-dev-" : "__Host-ddrgp-"}${purpose}`;
}
export function setAccountCookie(c: AccountContext, purpose: string, value: string, ttl: number) {
  setCookie(c, cookieName(c, purpose), value, { secure: !local(c), httpOnly: true,
    sameSite: "Lax", path: "/", maxAge: ttl });
}
export function clearAccountCookie(c: AccountContext, purpose: string) {
  deleteCookie(c, cookieName(c, purpose), { secure: !local(c), httpOnly: true, sameSite: "Lax", path: "/" });
}
export function rawCookie(c: AccountContext, purpose: string): string | null {
  const value = getCookie(c, cookieName(c, purpose));
  return value !== undefined && /^[A-Za-z0-9_-]{43}$/u.test(value) ? value : null;
}
export async function browserBinding(c: AccountContext, create = false) {
  let raw = rawCookie(c, "browser");
  if (raw === null && create) {
    raw = randomId("", 32);
    setAccountCookie(c, "browser", raw, 86400);
  }
  return raw === null ? null : { raw, digest: await accountDigest(c, "browser", raw) };
}
export async function readWebSession(c: AccountContext): Promise<WebSession | null> {
  const raw = rawCookie(c, "session");
  const browser = await browserBinding(c);
  if (raw === null || browser === null) return null;
  const digest = await accountDigest(c, "session", raw);
  return c.env.DB.prepare(`SELECT * FROM web_sessions WHERE digest = ?1 AND browser_digest = ?2 AND expires_at > ?3`)
    .bind(digest, browser.digest, serverNow()).first<WebSession>();
}
export async function csrfValue(c: AccountContext, purpose: "session" | "browser") {
  const raw = rawCookie(c, purpose);
  return raw === null ? null : deriveCredentialSecret(authorizationKey(c), `web-csrf-${purpose}`, raw);
}
export async function validOriginCsrf(c: AccountContext, session: WebSession | null, allowBrowser = false) {
  if (c.req.header("Origin") !== configuredOrigin(c)) return false;
  const csrf = c.req.header("X-CSRF-Token");
  if (csrf === undefined) return false;
  if (session !== null) return await accountDigest(c, "csrf", csrf) === session.csrf_digest;
  return allowBrowser && csrf === await csrfValue(c, "browser");
}
export async function linkedPlayer(c: AccountContext, session: Pick<WebSession, "issuer" | "sub">) {
  return c.env.DB.prepare(`SELECT p.id, p.public_player_id, p.display_name, p.created_at, p.updated_at
    FROM google_identities g JOIN players p ON p.id = g.player_id WHERE g.issuer = ?1 AND g.sub = ?2`)
    .bind(session.issuer, session.sub).first<{ id: string; public_player_id: string; display_name: string;
      created_at: string; updated_at: string }>();
}
export async function readAppAuthorization(c: AccountContext, id: string) {
  return c.env.DB.prepare("SELECT * FROM app_authorizations WHERE id = ?1").bind(id).first<AppAuthorization>();
}
export async function readOperationProof(c: AccountContext, purpose: string) {
  const raw = rawCookie(c, `operation-${purpose}`);
  if (raw === null) return null;
  return c.env.DB.prepare("SELECT * FROM web_operation_proofs WHERE digest = ?1")
    .bind(await accountDigest(c, "operation", raw)).first<OperationProof>();
}
export function sessionGuard(c: AccountContext, id: string, session: WebSession, playerId: string | null) {
  return c.env.DB.prepare(`INSERT INTO account_write_guards (id, guard) VALUES (?1,
    CASE WHEN EXISTS (SELECT 1 FROM web_sessions WHERE digest = ?2 AND issuer = ?3 AND sub = ?4
      AND browser_digest = ?5 AND expires_at > ?6)
      AND (?7 IS NULL OR EXISTS (SELECT 1 FROM google_identities WHERE issuer = ?3 AND sub = ?4 AND player_id = ?7))
    THEN 1 ELSE 0 END)`).bind(id, session.digest, session.issuer, session.sub, session.browser_digest, serverNow(), playerId);
}
export function removeGuard(c: AccountContext, id: string) {
  return c.env.DB.prepare("DELETE FROM account_write_guards WHERE id = ?1").bind(id);
}
export function operationGuard(c: AccountContext, id: string, proof: OperationProof) {
  return c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
    SELECT 1 FROM web_operation_proofs WHERE digest = ?1 AND purpose = ?2 AND player_id IS ?3
      AND issuer = ?4 AND sub = ?5 AND session_digest = ?6 AND browser_digest = ?7
      AND app_authorization_id IS ?8 AND expires_at > ?9 AND consumed_at IS NULL
  ) THEN 1 ELSE 0 END WHERE id = ?10`).bind(proof.digest, proof.purpose, proof.player_id, proof.issuer,
    proof.sub, proof.session_digest, proof.browser_digest, proof.app_authorization_id, serverNow(), id);
}
export function proofMatches(proof: OperationProof | null, session: WebSession, purpose: string, player: string | null, appId: string | null) {
  return proof !== null && proof.purpose === purpose && proof.player_id === player && proof.issuer === session.issuer &&
    proof.sub === session.sub && proof.session_digest === session.digest && proof.browser_digest === session.browser_digest &&
    proof.app_authorization_id === appId && proof.expires_at > serverNow() && proof.consumed_at === null;
}
export async function beginOAuth(c: AccountContext, options: {
  purpose: string; app?: AppAuthorization; session?: WebSession | null; playerId?: string | null;
  issuer?: string | null; sub?: string | null;
}) {
  googleCallback(c);
  const browser = await browserBinding(c, true);
  const state = randomId("", 32), nonce = randomId("", 32);
  const expiry = expiresIn(600_000);
  await c.env.DB.prepare(`INSERT INTO web_oauth_requests (state_digest, nonce, browser_digest, purpose,
    app_authorization_id, intent, start_session_digest, player_id, issuer, sub, created_at, expires_at)
    VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12)`).bind(
      await accountDigest(c, "state", state), nonce, browser!.digest, options.purpose, options.app?.id ?? null,
      options.app?.intent ?? null, options.session?.digest ?? null, options.playerId ?? null,
      options.issuer ?? null, options.sub ?? null, serverNow(), expiry).run();
  return `${configuredOrigin(c)}/api/v1/auth/web/google/start?flow=${state}`;
}
function googleUrl(c: AccountContext, state: string, row: OAuthRequest) {
  if (!c.env.GOOGLE_CLIENT_ID || !c.env.GOOGLE_CLIENT_SECRET) throw new Error("Google configuration is unavailable.");
  const params = new URLSearchParams({ client_id: c.env.GOOGLE_CLIENT_ID, response_type: "code",
    redirect_uri: googleCallback(c), scope: "openid email",
    state, nonce: row.nonce, prompt: "select_account" });
  return `https://accounts.google.com/o/oauth2/v2/auth?${params}`;
}
function returnPath(row: OAuthRequest) {
  return row.app_authorization_id === null ? (row.purpose === "delete" ? "/my/account-delete"
    : row.purpose === "public-bests-delete" ? "/my/public-data-delete" : "/my/profile")
    : `/my/app-connect?request=${encodeURIComponent(row.app_authorization_id)}`;
}
async function sessionInsert(c: AccountContext, claims: GoogleClaims, browserDigest: string) {
  const raw = randomId("", 32);
  const digest = await accountDigest(c, "session", raw);
  const csrf = await deriveCredentialSecret(authorizationKey(c), "web-csrf-session", raw);
  return { raw, digest, statement: c.env.DB.prepare(`INSERT INTO web_sessions
    (digest, issuer, sub, email, auth_time, browser_digest, csrf_digest, created_at, expires_at)
    VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)`).bind(digest, claims.issuer, claims.sub,
      claims.email, claims.auth_time, browserDigest, await accountDigest(c, "csrf", csrf), serverNow(), expiresIn(86400_000)) };
}

export function registerWebAccountRoutes(app: Hono<AppEnvironment>) {
  app.use("/api/v1/*", async (c, next) => {
    if (c.req.path.startsWith("/api/v1/auth/") || c.req.path.startsWith("/api/v1/account")) {
      c.header("Referrer-Policy", "no-referrer");
      const now = serverNow();
      await c.env.DB.batch([
        c.env.DB.prepare(`UPDATE player_credentials SET revoked_at = ?1 WHERE activation_state = 'pending'
          AND revoked_at IS NULL AND id IN (SELECT credential_id FROM app_authorizations
            WHERE status = 'APPROVED' AND (expires_at <= ?1 OR session_digest IN
              (SELECT digest FROM web_sessions WHERE expires_at <= ?1)))`).bind(now),
        c.env.DB.prepare(`UPDATE app_authorizations SET status = 'INVALIDATED', session_digest = NULL,
          browser_digest = NULL, issuer = NULL, sub = NULL WHERE status IN ('PENDING','APPROVED')
          AND session_digest IN (SELECT digest FROM web_sessions WHERE expires_at <= ?1)`).bind(now),
        c.env.DB.prepare(`DELETE FROM web_oauth_requests WHERE expires_at <= ?1 OR start_session_digest IN
          (SELECT digest FROM web_sessions WHERE expires_at <= ?1)`).bind(now),
        c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE expires_at <= ?1").bind(now),
        c.env.DB.prepare(`DELETE FROM account_deletion_confirmations WHERE expires_at <= ?1 OR
          (status != 'DELETED' AND session_digest IN (SELECT digest FROM web_sessions WHERE expires_at <= ?1))`).bind(now),
        c.env.DB.prepare("DELETE FROM web_sessions WHERE expires_at <= ?1").bind(now),
      ]);
    }
    await next();
  });
  app.use("/api/v1/auth/*", async (c, next) => { c.header("Cache-Control", "no-store"); await next(); });
  app.use("/api/v1/account/*", async (c, next) => { c.header("Cache-Control", "no-store"); await next(); });
  app.use("/api/v1/account", async (c, next) => { c.header("Cache-Control", "no-store"); await next(); });
  app.get("/api/v1/auth/web/context", async c => {
    const browser = rawCookie(c, "browser");
    if (browser === null) {
      const raw = randomId("", 32);
      setAccountCookie(c, "browser", raw, 86400);
      return c.json({ csrf_token: await deriveCredentialSecret(authorizationKey(c), "web-csrf-browser", raw) });
    }
    return c.json({ csrf_token: await csrfValue(c, "browser") });
  });
  app.get("/api/v1/auth/web/google/start", async c => {
    if (Object.keys(c.req.query()).some(key => key !== "flow")) return accountError(c, "INVALID_REQUEST", 400);
    const flow = c.req.query("flow");
    if (flow === undefined) {
      const url = await beginOAuth(c, { purpose: "login" });
      return c.redirect(url);
    }
    const browser = await browserBinding(c);
    const row = await c.env.DB.prepare("SELECT * FROM web_oauth_requests WHERE state_digest = ?1")
      .bind(await accountDigest(c, "state", flow)).first<OAuthRequest>();
    if (row === null || browser === null || row.browser_digest !== browser.digest || row.consumed_at !== null || row.expires_at <= serverNow())
      return accountError(c, "OAUTH_FLOW_INVALID", 400);
    return c.redirect(googleUrl(c, flow, row));
  });
  app.get("/api/v1/auth/web/google/callback", async c => {
    const state = c.req.query("state");
    const browser = await browserBinding(c);
    const row = state === undefined ? null : await c.env.DB.prepare("SELECT * FROM web_oauth_requests WHERE state_digest = ?1")
      .bind(await accountDigest(c, "state", state)).first<OAuthRequest>();
    if (row === null || browser === null || row.browser_digest !== browser.digest || row.consumed_at !== null || row.expires_at <= serverNow())
      return c.redirect("/my/auth-error");
    const failed = async (reason: string, safe = false) => {
      await c.env.DB.prepare("UPDATE web_oauth_requests SET consumed_at = ?1 WHERE state_digest = ?2 AND consumed_at IS NULL")
        .bind(serverNow(), row.state_digest).run();
      return c.redirect(safe ? "/my/auth-error" : `${returnPath(row)}${row.app_authorization_id === null ? "?" : "&"}auth_error=${reason}`);
    };
    const code = c.req.query("code");
    if (c.req.query("error") !== undefined || !code) {
      return failed("cancelled");
    }
    let claims: GoogleClaims;
    try {
      claims = await exchangeGoogleCode(code, c.env.GOOGLE_CLIENT_ID!, c.env.GOOGLE_CLIENT_SECRET!,
        googleCallback(c), row.nonce);
    } catch {
      return failed("failed");
    }
    if ((row.issuer !== null && claims.issuer !== row.issuer) || (row.sub !== null && claims.sub !== row.sub))
      return failed("identity");
    const linked = await linkedPlayer(c, claims);
    const appRequest = row.app_authorization_id === null ? null : await readAppAuthorization(c, row.app_authorization_id);
    if (row.app_authorization_id !== null && (appRequest === null || appRequest.status !== "PENDING" ||
        appRequest.expires_at <= serverNow() || appRequest.intent !== row.intent || appRequest.browser_digest !== browser.digest ||
        appRequest.start_player_id !== row.player_id || appRequest.session_digest !== row.start_session_digest))
      return failed("binding", true);
    // A valid App credential already pins the Player and validates its expected public ID at start.
    // Only credential-less recovery must resolve that ID through an existing Google link.
    if (appRequest?.start_player_id === null && appRequest.expected_public_player_id !== null &&
        linked?.public_player_id !== appRequest.expected_public_player_id)
      return failed("player");
    if (appRequest?.start_player_id !== null && appRequest?.start_player_id !== undefined &&
        linked !== null && linked.id !== appRequest.start_player_id)
      return failed("conflict");
    let session: { raw: string; digest: string; statement?: D1PreparedStatement };
    const existing = row.start_session_digest === null ? null : await readWebSession(c);
    if (row.start_session_digest !== null) {
      if (existing === null || existing.digest !== row.start_session_digest || existing.issuer !== claims.issuer || existing.sub !== claims.sub)
        return failed("session", true);
      session = { raw: rawCookie(c, "session")!, digest: existing.digest };
    } else session = await sessionInsert(c, claims, browser.digest);
    const proofRaw = randomId("", 32);
    const proofDigest = await accountDigest(c, "operation", proofRaw);
    const now = serverNow();
    const expiry = expiresIn(600_000);
    const guard = randomId("og_");
    const statements = [c.env.DB.prepare(`INSERT INTO account_write_guards (id, guard) VALUES (?1,
      CASE WHEN EXISTS (SELECT 1 FROM web_oauth_requests WHERE state_digest = ?2 AND browser_digest = ?3
        AND consumed_at IS NULL AND expires_at > ?4 AND purpose = ?5 AND app_authorization_id IS ?6
        AND intent IS ?7 AND start_session_digest IS ?8 AND player_id IS ?9 AND issuer IS ?10 AND sub IS ?11
        AND nonce = ?12) THEN 1 ELSE 0 END)`)
      .bind(guard, row.state_digest, browser.digest, now, row.purpose, row.app_authorization_id,
        row.intent, row.start_session_digest, row.player_id, row.issuer, row.sub, row.nonce)];
    if (existing !== null) statements.push(c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
      SELECT 1 FROM web_sessions WHERE digest = ?1 AND expires_at > ?2 AND issuer = ?3 AND sub = ?4 AND browser_digest = ?5
    ) THEN 1 ELSE 0 END WHERE id = ?6`).bind(existing.digest, now, claims.issuer, claims.sub, browser.digest, guard));
    if (session.statement !== undefined) statements.push(session.statement);
    const target = appRequest?.start_player_id ?? linked?.id ?? row.player_id;
    // The unified App entry resolves its action only after Google identity verification.
    // No account or credential is created until the user approves that action.
    const intent = appRequest !== null && appRequest.intent === null && row.purpose === "login-connect"
      ? linked === null ? "register" : "login" : row.intent;
    const operationPurpose = intent === "register" ? "register" : row.purpose;
    if (row.purpose !== "login") {
      statements.push(c.env.DB.prepare(`INSERT INTO web_operation_proofs
        (digest, purpose, player_id, issuer, sub, session_digest, browser_digest, app_authorization_id, confirmed_at, expires_at)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)`).bind(proofDigest, operationPurpose,
          target, claims.issuer, claims.sub, session.digest, browser.digest, row.app_authorization_id, now, expiry));
    }
    if (appRequest !== null) {
      statements.push(c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
        SELECT 1 FROM app_authorizations WHERE id = ?1 AND status = 'PENDING' AND expires_at > ?2 AND intent IS ?3
          AND browser_digest = ?4 AND session_digest IS ?5 AND start_player_id IS ?6 AND start_credential_id IS ?7
          AND expected_public_player_id IS ?8 AND (?7 IS NULL OR EXISTS (SELECT 1 FROM player_credentials
            WHERE id = ?7 AND player_id = ?6 AND revoked_at IS NULL AND activation_state = 'active'))
      ) THEN 1 ELSE 0 END WHERE id = ?9`).bind(appRequest.id, now, row.intent, browser.digest,
        row.start_session_digest, appRequest.start_player_id, appRequest.start_credential_id,
        appRequest.expected_public_player_id, guard));
      statements.push(c.env.DB.prepare(`UPDATE app_authorizations SET session_digest = ?1, issuer = ?2, sub = ?3,
        player_id = ?4, intent = ?5 WHERE id = ?6`).bind(session.digest, claims.issuer, claims.sub, target, intent, appRequest.id));
    }
    const deletionRaw = randomId("", 32);
    const deletionId = randomId("dc_");
    if (row.purpose === "delete" || row.purpose === "public-bests-delete") {
      if (linked?.id !== row.player_id) return failed("player", true);
      statements.push(c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
        SELECT 1 FROM google_identities WHERE player_id = ?1 AND issuer = ?2 AND sub = ?3
      ) THEN 1 ELSE 0 END WHERE id = ?4`).bind(row.player_id, claims.issuer, claims.sub, guard));
    }
    if (row.purpose === "delete") {
      statements.push(c.env.DB.prepare(`INSERT INTO account_deletion_confirmations
        (id, digest, operation_digest, player_id, issuer, sub, session_digest, browser_digest, status, expires_at)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, 'UNCONFIRMED', ?9)`).bind(deletionId,
          await accountDigest(c, "deletion", deletionRaw), proofDigest, target, claims.issuer,
          claims.sub, session.digest, browser.digest, expiry));
    }
    statements.push(c.env.DB.prepare("UPDATE web_oauth_requests SET consumed_at = ?1 WHERE state_digest = ?2").bind(now, row.state_digest), removeGuard(c, guard));
    try { await c.env.DB.batch(statements); } catch { return c.redirect("/my/auth-error"); }
    if (session.statement !== undefined) setAccountCookie(c, "session", session.raw, 86400);
    if (row.purpose !== "login") setAccountCookie(c, `operation-${operationPurpose}`, proofRaw, 600);
    if (row.purpose === "delete") setAccountCookie(c, "deletion", deletionRaw, 600);
    return c.redirect(returnPath(row));
  });
  app.get("/api/v1/account/session", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    const player = await linkedPlayer(c, session);
    return c.json({ email: session.email, google_linked: player !== null, csrf_token: await csrfValue(c, "session") });
  });
  app.get("/api/v1/account/profile", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    const player = await linkedPlayer(c, session);
    if (player === null) return accountError(c, "PLAYER_NOT_LINKED");
    return c.json({ public_player_id: player.public_player_id, display_name: player.display_name,
      public_url: `${configuredOrigin(c)}/player/${player.public_player_id}` });
  });
  app.patch("/api/v1/account/profile", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body === null || typeof body.display_name !== "string" || Object.keys(body).some(key => key !== "display_name"))
      return accountError(c, "INVALID_REQUEST", 400);
    const name = body.display_name.trim();
    if (Array.from(name).length < 1 || Array.from(name).length > 64) return accountError(c, "INVALID_DISPLAY_NAME", 400);
    const player = await linkedPlayer(c, session);
    if (player === null) return accountError(c, "PLAYER_NOT_LINKED");
    const guard = randomId("pg_");
    await c.env.DB.batch([sessionGuard(c, guard, session, player.id),
      c.env.DB.prepare("UPDATE players SET display_name = ?1, updated_at = ?2 WHERE id = ?3").bind(name, serverNow(), player.id), removeGuard(c, guard)]);
    return c.json({ public_player_id: player.public_player_id, display_name: name });
  });
  app.post("/api/v1/auth/web/logout", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body === null || Array.isArray(body) || typeof body !== "object" ||
        Object.keys(body).some(key => key !== "app_authorization_id") ||
        (body.app_authorization_id !== undefined && typeof body.app_authorization_id !== "string"))
      return accountError(c, "INVALID_REQUEST", 400);
    const preserved = body.app_authorization_id === undefined ? null : await readAppAuthorization(c, body.app_authorization_id);
    if (body.app_authorization_id !== undefined && (preserved === null || preserved.status !== "PENDING" ||
        preserved.session_digest !== session.digest || preserved.browser_digest !== session.browser_digest))
      return accountError(c, "AUTHORIZATION_INVALID", 403);
    const guard = randomId("lo_");
    await c.env.DB.batch([
      sessionGuard(c, guard, session, null),
      c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE start_session_digest = ?1").bind(session.digest),
      c.env.DB.prepare(`DELETE FROM web_oauth_requests WHERE app_authorization_id IN
        (SELECT id FROM app_authorizations WHERE session_digest = ?1 AND status = 'PENDING')`).bind(session.digest),
      c.env.DB.prepare(`UPDATE player_credentials SET revoked_at = ?1 WHERE activation_state = 'pending'
        AND id IN (SELECT credential_id FROM app_authorizations WHERE session_digest = ?2 AND status = 'APPROVED')`)
        .bind(serverNow(), session.digest),
      c.env.DB.prepare(`UPDATE app_authorizations SET status = 'INVALIDATED', issuer = NULL, sub = NULL,
        session_digest = NULL, browser_digest = NULL WHERE session_digest = ?1 AND status IN ('PENDING','APPROVED')
        AND (?2 IS NULL OR id != ?2)`).bind(session.digest, preserved?.id ?? null),
      c.env.DB.prepare(`UPDATE app_authorizations SET intent = NULL, player_id = NULL, issuer = NULL, sub = NULL,
        session_digest = NULL WHERE id = ?1 AND status = 'PENDING'`).bind(preserved?.id ?? null),
      c.env.DB.prepare("DELETE FROM account_deletion_confirmations WHERE session_digest = ?1 AND status != 'DELETED'").bind(session.digest),
      c.env.DB.prepare("DELETE FROM web_sessions WHERE digest = ?1").bind(session.digest),
      removeGuard(c, guard),
    ]);
    clearAccountCookie(c, "session");
    return c.body(null, 204);
  });
}
