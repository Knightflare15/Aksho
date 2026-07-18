export * from "./tacticalCombatTypes";

import type {
  ActionProfile,
  BattleSnapshot,
  BattleState,
  CommandPreview,
  CommandResult,
  Curse,
  EnemyPace,
  EnemyPaceSummary,
  ModifierDef,
  NounDef,
  ParsedAction,
  Position
} from "./tacticalCombatTypes";
import {
  acceptedNounForms,
  adverbDefs,
  applyModifierStats,
  buildSampleCommands,
  fallbackVerb,
  findModifier,
  findNoun,
  findVerb,
  modifierAllowsNoun,
  nounAllowsAdverb,
  nounAllowsVerb,
  nouns,
  pickEnemyForSummon,
  resolveAdverbRangeBonus,
  resolveCooldownSeconds,
  resolveVerb,
  toTacticalStats,
  verbAllowsAdverb
} from "./tacticalCombatDefinitions";
import {
  canEnemyAttackPlayer,
  canonicalDirection,
  canonicalPreposition,
  directionName,
  directionTo,
  emptyTerrain,
  facingArcCells,
  hasClearLine,
  hexDistance,
  isInFacingArc,
  isPreposition,
  isRelativeDirection,
  same
} from "./tacticalCombatGeometry";
import { planMovement } from "./tacticalCombatMovement";
import {
  enemyOpeningGraceSeconds,
  enemyPaceSummaryFromState,
  enemyThinkInterval,
  isPronounCurse,
  resolveEnemyResponse,
  resolvePendingEnemyAttackIfReady,
  resolveRealtimeEnemyDecision
} from "./tacticalCombatEnemy";
import { clamp, cloneState, curseName, normalize, pushLog, round, tokenize } from "./tacticalCombatUtils";

export function createInitialBattle(enemyPace: EnemyPace = "Beginner"): BattleSnapshot {
  const fallback = "BRAVE RAT";
  return startBattle(fallback, enemyPace);
}

export function startBattle(inputPhrase: string, enemyPace: EnemyPace = "Beginner"): BattleSnapshot {
  const parsed = parseSummon(inputPhrase) ?? parseSummon("BRAVE RAT");
  const noun = parsed?.noun ?? findNoun("RAT")!;
  const enemy = pickEnemyForSummon(noun);
  const state = buildState(noun, parsed?.adjective ?? null, enemy, enemyPace);
  pushLog(state, `Tactical grid ready. Summoned ${state.playerUnit?.displayPhrase}. Enemy ${enemy.canonicalNoun} appeared from the same noun family.`);
  return snapshot(state);
}

export function setEnemyPace(snapshotInput: BattleSnapshot, enemyPace: EnemyPace): BattleSnapshot {
  const state = cloneState(snapshotInput.state);
  state.enemyPace = enemyPace;
  if (!state.pendingEnemyAttack)
    state.nextEnemyDecisionAt = state.clock + enemyThinkInterval(state);
  const pace = enemyPaceSummaryFromState(state);
  pushLog(state, `Enemy pace set to ${pace.label}: about ${pace.decisionIntervalSeconds.toFixed(1)} seconds between decisions.`);
  return snapshot(state);
}

export function enemyPaceSummary(snapshotInput: BattleSnapshot): EnemyPaceSummary {
  return enemyPaceSummaryFromState(snapshotInput.state);
}

export function executeCommand(snapshotInput: BattleSnapshot, phrase: string, curse: Curse): BattleSnapshot {
  const state = cloneState(snapshotInput.state);
  resolvePendingEnemyAttackIfReady(state);
  const effectiveCurse = curse === "None" ? state.activeCurse : curse;
  const result = handlePhrase(state, phrase, effectiveCurse);
  if (result.accepted && curse === "None" && state.activeCurse !== "None") {
    const persistence = isPronounCurse(state.activeCurse)
      ? `${Math.max(0, Math.ceil(state.curseExpiresAt - state.clock))} seconds remain.`
      : "It remains for this battle.";
    result.message = `${result.message} ${curseName(state.activeCurse)} obeyed; ${persistence}`;
  }
  pushLog(state, result.message);
  return snapshot(state);
}

