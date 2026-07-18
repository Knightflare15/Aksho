import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), "utf8");
const errors = [];
const warnings = [];
const expect = (condition, message) => { if (!condition) errors.push(message); };
const warn = (condition, message) => { if (!condition) warnings.push(message); };
const normalizeTopic = (value) => String(value ?? "").toUpperCase().replace(/[^A-Z0-9]/g, "");

const progression = [
  ["Greetings", "Greetings and Survival English", 1],
  ["Alphabet", "Alphabet", 2],
  ["VowelsConsonants", "Vowels and Consonants", 3],
  ["SentenceStartEnd", "Sentence Start and Full Stop", 4],
  ["BasicNouns", "Nouns", 5],
  ["BasicVerbs", "Verbs", 6],
  ["Articles", "Articles", 7],
  ["Pronouns", "Pronouns", 8],
  ["Plurals", "Plurals", 9],
  ["Adjectives", "Adjectives", 10],
  ["BasicPrepositions", "Basic Prepositions", 11]
];
const conceptIds = new Set(progression.map(([id]) => id));

const source = JSON.parse(read("ContentSource", "Grammar", "grimoire.curriculum.json"));
expect(typeof source.version === "string" && source.version.length > 0, "Grimoire source needs a version.");
expect(Array.isArray(source.concepts), "Grimoire source needs a concepts array.");
const concepts = Array.isArray(source.concepts) ? source.concepts : [];
expect(concepts.length === progression.length, `Expected ${progression.length} concepts, found ${concepts.length}.`);
expect(new Set(concepts.map((item) => item.id)).size === concepts.length, "Concept IDs must be unique.");
for (const [expectedId] of progression) expect(concepts.some((item) => item.id === expectedId), `Missing concept ${expectedId}.`);

for (const concept of concepts) {
  expect(conceptIds.has(concept.id), `Unknown concept ID ${concept.id}.`);
  for (const field of ["title", "summary", "rule"]) {
    const minimumLength = field === "title" ? 3 : 8;
    expect(typeof concept[field] === "string" && concept[field].trim().length >= minimumLength, `${concept.id}.${field} is too short or missing.`);
  }
  expect(Array.isArray(concept.examples) && concept.examples.length >= 6, `${concept.id} needs at least six examples.`);
  expect(Array.isArray(concept.commonGoofs) && concept.commonGoofs.length >= 4, `${concept.id} needs at least four common-goof corrections.`);
  const guides = Array.isArray(concept.pronunciationGuides) ? concept.pronunciationGuides : [];
  if (guides.length > 0) {
    expect(new Set(guides.map((guide) => String(guide.word).toLowerCase())).size === guides.length, `${concept.id} has duplicate pronunciation guide words.`);
    for (const guide of guides) {
      for (const field of ["word", "ipa", "soundGuide", "buddyHint", "commonIssue"])
        expect(typeof guide[field] === "string" && guide[field].trim().length > 0, `${concept.id} pronunciation '${guide.word ?? "?"}' lacks ${field}.`);
    }
  } else {
    warn(concept.id === "SentenceStartEnd", `${concept.id} has no pronunciation guidance.`);
  }
}

function readGeneratedArray(file, property) {
  try {
    const parsed = JSON.parse(read(...file));
    const values = parsed?.[property];
    expect(Array.isArray(values), `${file.join("/")} lacks a ${property} array.`);
    return Array.isArray(values) ? values : [];
  } catch (error) {
    errors.push(`${file.join("/")} is not parseable JSON: ${error.message}`);
    return [];
  }
}

function validateGeneratedPages(label, pages) {
  expect(pages.length === concepts.length, `${label} Grimoire page count differs from source.`);
  for (const concept of concepts) {
    const page = pages.find((candidate) => candidate.conceptId === concept.id);
    expect(Boolean(page), `${label} Grimoire page missing ${concept.id}.`);
    if (!page) continue;
    for (const field of ["title", "summary", "rule"])
      expect(page[field] === concept[field], `${label} ${concept.id}.${field} drifted from source.`);
    expect(JSON.stringify(page.examples) === JSON.stringify(concept.examples), `${label} ${concept.id}.examples drifted from source.`);
  }
}

