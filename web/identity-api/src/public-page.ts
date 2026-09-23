import { Hono } from "hono";
import type { Context } from "hono";
import type { AppEnvironment } from "./index";
import { loadPublicPlayer } from "./public-player";

const headMarker = "<!--PLAYER_HEAD-->";
const bootstrapMarker = "<!--PLAYER_BOOTSTRAP-->";

function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

export function serializeBootstrap(value: unknown): string {
  return JSON.stringify(value)
    .replaceAll("<", "\\u003c")
    .replaceAll(">", "\\u003e")
    .replaceAll("&", "\\u0026")
    .replaceAll("\u2028", "\\u2028")
    .replaceAll("\u2029", "\\u2029");
}

function randomNonce(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(18));
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

function publicOrigin(c: Context<AppEnvironment>): URL {
  const configured = c.env.PUBLIC_WEB_ORIGIN;
  if (configured !== undefined) {
    try {
      const origin = new URL(configured);
      if (origin.protocol === "https:") return origin;
    } catch {
      // Invalid deployment configuration falls back to the current request origin.
    }
  }
  return new URL(c.req.url);
}

function securityHeaders(nonce: string): HeadersInit {
  return {
    "Cache-Control": "no-store",
    "Content-Security-Policy": [
      "default-src 'self'",
      `script-src 'self' 'nonce-${nonce}'`,
      "style-src 'self'",
      "img-src 'self' data:",
      "font-src 'self'",
      "connect-src 'self'",
      "object-src 'none'",
      "base-uri 'none'",
      "frame-ancestors 'none'",
      "form-action 'none'",
    ].join("; "),
    "Content-Type": "text/html; charset=UTF-8",
    "Permissions-Policy": "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
    "Referrer-Policy": "no-referrer",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
  };
}

function notFoundPage(nonce: string): Response {
  const html = '<!doctype html><html lang="ja"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,follow"><title>Playerが見つかりません - GP Score Log</title><link rel="stylesheet" href="/404.css"></head><body><main><p class="error-code">404</p><h1>Playerが見つかりません</h1><p>URLが正しいか確認してください。Playerが削除されている場合、この公開ページは表示できません。</p></main></body></html>';
  return new Response(html, { status: 404, headers: securityHeaders(nonce) });
}

export function registerPublicPageRoute(app: Hono<AppEnvironment>): void {
  app.get("/player/:publicPlayerId", async (c) => {
    const nonce = randomNonce();
    const player = await loadPublicPlayer(c.env.DB, c.req.param("publicPlayerId"));
    if (player === null) return notFoundPage(nonce);

    const assetResponse = await c.env.ASSETS.fetch(new URL("/", c.req.url));
    if (!assetResponse.ok) throw new Error("The SPA shell could not be loaded.");
    const shell = await assetResponse.text();
    if (!shell.includes(headMarker) || !shell.includes(bootstrapMarker)) {
      throw new Error("The SPA shell injection markers are missing.");
    }

    const origin = publicOrigin(c);
    const canonical = new URL(`/player/${player.public_player_id}`, origin).toString();
    const title = `${player.display_name} - GP Score Log`;
    const description = "DDR GRAND PRIX Player Data";
    const head = [
      `<title>${escapeHtml(title)}</title>`,
      `<meta name="description" content="${description}">`,
      '<meta name="robots" content="noindex,follow">',
      '<meta property="og:type" content="profile">',
      `<meta property="og:title" content="${escapeHtml(title)}">`,
      `<meta property="og:description" content="${description}">`,
      `<meta property="og:url" content="${escapeHtml(canonical)}">`,
      `<link rel="canonical" href="${escapeHtml(canonical)}">`,
    ].join("");
    const bootstrap = `<script id="player-bootstrap" type="application/json" nonce="${nonce}">${serializeBootstrap(player)}</script>`;
    const html = shell.replace(headMarker, head).replace(bootstrapMarker, bootstrap);
    return new Response(html, { status: 200, headers: securityHeaders(nonce) });
  });
}
