const maximumGroundingCharacters = 900;
const maximumSourceIds = 8;

export interface BuddyContextComposerInput {
  task: Record<string, unknown>;
  profile: Record<string, unknown>;
  learnerState: Record<string, unknown>;
  grimoirePage?: Record<string, unknown>;
  practiceScaffold?: Record<string, unknown>;
  buddyContract?: Record<string, unknown>;
}

export interface BuddyContextComposerOutput {
  groundingContext: string;
  groundingSourceIds: string[];
  estimatedTokens: number;
}

export function composeBuddyGroundingContext(input: BuddyContextComposerInput): BuddyContextComposerOutput {
  const task = input.task ?? {};
  const conceptId = trimmedString(task.conceptId, 96);
  const contractId = trimmedString(task.buddyContractId, 96);
  const scaffold = input.practiceScaffold ?? {};
  const grimoire = input.grimoirePage ?? {};
  const contract = input.buddyContract ?? {};
  const conceptState = cloneRecord(cloneRecord(input.learnerState?.concepts)[conceptId]);
  const answerPolicy = buildAnswerPolicy(task, scaffold);
  const protectText = (value: unknown, maximum: number) => redactProtectedAnswer(
    redactBuddyText(value, maximum),
    answerPolicy.protectedAnswers,
  );

  const lines = [
    joinLine("Curriculum", [
      trimmedString(grimoire.title, 100) || trimmedString(task.conceptTitle, 100) || conceptId,
      protectText(grimoire.rule, 220),
    ]),
    joinLine("Examples", normalizedStringArray(grimoire.examples, 3).map(value => protectText(value, 90))),
    joinLine("Common mistakes", normalizedStringArray(grimoire.commonGoofs, 3).map(value => protectText(value, 90))),
    joinLine("Task scaffold", [
      trimmedString(scaffold.mode, 64) || trimmedString(task.scaffoldMode, 64),
      protectText(scaffold.prompt || task.displayTranscript || task.npcLine, 180),
    ]),
    joinLine("Buddy contract", [
      trimmedString(contract.instruction, 180),
      forbiddenActions(contract),
    ]),
    joinLine("Learner pattern", [
      masteryLine(conceptState),
      normalizedStringArray(input.learnerState?.recurringErrorTags, 5).length
        ? `recurring errors: ${normalizedStringArray(input.learnerState?.recurringErrorTags, 5).join(", ")}`
        : "",
    ]),
  ].filter(Boolean);

  const groundingContext = clampContext(lines.join(" "));
  return {
    groundingContext,
    groundingSourceIds: sourceIds(task, grimoire, scaffold, contract),
    estimatedTokens: estimatePromptTokens(groundingContext),
  };
}

export function redactTaskAnswerText(
  task: Record<string, unknown>,
  scaffold: Record<string, unknown>,
  value: unknown,
  maximum: number,
): string {
  const answerPolicy = buildAnswerPolicy(task, scaffold);
  return redactProtectedAnswer(
    redactBuddyText(value, maximum),
    answerPolicy.protectedAnswers,
  );
}

export function estimatePromptTokens(value: string): number {
  const normalized = value.trim();
  if (!normalized) return 0;
  const latinWords = normalized.match(/[A-Za-z0-9]+/g)?.length ?? 0;
  const indicRuns = normalized.match(/[\u0900-\u097F]+/g)?.length ?? 0;
  const punctuation = normalized.match(/[.,:;!?()[\]{}]/g)?.length ?? 0;
  return Math.ceil(latinWords + indicRuns * 1.4 + punctuation * 0.25);
}

function joinLine(label: string, values: string[]): string {
  const clean = values
    .map(value => trimmedString(value, 240))
    .filter(Boolean);
  return clean.length ? `${label}: ${clean.join("; ")}.` : "";
}

function forbiddenActions(contract: Record<string, unknown>): string {
  const forbidden = normalizedStringArray(contract.forbiddenActions, 4);
  return forbidden.length ? `must not: ${forbidden.join(", ")}` : "";
}

