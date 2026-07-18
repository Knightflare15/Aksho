import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const sourcePath = path.join(root, "ContentSource", "Grammar", "grimoire.curriculum.json");
const unityOutPath = path.join(root, "Assets", "Resources", "Grammar", "generated-grimoire-pages.json");
const functionsOutPath = path.join(root, "TeacherPortal", "functions", "src", "generated", "grimoire-pages.json");

const validConceptIds = new Set([
  "Greetings",
  "Alphabet",
  "VowelsConsonants",
  "SentenceStartEnd",
  "BasicNouns",
  "BasicVerbs",
  "Articles",
  "Pronouns",
  "Plurals",
  "Adjectives",
  "BasicPrepositions"
]);

const raw = fs.readFileSync(sourcePath, "utf8");
const source = JSON.parse(raw);
const concepts = Array.isArray(source.concepts) ? source.concepts : [];
if (concepts.length === 0) {
  throw new Error("grimoire.curriculum.json must contain a non-empty concepts array.");
}

for (const concept of concepts) {
  if (!validConceptIds.has(concept.id)) {
    throw new Error(`Unknown GrammarConceptId '${concept.id}' in ${sourcePath}.`);
  }
  for (const field of ["title", "summary", "rule"]) {
    if (typeof concept[field] !== "string" || concept[field].trim().length === 0) {
      throw new Error(`Concept '${concept.id}' must define ${field}.`);
    }
  }
}

const generatedPages = concepts.map((concept) => ({
  id: concept.id,
  conceptId: concept.id,
  title: concept.title,
  summary: concept.summary,
  rule: concept.rule,
  examples: strings(concept.examples),
  commonGoofs: strings(concept.commonGoofs),
  nouns: strings(concept.nouns),
  adjectives: strings(concept.adjectives),
  functionWords: strings(concept.functionWords),
  pronunciationGuides: pronunciationGuides(concept.pronunciationGuides),
  conjugations: buildConjugations(strings(concept.conjugationVerbs))
}));

const payload = JSON.stringify({ pages: generatedPages }, null, 2) + "\n";
fs.mkdirSync(path.dirname(unityOutPath), { recursive: true });
fs.mkdirSync(path.dirname(functionsOutPath), { recursive: true });
fs.writeFileSync(unityOutPath, payload, "utf8");
fs.writeFileSync(functionsOutPath, payload, "utf8");

console.log(`Generated ${generatedPages.length} Grimoire pages.`);
console.log(unityOutPath);
console.log(functionsOutPath);

function strings(value) {
  return Array.isArray(value)
    ? value.map((item) => String(item ?? "").trim()).filter(Boolean)
    : [];
}

function pronunciationGuides(value) {
  if (!Array.isArray(value)) return [];
  return value
    .map((row) => ({
      word: String(row?.word ?? "").trim(),
      ipa: String(row?.ipa ?? "").trim(),
      soundGuide: String(row?.soundGuide ?? "").trim(),
      buddyHint: String(row?.buddyHint ?? "").trim(),
      commonIssue: String(row?.commonIssue ?? "").trim()
    }))
    .filter((row) => row.word);
}

function buildConjugations(verbs) {
  const rows = [];
  const pronouns = ["I", "you", "he/she/it", "we", "they"];
  for (const verb of verbs) {
    for (const pronoun of pronouns) {
      const thirdPerson = pronoun === "he/she/it";
      const presentVerb = thirdPerson ? thirdPersonForm(verb) : verb;
      rows.push({
        pronoun: `${pronoun} + ${verb}`,
        present: `${pronoun} ${presentVerb}`,
        past: `${pronoun} ${pastForm(verb)}`,
        progressive: `${pronoun} ${thirdPerson ? "is" : pronoun === "I" ? "am" : "are"} ${progressiveForm(verb)}`,
        future: `${pronoun} will ${verb}`
      });
    }
  }
  return rows;
}

function thirdPersonForm(verb) {
  if (verb === "fly") return "flies";
  if (verb === "go") return "goes";
  if (verb.endsWith("ch") || verb.endsWith("sh") || verb.endsWith("x") || verb.endsWith("s")) return `${verb}es`;
  return `${verb}s`;
}

function pastForm(verb) {
  switch (verb) {
    case "bite": return "bit";
    case "go": return "went";
    case "run": return "ran";
    case "fly": return "flew";
    case "swim": return "swam";
    case "sit": return "sat";
    case "scratch": return "scratched";
    case "peck": return "pecked";
    case "help": return "helped";
    default: return `${verb}ed`;
  }
}

function progressiveForm(verb) {
  switch (verb) {
    case "bite": return "biting";
    case "go": return "going";
    case "run": return "running";
    case "swim": return "swimming";
    case "sit": return "sitting";
    case "fly": return "flying";
    default: return `${verb}ing`;
  }
}
