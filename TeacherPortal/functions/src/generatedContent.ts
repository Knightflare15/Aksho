import fs from "node:fs";

export type GeneratedDialogueAssistMode = "Full" | "Partial" | "Off";
export type GeneratedDialogueInputMode =
  | "SpeakOnly"
  | "WriteOnly"
  | "SpeakOrWrite"
  | "SpeakAndWrite"
  | "DrawAndSpeakBlank"
  | "DragDropWords";
export type GeneratedDialogueMalfunction =
  | "None"
  | "MissingWord"
  | "ScrambledSentence"
  | "PartialTranscript"
  | "HeardWrong";

export interface GeneratedDialogueTaskSeed {
  id: string;
  conceptId: string;
  zoneKind: string;
  npcLine: string;
  expectedResponse: string;
  grammarPattern: string;
  assistMode: GeneratedDialogueAssistMode;
  inputMode: GeneratedDialogueInputMode;
  malfunctionType: GeneratedDialogueMalfunction;
  alternatives?: string[];
}

export interface GeneratedPronunciationGuideRow {
  word: string;
  ipa: string;
  soundGuide: string;
  buddyHint: string;
  commonIssue: string;
}

export interface GeneratedVerbConjugationRow {
  pronoun: string;
  present: string;
  past: string;
  progressive: string;
  future: string;
}

export interface GeneratedGrimoirePage {
  id: string;
  conceptId: string;
  title: string;
  summary: string;
  rule: string;
  examples: string[];
  commonGoofs: string[];
  nouns: string[];
  adjectives: string[];
  functionWords: string[];
  pronunciationGuides: GeneratedPronunciationGuideRow[];
  conjugations: GeneratedVerbConjugationRow[];
}

function readGeneratedJson<T>(fileName: string): T {
  const source = fs.readFileSync(new URL(`./generated/${fileName}`, import.meta.url), "utf8");
  return JSON.parse(source) as T;
}

function requireArray<T>(value: T[] | undefined, label: string): T[] {
  if (!Array.isArray(value))
    throw new Error(`Generated content file is missing its ${label} array.`);
  return value;
}

const dialogueTaskFile = readGeneratedJson<{ tasks?: GeneratedDialogueTaskSeed[] }>("dialogue-task-seeds.json");
const grimoirePageFile = readGeneratedJson<{ pages?: GeneratedGrimoirePage[] }>("grimoire-pages.json");

export const generatedDialogueTaskSeeds = requireArray(dialogueTaskFile.tasks, "tasks");
export const generatedGrimoirePages = requireArray(grimoirePageFile.pages, "pages");