export function advanceBattleTime(snapshotInput: BattleSnapshot, deltaSeconds: number): BattleSnapshot {
  const state = cloneState(snapshotInput.state);
  const delta = clamp(deltaSeconds, 0, 1);
  if (delta <= 0 || !state.playerUnit || state.playerUnit.currentHp <= 0 || state.enemyUnit.currentHp <= 0) return snapshot(state);

  state.clock = round(state.clock + delta);
  if (isPronounCurse(state.activeCurse) && state.clock >= state.curseExpiresAt) {
    pushLog(state, `${curseName(state.activeCurse)} faded after 30 seconds.`);
    state.activeCurse = "None";
    state.curseExpiresAt = -999;
  }

  resolvePendingEnemyAttackIfReady(state);
  if (!state.pendingEnemyAttack && state.clock >= state.nextEnemyDecisionAt) {
    pushLog(state, resolveRealtimeEnemyDecision(state));
    state.nextEnemyDecisionAt = state.clock + enemyThinkInterval(state);
  }
  return snapshot(state);
}

export function previewCommand(snapshotInput: BattleSnapshot, phrase: string, curse: Curse): CommandPreview {
  const state = cloneState(snapshotInput.state);
  if (!state.playerUnit) return { ok: false, message: "Summon a unit before issuing commands." };
  if (!phrase.trim()) return { ok: false, message: "Type a command to preview its tactical range." };

  const curseGate = applyCurseGate(state, phrase, curse === "None" ? state.activeCurse : curse);
  if (!curseGate.ok) return { ok: false, message: curseGate.message };

  const clauses = buildConjunctionClauses(curseGate.phrase);
  const previewPhrase = clauses.clauses.length > 0 ? clauses.clauses[0] : curseGate.phrase;
  if (clauses.error) return { ok: false, message: clauses.error };

  const action = parseAction(state, previewPhrase);
  if (!action) return { ok: false, message: "That command cannot be previewed yet." };
  if (normalize(action.subjectNoun) !== state.playerUnit.noun) {
    return { ok: false, message: "Preview subject does not match the current summon." };
  }

  const noun = findNoun(state.playerUnit.noun);
  if (noun && !nounAllowsVerb(noun, action.verb.verb)) {
    return { ok: false, message: `${state.playerUnit.noun} cannot use ${action.verb.verb}.` };
  }
  if (noun && action.adverb && (!verbAllowsAdverb(action.verb, action.adverb) || !nounAllowsAdverb(noun, action.verb.verb, action.adverb.modifier))) {
    return { ok: false, message: `${action.adverb.modifier} does not fit ${action.verb.verb}.` };
  }

  const profile = buildActionProfile(state, action);
  const movement = profile.movementAction ? planMovement(state, action, profile) : null;
  if (movement && !movement.ok) {
    return { ok: false, profile, message: movement.message, transformedPhrase: curseGate.transformed };
  }
  return {
    ok: true,
    profile,
    movementPath: movement?.path,
    destination: movement?.destination,
    transformedPhrase: curseGate.transformed,
    message: `${profile.verb}${profile.adverb ? ` ${profile.adverb}` : ""}: ${profile.category}, range ${profile.rangeCells}, move ${profile.movementCells}, PP ${profile.ppCost}.`
  };
}

export function turnPlayer(snapshotInput: BattleSnapshot, target: Position): BattleSnapshot {
  const state = cloneState(snapshotInput.state);
  if (state.playerUnit && !same(state.playerUnit.position, target)) {
    state.playerFacing = directionTo(state.playerUnit.position, target);
    state.selectedAimCell = { ...target };
    pushLog(state, `Facing ${directionName(state.playerUnit.position, state.playerFacing)} toward (${target.x}, ${target.y}). Movement commands now follow this heading.`);
  }
  return snapshot(state);
}

export function attackPreviewCells(snapshotInput: BattleSnapshot, profile?: ActionProfile): Position[] {
  const state = snapshotInput.state;
  const player = state.playerUnit;
  if (!player || profile?.category !== "Attack") return [];
  return facingArcCells(state, player.position, state.playerFacing, profile.rangeCells);
}

