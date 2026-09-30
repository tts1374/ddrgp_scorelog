import type {
  PageState,
  PublicBestsResponse,
  PublicFlareResponse,
  PublicPlayer,
} from "./types";

interface ApiErrorEnvelope {
  error?: { code?: string; message?: string };
}

export class PublicApiError extends Error {
  constructor(readonly code: string, message: string) {
    super(message);
  }
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { headers: { Accept: "application/json" }, signal });
  if (!response.ok) {
    let envelope: ApiErrorEnvelope = {};
    try { envelope = await response.json() as ApiErrorEnvelope; } catch { /* use fallback */ }
    throw new PublicApiError(
      envelope.error?.code ?? "REQUEST_FAILED",
      envelope.error?.message ?? "公開データを読み込めませんでした。",
    );
  }
  return response.json() as Promise<T>;
}

export function fetchPlayer(publicPlayerId: string, signal?: AbortSignal): Promise<PublicPlayer> {
  return getJson(`/api/v1/public/players/${encodeURIComponent(publicPlayerId)}`, signal);
}

export function fetchBests(
  publicPlayerId: string,
  state: PageState,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<PublicBestsResponse> {
  const query = new URLSearchParams({ style: state.style, mode: state.mode, sort: state.sort });
  if (state.mode === "level") query.set("level", String(state.level));
  if (state.mode === "version") query.set("version", state.version);
  if (state.mode === "title" && state.q.trim().length > 0) query.set("q", state.q.trim());
  if (cursor !== null) query.set("cursor", cursor);
  return getJson(`/api/v1/public/players/${encodeURIComponent(publicPlayerId)}/bests?${query}`, signal);
}

export function fetchFlareSkill(
  publicPlayerId: string,
  style: PageState["style"],
  signal?: AbortSignal,
): Promise<PublicFlareResponse> {
  return getJson(`/api/v1/public/players/${encodeURIComponent(publicPlayerId)}/flare-skill?style=${style}`, signal);
}
