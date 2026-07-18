import { spawn } from "node:child_process";
import path from "node:path";
import process from "node:process";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const firebaseAuth = require("firebase-tools/lib/auth");
const defaultCredentials = require("firebase-tools/lib/defaultCredentials");
const functionsDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const projectId = process.argv.find((value) => value.startsWith("--projectId="))?.slice("--projectId=".length) ?? "the-script-dea4f";

const account = firebaseAuth.getProjectDefaultAccount(functionsDir) ?? firebaseAuth.getGlobalDefaultAccount();
if (!account?.tokens?.refresh_token)
  throw new Error("Firebase CLI is not signed in. Run 'npx firebase-tools login' first.");

const credentialPath = await defaultCredentials.getCredentialPathAsync(account);
if (!credentialPath)
  throw new Error("Firebase CLI could not create temporary Application Default Credentials.");

console.log("Seeding canonical game content with the signed-in Firebase CLI account.");
await runSeeder(credentialPath, projectId);

function runSeeder(temporaryCredentialPath, targetProjectId) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ["lib/seedDeterministicGameContent.js"], {
      cwd: functionsDir,
      env: {
        ...process.env,
        FIREBASE_PROJECT_ID: targetProjectId,
        GOOGLE_APPLICATION_CREDENTIALS: temporaryCredentialPath
      },
      stdio: "inherit"
    });
    child.on("error", reject);
    child.on("close", (code) => code === 0
      ? resolve()
      : reject(new Error(`Canonical content seed exited with code ${code}.`)));
  });
}
