export interface Session { email: string; google_linked: boolean; csrf_token: string }
export interface Profile { public_player_id: string; display_name: string; public_url: string }
export interface Confirmation {
  status: string; purpose: "connect" | "unlink"; intent: "register" | "login" | "link" | "unlink" | null;
  player: Pick<Profile, "public_player_id" | "display_name"> | null;
  registered_google: boolean; comparison_code: string;
}
export interface Deletion { id: string; status: "UNCONFIRMED" | "CONFIRMED" | "DELETED" }
export class ManagementError extends Error {
  constructor(public code: string, public status: number) { super(code); }
}
export async function accountRequest<T>(path: string, method = "GET", csrf?: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/${path}`, {
    method, credentials: "same-origin", cache: "no-store",
    headers: { ...(csrf ? { "X-CSRF-Token": csrf } : {}), ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (!response.ok) {
    const data = await response.json().catch(() => null) as { error?: { code?: string } } | null;
    throw new ManagementError(data?.error?.code ?? "REQUEST_FAILED", response.status);
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}
