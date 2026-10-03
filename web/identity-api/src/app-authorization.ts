import type { Hono } from "hono";
import type { AppEnvironment } from "./index";
import { deriveCredentialSecret, digestCredentialSecret, randomId, verifyCredentialSecret } from "./crypto";
import { accountDigest, accountError, authorizationKey, beginOAuth, browserBinding, configuredOrigin,
  expiresIn, linkedPlayer, operationGuard, proofMatches, readAppAuthorization, readOperationProof,
  readWebSession, removeGuard, serverNow, sessionGuard, validOriginCsrf,
  type AccountContext, type AppAuthorization } from "./web-account";

interface CredentialOwner { id: string; player_id: string; public_player_id: string; activation_state: string; revoked_at: string | null }
export async function appCredentialOwner(c: AccountContext, allowPending = false): Promise<CredentialOwner | null> {
  const bearer = c.req.header("Authorization");
  const match = bearer?.match(/^Bearer (ac_[A-Za-z0-9_-]{20,64})\.([A-Za-z0-9_-]{43})$/u);
  if (!match) return null;
  const row = await c.env.DB.prepare(`SELECT c.id, c.player_id, c.activation_state, c.revoked_at,
    c.secret_digest, p.public_player_id FROM player_credentials c JOIN players p ON p.id = c.player_id
    WHERE c.id = ?1 AND c.type = 'app' AND c.revoked_at IS NULL`)
    .bind(match[1]).first<CredentialOwner & { secret_digest: string }>();
  const valid = await verifyCredentialSecret(c.env.CREDENTIAL_PEPPER, match[2], row?.secret_digest ?? "0".repeat(64));
  return row !== null && valid && (allowPending || row.activation_state === "active") ? row : null;
}
async function appProof(c: AccountContext, row: AppAuthorization) {
  const body = await c.req.json().catch(() => null);
  const secret = body?.request_secret;
  if (typeof secret !== "string" || !/^[A-Za-z0-9_-]{43}$/u.test(secret) ||
      Object.keys(body).some(key => key !== "request_secret")) return null;
  return await accountDigest(c, "app-request", secret) === row.secret_digest ? secret : null;
}
async function appResult(c: AccountContext, row: AppAuthorization) {
  const player = row.player_id === null ? null : await c.env.DB.prepare(`SELECT public_player_id,
    display_name, created_at, updated_at FROM players WHERE id = ?1`).bind(row.player_id).first();
  let credential: string | undefined;
  if (row.credential_id !== null && ["APPROVED", "ACTIVATED"].includes(row.status)) {
    const active = await c.env.DB.prepare("SELECT 1 FROM player_credentials WHERE id = ?1 AND revoked_at IS NULL")
      .bind(row.credential_id).first();
    if (active !== null) credential = `${row.credential_id}.${await deriveCredentialSecret(authorizationKey(c),
      `app-authorization\0${row.id}\0${row.secret_digest}`, row.credential_id)}`;
  }
  return c.json({ status: row.status, ...(player ?? {}), ...(credential === undefined ? {} : { credential }) });
}
function appStateGuard(c: AccountContext, id: string, row: AppAuthorization, status: string) {
  return c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
    SELECT 1 FROM app_authorizations WHERE id = ?1 AND purpose = ?2 AND intent IS ?3
      AND start_player_id IS ?4 AND start_credential_id IS ?5 AND status = ?6 AND expires_at > ?7
      AND secret_digest = ?8 AND session_digest IS ?9 AND browser_digest IS ?10
      AND issuer IS ?11 AND sub IS ?12 AND player_id IS ?13
  ) THEN 1 ELSE 0 END WHERE id = ?14`).bind(row.id, row.purpose, row.intent, row.start_player_id,
    row.start_credential_id, status, serverNow(), row.secret_digest, row.session_digest,
    row.browser_digest, row.issuer, row.sub, row.player_id, id);
}
function startCredentialGuard(c: AccountContext, guard: string, row: AppAuthorization) {
  return c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN ?1 IS NULL OR EXISTS (
    SELECT 1 FROM player_credentials WHERE id = ?1 AND player_id = ?2 AND activation_state = 'active' AND revoked_at IS NULL
  ) THEN 1 ELSE 0 END WHERE id = ?3`).bind(row.start_credential_id, row.start_player_id, guard);
}

