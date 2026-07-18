import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const outJson = path.join(root, "Assets", "Resources", "Grammar", "generated-dialogue-tasks.json");
const functionsJson = path.join(root, "TeacherPortal", "functions", "src", "generated", "dialogue-task-seeds.json");

const nouns = [
  ["rat", "rats", "animal", "a"],
  ["cat", "cats", "animal", "a"],
  ["dog", "dogs", "animal", "a"],
  ["pup", "pups", "animal", "a"],
  ["bird", "birds", "animal", "a"],
  ["duck", "ducks", "animal", "a"],
  ["fish", "fish", "animal", "a"],
  ["owl", "owls", "animal", "an"],
  ["ant", "ants", "animal", "an"],
  ["egg", "eggs", "thing", "an"],
  ["apple", "apples", "thing", "an"],
  ["orange", "oranges", "thing", "an"],
  ["box", "boxes", "thing", "a"],
  ["shop", "shops", "place", "a"],
  ["park", "parks", "place", "a"],
  ["school", "schools", "place", "a"],
  ["rock", "rocks", "thing", "a"],
  ["wall", "walls", "thing", "a"],
  ["roof", "roofs", "thing", "a"],
  ["bus", "buses", "thing", "a"],
  ["baby", "babies", "person", "a"],
  ["puppy", "puppies", "animal", "a"]
];

const verbs = [
  ["bite", "bites", ["rat", "cat", "dog", "pup"]],
  ["run", "runs", ["rat", "cat", "dog", "pup"]],
  ["jump", "jumps", ["rat", "cat", "dog", "pup"]],
  ["scratch", "scratches", ["rat", "cat", "dog"]],
  ["fly", "flies", ["bird", "duck", "owl"]],
  ["peck", "pecks", ["bird", "duck", "owl"]],
  ["swim", "swims", ["fish", "duck"]],
  ["sit", "sits", ["rat", "cat", "dog", "pup"]],
  ["help", "helps", ["rat", "cat", "dog", "bird"]],
  ["look", "looks", ["rat", "cat", "dog", "bird"]]
];

const adjectives = ["big", "small", "fast", "slow", "brave", "quiet", "bright", "heavy", "light", "happy"];
const prepositions = ["in", "on", "under", "behind", "near", "beside", "over"];
const validTargetsByPreposition = {
  in: ["box", "shop", "school", "water"],
  on: ["box", "rock", "roof"],
  under: ["box", "rock", "roof"],
  behind: ["box", "rock", "wall", "shop", "school"],
  near: ["box", "rock", "wall", "roof", "shop", "school", "water"],
  beside: ["box", "rock", "wall", "shop", "school", "water"],
  over: ["box", "rock", "wall", "roof", "water"]
};
const pronouns = ["I", "you", "he", "she", "it", "we", "they"];
const greetings = [
  ["hello", "Hello"],
  ["goodbye", "Goodbye"],
  ["thank-you", "Thank you"],
  ["yes-please", "Yes please"],
  ["no-thank-you", "No thank you"],
  ["my-name-is-aryan", "My name is Aryan"],
  ["good-morning", "Good morning"],
  ["good-night", "Good night"]
];

const tasks = [];
const seen = new Set();

function title(text) {
  return text.length ? text[0].toUpperCase() + text.slice(1) : text;
}