export function enemyThreatCells(snapshotInput: BattleSnapshot): Position[] {
  const state = snapshotInput.state;
  return state.pendingEnemyAttack
    ? facingArcCells(state, state.enemyUnit.position, state.enemyFacing, 1)
    : [];
}

export function availableSummons() {
  return nouns
    .filter((noun) => noun.role === "Creature")
    .flatMap((noun) => acceptedNounForms(noun).flatMap((form) => noun.allowedAdjectives.slice(0, 4).map((adjectiveName) => `${adjectiveName} ${form}`)))
    .slice(0, 180);
}

function handlePhrase(state: BattleState, phrase: string, curse: Curse): CommandResult {
  if (!state.playerUnit) {
    return { accepted: false, message: "Summon a unit before issuing commands.", damage: 0 };
  }

  const curseGate = applyCurseGate(state, phrase, curse);
  if (!curseGate.ok) {
    return { accepted: false, message: curseGate.message, damage: 0 };
  }

  const clauses = buildConjunctionClauses(curseGate.phrase);
  if (clauses.error) {
    return { accepted: false, message: clauses.error, damage: 0 };
  }

  if (clauses.clauses.length > 0) {
    let totalDamage = 0;
    const outcomes: string[] = [];
    let lastProfile: ActionProfile | undefined;
    for (const clause of clauses.conjunction === "OR" ? clauses.clauses : clauses.clauses.slice(0, 2)) {
      const result = executeSingle(state, clause);
      if (!result.accepted) {
        if (clauses.conjunction === "OR") continue;
        return result;
      }
      totalDamage += result.damage;
      lastProfile = result.profile;
      outcomes.push(result.message);
      if (clauses.conjunction === "OR") break;
    }
    if (clauses.conjunction === "OR" && outcomes.length === 0) {
      return { accepted: false, message: "Neither OR action worked for this battle.", damage: 0 };
    }
    const enemy = resolveEnemyResponse(state, totalDamage > 0, lastProfile);
    return {
      accepted: true,
      damage: totalDamage,
      profile: lastProfile,
      message: `Chained verbs with ${clauses.conjunction}: ${outcomes.join(" ")} ${enemy}`.trim()
    };
  }

  const result = executeSingle(state, curseGate.phrase);
  if (!result.accepted) {
    return result;
  }
  const enemy = resolveEnemyResponse(state, result.damage > 0, result.profile);
  return { ...result, message: `${result.message} ${enemy}`.trim(), transformedPhrase: curseGate.transformed };
}

