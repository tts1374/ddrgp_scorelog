import type { Hono } from "hono";
import type { AppEnvironment } from "./index";
import { randomId } from "./crypto";
import { accountError, beginOAuth, clearAccountCookie, linkedPlayer, operationGuard, readOperationProof, readWebSession,
  removeGuard, serverNow, sessionGuard, validOriginCsrf, type AccountContext, type OperationProof,
  type WebSession } from "./web-account";

const purpose = "public-bests-delete";
function boundProof(proof: OperationProof | null, session: WebSession, playerId: string) {
  return proof !== null && proof.purpose === purpose && proof.player_id === playerId &&
    proof.issuer === session.issuer && proof.sub === session.sub && proof.session_digest === session.digest &&
    proof.browser_digest === session.browser_digest && proof.app_authorization_id === null && proof.expires_at > serverNow();
}
async function deletionContext(c: AccountContext) {
  const session = await readWebSession(c);
  const player = session === null ? null : await linkedPlayer(c, session);
  const proof = await readOperationProof(c, purpose);
  return { session, player, proof };
}

export function registerPublicBestDeletionRoutes(app: Hono<AppEnvironment>) {
  app.post("/api/v1/account/bests/deletion-cancel", async c => {
    const session = await readWebSession(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body === null || typeof body !== "object" || Array.isArray(body) || Object.keys(body).length !== 0)
      return accountError(c, "INVALID_REQUEST", 400);
    const guard = randomId("pb_");
    await c.env.DB.batch([sessionGuard(c, guard, session, null),
      c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE start_session_digest = ?1 AND purpose = ?2").bind(session.digest, purpose),
      c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE session_digest = ?1 AND purpose = ?2").bind(session.digest, purpose),
      removeGuard(c, guard)]);
    clearAccountCookie(c, `operation-${purpose}`);
    return c.body(null, 204);
  });
  app.post("/api/v1/account/bests/deletion-start", async c => {
    const { session, player } = await deletionContext(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body === null || typeof body !== "object" || Array.isArray(body) || Object.keys(body).length !== 0)
      return accountError(c, "INVALID_REQUEST", 400);
    if (player === null) return accountError(c, "PLAYER_NOT_LINKED");
    const guard = randomId("pb_");
    await c.env.DB.batch([sessionGuard(c, guard, session, player.id),
      c.env.DB.prepare("DELETE FROM web_oauth_requests WHERE start_session_digest = ?1 AND purpose = ?2").bind(session.digest, purpose),
      c.env.DB.prepare("DELETE FROM web_operation_proofs WHERE session_digest = ?1 AND purpose = ?2").bind(session.digest, purpose),
      removeGuard(c, guard)]);
    return c.json({ url: await beginOAuth(c, { purpose, session, playerId: player.id, issuer: session.issuer, sub: session.sub }) });
  });
  app.get("/api/v1/account/bests/deletion-confirmation", async c => {
    const { session, player, proof } = await deletionContext(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (player === null || !boundProof(proof, session, player.id)) return accountError(c, "OPERATION_PROOF_REQUIRED", 403);
    return c.json({ status: proof!.consumed_at === null ? "UNCONFIRMED" : "DELETED" });
  });
  app.delete("/api/v1/account/bests", async c => {
    const { session, player, proof } = await deletionContext(c);
    if (session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
    if (!await validOriginCsrf(c, session)) return accountError(c, "CSRF_REJECTED", 403);
    const body = await c.req.json().catch(() => null);
    if (body?.confirmed !== true || body?.sync_stopped !== true ||
        Object.keys(body).some(key => key !== "confirmed" && key !== "sync_stopped"))
      return accountError(c, "CONFIRMATION_REQUIRED", 400);
    if (player === null || !boundProof(proof, session, player.id)) return accountError(c, "OPERATION_PROOF_REQUIRED", 403);
    if (proof!.consumed_at !== null) return c.json({ status: "DELETED", retry: true });
    const guard = randomId("pb_"), now = serverNow();
    try {
      await c.env.DB.batch([sessionGuard(c, guard, session, player.id), operationGuard(c, guard, proof!),
        c.env.DB.prepare("DELETE FROM player_chart_bests WHERE player_id = ?1").bind(player.id),
        c.env.DB.prepare(`UPDATE players SET best_sync_revision = best_sync_revision + 1,
          public_bests_updated_at = ?1 WHERE id = ?2 AND changes() > 0`).bind(now, player.id),
        c.env.DB.prepare("UPDATE best_sync_snapshots SET status = 'ABORTED' WHERE player_id = ?1 AND status = 'PENDING'").bind(player.id),
        c.env.DB.prepare(`DELETE FROM best_sync_snapshot_items WHERE snapshot_id IN
          (SELECT snapshot_id FROM best_sync_snapshots WHERE player_id = ?1 AND status = 'ABORTED')`).bind(player.id),
        c.env.DB.prepare(`DELETE FROM best_sync_snapshot_chunks WHERE snapshot_id IN
          (SELECT snapshot_id FROM best_sync_snapshots WHERE player_id = ?1 AND status = 'ABORTED')`).bind(player.id),
        c.env.DB.prepare(`DELETE FROM best_replacement_authorizations WHERE snapshot_id IN
          (SELECT snapshot_id FROM best_sync_snapshots WHERE player_id = ?1 AND status = 'ABORTED')`).bind(player.id),
        c.env.DB.prepare("UPDATE web_operation_proofs SET consumed_at = ?1 WHERE digest = ?2").bind(now, proof!.digest),
        removeGuard(c, guard)]);
    } catch {
      const current = await deletionContext(c);
      if (current.session === null) return accountError(c, "WEB_SESSION_REQUIRED", 401);
      if (current.player !== null && boundProof(current.proof, current.session, current.player.id) &&
          current.proof!.digest === proof!.digest && current.proof!.consumed_at !== null)
        return c.json({ status: "DELETED", retry: true });
      return accountError(c, "OPERATION_PROOF_REQUIRED", 403);
    }
    return c.json({ status: "DELETED", retry: false });
  });
}