function masteryLine(conceptState: Record<string, unknown>): string {
  const attempts = Number(conceptState.attempts ?? 0);
  const mastery = finiteNumber(conceptState.masteryEstimate);
  const hintDependency = finiteNumber(conceptState.hintDependency);
  if (attempts <= 0 && mastery <= 0 && hintDependency <= 0) return "";
  return `concept attempts: ${Math.max(0, Math.round(attempts))}, mastery: ${mastery.toFixed(2)}, hint dependency: ${hintDependency.toFixed(2)}`;
}

function sourceIds(
  task: Record<string, unknown>,
  grimoire: Record<string, unknown>,
  scaffold: Record<string, unknown>,
  contract: Record<string, unknown>,
): string[] {
  const candidates = [
    sourceId("gameContentDialogueTasks", task.id || task.taskId || task.lineId),
    sourceId("gameContentGrimoirePages", grimoire.id || grimoire.conceptId || task.conceptId),
    sourceId("gameContentPracticeScaffolds", scaffold.id || scaffold.taskId || task.id),
    sourceId("gameContentBuddyContracts", contract.id || task.buddyContractId),
  ].filter(Boolean);
  return Array.from(new Set(candidates)).slice(0, maximumSourceIds);
}

function sourceId(collection: string, id: unknown): string {
  const safeId = trimmedString(id, 160);
  return safeId ? `${collection}/${safeId}` : "";
}

function clampContext(value: string): string {
  const compact = value.replace(/\s+/g, " ").trim();
  if (compact.length <= maximumGroundingCharacters) return compact;
  const clipped = compact.slice(0, maximumGroundingCharacters);
  const sentenceEnd = Math.max(clipped.lastIndexOf("."), clipped.lastIndexOf(";"));
  return (sentenceEnd > 320 ? clipped.slice(0, sentenceEnd + 1) : clipped).trim();
}

function buildAnswerPolicy(
  task: Record<string, unknown>,
  scaffold: Record<string, unknown>,
): { protectedAnswers: string[] } {
  const assistMode = trimmedString(task.assistMode, 32);
  const allowExactAnswer = assistMode === "Full";
  if (allowExactAnswer) return { protectedAnswers: [] };
  const candidates = [
    task.expectedResponse,
    task.expectedFullResponse,
    task.expectedInputResponse,
    scaffold.fullAnswer,
    scaffold.answer,
    scaffold.challengeAudioText,
    scaffold.heardTranscript,
  ]
    .map(value => trimmedString(value, 120))
    .filter(value => value.split(/\s+/).length >= 2 || value.length >= 4);
  return { protectedAnswers: Array.from(new Set(candidates)) };
}

function redactProtectedAnswer(value: string, protectedAnswers: string[]): string {
  let result = value;
  for (const answer of protectedAnswers) {
    const escaped = answer.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    result = result.replace(new RegExp(escaped, "gi"), "[current answer]");
  }
  return result;
}

function cloneRecord(value: unknown): Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? { ...(value as Record<string, unknown>) }
    : {};
}

function finiteNumber(value: unknown): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function normalizedStringArray(value: unknown, maximum: number): string[] {
  if (!Array.isArray(value)) return [];
  const result: string[] = [];
  for (const item of value) {
    const text = trimmedString(item, 240);
    if (!text || result.includes(text)) continue;
    result.push(text);
    if (result.length >= maximum) break;
  }
  return result;
}

function trimmedString(value: unknown, maximum = 2048): string {
  return String(value ?? "").replace(/\s+/g, " ").trim().slice(0, Math.max(0, maximum)).trim();
}

function redactBuddyText(value: unknown, maximum: number): string {
  let text = trimmedString(value, maximum);
  text = text.replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, "[email]");
  text = text.replace(/(?:\+?\d[\d\s().-]{7,}\d)/g, "[phone]");
  text = text.replace(/\b(my name is|i am called)\s+[a-z][a-z'-]*/gi, "$1 [learner]");
  return text;
}
