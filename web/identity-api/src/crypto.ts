const encoder = new TextEncoder();

function requireSecret(value: string, name: string): string {
  if (encoder.encode(value).length < 32) {
    throw new Error(`${name} must contain at least 32 UTF-8 bytes.`);
  }
  return value;
}

async function importHmacKey(secret: string, name: string): Promise<CryptoKey> {
  return crypto.subtle.importKey(
    "raw",
    encoder.encode(requireSecret(secret, name)),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign", "verify"],
  );
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function hexToBytes(value: string): Uint8Array | null {
  if (!/^[0-9a-f]{64}$/u.test(value)) {
    return null;
  }
  const bytes = new Uint8Array(value.length / 2);
  for (let index = 0; index < value.length; index += 2) {
    bytes[index / 2] = Number.parseInt(value.slice(index, index + 2), 16);
  }
  return bytes;
}

function bytesToBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

export function randomId(prefix: string, byteLength = 16): string {
  const bytes = crypto.getRandomValues(new Uint8Array(byteLength));
  return `${prefix}${bytesToBase64Url(bytes)}`;
}

export async function digestRegistrationRequest(
  registrationSecret: string,
  requestId: string,
): Promise<string> {
  const key = await importHmacKey(registrationSecret, "REGISTRATION_SECRET");
  const digest = await crypto.subtle.sign(
    "HMAC",
    key,
    encoder.encode(`registration-request\0${requestId}`),
  );
  return bytesToHex(new Uint8Array(digest));
}

export async function deriveCredentialSecret(
  registrationSecret: string,
  requestId: string,
  credentialId: string,
): Promise<string> {
  const key = await importHmacKey(registrationSecret, "REGISTRATION_SECRET");
  const digest = await crypto.subtle.sign(
    "HMAC",
    key,
    encoder.encode(`app-credential\0${requestId}\0${credentialId}`),
  );
  return bytesToBase64Url(new Uint8Array(digest));
}

export async function digestCredentialSecret(
  credentialPepper: string,
  secret: string,
): Promise<string> {
  const key = await importHmacKey(credentialPepper, "CREDENTIAL_PEPPER");
  const digest = await crypto.subtle.sign("HMAC", key, encoder.encode(secret));
  return bytesToHex(new Uint8Array(digest));
}

export async function verifyCredentialSecret(
  credentialPepper: string,
  secret: string,
  storedDigest: string,
): Promise<boolean> {
  const digest = hexToBytes(storedDigest);
  if (digest === null) {
    return false;
  }
  const key = await importHmacKey(credentialPepper, "CREDENTIAL_PEPPER");
  return crypto.subtle.verify("HMAC", key, digest, encoder.encode(secret));
}
