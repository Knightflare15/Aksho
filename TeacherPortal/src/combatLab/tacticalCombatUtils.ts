import type { BattleState, Curse, VerbDef } from "./tacticalCombatTypes";

export function curseName(curse: Curse) {
  switch (curse) {
    case "I": return "I Curse";
    case "You": return "You Curse";
    case "HeSheIt": return "He/She/It Curse";
    case "They": return "They Curse";
    case "PastFog": return "Past Fog";
    case "NowMist": return "Now Mist";
    default: return "No curse";
  }
}

export function preferredThird(verb: VerbDef) {
  return verb.third[0] ?? thirdPerson(verb.verb);
}

export function thirdPerson(verb: string) {
  if (verb.endsWith("Y") && !/[AEIOU]Y$/.test(verb)) return `${verb.slice(0, -1)}IES`;
  if (/(S|SH|CH|X|Z|O)$/.test(verb)) return `${verb}ES`;
  return `${verb}S`;
}

export function pastTense(verb: string) {
  const irregular: Record<string, string> = { BITE: "BIT", RUN: "RAN", SWIM: "SWAM", FLY: "FLEW", EAT: "ATE", DRINK: "DRANK", FALL: "FELL", THROW: "THREW", DRIVE: "DROVE", WRITE: "WROTE", READ: "READ" };
  if (irregular[verb]) return irregular[verb];
  if (verb.endsWith("E")) return `${verb}D`;
  if (verb.endsWith("Y") && !/[AEIOU]Y$/.test(verb)) return `${verb.slice(0, -1)}IED`;
  return `${verb}ED`;
}

export function progressive(verb: string) {
  if (verb.endsWith("IE")) return `${verb.slice(0, -2)}YING`;
  if (verb.endsWith("E") && !verb.endsWith("EE")) return `${verb.slice(0, -1)}ING`;
  return `${verb}ING`;
}

export function tokenize(phrase: string) {
  return (phrase ?? "")
    .split(/[\s\-_]+/)
    .map((token) => normalize(token.replace(/^[,!.?:;"']+|[,!.?:;"']+$/g, "")))
    .filter(Boolean);
}

export function normalize(value: string | undefined) {
  return (value ?? "").trim().toUpperCase();
}

export function clamp(value: number, min: number, max: number) {
  return Math.max(min, Math.min(max, value));
}

export function lerp(from: number, to: number, amount: number) {
  return from + (to - from) * clamp(amount, 0, 1);
}

export function round(value: number) {
  return Math.round(value * 10) / 10;
}

export function pushLog(state: BattleState, message: string) {
  if (!message.trim()) return;
  state.log = [`${state.clock.toFixed(1)}s - ${message}`, ...state.log].slice(0, 16);
}

export function cloneState(state: BattleState): BattleState {
  return JSON.parse(JSON.stringify(state)) as BattleState;
}