export function registerAppAuthorizationRoutes(app: Hono<AppEnvironment>) {
  app.post("/api/v1/auth/app-authorizations", async c => {
    const body = await c.req.json().catch(() => null);
    if (body === null || typeof body.id !== "string" || !/^[A-Za-z0-9_-]{22,64}$/u.test(body.id) ||
        !["connect", "unlink"].includes(body.purpose) || typeof body.request_secret !== "string" ||
        !/^[A-Za-z0-9_-]{43}$/u.test(body.request_secret) ||
        (body.expected_public_player_id !== undefined && (typeof body.expected_public_player_id !== "string" || !/^p_[A-Za-z0-9_-]{20,64}$/u.test(body.expected_public_player_id))) ||
        Object.keys(body).some(key => !["id", "purpose", "request_secret", "expected_public_player_id"].includes(key)))
      return accountError(c, "INVALID_REQUEST", 400);
    const owner = await appCredentialOwner(c);
    if ((c.req.header("Authorization") !== undefined && owner === null) || (body.purpose === "unlink" && owner === null))
      return accountError(c, "UNAUTHORIZED", 401);
    if (owner !== null && body.expected_public_player_id !== undefined && owner.public_player_id !== body.expected_public_player_id)
      return accountError(c, "PLAYER_CONFLICT");
    const secretDigest = await accountDigest(c, "app-request", body.request_secret);
    const previous = await readAppAuthorization(c, body.id);
    if (previous !== null) {
      if (previous.secret_digest !== secretDigest || previous.purpose !== body.purpose ||
          previous.start_credential_id !== (owner?.id ?? null) ||
          previous.expected_public_player_id !== (body.expected_public_player_id ?? null)) return accountError(c, "AUTHORIZATION_CONFLICT");
      if (previous.status !== "PENDING" || previous.expires_at <= serverNow()) return accountError(c, "AUTHORIZATION_FINISHED");
      return c.json({ authorization_id: previous.id, expires_at: previous.expires_at,
        comparison_code: (await accountDigest(c, "comparison", previous.id)).slice(0, 8).toUpperCase(),
        url: `${configuredOrigin(c)}/my/app-connect?request=${encodeURIComponent(previous.id)}` });
    }
    const expiry = expiresIn(600_000), now = serverNow();
    const guard = randomId("as_");
    await c.env.DB.batch([c.env.DB.prepare(`INSERT INTO account_write_guards (id, guard) VALUES (?1,
      CASE WHEN ?2 IS NULL OR EXISTS (SELECT 1 FROM player_credentials
        WHERE id = ?2 AND player_id = ?3 AND revoked_at IS NULL AND activation_state = 'active') THEN 1 ELSE 0 END)`)
      .bind(guard, owner?.id ?? null, owner?.player_id ?? null),
      c.env.DB.prepare(`INSERT INTO app_authorizations (id, secret_digest, purpose, start_player_id,
      start_credential_id, expected_public_player_id, status, created_at, expires_at)
      VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'PENDING', ?7, ?8) ON CONFLICT(id) DO NOTHING`).bind(body.id, secretDigest,
        body.purpose, owner?.player_id ?? null, owner?.id ?? null, body.expected_public_player_id ?? null, now, expiry), removeGuard(c, guard)]);
    const created = (await readAppAuthorization(c, body.id))!;
    if (created.secret_digest !== secretDigest || created.purpose !== body.purpose ||
        created.start_credential_id !== (owner?.id ?? null) || created.expected_public_player_id !== (body.expected_public_player_id ?? null))
      return accountError(c, "AUTHORIZATION_CONFLICT");
    if (created.status !== "PENDING" || created.expires_at <= serverNow()) return accountError(c, "AUTHORIZATION_FINISHED");
    return c.json({ authorization_id: body.id, expires_at: created.expires_at,
      comparison_code: (await accountDigest(c, "comparison", body.id)).slice(0, 8).toUpperCase(),
      url: `${configuredOrigin(c)}/my/app-connect?request=${encodeURIComponent(body.id)}` }, 201);
  });
  app.post("/api/v1/auth/app-authorizations/:id/web-start", async c => {
    const row = await readAppAuthorization(c, c.req.param("id"));
    if (row === null || row.status !== "PENDING" || row.expires_at <= serverNow()) return accountError(c, "AUTHORIZATION_FINISHED");
    const session = await readWebSession(c);
    if (!await validOriginCsrf(c, session, true)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body === null || typeof body !== "object" || Array.isArray(body) ||
        (body.intent !== undefined && !["register", "login"].includes(body.intent)) || Object.keys(body).some(key => key !== "intent"))
      return accountError(c, "INVALID_REQUEST", 400);
    const browser = await browserBinding(c);
    if (browser === null) return accountError(c, "BROWSER_REQUIRED", 403);
    const intent = row.purpose === "unlink" ? "unlink" : row.start_player_id !== null ? "link" : body.intent ?? null;
    const guard = randomId("ws_");
    const statements = [c.env.DB.prepare(`INSERT INTO account_write_guards (id, guard) VALUES (?1,
      CASE WHEN EXISTS (SELECT 1 FROM app_authorizations WHERE id = ?2 AND status = 'PENDING'
        AND expires_at > ?3 AND (browser_digest IS NULL OR browser_digest = ?4)) THEN 1 ELSE 0 END)`)
      .bind(guard, row.id, serverNow(), browser.digest), startCredentialGuard(c, guard, row),
      c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE app_authorization_id = ?1").bind(row.id),
      c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE app_authorization_id = ?1").bind(row.id),
      c.env.DB.prepare(`UPDATE app_authorizations SET intent = ?1, browser_digest = ?2,
        session_digest = ?3, player_id = NULL, issuer = NULL, sub = NULL WHERE id = ?4`)
        .bind(intent, browser.digest, session?.digest ?? null, row.id), removeGuard(c, guard)];
    await c.env.DB.batch(statements);
    const updated = (await readAppAuthorization(c, row.id))!;
    let identity: { issuer: string; sub: string } | null = null;
    if (row.start_player_id !== null) identity = await c.env.DB.prepare("SELECT issuer, sub FROM google_identities WHERE player_id = ?1")
      .bind(row.start_player_id).first();
    const url = await beginOAuth(c, { purpose: intent === null || intent === "login" ? "login-connect" : intent,
      app: updated, session, playerId: row.start_player_id,
      issuer: identity?.issuer, sub: identity?.sub });
    return c.json({ url });
  });
  app.get("/api/v1/auth/app-authorizations/:id/confirmation", async c => {
    const session = await readWebSession(c), row = await readAppAuthorization(c, c.req.param("id"));
    const browser = await browserBinding(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (row === null || row.session_digest !== session.digest || row.browser_digest !== browser?.digest || row.expires_at <= serverNow())
      return accountError(c, "AUTHORIZATION_INVALID", 403);
    const player = row.player_id === null ? null : await c.env.DB.prepare("SELECT public_player_id, display_name FROM players WHERE id = ?1")
      .bind(row.player_id).first();
    const comparison = (await accountDigest(c, "comparison", row.id)).slice(0, 8).toUpperCase();
    return c.json({ status: row.status, purpose: row.purpose, intent: row.intent, player,
      registered_google: (await linkedPlayer(c, session)) !== null, comparison_code: comparison });
  });
  app.post("/api/v1/auth/app-authorizations/:id/approve", async c => {
    const row = await readAppAuthorization(c, c.req.param("id")), session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body?.confirmed !== true || body?.app_compared !== true ||
        Object.keys(body).some(key => !["confirmed", "app_compared"].includes(key))) return accountError(c, "CONFIRMATION_REQUIRED");
    if (row === null || row.session_digest !== session.digest || row.browser_digest !== session.browser_digest ||
        row.issuer !== session.issuer || row.sub !== session.sub) return accountError(c, "AUTHORIZATION_INVALID", 403);
    if (["APPROVED", "ACTIVATED", "LINKED"].includes(row.status)) return c.json({ status: row.status, retry: true });
    if (row.status !== "PENDING" || row.expires_at <= serverNow()) return accountError(c, "AUTHORIZATION_FINISHED");
    const purpose = row.intent === "login" ? "login-connect" : row.intent!;
    const proof = await readOperationProof(c, purpose);
    if (!proofMatches(proof, session, purpose, row.player_id, row.id)) return accountError(c, "OPERATION_PROOF_REQUIRED", 403);
    const linked = await linkedPlayer(c, session);
    if (row.start_player_id === null && row.expected_public_player_id !== null && linked?.public_player_id !== row.expected_public_player_id)
      return accountError(c, "PLAYER_CONFLICT");
    if (row.intent === "register" && linked !== null) return accountError(c, "GOOGLE_ALREADY_REGISTERED");
    if (row.intent === "login" && linked === null) return accountError(c, "PLAYER_NOT_LINKED");
    if (row.start_player_id !== null && linked !== null && linked.id !== row.start_player_id) return accountError(c, "PLAYER_CONFLICT");
    if (row.purpose === "unlink" && (linked === null || linked.id !== row.start_player_id)) return accountError(c, "PLAYER_CONFLICT");
    const now = serverNow(), guard = randomId("ap_");
    const playerId = row.start_player_id ?? linked?.id ?? randomId("pl_");
    const publicId = randomId("p_");
    const statements = [sessionGuard(c, guard, session, row.intent === "login" || row.intent === "unlink" ? playerId : null),
      appStateGuard(c, guard, row, "PENDING"), operationGuard(c, guard, proof!), startCredentialGuard(c, guard, row)];
    if (row.intent === "register") {
      if (row.start_player_id !== null || row.expected_public_player_id !== null) return accountError(c, "PLAYER_CONFLICT");
      statements.push(c.env.DB.prepare(`INSERT INTO players (id, public_player_id, display_name, created_at, updated_at)
        VALUES (?1, ?2, 'Player', ?3, ?3)`).bind(playerId, publicId, now));
    }
    if (row.intent === "register" || row.intent === "link") {
      statements.push(c.env.DB.prepare(`INSERT INTO google_identities (issuer, sub, player_id) VALUES (?1, ?2, ?3)
        ON CONFLICT(issuer, sub) DO UPDATE SET player_id = excluded.player_id
        WHERE google_identities.player_id = excluded.player_id`).bind(session.issuer, session.sub, playerId));
      statements.push(c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
        SELECT 1 FROM google_identities WHERE issuer = ?1 AND sub = ?2 AND player_id = ?3
      ) THEN 1 ELSE 0 END WHERE id = ?4`).bind(session.issuer, session.sub, playerId, guard));
    }
    let newCredentialId: string | null = null;
    if (row.intent === "register" || row.intent === "login") {
      newCredentialId = randomId("ac_");
      // The request secret is never retained server-side. Store a digest derived from its
      // digest, then reproduce the same credential only after verifying the App proof.
      const derived = await deriveCredentialSecret(authorizationKey(c),
        `app-authorization\0${row.id}\0${row.secret_digest}`, newCredentialId);
      statements.push(c.env.DB.prepare(`INSERT INTO player_credentials
        (id, player_id, type, secret_digest, created_at, activation_state) VALUES (?1, ?2, 'app', ?3, ?4, 'pending')`)
        .bind(newCredentialId, playerId, await digestCredentialSecret(c.env.CREDENTIAL_PEPPER, derived), now));
    }
    const status = row.purpose === "unlink" ? "UNLINKED" : row.intent === "link" ? "LINKED" : "APPROVED";
    statements.push(c.env.DB.prepare("UPDATE web_operation_proofs SET consumed_at = ?1 WHERE digest = ?2").bind(now, proof!.digest),
      c.env.DB.prepare(`UPDATE app_authorizations SET status = ?1, player_id = ?2, credential_id = ?3 WHERE id = ?4`)
        .bind(status, playerId, newCredentialId, row.id));
    if (row.purpose === "unlink") {
      statements.push(
        c.env.DB.prepare("DELETE FROM google_identities WHERE issuer = ?1 AND sub = ?2 AND player_id = ?3").bind(session.issuer, session.sub, playerId),
        c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE (issuer = ?1 AND sub = ?2) OR start_session_digest IN (SELECT digest FROM web_sessions WHERE issuer = ?1 AND sub = ?2)").bind(session.issuer, session.sub),
        c.env.DB.prepare(`UPDATE player_credentials SET revoked_at = ?1 WHERE activation_state = 'pending' AND id IN (
          SELECT credential_id FROM app_authorizations WHERE issuer = ?2 AND sub = ?3 AND status = 'APPROVED'
        )`).bind(now, session.issuer, session.sub),
        c.env.DB.prepare(`UPDATE app_authorizations SET status = 'INVALIDATED', issuer = NULL, sub = NULL,
          session_digest = NULL, browser_digest = NULL WHERE id != ?1 AND ((issuer = ?2 AND sub = ?3)
            OR session_digest IN (SELECT digest FROM web_sessions WHERE issuer = ?2 AND sub = ?3))
          AND status IN ('PENDING','APPROVED')`).bind(row.id, session.issuer, session.sub),
        c.env.DB.prepare("DELETE FROM account_deletion_confirmations WHERE issuer = ?1 AND sub = ?2").bind(session.issuer, session.sub),
        c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE issuer = ?1 AND sub = ?2").bind(session.issuer, session.sub),
        c.env.DB.prepare("DELETE FROM web_sessions WHERE issuer = ?1 AND sub = ?2").bind(session.issuer, session.sub),
        c.env.DB.prepare("UPDATE app_authorizations SET issuer = NULL, sub = NULL, session_digest = NULL, browser_digest = NULL WHERE id = ?1").bind(row.id),
      );
    }
    statements.push(removeGuard(c, guard));
    try { await c.env.DB.batch(statements); } catch {
      const current = await readAppAuthorization(c, row.id);
      if (current !== null && ["APPROVED", "ACTIVATED", "LINKED"].includes(current.status) &&
          current.session_digest === session.digest && current.browser_digest === session.browser_digest &&
          current.issuer === session.issuer && current.sub === session.sub)
        return c.json({ status: current.status, retry: true });
      if (await readWebSession(c) === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
      return accountError(c, "AUTHORIZATION_CONFLICT");
    }
    return c.json({ status });
  });
  app.post("/api/v1/auth/app-authorizations/:id/result", async c => {
    const row = await readAppAuthorization(c, c.req.param("id"));
    if (row === null) return accountError(c, "AUTHORIZATION_NOT_FOUND", 404);
    const secret = await appProof(c, row);
    if (secret === null) return accountError(c, "APP_PROOF_REQUIRED", 401);
    if (row.expires_at <= serverNow() && !["LINKED", "UNLINKED"].includes(row.status)) return accountError(c, "AUTHORIZATION_EXPIRED");
    return appResult(c, row);
  });
  app.get("/api/v1/auth/app-authorizations/:id/status", async c => {
    const row = await readAppAuthorization(c, c.req.param("id")), session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (row === null || row.session_digest !== session.digest || row.browser_digest !== session.browser_digest || row.expires_at <= serverNow())
      return accountError(c, "AUTHORIZATION_INVALID", 403);
    return c.json({ status: row.status });
  });
  app.post("/api/v1/auth/app-authorizations/:id/activate", async c => {
    const row = await readAppAuthorization(c, c.req.param("id"));
    if (row === null) return accountError(c, "AUTHORIZATION_NOT_FOUND", 404);
    const secret = await appProof(c, row), owner = await appCredentialOwner(c, true);
    if (secret === null || owner === null || owner.id !== row.credential_id || owner.player_id !== row.player_id)
      return accountError(c, "UNAUTHORIZED", 401);
    if (row.status === "ACTIVATED" && owner.activation_state === "active") return c.json({ status: "ACTIVATED", retry: true });
    if (row.status !== "APPROVED" || row.expires_at <= serverNow() || owner.activation_state !== "pending") return accountError(c, "AUTHORIZATION_FINISHED");
    const now = serverNow(), guard = randomId("ag_");
    try { await c.env.DB.batch([
      c.env.DB.prepare(`INSERT INTO account_write_guards (id, guard) VALUES (?1, CASE WHEN EXISTS (
        SELECT 1 FROM player_credentials WHERE id = ?2 AND revoked_at IS NULL AND activation_state = 'pending'
      ) THEN 1 ELSE 0 END)`).bind(guard, owner.id),
      appStateGuard(c, guard, row, "APPROVED"),
      c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
        SELECT 1 FROM google_identities WHERE player_id = ?1 AND issuer = ?2 AND sub = ?3
      ) THEN 1 ELSE 0 END WHERE id = ?4`).bind(row.player_id, row.issuer, row.sub, guard),
      c.env.DB.prepare("UPDATE player_credentials SET activation_state = 'active' WHERE id = ?1").bind(owner.id),
      c.env.DB.prepare("UPDATE player_credentials SET revoked_at = ?1 WHERE player_id = ?2 AND id != ?3 AND revoked_at IS NULL")
        .bind(now, owner.player_id, owner.id),
      c.env.DB.prepare("UPDATE app_authorizations SET status = 'INVALIDATED' WHERE (player_id = ?1 OR start_player_id = ?1) AND id != ?2 AND status IN ('PENDING','APPROVED')")
        .bind(owner.player_id, row.id),
      c.env.DB.prepare("UPDATE best_sync_snapshots SET status = 'ABORTED' WHERE player_id = ?1 AND status = 'PENDING'").bind(owner.player_id),
      c.env.DB.prepare("DELETE FROM best_sync_snapshot_items WHERE snapshot_id IN (SELECT snapshot_id FROM best_sync_snapshots WHERE player_id = ?1 AND status = 'ABORTED')").bind(owner.player_id),
      c.env.DB.prepare("DELETE FROM best_sync_snapshot_chunks WHERE snapshot_id IN (SELECT snapshot_id FROM best_sync_snapshots WHERE player_id = ?1 AND status = 'ABORTED')").bind(owner.player_id),
      c.env.DB.prepare("DELETE FROM best_replacement_authorizations WHERE credential_id != ?1 AND snapshot_id IN (SELECT snapshot_id FROM best_sync_snapshots WHERE player_id = ?2)").bind(owner.id, owner.player_id),
      c.env.DB.prepare("UPDATE app_authorizations SET status = 'ACTIVATED' WHERE id = ?1").bind(row.id), removeGuard(c, guard),
    ]); } catch {
      const currentOwner = await appCredentialOwner(c, true), current = await readAppAuthorization(c, row.id);
      if (currentOwner === null) return accountError(c, "UNAUTHORIZED", 401);
      if (current?.status === "ACTIVATED" && currentOwner.activation_state === "active")
        return c.json({ status: "ACTIVATED", retry: true });
      return accountError(c, "AUTHORIZATION_CONFLICT");
    }
    return c.json({ status: "ACTIVATED", retry: false });
  });
  app.post("/api/v1/auth/app-authorizations/:id/cancel", async c => {
    const row = await readAppAuthorization(c, c.req.param("id"));
    if (row === null) return accountError(c, "AUTHORIZATION_NOT_FOUND", 404);
    const body = await c.req.json().catch(() => null);
    const session = await readWebSession(c);
    const secret = typeof body?.request_secret === "string" && await accountDigest(c, "app-request", body.request_secret) === row.secret_digest;
    const web = session !== null && row.session_digest === session.digest && row.browser_digest === session.browser_digest && await validOriginCsrf(c, session);
    if (!secret && !web) return accountError(c, "APP_PROOF_REQUIRED", 401);
    await c.env.DB.batch([
      c.env.DB.prepare("UPDATE app_authorizations SET status = 'CANCELLED' WHERE id = ?1 AND status = 'PENDING'").bind(row.id),
      c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE app_authorization_id = ?1").bind(row.id),
      c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE app_authorization_id = ?1").bind(row.id),
    ]);
    return c.json({ status: (await readAppAuthorization(c, row.id))!.status });
  });
}