function executeSingle(state: BattleState, phrase: string): CommandResult {
  const action = parseAction(state, phrase);
  if (!action) return { accepted: false, message: "That battle command is not supported.", damage: 0 };
  if (!state.playerUnit || normalize(action.subjectNoun) !== state.playerUnit.noun) {
    return { accepted: false, message: "That subject does not match the current player unit.", damage: 0 };
  }

  const noun = findNoun(state.playerUnit.noun);
  if (noun && !nounAllowsVerb(noun, action.verb.verb)) {
    return { accepted: false, message: `${state.playerUnit.noun} cannot use ${action.verb.verb}.`, damage: 0 };
  }
  if (noun && action.adverb && (!verbAllowsAdverb(action.verb, action.adverb) || !nounAllowsAdverb(noun, action.verb.verb, action.adverb.modifier))) {
    return { accepted: false, message: `${action.adverb.modifier} does not fit ${action.verb.verb}.`, damage: 0 };
  }

  const profile = buildActionProfile(state, action);
  const readyAt = state.playerUnit.cooldowns[profile.verb] ?? 0;
  if (state.clock < readyAt) {
    return { accepted: false, message: `${profile.verb} is recovering for ${(readyAt - state.clock).toFixed(1)}s.`, damage: 0, profile };
  }
  if (state.playerUnit.currentPp < profile.ppCost) {
    return { accepted: false, message: "Not enough PP for that action.", damage: 0, profile };
  }

  let actionMessage = "";
  if (profile.movementAction) {
    const movement = planMovement(state, action, profile);
    if (!movement.ok || !movement.destination) {
      return { accepted: false, message: movement.message, damage: 0, profile };
    }
    spendAndCooldown(state, profile);
    if (profile.verb === "TURN") {
      state.playerFacing = movement.facing ?? state.playerFacing;
      actionMessage = `Turned ${profile.direction.toLowerCase() || directionName(state.playerUnit.position, state.playerFacing)}.`;
    } else {
      state.playerUnit.position = movement.destination;
      actionMessage = action.preposition
        ? `Moved to (${movement.destination.x}, ${movement.destination.y}) using ${action.preposition.toLowerCase()}.`
        : `Moved ${profile.direction.toLowerCase() || "forward"} to (${movement.destination.x}, ${movement.destination.y}) with ${profile.verb}.`;
    }
    state.selectedAimCell = null;
  } else if (profile.category === "Utility") {
    spendAndCooldown(state, profile);
    actionMessage = applyUtilityBoost(state, profile);
  } else {
    spendAndCooldown(state, profile);
    actionMessage = `Resolved ${profile.verb}${profile.adverb ? ` ${profile.adverb}` : ""}.`;
  }

  applyActionProtection(state, profile);
  const damageResult = resolveDamage(state, profile);
  if (damageResult.damage > 0) {
    state.enemyUnit.currentHp = Math.max(0, state.enemyUnit.currentHp - damageResult.damage);
  }

  return {
    accepted: true,
    message: damageResult.damage > 0 ? `${actionMessage} Hit the enemy for ${damageResult.damage}.` : `${actionMessage} ${damageResult.reason}`.trim(),
    profile,
    damage: damageResult.damage
  };
}

function buildState(noun: NounDef, adjectiveMod: ModifierDef | null, enemy: NounDef, enemyPace: EnemyPace): BattleState {
  const terrain = emptyTerrain();
  terrain[1][2] = "Rock";
  terrain[2][3] = "Wall";
  terrain[3][2] = "Spikes";
  terrain[3][3] = "Water";

  let stats = toTacticalStats(noun.baseStats);
  if (adjectiveMod) stats = applyModifierStats(stats, adjectiveMod);
  const displayPhrase = adjectiveMod ? `${adjectiveMod.modifier} ${noun.canonicalNoun}` : noun.canonicalNoun;

  const state: BattleState = {
    width: 5,
    height: 5,
    terrain,
    playerUnit: {
      displayPhrase,
      noun: noun.canonicalNoun,
      determiner: "",
      adjective: adjectiveMod?.modifier ?? "",
      stats,
      currentHp: stats.maxHp,
      currentPp: stats.maxPp,
      position: { x: 0, y: 2 },
      cooldowns: {}
    },
    enemyUnit: {
      displayPhrase: enemy.canonicalNoun,
      noun: enemy.canonicalNoun,
      determiner: "",
      adjective: "",
      stats: toTacticalStats(enemy.baseStats),
      currentHp: enemy.baseStats.maxHp,
      currentPp: enemy.baseStats.maxPp,
      position: { x: 4, y: 2 },
      cooldowns: {}
    },
    playerFacing: { x: 1, y: 0 },
    enemyFacing: { x: -1, y: 0 },
    selectedAimCell: null,
    activeShield: 0,
    shieldExpiresAt: -999,
    pendingEnemyAttack: null,
    activeCurse: "None",
    curseExpiresAt: -999,
    nextEnemyDecisionAt: 0,
    enemyDecisionCount: 0,
    enemyPace,
    clock: 0,
    log: []
  };
  state.nextEnemyDecisionAt = enemyThinkInterval(state) + enemyOpeningGraceSeconds(state);
  return state;
}

