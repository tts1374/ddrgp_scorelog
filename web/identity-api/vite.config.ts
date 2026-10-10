import { cloudflare } from "@cloudflare/vite-plugin";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig(({ command, mode }) => ({
  plugins: [
    react(),
    cloudflare({
      // Browser tests exercise real cookie/CSRF routes with test-only keys.
      config: mode === "e2e" ? config => ({
        vars: {
          ...config.vars,
          CREDENTIAL_PEPPER: "e2e-dummy-pepper-0123456789abcdef0123456789",
          APP_AUTHORIZATION_SECRET: "e2e-dummy-authorization-0123456789abcdef0123456789",
        },
      }) : undefined,
      persistState: command === "serve"
        ? { path: mode === "e2e" ? ".wrangler/e2e" : ".wrangler/development" }
        : true,
    }),
  ],
  html: { cspNonce: "__PLAYER_CSP_NONCE__" },
  server: { host: "127.0.0.1", port: 5173, strictPort: true },
}));
