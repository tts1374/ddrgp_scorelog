import path from "node:path";
import { fileURLToPath } from "node:url";
import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

const directory = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  plugins: [
    cloudflareTest(async () => ({
      main: "./src/index.ts",
      miniflare: {
        compatibilityDate: "2026-09-20",
        d1Databases: { DB: "identity-test-db" },
        bindings: {
          CREDENTIAL_PEPPER: "test-credential-pepper-at-least-32-bytes",
          REGISTRATION_SECRET: "test-registration-secret-at-least-32-bytes",
          TEST_MIGRATIONS: await readD1Migrations(
            path.join(directory, "migrations"),
          ),
        },
      },
    })),
  ],
  test: {
    include: ["test/**/*.test.ts"],
  },
});