function snapshot(state: BattleState): BattleSnapshot {
  const noun = state.playerUnit ? findNoun(state.playerUnit.noun) : null;
  const enemy = findNoun(state.enemyUnit.noun);
  const allowedVerbs = noun ? noun.moveSet.map((slot) => findVerb(slot.verbId) ?? fallbackVerb(slot.verbId, slot.category)).filter(Boolean) : [];
  const allowedAdverbs = allowedVerbs.length
    ? adverbDefs.filter((mod) => allowedVerbs.some((verb) => verbAllowsAdverb(verb, mod)))
    : [];
  const sampleCommands = buildSampleCommands(state, allowedVerbs, allowedAdverbs);
  return {
    state,
    noun,
    enemy: enemy ?? null,
    allowedVerbs,
    allowedAdverbs,
    sampleCommands,
    grammarNotes: [
      "Click any cell to look that way. Movement is facing-relative: RAT WALKS FORWARD, RAT RUNS LEFT, RAT DODGES BACKWARD.",
      "Attack verbs project a cone. Movement verbs project a straight route; their own move range decides the landing cell.",
      "Landmarks resolve ambiguity: NEAR/BESIDE choose a reachable side, BEHIND means out of the enemy's line, and OVER requires a verb that can clear an obstacle.",
      `Summons are locked to the enemy noun family: ${enemy?.canonicalNoun ?? state.enemyUnit.noun}${enemy ? acceptedNounForms(enemy).length > 1 ? ` accepts ${acceptedNounForms(enemy).join(", ")}.` : "." : "."}`,
      "AND/OR/BECAUSE are supported for chained clauses. OR chooses the first clause that can resolve.",
      "The enemy thinks on its own clock: it advances, winds up attacks, and inflicts grammar curses. Pronoun curses last 30 seconds; Past Fog and Now Mist last until the battle ends."
    ]
  };
}

function parseSummon(phrase: string): { noun: NounDef; adjective: ModifierDef | null } | null {
  const tokens = tokenize(phrase);
  if (tokens.length === 0) return null;
  let index = ["A", "AN", "THE"].includes(tokens[0]) ? 1 : 0;
  let adjectiveMod: ModifierDef | null = null;
  if (tokens[index] && findModifier(tokens[index], "Adjective")) {
    adjectiveMod = findModifier(tokens[index], "Adjective");
    index += 1;
  }
  const noun = findNoun(tokens[index]);
  if (!noun || noun.role !== "Creature") return null;
  if (adjectiveMod && (!noun.allowedAdjectives.includes(adjectiveMod.modifier) || !modifierAllowsNoun(adjectiveMod, noun))) return null;
  return { noun, adjective: adjectiveMod };
}

function parseAction(state: BattleState, phrase: string): ParsedAction | null {
  const tokens = tokenize(phrase);
  if (tokens.length < 2) return null;
  let index = 0;
  if (["A", "AN", "THE"].includes(tokens[index])) index += 1;
  const noun = findNoun(tokens[index]);
  if (!noun) return null;
  index += 1;
  const verb = resolveVerb(tokens[index]);
  if (!verb) return null;
  index += 1;
  let adverbMod: ModifierDef | null = null;
  let direction = "";
  let preposition = "";
  let objectToken = "";
  while (index < tokens.length) {
    const token = tokens[index];
    const modifier = findModifier(token, "Adverb");
    if (!adverbMod && modifier) {
      adverbMod = modifier;
      index += 1;
      continue;
    }
    if (!direction && isRelativeDirection(token)) {
      direction = canonicalDirection(token);
      index += 1;
      continue;
    }
    if (!preposition && isPreposition(token)) {
      preposition = canonicalPreposition(token);
      index += 1;
      if (preposition === "AWAY" && tokens[index] === "FROM") index += 1;
      if (["A", "AN", "THE"].includes(tokens[index])) index += 1;
      objectToken = tokens[index] ?? "";
      index += objectToken ? 1 : 0;
      continue;
    }
    index += 1;
  }
  return { subjectNoun: noun.canonicalNoun, verb, adverb: adverbMod, direction, preposition, objectToken, clock: state.clock };
}

