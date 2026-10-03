import type { Hono } from "hono";
import type { AppEnvironment } from "./index";
import { randomId } from "./crypto";
import { accountDigest, accountError, beginOAuth, clearAccountCookie, expiresIn, linkedPlayer,
  operationGuard, proofMatches, rawCookie, readOperationProof, readWebSession, removeGuard,
  serverNow, sessionGuard, setAccountCookie, validOriginCsrf, type AccountContext } from "./web-account";

interface DeletionConfirmation {
  id: string; digest: string; operation_digest: string | null; player_id: string | null;
  issuer: string | null; sub: string | null; session_digest: string | null; browser_digest: string | null;
  status: "UNCONFIRMED" | "CONFIRMED" | "DELETED"; expires_at: string; consumed_at: string | null;
}
async function readDeletion(c: AccountContext) {
  const raw = rawCookie(c, "deletion");
  if (raw === null) return null;
  return c.env.DB.prepare("SELECT * FROM account_deletion_confirmations WHERE digest = ?1")
    .bind(await accountDigest(c, "deletion", raw)).first<DeletionConfirmation>();
}
function deletionGuard(c: AccountContext, guard: string, row: DeletionConfirmation, status: string) {
  return c.env.DB.prepare(`UPDATE account_write_guards SET guard = CASE WHEN EXISTS (
    SELECT 1 FROM account_deletion_confirmations WHERE id = ?1 AND digest = ?2 AND operation_digest IS ?3
      AND player_id IS ?4 AND issuer IS ?5 AND sub IS ?6 AND session_digest IS ?7 AND browser_digest IS ?8
      AND status = ?9 AND expires_at > ?10 AND consumed_at IS NULL
  ) THEN 1 ELSE 0 END WHERE id = ?11`).bind(row.id, row.digest, row.operation_digest,
    row.player_id, row.issuer, row.sub, row.session_digest, row.browser_digest, status, serverNow(), guard);
}
export function registerAccountDeletionRoutes(app: Hono<AppEnvironment>) {
  app.post("/api/v1/account/deletion-start", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (new URL(c.req.url).search !== "" || body === null || Array.isArray(body) || typeof body !== "object" ||
        Object.keys(body).length !== 0) return accountError(c, "INVALID_REQUEST", 400);
    const player = await linkedPlayer(c, session);
    if (player === null) return accountError(c, "PLAYER_NOT_LINKED");
    const guard = randomId("ds_");
    await c.env.DB.batch([sessionGuard(c, guard, session, player.id),
      c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE start_session_digest = ?1 AND purpose = 'delete'").bind(session.digest),
      c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE session_digest = ?1 AND purpose = 'delete'").bind(session.digest),
      c.env.DB.prepare("DELETE FROM account_deletion_confirmations WHERE session_digest = ?1 AND status != 'DELETED'").bind(session.digest),
      removeGuard(c, guard)]);
    return c.json({ url: await beginOAuth(c, { purpose: "delete", session, playerId: player.id,
      issuer: session.issuer, sub: session.sub }) });
  });
  app.get("/api/v1/account/deletion-confirmations/current", async c => {
    const row = await readDeletion(c);
    if (row === null || row.expires_at <= serverNow()) return accountError(c, "DELETION_CONFIRMATION_EXPIRED");
    return c.json({ id: row.id, status: row.status, expires_at: row.expires_at });
  });
  app.post("/api/v1/account/deletion-confirmations", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body?.confirmed !== true || Object.keys(body).some(key => key !== "confirmed")) return accountError(c, "CONFIRMATION_REQUIRED");
    const row = await readDeletion(c), player = await linkedPlayer(c, session);
    if (row === null || player === null || row.player_id !== player.id || row.session_digest !== session.digest ||
        row.issuer !== session.issuer || row.sub !== session.sub || row.browser_digest !== session.browser_digest || row.expires_at <= serverNow())
      return accountError(c, "DELETION_CONFIRMATION_INVALID", 403);
    if (row.status === "CONFIRMED") return c.json({ id: row.id, status: row.status, expires_at: row.expires_at, retry: true });
    const proof = await readOperationProof(c, "delete");
    if (row.status !== "UNCONFIRMED" || !proofMatches(proof, session, "delete", player.id, null) || proof!.digest !== row.operation_digest)
      return accountError(c, "OPERATION_PROOF_REQUIRED", 403);
    const guard = randomId("dcg_"), now = serverNow(), expiry = expiresIn(600_000);
    try { await c.env.DB.batch([sessionGuard(c, guard, session, player.id), deletionGuard(c, guard, row, "UNCONFIRMED"),
      operationGuard(c, guard, proof!),
      c.env.DB.prepare("UPDATE web_operation_proofs SET consumed_at = ?1 WHERE digest = ?2").bind(now, proof!.digest),
      c.env.DB.prepare(`UPDATE account_deletion_confirmations SET status = 'CONFIRMED', confirmed_at = ?1,
        expires_at = ?2 WHERE id = ?3`).bind(now, expiry, row.id), removeGuard(c, guard)]); } catch (error) {
      const current = await readDeletion(c);
      if (current?.status === "CONFIRMED" && current.session_digest === session.digest &&
          current.browser_digest === session.browser_digest && current.player_id === player.id && current.expires_at > serverNow())
        return c.json({ id: current.id, status: current.status, expires_at: current.expires_at, retry: true });
      throw error;
    }
    setAccountCookie(c, "deletion", rawCookie(c, "deletion")!, 600);
    return c.json({ id: row.id, status: "CONFIRMED", expires_at: expiry, retry: false });
  });
  app.delete("/api/v1/account", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    if (new URL(c.req.url).search !== "" || (await c.req.text()).trim() !== "") return accountError(c, "INVALID_REQUEST", 400);
    const row = await readDeletion(c), player = await linkedPlayer(c, session);
    if (row === null || player === null || row.status !== "CONFIRMED" || row.player_id !== player.id ||
        row.issuer !== session.issuer || row.sub !== session.sub || row.session_digest !== session.digest ||
        row.browser_digest !== session.browser_digest || row.expires_at <= serverNow() || row.consumed_at !== null)
      return accountError(c, "DELETION_CONFIRMATION_REQUIRED", 403);
    const guard = randomId("del_"), now = serverNow();
    await c.env.DB.batch([sessionGuard(c, guard, session, player.id), deletionGuard(c, guard, row, "CONFIRMED"),
      c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE (issuer = ?1 AND sub = ?2) OR start_session_digest IN (SELECT digest FROM web_sessions WHERE issuer = ?1 AND sub = ?2)").bind(session.issuer, session.sub),
      c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE issuer = ?1 AND sub = ?2").bind(session.issuer, session.sub),
      c.env.DB.prepare("DELETE FROM account_deletion_confirmations WHERE id != ?1 AND (player_id = ?2 OR (issuer = ?3 AND sub = ?4))").bind(row.id, player.id, session.issuer, session.sub),
      c.env.DB.prepare(`DELETE FROM app_authorizations WHERE (issuer = ?1 AND sub = ?2) OR
        session_digest IN (SELECT digest FROM web_sessions WHERE issuer = ?1 AND sub = ?2)`)
        .bind(session.issuer, session.sub),
      c.env.DB.prepare("DELETE FROM web_sessions WHERE issuer = ?1 AND sub = ?2").bind(session.issuer, session.sub),
      c.env.DB.prepare("DELETE FROM players WHERE id = ?1").bind(player.id),
      c.env.DB.prepare(`UPDATE account_deletion_confirmations SET status = 'DELETED', consumed_at = ?1,
        player_id = NULL, issuer = NULL, sub = NULL, session_digest = NULL, browser_digest = NULL,
        operation_digest = NULL WHERE id = ?2`).bind(now, row.id), removeGuard(c, guard)]);
    clearAccountCookie(c, "session");
    clearAccountCookie(c, "operation-delete");
    return c.json({ id: row.id, status: "DELETED" });
  });
  app.post("/api/v1/account/deletion-confirmations/:id/result", async c => {
    const row = await readDeletion(c);
    if (row === null || row.id !== c.req.param("id")) return accountError(c, "RESULT_PROOF_REQUIRED", 403);
    if (row.expires_at <= serverNow()) {
      await c.env.DB.prepare("DELETE FROM account_deletion_confirmations WHERE id = ?1").bind(row.id).run();
      clearAccountCookie(c, "deletion");
      return accountError(c, "DELETION_CONFIRMATION_EXPIRED");
    }
    return c.json({ id: row.id, status: row.status, expires_at: row.expires_at });
  });
}
