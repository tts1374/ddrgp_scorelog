import { env, exports } from "cloudflare:workers";
import { expect, vi } from "vitest";
import { deriveCredentialSecret, digestCredentialSecret, randomId } from "../src/crypto";

export const origin = "https://identity.example.test";
export async function seedPlayer(displayName = "Player") {
  const id = randomId("pl_"), publicId = randomId("p_"), credentialId = randomId("ac_");
  const secret = await deriveCredentialSecret(env.REGISTRATION_SECRET, randomId("fixture_"), credentialId);
  const now = new Date().toISOString();
  await env.DB.batch([
    env.DB.prepare("INSERT INTO players (id, public_player_id, display_name, created_at, updated_at) VALUES (?1,?2,?3,?4,?4)")
      .bind(id, publicId, displayName, now),
    env.DB.prepare("INSERT INTO player_credentials (id, player_id, type, secret_digest, created_at) VALUES (?1,?2,'app',?3,?4)")
      .bind(credentialId, id, await digestCredentialSecret(env.CREDENTIAL_PEPPER, secret), now),
  ]);
  return { id, public_player_id: publicId, display_name: displayName, credential: `${credentialId}.${secret}`, created_at: now, updated_at: now };
}
function base64(value: Uint8Array) {
  return btoa(String.fromCharCode(...value)).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}
function jsonPart(value: unknown) { return base64(new TextEncoder().encode(JSON.stringify(value))); }

export class GoogleFixture {
  private keys!: CryptoKeyPair;
  private jwk!: JsonWebKey;
  private codes = new Map<string, string>();
  async initialize() {
    this.keys = await crypto.subtle.generateKey({ name: "RSASSA-PKCS1-v1_5", modulusLength: 2048,
      publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" }, true, ["sign", "verify"]);
    this.jwk = await crypto.subtle.exportKey("jwk", this.keys.publicKey);
    vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
      if (url === "https://www.googleapis.com/oauth2/v3/certs") return Response.json({ keys: [{ ...this.jwk, kid: "fixture-key", use: "sig", alg: "RS256" }] });
      if (url === "https://oauth2.googleapis.com/token") {
        const params = new URLSearchParams(String(init?.body));
        expect(params.get("client_id")).toBe(env.GOOGLE_CLIENT_ID);
        expect(params.get("redirect_uri")).toBe(`${origin}/api/v1/auth/web/google/callback`);
        expect(params.get("grant_type")).toBe("authorization_code");
        const token = this.codes.get(params.get("code")!);
        return token === undefined ? Response.json({ error: "invalid_grant" }, { status: 400 }) : Response.json({ id_token: token });
      }
      throw new Error("Unexpected network request in account fixture.");
    }));
  }
  async token(nonce: string, overrides: Record<string, unknown> = {}, header: Record<string, unknown> = {}) {
    const now = Math.floor(Date.now() / 1000);
    const claims = { iss: "https://accounts.google.com", aud: env.GOOGLE_CLIENT_ID, sub: "google-fixture-sub",
      email: "private@example.test", email_verified: true, iat: now - 10, exp: now + 3600, nonce, ...overrides };
    const unsigned = `${jsonPart({ alg: "RS256", kid: "fixture-key", ...header })}.${jsonPart(claims)}`;
    const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", this.keys.privateKey, new TextEncoder().encode(unsigned));
    return `${unsigned}.${base64(new Uint8Array(signature))}`;
  }
  async callback(browser: BrowserFixture, googleUrl: string, overrides: Record<string, unknown> = {}) {
    const url = new URL(googleUrl), code = randomId("code_");
    this.codes.set(code, await this.token(url.searchParams.get("nonce")!, overrides));
    return browser.request(`/api/v1/auth/web/google/callback?state=${url.searchParams.get("state")}&code=${code}`);
  }
}
export class BrowserFixture {
  cookies = new Map<string, string>();
  csrf = "";
  async request(path: string, body?: unknown, method = body === undefined ? "GET" : "POST", extra: Record<string, string> = {}) {
    const response = await exports.default.fetch(path.startsWith(origin) ? path : `${origin}${path}`, {
      method, redirect: "manual", headers: { Cookie: [...this.cookies].map(([key, value]) => `${key}=${value}`).join("; "),
        Origin: origin, "X-CSRF-Token": this.csrf, "Content-Type": "application/json", ...extra },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    for (const cookie of response.headers.getSetCookie()) {
      const [pair] = cookie.split(";"), split = pair.indexOf("=");
      if (cookie.includes("Max-Age=0")) this.cookies.delete(pair.slice(0, split));
      else this.cookies.set(pair.slice(0, split), pair.slice(split + 1));
    }
    return response;
  }
  async context() {
    const result = await this.request("/api/v1/auth/web/context");
    this.csrf = (await result.json<{ csrf_token: string }>()).csrf_token;
  }
  async session() {
    const result = await this.request("/api/v1/account/session");
    expect(result.status).toBe(200);
    const data = await result.json<{ csrf_token: string; email: string; google_linked: boolean }>();
    this.csrf = data.csrf_token;
    return data;
  }
  async login(google: GoogleFixture, overrides: Record<string, unknown> = {}) {
    const start = await this.request("/api/v1/auth/web/google/start");
    const redirect = await this.request(start.headers.get("Location")!);
    const callback = await google.callback(this, redirect.headers.get("Location")!, overrides);
    expect(callback.headers.get("Location")).toBe("/my/profile");
    await this.session();
  }
  async operation(google: GoogleFixture, id: string, intent?: "register" | "login", overrides: Record<string, unknown> = {}) {
    if (!this.csrf) await this.context();
    const start = await this.request(`/api/v1/auth/app-authorizations/${id}/web-start`, intent ? { intent } : {});
    expect(start.status).toBe(200);
    const { url } = await start.json<{ url: string }>();
    const redirect = await this.request(url);
    expect(redirect.status).toBe(302);
    const callback = await google.callback(this, redirect.headers.get("Location")!, overrides);
    if (!callback.headers.get("Location")?.includes("auth_error") && callback.headers.get("Location") !== "/my/auth-error") await this.session();
    return callback;
  }
}
export async function startApp(credential?: string, expectedPublicId?: string, purpose = "connect") {
  const id = randomId("aa_"), request_secret = randomId("", 32);
  const response = await exports.default.fetch(`${origin}/api/v1/auth/app-authorizations`, {
    method: "POST", headers: { "Content-Type": "application/json", ...(credential ? { Authorization: `Bearer ${credential}` } : {}) },
    body: JSON.stringify({ id, request_secret, purpose, ...(expectedPublicId ? { expected_public_player_id: expectedPublicId } : {}) }),
  });
  return { id, request_secret, response };
}
export async function appRequest(id: string, secret: string, action: string, credential?: string) {
  return exports.default.fetch(`${origin}/api/v1/auth/app-authorizations/${id}/${action}`, {
    method: "POST", headers: { "Content-Type": "application/json", ...(credential ? { Authorization: `Bearer ${credential}` } : {}) },
    body: JSON.stringify({ request_secret: secret }),
  });
}
