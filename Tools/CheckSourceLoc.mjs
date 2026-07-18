import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const softLimit = 600;
const hardLimit = 1000;
const enforce = process.argv.includes("--check");
const sourceRoots = [
  "Assets/Scripts",
  "TeacherPortal/src",
  "TeacherPortal/functions/src",
  "Server",
  "Tools"
];
const sourceExtensions = new Set([".cs", ".css", ".js", ".jsx", ".mjs", ".py", ".ts", ".tsx"]);
const ignoredDirectoryNames = new Set([
  ".git",
  ".test-dist",
  "bin",
  "build",
  "coverage",
  "dist",
  "Library",
  "lib",
  "node_modules",
  "obj",
  "Temp",
  "TestResults",
  "tests",
  "Tests",
  ".venv",
  "venv"
]);

// Add a hard-limit exception only with a named, temporary ownership boundary.
// Generated JSON content is excluded rather than allowlisted.
const hardLimitAllowlist = new Map();

function visit(directory, results) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!ignoredDirectoryNames.has(entry.name) && entry.name !== "generated" && !entry.name.startsWith("."))
        visit(path.join(directory, entry.name), results);
      continue;
    }

    const filePath = path.join(directory, entry.name);
    const extension = path.extname(entry.name).toLowerCase();
    const isTestFile = /(?:Test|Tests)\.(?:cs|ts|tsx|js|jsx|mjs|py)$/i.test(entry.name);
    if (!sourceExtensions.has(extension) || entry.name.endsWith(".d.ts") || entry.name.endsWith(".g.cs") || isTestFile)
      continue;
    results.push(filePath);
  }
}

function lineCount(filePath) {
  const text = fs.readFileSync(filePath, "utf8");
  return text.length === 0 ? 0 : text.split(/\r?\n/).length - (text.endsWith("\n") ? 1 : 0);
}

const files = [];
for (const relativeRoot of sourceRoots) {
  const directory = path.join(root, relativeRoot);
  if (fs.existsSync(directory)) visit(directory, files);
}

const rows = files
  .map((filePath) => ({
    path: path.relative(root, filePath).replaceAll("\\", "/"),
    lines: lineCount(filePath)
  }))
  .sort((left, right) => right.lines - left.lines || left.path.localeCompare(right.path));
const softOffenders = rows.filter((row) => row.lines > softLimit);
const hardOffenders = rows.filter((row) => row.lines > hardLimit);
const unallowlistedHardOffenders = hardOffenders.filter((row) => !hardLimitAllowlist.has(row.path));

console.log(`Source LOC: ${rows.length} hand-maintained files, ${rows.reduce((sum, row) => sum + row.lines, 0)} lines.`);
console.log(`Soft target: ${softLimit}; hard review line: ${hardLimit}.`);

if (softOffenders.length > 0) {
  console.warn(`\nOver soft target (${softOffenders.length}):`);
  for (const row of softOffenders) {
    const allowlistReason = hardLimitAllowlist.get(row.path);
    console.warn(`  ${String(row.lines).padStart(5)}  ${row.path}${allowlistReason ? ` [allowlisted: ${allowlistReason}]` : ""}`);
  }
}

if (unallowlistedHardOffenders.length > 0) {
  console.error(`\nUnallowlisted hard-limit violations (${unallowlistedHardOffenders.length}):`);
  for (const row of unallowlistedHardOffenders)
    console.error(`  ${String(row.lines).padStart(5)}  ${row.path}`);
  if (enforce) process.exitCode = 1;
}

if (hardOffenders.length === 0) {
  console.log("\nNo file exceeds the hard review line.");
} else if (unallowlistedHardOffenders.length === 0) {
  console.log("\nAll hard-limit files have explicit, temporary refactor ownership.");
}