function slug(text) {
  return text.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

function add(partial) {
  if (seen.has(partial.id)) return;
  seen.add(partial.id);
  tasks.push({
    acceptedResponses: [partial.expectedResponse, ...(partial.alternatives ?? [])],
    alternatives: partial.alternatives ?? [],
    localLanguageHint: partial.localLanguageHint ?? "",
    teachingNote: partial.teachingNote ?? "",
    ...partial
  });
}

function modeFor(index) {
  const bucket = index % 5;
  if (bucket === 0) return ["Town", "Full", "SpeakOnly", "None"];
  if (bucket === 1) return ["Route", "Partial", "WriteOnly", "MissingWord"];
  if (bucket === 2) return ["Route", "Partial", "WriteOnly", "HeardWrong"];
  if (bucket === 3) return ["Route", "Partial", "SpeakOrWrite", "PartialTranscript"];
  return ["Gym", "Off", "SpeakOrWrite", "None"];
}

function promptFor(conceptId, zoneKind, answer, a, b = "") {
  const gym = zoneKind === "Gym" ? "Gym check: " : "";
  switch (conceptId) {
    case "Greetings": {
      const situations = {
        "Hello": "A friendly villager greets you. Reply with an English greeting.",
        "Goodbye": "A friendly villager is leaving. Say an English farewell.",
        "Thank you": "A friendly villager gives you a gift. Show gratitude in English.",
        "Yes please": "A friendly villager offers help and you want it. Accept politely in English.",
        "No thank you": "A friendly villager offers help and you do not want it. Refuse politely in English.",
        "My name is Aryan": "Introduce yourself as Aryan using a complete English sentence.",
        "Good morning": "You meet a friendly villager early in the day. Greet them in English.",
        "Good night": "A friendly villager is going to sleep. Say the appropriate English farewell."
      };
      return `${gym}${situations[answer] ?? "Respond politely to the friendly villager in English."}`;
    }
    case "Alphabet":
      return `${gym}${a}`;
    case "VowelsConsonants":
      return `${gym}${a}`;
    case "SentenceStartEnd": {
      const words = answer.replace(/[.!?]+$/, "").toLowerCase().split(/\s+/).reverse().join(" / ");
      return `${gym}Put these words into natural English sentence order: ${words}`;
    }
    case "BasicNouns":
      return `${gym}Find the ${b || "naming"} noun in this short sentence: "${title(answer)} waits." Say or write only the noun.`;
    case "BasicVerbs":
      return `${gym}Make a present-tense sentence using subject ${String(a).toUpperCase()} and action ${String(b).toUpperCase()}.`;
    case "Articles": {
      const [article, ...nounParts] = answer.toLowerCase().split(/\s+/);
      const noun = nounParts.join(" ").toUpperCase();
      return article === "the"
        ? `${gym}The listener already knows the specific ${noun}. Use the definite article with the noun.`
        : `${gym}Introduce one ${noun} for the first time. Use the correct indefinite article with the noun.`;
    }
    case "Pronouns":
      return `${gym}Make a present-tense sentence using pronoun ${String(a).toUpperCase()} and action ${String(b).toUpperCase()}.`;
    case "Plurals":
      return `${gym}Write or say the plural form of ${String(a).toUpperCase()}.`;
    case "Adjectives":
      return `${gym}Arrange these words in natural English order: ${String(b).toUpperCase()} / ${String(a).toUpperCase()}.`;
    case "BasicPrepositions": {
      const tokens = answer.toUpperCase().split(/\s+/);
      const words = [tokens[2], tokens[4], tokens[0], tokens[1], tokens[3]].filter(Boolean).join(" / ");
      return `${gym}Build a movement sentence by putting these command words in natural English order: ${words}`;
    }
    default:
      return `${gym}Respond in English using the grammar pattern you practised.`;
  }
}

function task(id, conceptId, zoneKind, npcLine, expectedResponse, grammarPattern, assistMode, inputMode, malfunctionType, alternatives = []) {
  add({
    id,
    taskId: id,
    lineId: id,
    conceptId,
    zoneKind,
    npcLine,
    englishText: npcLine,
    expectedResponse,
    grammarPattern,
    assistMode,
    inputMode,
    malfunctionType,
    alternatives,
    contentId: id
  });
}

let i = 0;
for (const [key, answer] of greetings) {
  for (let round = 1; round <= 40; round++) {
    const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
    task(`gen-greetings-${key}-${round}`, "Greetings", zoneKind, promptFor("Greetings", zoneKind, answer), answer, "FullSentence", assistMode, inputMode, malfunctionType, answer === "Yes please" ? ["Yes, please"] : []);
  }
}

const letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".split("");
for (let index = 0; index < letters.length; index++) {
  const letter = letters[index];
  const next = letters[(index + 1) % letters.length];
  for (const variant of [
    [`capital-${letter.toLowerCase()}`, `Which capital letter matches lowercase ${letter.toLowerCase()}?`, letter],
    [`small-${letter.toLowerCase()}`, `Which lowercase letter matches capital ${letter}?`, letter.toLowerCase()],
    [`after-${letter.toLowerCase()}`, `What letter comes after ${letter}?`, next]
  ]) {
    const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
    task(`gen-alphabet-${variant[0]}`, "Alphabet", zoneKind, promptFor("Alphabet", zoneKind, variant[2], variant[1]), variant[2], "LetterOnly", assistMode, inputMode, malfunctionType);
  }
}

const vowelWords = [["rat", "A"], ["hen", "E"], ["pig", "I"], ["dog", "O"], ["pup", "U"], ["cat", "A"], ["fish", "I"], ["duck", "U"]];
for (let round = 1; round <= 35; round++) {
  for (const [word, vowel] of vowelWords) {
    const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
    task(`gen-vowels-${word}-${round}`, "VowelsConsonants", zoneKind, promptFor("VowelsConsonants", zoneKind, vowel, `Find the vowel in ${word.toUpperCase()}.`), vowel, "LetterOnly", assistMode, inputMode, malfunctionType);
  }
}

const sentenceSubjects = ["I", "We", "You", "The cat", "The dog", "The bird", "The fish", "My pup"];
const sentencePredicates = ["am ready", "are here", "play", "run", "jump", "sit", "can help", "look up"];
for (const subject of sentenceSubjects) {
  for (const predicate of sentencePredicates) {
    const answer = `${subject} ${predicate}.`;
    const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
    task(`gen-sentence-${slug(subject)}-${slug(predicate)}`, "SentenceStartEnd", zoneKind, promptFor("SentenceStartEnd", zoneKind, answer), answer, "FullSentence", assistMode, inputMode, malfunctionType);
  }
}

for (let round = 1; round <= 12; round++) {
  for (const [nounWord, , category] of nouns) {
    const answer = title(nounWord);
    const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
    task(`gen-noun-${nounWord}-${round}`, "BasicNouns", zoneKind, promptFor("BasicNouns", zoneKind, answer, "", category), answer, "NounOnly", assistMode, inputMode, malfunctionType);
  }
}

for (const [verb, third, allowed] of verbs) {
  for (const nounWord of allowed) {
    for (let round = 1; round <= 7; round++) {
      const answer = `${title(nounWord)} ${third}`;
      const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
      task(`gen-verb-${nounWord}-${verb}-${round}`, "BasicVerbs", zoneKind, promptFor("BasicVerbs", zoneKind, answer, nounWord, verb), answer, "NounVerbPresent", assistMode, inputMode, malfunctionType, [`${title(nounWord)} ${verb}`]);
    }
  }
}

for (let round = 1; round <= 12; round++) {
  for (const [nounWord, , , article] of nouns) {
    for (const chosen of [article, "the"]) {
      const answer = `${title(chosen)} ${nounWord}`;
      const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
      task(`gen-article-${chosen}-${nounWord}-${round}`, "Articles", zoneKind, promptFor("Articles", zoneKind, answer), answer, "DeterminerNoun", assistMode, inputMode, malfunctionType);
    }
  }
}

for (const pronoun of pronouns) {
  for (const [verb, third] of verbs.slice(0, 8)) {
    for (let round = 1; round <= 6; round++) {
      const action = ["he", "she", "it"].includes(pronoun.toLowerCase()) ? third : verb;
      const answer = `${pronoun} ${action}`;
      const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
      task(`gen-pronoun-${slug(pronoun)}-${verb}-${round}`, "Pronouns", zoneKind, promptFor("Pronouns", zoneKind, answer, pronoun, verb), answer, "PronounVerbPresent", assistMode, inputMode, malfunctionType);
    }
  }
}

for (let round = 1; round <= 13; round++) {
  for (const [nounWord, plural] of nouns) {
    const answer = title(plural);
    const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
    task(`gen-plural-${nounWord}-${round}`, "Plurals", zoneKind, promptFor("Plurals", zoneKind, answer, nounWord), answer, "FullSentence", assistMode, inputMode, malfunctionType);
  }
}

for (let round = 1; round <= 4; round++) {
  for (const adjective of adjectives) {
    for (const [nounWord] of nouns.slice(0, 16)) {
      const answer = `${title(adjective)} ${nounWord}`;
      const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
      task(`gen-adjective-${adjective}-${nounWord}-${round}`, "Adjectives", zoneKind, promptFor("Adjectives", zoneKind, answer, adjective, nounWord), answer, "AdjectiveNoun", assistMode, inputMode, malfunctionType);
    }
  }
}

for (const [nounWord] of nouns.slice(0, 8)) {
  for (const prep of prepositions) {
    for (const target of validTargetsByPreposition[prep]) {
      const move = nounWord === "bird" || nounWord === "owl" ? "flies" : nounWord === "fish" || nounWord === "duck" ? "swims" : "runs";
      const answer = `${title(nounWord)} ${move} ${prep} the ${target}`;
      const [zoneKind, assistMode, inputMode, malfunctionType] = modeFor(i++);
      task(`gen-preposition-${nounWord}-${move}-${prep}-${target}`, "BasicPrepositions", zoneKind, promptFor("BasicPrepositions", zoneKind, answer), answer, "FullSentence", assistMode, inputMode, malfunctionType);
    }
  }
}

function normalizeAssessmentText(value) {
  return String(value ?? "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim().replace(/\s+/g, " ");
}

const leakedGymTasks = tasks.filter((item) => {
  if (item.zoneKind !== "Gym" || item.grammarPattern === "LetterOnly" || item.grammarPattern === "NounOnly" || item.conceptId === "Plurals") return false;
  const prompt = ` ${normalizeAssessmentText(item.npcLine)} `;
  const answer = normalizeAssessmentText(item.expectedResponse);
  return answer.length >= 4 && prompt.includes(` ${answer} `);
});
if (leakedGymTasks.length > 0) {
  throw new Error(`Refusing to generate ${leakedGymTasks.length} Gym task(s) that expose their exact answer. First: ${leakedGymTasks[0].id}`);
}

fs.mkdirSync(path.dirname(outJson), { recursive: true });
fs.writeFileSync(outJson, JSON.stringify({ tasks }, null, 2) + "\n");
fs.mkdirSync(path.dirname(functionsJson), { recursive: true });
fs.writeFileSync(functionsJson, JSON.stringify({ tasks }, null, 2) + "\n");

console.log(`Generated ${tasks.length} dialogue exercise instances.`);
console.log(outJson);
console.log(functionsJson);