function buildActionProfile(state: BattleState, action: ParsedAction): ActionProfile {
  const player = state.playerUnit!;
  const slot = findNoun(player.noun)?.moveSet.find((move) => normalize(move.verbId) === action.verb.verb);
  const category = slot?.category ?? action.verb.role;
  const adverbMod = action.adverb;
  const powerMultiplier = adverbMod?.powerMultiplier ?? 1;
  const defenseMultiplier = adverbMod?.defenseMultiplier ?? 1;
  const speedMultiplier = adverbMod?.speedMultiplier ?? 1;
  const ppCostMultiplier = adverbMod?.ppCostMultiplier ?? 1;
  const power = Math.max(0, Math.round((action.verb.power + (slot?.nounPowerOffset ?? 0)) * powerMultiplier));
  const rangeCells = clamp(action.verb.tacticalRangeCells + resolveAdverbRangeBonus(adverbMod), 1, 3);
  const movementCells = clamp(action.verb.movementVerb && action.verb.tacticalMovementCells <= 0 ? 1 : action.verb.tacticalMovementCells, 0, 3);
  const actionSpeed = Math.max(1, player.stats.speed * speedMultiplier);
  return {
    verb: action.verb.verb,
    adverb: adverbMod?.modifier ?? "",
    preposition: action.preposition,
    direction: action.direction,
    category,
    power,
    speedScore: actionSpeed,
    actionSpeed,
    ppCost: Math.max(1, Math.round(action.verb.ppCost * ppCostMultiplier)),
    rangeCells,
    movementCells,
    shieldAmount: category === "Defense" ? Math.max(1, Math.round(player.stats.defense * defenseMultiplier)) : 0,
    shieldDurationSeconds: category === "Defense" ? Math.max(1, 5 * clamp(defenseMultiplier, 0.5, 2)) : 0,
    cooldownSeconds: resolveCooldownSeconds(category, power, rangeCells, movementCells, player.stats.speed, actionSpeed, action.verb.cooldownSeconds),
    damageMultiplier: action.verb.tacticalDamageMultiplier,
    movementAction: action.verb.movementVerb
  };
}

function resolveDamage(state: BattleState, profile: ActionProfile): { damage: number; reason: string } {
  if (profile.category !== "Attack") return { damage: 0, reason: "" };
  const canAttack = canPlayerAttackTarget(state, profile);
  if (!canAttack.ok) return { damage: 0, reason: `No damage: ${canAttack.reason}` };
  return { damage: Math.max(1, Math.round((state.playerUnit!.stats.attack + profile.power) * profile.damageMultiplier)), reason: "" };
}

function canPlayerAttackTarget(state: BattleState, profile: ActionProfile) {
  const player = state.playerUnit;
  const enemy = state.enemyUnit;
  if (!player || !enemy) return { ok: false, reason: "There is no target on the tactical grid." };
  const distance = hexDistance(player.position, enemy.position);
  if (distance > clamp(profile.rangeCells, 1, 3)) return { ok: false, reason: `Target is ${distance} cells away; ${profile.verb} reaches ${profile.rangeCells}.` };
  if (!isInFacingArc(player.position, enemy.position, state.playerFacing)) return { ok: false, reason: "Target is outside the current attack arc." };
  const line = hasClearLine(state, player.position, enemy.position);
  if (!line.ok) return { ok: false, reason: `${line.blocker} blocks the direct path at (${line.position?.x}, ${line.position?.y}).` };
  return { ok: true, reason: "" };
}

