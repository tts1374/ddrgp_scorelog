export interface GoogleClaims {
  issuer: "https://accounts.google.com";
  sub: string;
  email: string;
  auth_time: number | null;
}

function decode(value: string): Uint8Array<ArrayBuffer> {
  if (!/^[A-Za-z0-9_-]+$/u.test(value)) throw new Error("Invalid Google token.");
  return Uint8Array.from(atob(value.replaceAll("-", "+").replaceAll("_", "/")), c => c.charCodeAt(0));
}

export async function validateGoogleToken(token: string, clientId: string, nonce: string,
  nowSeconds = Math.floor(Date.now() / 1000)): Promise<GoogleClaims> {
  const parts = token.split(".");
  if (parts.length !== 3) throw new Error("Invalid Google token.");
  const header = JSON.parse(new TextDecoder().decode(decode(parts[0])));
  const claims = JSON.parse(new TextDecoder().decode(decode(parts[1])));
  if (header.alg !== "RS256" || typeof header.kid !== "string" ||
      !["accounts.google.com", "https://accounts.google.com"].includes(claims.iss) ||
      claims.aud !== clientId || (claims.azp !== undefined && claims.azp !== clientId) ||
      !Number.isSafeInteger(claims.exp) || claims.exp <= nowSeconds ||
      !Number.isSafeInteger(claims.iat) || claims.iat > nowSeconds || claims.iat < 0 || claims.iat >= claims.exp ||
      typeof claims.sub !== "string" || claims.sub.length === 0 || claims.sub.length > 255 ||
      claims.nonce !== nonce || typeof claims.email !== "string" || claims.email.length === 0 ||
      claims.email_verified !== true) throw new Error("Invalid Google claims.");
  const response = await fetch("https://www.googleapis.com/oauth2/v3/certs");
  if (!response.ok) throw new Error("Google keys are unavailable.");
  const jwks = await response.json<{ keys: Array<JsonWebKey & { kid?: string }> }>();
  const candidates = jwks.keys.filter(key => key.kid === header.kid && key.kty === "RSA" &&
    (key.alg === undefined || key.alg === "RS256") && (key.use === undefined || key.use === "sig"));
  if (candidates.length !== 1) throw new Error("Invalid Google signing key.");
  const key = await crypto.subtle.importKey("jwk", candidates[0],
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]);
  if (!await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, decode(parts[2]),
    new TextEncoder().encode(`${parts[0]}.${parts[1]}`))) throw new Error("Invalid Google signature.");
  return { issuer: "https://accounts.google.com", sub: claims.sub, email: claims.email,
    auth_time: Number.isSafeInteger(claims.auth_time) && claims.auth_time >= 0 && claims.auth_time <= nowSeconds
      ? claims.auth_time : null };
}

export async function exchangeGoogleCode(code: string, clientId: string, clientSecret: string,
  callback: string, nonce: string): Promise<GoogleClaims> {
  const response = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ code, client_id: clientId, client_secret: clientSecret,
      redirect_uri: callback, grant_type: "authorization_code" }),
  });
  if (!response.ok) throw new Error("Google authentication failed.");
  const token = await response.json<{ id_token?: unknown }>();
  if (typeof token.id_token !== "string") throw new Error("Google authentication failed.");
  return validateGoogleToken(token.id_token, clientId, nonce);
}