const generatedPages = readGeneratedArray(
  ["TeacherPortal", "functions", "src", "generated", "grimoire-pages.json"],
  "pages");
const unityGeneratedPages = readGeneratedArray(
  ["Assets", "Resources", "Grammar", "generated-grimoire-pages.json"],
  "pages");
validateGeneratedPages("Backend content", generatedPages);
validateGeneratedPages("Unity", unityGeneratedPages);
expect(JSON.stringify(generatedPages) === JSON.stringify(unityGeneratedPages), "Unity and Firebase Grimoire JSON artifacts differ.");

const unityLoader = read("Assets", "Scripts", "World", "GeneratedGrammarGrimoireData.cs");
expect(unityLoader.includes("ResourcePath = \"Grammar/generated-grimoire-pages\""), "Unity Grimoire loader does not use the generated JSON resource.");

const tasks = readGeneratedArray(
  ["TeacherPortal", "functions", "src", "generated", "dialogue-task-seeds.json"],
  "tasks");
const unityTasks = readGeneratedArray(
  ["Assets", "Resources", "Grammar", "generated-dialogue-tasks.json"],
  "tasks");
expect(JSON.stringify(tasks) === JSON.stringify(unityTasks), "Unity and Firebase dialogue-task JSON artifacts differ.");
expect(tasks.length >= progression.length * 3, "Generated task bank is unexpectedly small.");
expect(new Set(tasks.map((task) => task.id)).size === tasks.length, "Generated dialogue task IDs must be unique.");
for (const task of tasks) {
  expect(conceptIds.has(task.conceptId), `Task ${task.id} uses unknown concept ${task.conceptId}.`);
  expect(typeof task.npcLine === "string" && task.npcLine.trim().length > 8, `Task ${task.id} lacks a useful NPC prompt.`);
  expect(typeof task.expectedResponse === "string" && task.expectedResponse.trim().length > 0, `Task ${task.id} lacks an expected response.`);
  const expectedAssist = task.zoneKind === "Town" ? "Full" : task.zoneKind === "Route" ? "Partial" : task.zoneKind === "Gym" ? "Off" : "";
  expect(Boolean(expectedAssist), `Task ${task.id} has invalid zone ${task.zoneKind}.`);
  expect(task.assistMode === expectedAssist, `Task ${task.id} ${task.zoneKind} assist must be ${expectedAssist}, not ${task.assistMode}.`);
}

const natural = read("Assets", "Scripts", "World", "NaturalGrammarProgression.cs");
const naturalRows = [...natural.matchAll(/displayName\s*=\s*"([^"]+)"[\s\S]*?grammarTopic\s*=\s*"([^"]+)"[\s\S]*?tier\s*=\s*(\d+)/g)]
  .slice(0, progression.length)
  .map((match) => ({ displayName: match[1], topic: match[2], tier: Number(match[3]) }));
expect(naturalRows.length === progression.length, "Could not read all Unity natural-progression regions.");
for (let index = 0; index < progression.length; index++) {
  const [, topic, tier] = progression[index];
  const row = naturalRows[index];
  if (!row) continue;
  expect(row.tier === tier, `Unity progression tier drift at ${topic}: expected ${tier}, got ${row.tier}.`);
  expect(normalizeTopic(row.topic) === normalizeTopic(topic), `Unity progression topic drift at tier ${tier}: ${row.topic} vs ${topic}.`);
}

const portalMissionPlanning = read("TeacherPortal", "src", "utils", "missionPlanning.ts");
for (const [, topic, tier] of progression) {
  const areaId = `TOWN:${normalizeTopic(topic)}:${tier}`;
  expect(portalMissionPlanning.includes(`targetAreaId: "${areaId}"`), `Teacher Portal is missing canonical area ${areaId}.`);
}

