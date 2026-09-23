import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const directory = path.dirname(fileURLToPath(import.meta.url));
const project = path.resolve(directory, "..");
const wrangler = path.join(project, "node_modules", "wrangler", "bin", "wrangler.js");

execFileSync(process.execPath, [wrangler, "d1", "migrations", "apply", "DB", "--local"], {
  cwd: project,
  stdio: "inherit",
});
execFileSync(process.execPath, [wrangler, "d1", "execute", "DB", "--local", "--file", "e2e/seed.sql"], {
  cwd: project,
  stdio: "inherit",
});
