import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    include: ["test/frontend/**/*.test.tsx"],
    setupFiles: ["./test/frontend/setup.ts"],
  },
});
