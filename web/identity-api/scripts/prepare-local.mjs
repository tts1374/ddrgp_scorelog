import { execFileSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repository = path.resolve(project, "../..");
const master = path.join(repository, "databases/ddrgp-master.sqlite");
const sql = path.join(repository, "data/master/ddrgp-web-master.local.sql");
const wrangler = path.join(project, "node_modules/wrangler/bin/wrangler.js");
const varsPath = path.join(project, ".dev.vars");

if (!existsSync(master)) {
  throw new Error(`Local master is missing: ${master}`);
}

// Keep local credentials valid across restarts by preserving existing secrets.
let vars = existsSync(varsPath) ? readFileSync(varsPath, "utf8") : "";
const newline = vars.includes("\r\n") ? "\r\n" : "\n";
if (vars.length > 0 && !vars.endsWith("\n")) vars += newline;
for (const key of ["CREDENTIAL_PEPPER", "REGISTRATION_SECRET", "APP_AUTHORIZATION_SECRET"]) {
  if (!new RegExp(`^${key}\\s*=`, "m").test(vars)) {
    vars += `${key}=${randomBytes(32).toString("hex")}${newline}`;
  }
}
const publicOrigin = "PUBLIC_WEB_ORIGIN=http://127.0.0.1:5173";
vars = /^PUBLIC_WEB_ORIGIN\s*=/m.test(vars)
  ? vars.replace(/^PUBLIC_WEB_ORIGIN\s*=[^\r\n]*/m, publicOrigin)
  : vars + publicOrigin + newline;
const localHttp = "GOOGLE_ALLOW_LOCAL_HTTP=true";
vars = /^GOOGLE_ALLOW_LOCAL_HTTP\s*=/m.test(vars)
  ? vars.replace(/^GOOGLE_ALLOW_LOCAL_HTTP\s*=[^\r\n]*/m, localHttp)
  : vars + localHttp + newline;
writeFileSync(varsPath, vars, { encoding: "utf8" });

execFileSync("uv", [
  "run", "python", "-m", "master.d1_export", "--master-db", master, "--output", sql,
], { cwd: repository, stdio: "inherit" });

const local = ["--config", "wrangler.jsonc", "--local", "--persist-to", ".wrangler/development"];
execFileSync(process.execPath, [
  wrangler, "d1", "migrations", "apply", "DB", ...local,
], { cwd: project, stdio: "inherit" });
execFileSync(process.execPath, [
  wrangler, "d1", "execute", "DB", ...local, "--file", sql,
], { cwd: project, stdio: ["ignore", "ignore", "inherit"] });
console.log("Local Web is ready at http://127.0.0.1:5173 (local D1 and master prepared).");