const seed = read("TeacherPortal", "functions", "src", "seedDeterministicGameContent.ts");
for (const [conceptId, topic, tier] of progression) {
  const progressionPattern = new RegExp(`conceptId:\\s*"${conceptId}"[\\s\\S]{0,100}?topicKey:\\s*"${normalizeTopic(topic)}"[\\s\\S]{0,100}?tier:\\s*${tier}\\b`);
  expect(progressionPattern.test(seed), `Backend seed progression drifted for ${conceptId}.`);
}
expect(seed.includes("const concepts: SeedRecord[] = conceptProgression.map"), "Backend concepts must derive from generated Grimoire pages.");
expect(seed.includes("const areas: SeedRecord[] = conceptProgression.flatMap"), "Backend areas must derive from canonical progression.");
const authoredSection = seed.slice(seed.indexOf("const authoredDialogueTasks"), seed.indexOf("const dialogueTasks"));
const authoredTasks = [...authoredSection.matchAll(/task\("([^"]+)",\s*"([^"]+)",\s*"([^"]*)",\s*"([^"]+)",\s*"([^"]+)",\s*"(Full|Partial|Off)"/g)]
  .map((match) => ({ id: match[1], conceptId: match[2], npcLine: match[3], expectedResponse: match[4], grammarPattern: match[5], assistMode: match[6] }));
expect(authoredTasks.length > 0, "Could not parse authored dialogue tasks.");
expect(new Set(authoredTasks.map((task) => task.id)).size === authoredTasks.length, "Authored dialogue task IDs must be unique.");
for (const task of authoredTasks) {
  expect(conceptIds.has(task.conceptId), `Authored task ${task.id} uses unknown concept ${task.conceptId}.`);
  expect(task.npcLine.trim().length >= 8, `Authored task ${task.id} lacks a useful NPC prompt.`);
  expect(task.expectedResponse.trim().length > 0, `Authored task ${task.id} lacks an expected response.`);
  if (task.npcLine.startsWith("Gym check:")) expect(task.assistMode === "Off", `Authored Gym task ${task.id} must disable Buddy assistance.`);
}
const allTaskIds = [...authoredTasks.map((task) => task.id), ...tasks.map((task) => task.id)];
expect(new Set(allTaskIds).size === allTaskIds.length, "Authored and generated dialogue task IDs collide.");
const seedVerbIds = new Set([...seed.matchAll(/^\s*verb\("([^"]+)"/gm)].map((match) => match[1].toLowerCase()));
const seedNounIds = new Set([...seed.matchAll(/^\s*noun\("([^"]+)"/gm)].map((match) => match[1].toLowerCase()));
const seedAdjectiveIds = new Set([...seed.matchAll(/^\s*adjective\("([^"]+)"/gm)].map((match) => match[1].toLowerCase()));
for (const concept of concepts) {
  for (const verb of concept.conjugationVerbs ?? []) expect(seedVerbIds.has(String(verb).toLowerCase()), `Grimoire verb '${verb}' is absent from backend/game seed lexicon.`);
  for (const noun of concept.nouns ?? []) {
    const singular = String(noun).split("/")[0].toLowerCase();
    const acceptedAlias = singular === "puppy" && seedNounIds.has("pup");
    expect(seedNounIds.has(singular) || acceptedAlias, `Grimoire noun '${singular}' is absent from backend/game seed lexicon.`);
  }
  for (const adjective of concept.adjectives ?? []) expect(seedAdjectiveIds.has(String(adjective).toLowerCase()), `Grimoire adjective '${adjective}' is absent from backend/game seed lexicon.`);
}

if (warnings.length > 0) {
  console.warn(`Content QA warnings (${warnings.length}):`);
  for (const warning of warnings) console.warn(`  - ${warning}`);
}
if (errors.length > 0) {
  console.error(`Content QA failed (${errors.length}):`);
  for (const error of errors) console.error(`  - ${error}`);
  process.exitCode = 1;
} else {
  console.log(`Content QA passed: ${concepts.length} concepts, ${authoredTasks.length} authored + ${tasks.length} generated tasks, ${generatedPages.length} synchronized backend pages.`);
}
