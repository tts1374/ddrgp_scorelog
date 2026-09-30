import { cloudflare } from "@cloudflare/vite-plugin";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig(({ command, mode }) => ({
  plugins: [
    react(),
    cloudflare({
      persistState: command === "serve"
        ? { path: mode === "e2e" ? ".wrangler/e2e" : ".wrangler/development" }
        : true,
    }),
  ],
  html: { cspNonce: "__PLAYER_CSP_NONCE__" },
  server: { host: "127.0.0.1", port: 5173, strictPort: true },
}));