function applyCurseGate(state: BattleState, phrase: string, curse: Curse): { ok: boolean; phrase: string; message: string; transformed?: string } {
  if (curse === "None") return { ok: true, phrase, message: "" };
  const tokens = tokenize(phrase);
  const verbIndex = tokens.findIndex((token) => resolveVerb(token));
  const verbToken = verbIndex >= 0 ? tokens[verbIndex] : undefined;
  const verb = verbToken ? resolveVerb(verbToken) : null;
  const playerNoun = state.playerUnit?.noun ?? "";
  if (!verb) return { ok: false, phrase, message: "The curse needs a usable action verb." };
  switch (curse) {
    case "I":
      if (tokens[0] !== "I") return { ok: false, phrase, message: "The curse forces I. Say something like: I bite." };
      break;
    case "You":
      if (tokens[0] !== "YOU") return { ok: false, phrase, message: "The curse forces you. Say something like: you bite." };
      break;
    case "HeSheIt":
      if (!["HE", "SHE", "IT"].includes(tokens[0]) || !verb.third.includes(verbToken!)) return { ok: false, phrase, message: "The curse forces he/she/it. Use the third-person form, like: he bites." };
      break;
    case "They":
      if (tokens[0] !== "THEY") return { ok: false, phrase, message: "The curse forces they. Say something like: they bite." };
      break;
    case "PastFog":
      if (!verb.past.includes(verbToken!)) return { ok: false, phrase, message: "Past Fog is active. Use past tense, like: rat bit." };
      break;
    case "NowMist":
      if (!tokens.some((token) => ["AM", "IS", "ARE"].includes(token)) || !verb.progressive.includes(verbToken!)) return { ok: false, phrase, message: "Now Mist is active. Use am/is/are + -ing, like: rat is biting." };
      break;
  }
  const suffix = verbIndex >= 0 ? tokens.slice(verbIndex + 1) : [];
  const transformed = [playerNoun, verb.verb, ...suffix].filter(Boolean).join(" ");
  return { ok: true, phrase: transformed, message: "", transformed };
}

function buildConjunctionClauses(phrase: string): { conjunction: string; clauses: string[]; error: string } {
  const tokens = tokenize(phrase);
  const index = tokens.findIndex((token) => ["AND", "OR", "BECAUSE"].includes(token));
  if (index < 0) return { conjunction: "", clauses: [], error: "" };
  const conjunction = tokens[index];
  if (index === 0 || index >= tokens.length - 1) return { conjunction, clauses: [], error: `Put ${conjunction} between two useful clauses.` };
  const first = tokens.slice(0, index);
  const second = tokens.slice(index + 1);
  if (!startsWithSubjectVerb(first)) return { conjunction, clauses: [], error: "Start the conjunction sentence with a creature and an action." };
  if (conjunction === "BECAUSE") {
    if (!startsWithSubjectVerb(second)) return { conjunction, clauses: [], error: "After BECAUSE, give a reason with a subject and a verb." };
    return { conjunction, clauses: [first.join(" "), second.join(" ")], error: "" };
  }
  const expandedSecond = startsWithSubjectVerb(second) ? second : [...first.slice(0, first[0] === "THE" ? 2 : 1), ...second];
  if (!startsWithSubjectVerb(expandedSecond)) return { conjunction, clauses: [], error: `After ${conjunction}, add another verb for the same creature.` };
  return { conjunction, clauses: [first.join(" "), expandedSecond.join(" ")], error: "" };
}

function startsWithSubjectVerb(tokens: string[]) {
  let index = 0;
  if (["A", "AN", "THE"].includes(tokens[index])) index += 1;
  if (!findNoun(tokens[index])) return false;
  return Boolean(resolveVerb(tokens[index + 1]));
}

function spendAndCooldown(state: BattleState, profile: ActionProfile) {
  const player = state.playerUnit!;
  player.currentPp = Math.max(0, player.currentPp - profile.ppCost);
  player.cooldowns[profile.verb] = state.clock + Math.max(0, profile.cooldownSeconds);
}

function applyActionProtection(state: BattleState, profile: ActionProfile) {
  if (profile.category !== "Defense" || profile.shieldAmount <= 0) return;
  state.activeShield = Math.max(state.activeShield, profile.shieldAmount);
  state.shieldExpiresAt = Math.max(state.shieldExpiresAt, state.clock + profile.shieldDurationSeconds);
}

function applyUtilityBoost(state: BattleState, profile: ActionProfile) {
  const player = state.playerUnit!;
  if (["FAST", "EAGERLY"].includes(profile.adverb) || ["LOOK", "NOTICE", "OBSERVE"].includes(profile.verb)) {
    player.stats.speed += 1;
    return `${profile.verb} sharpened movement. SPD +1.`;
  }
  if (["HEAVILY", "LOUDLY", "BRAVELY"].includes(profile.adverb) || ["GLOW", "SHINE"].includes(profile.verb)) {
    player.stats.attack += 1;
    return `${profile.verb} built power. ATK +1.`;
  }
  player.stats.defense += 1;
  return `${profile.verb} steadied the summon. DEF +1.`;
}
