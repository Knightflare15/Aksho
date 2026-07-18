import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const functionsRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const sourceDirectory = path.join(functionsRoot, "src", "generated");
const outputDirectory = path.join(functionsRoot, "lib", "generated");
const contentFiles = ["dialogue-task-seeds.json", "grimoire-pages.json"];

fs.mkdirSync(outputDirectory, { recursive: true });
for (const fileName of contentFiles) {
  const source = path.join(sourceDirectory, fileName);
  if (!fs.existsSync(source))
    throw new Error(`Missing generated content: ${source}. Run the grammar generators before building Functions.`);
  fs.copyFileSync(source, path.join(outputDirectory, fileName));
}

for (const staleModule of ["generatedDialogueTasks.js", "generatedGrimoirePages.js"]) {
  const stalePath = path.join(functionsRoot, "lib", staleModule);
  if (fs.existsSync(stalePath)) fs.unlinkSync(stalePath);
}
