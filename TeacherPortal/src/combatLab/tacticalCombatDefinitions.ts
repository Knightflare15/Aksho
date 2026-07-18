import type { BattleState, ModifierDef, MoveSlot, NounDef, Stats, VerbCategory, VerbDef } from "./tacticalCombatTypes";
import { clamp, normalize, pastTense, preferredThird, progressive, thirdPerson } from "./tacticalCombatUtils";

const defaultStats = { maxHp: 5, attack: 2, defense: 1, speed: 5, maxPp: 12 };
const nounWords = [
  "ANT", "APPLE", "ARM", "BAG", "BALL", "BAT", "BED", "BELL", "BIN", "BIRD",
  "BOAT", "BOOK", "BOX", "BROOM", "BUN", "BUS", "CAN", "CAP", "CAR", "CAT",
  "COW", "CUP", "DAD", "DOG", "DOLL", "DOOR", "DOT", "DRUM", "DUCK", "EAR",
  "EGG", "EYE", "FAN", "FIN", "FISH", "FOX", "GAP", "GOAT", "GRAPES", "GUM",
  "HAT", "HEN", "ICE", "INK", "JAM", "JAR", "JUG", "KEY", "KID", "KING",
  "KITE", "LEG", "LION", "LOG", "MAN", "MAT", "MEN", "MOON", "MOP", "MUG",
  "NEST", "NET", "NOSE", "NUT", "OWL", "OX", "PAN", "PEG", "PEN", "PET",
  "PARK", "PIG", "PIN", "POT", "PUP", "QUAIL", "QUILT", "RAIN", "RAT", "RING", "ROCK", "RUG",
  "SCHOOL", "SHOP", "SOCK", "SPOON", "STAR", "SUN", "TAP", "TIGER", "TOY", "TREE", "UMBRELLA",
  "UNICORN", "VAN", "VASE", "WALL", "WATCH", "WIG", "XRAY", "XYLOPHONE",
  "YAK", "YOYO", "ZEBRA"
];

export const adjectiveDefs: ModifierDef[] = [
  adjective("BIG", { maxHp: 1.25, attack: 1.35, defense: 1.1, speed: 0.75 }, ["LARGE", "HUGE"]),
  adjective("SMALL", { maxHp: 0.8, attack: 0.75, defense: 0.85, speed: 1.3, evasion: 1.15 }, ["TINY", "LITTLE"]),
  adjective("TOUGH", { maxHp: 1.15, attack: 0.95, defense: 1.35, speed: 0.85 }, ["STURDY", "SOLID"]),
  adjective("SWIFT", { maxHp: 0.9, defense: 0.9, speed: 1.45 }, ["QUICK"], ["ANIMAL", "BIRD", "AQUATIC", "OBJECT", "FANTASY"]),
  adjective("BRAVE", { maxHp: 1.05, attack: 1.2, defense: 1.05, speed: 0.95 }, ["BOLD", "FIERCE"], ["ANIMAL", "PERSON", "FANTASY"]),
  adjective("GENTLE", { attack: 0.85, defense: 1.15, speed: 1.05 }),
  adjective("BRIGHT", { defense: 0.95, speed: 1.1 }, ["SHINY", "GLOWING"]),
  adjective("HEAVY", { attack: 1.18, defense: 1.12, speed: 0.78 }),
  adjective("LIGHT", { maxHp: 0.92, defense: 0.92, speed: 1.25, evasion: 1.08 }, ["LIGHTWEIGHT"]),
  adjective("ANGRY", { attack: 1.18, defense: 0.94, speed: 1.02 }, ["MAD"]),
  adjective("SMART", { attack: 1.02, defense: 1.04, accuracy: 1.12 }, ["CLEVER", "WISE"]),
  adjective("SAFE", { attack: 0.94, defense: 1.18, speed: 0.96 }, ["SECURE"]),
  adjective("YOUNG", { attack: 0.98, speed: 1.16, evasion: 1.06 }),
  adjective("OLD", { attack: 1.04, defense: 1.14, speed: 0.9 })
];

export const adverbDefs: ModifierDef[] = [
  adverb("FAST", ["QUICKLY", "RAPIDLY", "SWIFTLY"], ["MOVEMENT", "OFFENSE"], { speed: 1.5, power: 1.1, accuracy: 0.92, evasion: 1.25, pp: 1.35 }),
  adverb("SLOWLY", ["SLOW"], ["MOVEMENT", "DEFENSE", "UTILITY"], { defense: 1.2, speed: 0.65, power: 0.85, accuracy: 1.15, evasion: 0.8, pp: 0.65 }),
  adverb("GENTLY", ["LIGHTLY", "SOFTLY"], ["DEFENSE", "UTILITY", "OFFENSE"], { power: 0.72, defense: 1.15, accuracy: 1.08, pp: 0.7 }),
  adverb("HEAVILY", ["BOLDLY", "FIERCELY"], ["OFFENSE", "UTILITY"], { power: 1.35, accuracy: 0.82, speed: 0.85, pp: 1.55 }),
  adverb("CAREFULLY", ["CALMLY", "NEATLY"], ["DEFENSE", "UTILITY", "MOVEMENT", "OFFENSE"], { power: 0.92, accuracy: 1.25, defense: 1.1, pp: 0.95 }),
  adverb("QUIETLY", ["SOFT"], ["DEFENSE", "UTILITY", "OFFENSE"], { power: 0.72, defense: 1.15, accuracy: 1.08, pp: 0.7 }),
  adverb("SHARPLY", ["CLEANLY"], ["OFFENSE"], { power: 1.18, accuracy: 1.04, speed: 0.96, pp: 1.18 }),
  adverb("SMOOTHLY", ["EVENLY"], ["MOVEMENT", "UTILITY"], { speed: 1.2, accuracy: 1.08 }),
  adverb("CLEARLY", ["PLAINLY"], ["UTILITY", "DEFENSE", "OFFENSE"], { accuracy: 1.2, defense: 1.05, pp: 0.95 }),
  adverb("SAFELY", [], ["DEFENSE", "MOVEMENT", "UTILITY"], { speed: 0.9, defense: 1.16, accuracy: 1.06, pp: 0.9 }),
  adverb("EAGERLY", [], ["UTILITY", "MOVEMENT", "OFFENSE"], { speed: 1.16, power: 1.05, accuracy: 0.96, pp: 1.08 }),
  adverb("LOUDLY", ["NOISILY"], ["OFFENSE", "UTILITY"], { power: 1.16, accuracy: 0.94, pp: 1.12 }),
  adverb("BRAVELY", [], ["OFFENSE", "DEFENSE"], { power: 1.14, defense: 1.04, pp: 1.08 })
];

const modifiers = [...adjectiveDefs, ...adverbDefs];
const verbs = buildVerbs();
export const nouns = nounWords.map((word, index) => buildNoun(word, index));

export function buildSampleCommands(state: BattleState, allowedVerbs: VerbDef[], allowedAdverbs: ModifierDef[]) {
  const noun = state.playerUnit?.noun ?? "RAT";
  const attack = allowedVerbs.find((verb) => verb.role === "Attack");
  const move = allowedVerbs.find((verb) => verb.movementVerb);
  const defense = allowedVerbs.find((verb) => verb.role === "Defense");
  const samples: string[] = [];
  if (move) samples.push(`${noun} ${preferredThird(move)} LEFT`);
  if (move) samples.push(`${noun} ${preferredThird(move)} FORWARD`);
  samples.push(...allowedVerbs.slice(0, 9).map((verb) => `${noun} ${preferredThird(verb)}`));
  if (attack && allowedAdverbs[0]) samples.push(`${noun} ${preferredThird(attack)} ${allowedAdverbs[0].modifier}`);
  if (move) samples.push(`${noun} ${preferredThird(move)} NEAR THE WALL`);
  const jump = allowedVerbs.find((verb) => verb.verb === "JUMP");
  if (jump) samples.push(`${noun} ${preferredThird(jump)} OVER THE ROCK`);
  if (defense) samples.push(`${noun} ${preferredThird(defense)} safely`);
  return Array.from(new Set(samples)).slice(0, 14);
}

export function buildNoun(word: string, index: number): NounDef {
  const tags = buildTags(word);
  const role = ["SHOP", "SCHOOL", "PARK"].includes(word) ? "Place" : (tags.some((tag) => ["ANIMAL", "PET", "FANTASY"].includes(tag)) ? "Creature" : "Object");
  const moveSet = buildMoveSet(word, tags);
  const stats = {
    maxHp: 5 + index % 5,
    attack: 2 + index % 2,
    defense: 1 + index % 3,
    speed: 3 + index % 6,
    maxPp: Math.max(6, moveSet.length ? Math.min(18, moveSet.reduce((total) => total + 1, 0) + 5) : defaultStats.maxPp)
  };
  return {
    canonicalNoun: word,
    synonyms: trueAliasesFor(word),
    role,
    tags,
    baseStats: stats,
    allowedAdjectives: adjectiveDefs.filter((mod) => modifierAllowsTags(mod, tags)).map((mod) => mod.modifier),
    moveSet
  };
}

export function buildTags(noun: string) {
  const tags: string[] = [];
  const add = (tag: string) => { if (!tags.includes(tag)) tags.push(tag); };
  if (["BIRD", "DUCK", "OWL", "QUAIL", "HEN", "BAT"].includes(noun)) { add("ANIMAL"); add("BIRD"); }
  if (["CAT", "DOG", "PUP", "PET"].includes(noun)) { add("ANIMAL"); add("PET"); }
  if (["FISH", "DUCK", "BOAT"].includes(noun)) add("AQUATIC");
  if (noun === "FISH") add("ANIMAL");
  if (["DAD", "KID", "KING", "MAN", "MEN"].includes(noun)) add("PERSON");
  if (["APPLE", "BUN", "EGG", "GRAPES", "JAM", "GUM", "NUT"].includes(noun)) add("FOOD");
  if (["BROOM", "MOP", "PEN", "KEY", "WATCH", "XRAY", "XYLOPHONE"].includes(noun)) add("TOOL");
  if (["BOAT", "BUS", "CAR", "VAN"].includes(noun)) add("VEHICLE");
  if (["BAG", "BIN", "BOX", "CAN", "CUP", "JAR", "JUG", "MUG", "PAN", "POT"].includes(noun)) add("CONTAINER");
  if (["BELL", "DRUM", "XYLOPHONE"].includes(noun)) add("INSTRUMENT");
  if (noun === "BOOK") add("BOOK");
  if (["CAP", "HAT", "SOCK", "WIG"].includes(noun)) add("CLOTHING");
  if (["MOON", "STAR", "SUN"].includes(noun)) add("CELESTIAL");
  if (["TREE", "GRAPES"].includes(noun)) add("PLANT");
  if (["MOON", "RAIN", "STAR", "SUN", "TREE", "LOG", "NEST"].includes(noun)) add("NATURE");
  if (noun === "UNICORN") { add("ANIMAL"); add("FANTASY"); }
  if (["RAT", "FOX", "GOAT", "LION", "OWL", "OX", "PIG", "TIGER", "YAK", "ZEBRA", "COW", "ANT"].includes(noun)) add("ANIMAL");
  if (["BALL", "BOOK", "BOX", "DOOR", "DRUM", "KITE", "RING", "SPOON", "TOY", "YOYO"].includes(noun)) add("OBJECT");
  if (["BED", "BROOM", "DOOR", "KEY", "MAT", "MOP", "RUG", "SPOON", "WALL"].includes(noun)) add("HOUSEHOLD");
  if (tags.length === 0) add("OBJECT");
  return tags;
}

export function buildMoveSet(noun: string, tags: string[]) {
  const moves: MoveSlot[] = [];
  const add = (verbId: string, category: VerbCategory, nounPowerOffset = 0, allowedAdverbs: string[] = tagBasedAdverbs(category)) => {
    if (!moves.some((slot) => slot.verbId === verbId)) moves.push({ verbId, category, nounPowerOffset, allowedAdverbs });
  };
  const has = (tag: string) => tags.includes(tag);
  if (has("ANIMAL") || has("PET") || has("PERSON") || has("FANTASY")) {
    ["WALK", "RUN", "DODGE", "JUMP"].forEach((verb) => add(verb, "Movement"));
    add("LOOK", "Utility");
  }
  if (has("BIRD")) ["FLY", "GLIDE", "LAND", "HOVER"].forEach((verb) => add(verb, "Movement"));
  if (has("BIRD")) { add("PECK", "Attack", 1); add("SING", "Utility"); }
  if (has("AQUATIC")) ["SWIM", "DRIFT", "DIVE", "FLOAT"].forEach((verb) => add(verb, "Movement"));
  if (has("AQUATIC")) add("SPLASH", "Attack");
  if (has("ANIMAL") || has("PET")) ["ATTACK", "BITE", "SCRATCH", "CHARGE"].forEach((verb) => add(verb, "Attack", verb === "SCRATCH" ? 0 : 1));
  if (noun === "ANT") { add("STING", "Attack"); add("CRAWL", "Movement"); add("CLIMB", "Movement"); add("DIG", "Utility"); }
  if (has("PERSON")) ["THROW", "HIDE", "CARRY", "WAVE", "CALL", "SING", "DANCE", "READ", "WRITE", "COUNT", "POINT", "SIT", "REST", "EAT", "DRINK", "ASK", "ANSWER", "EXPLAIN", "DESCRIBE", "LEARN", "THINK", "REMEMBER", "SOLVE", "COMPARE", "MEASURE", "OBSERVE", "NOTICE", "FIND", "DISCOVER", "DRAW", "COLOR", "HELP", "SHARE", "TEACH"].forEach((verb) => add(verb, inferCategory(verb)));
  if (has("TOOL") || has("OBJECT")) { add("DROP", "Attack"); add("SLIDE", "Movement"); }
  if (has("TOOL") || has("HOUSEHOLD")) ["CLEAN", "WIPE", "WASH"].forEach((verb) => add(verb, "Defense"));
  if (has("CONTAINER") || has("BOOK")) ["STACK", "SORT"].forEach((verb) => add(verb, "Utility"));
  if (has("CLOTHING")) add("FOLD", "Utility");
  if (["APPLE", "BALL", "BELL", "BUN", "DOT", "EGG", "MOON", "NUT", "RING", "SUN", "YOYO"].includes(noun)) ["ROLL", "BOUNCE", "SPIN"].forEach((verb) => add(verb, "Movement"));
  if (has("VEHICLE")) ["DRIVE", "TURN", "DRIFT", "ROCK", "HONK", "PARK"].forEach((verb) => add(verb, inferCategory(verb)));
  if (has("FOOD")) { add("ROLL", "Movement"); add("DROP", "Attack"); add("SLIDE", "Movement"); }
  if (has("NATURE")) ["GLOW", "SHAKE", "FALL", "SWAY"].forEach((verb) => add(verb, inferCategory(verb)));
  if (has("FANTASY")) ["HOVER", "GLITTER", "SHINE"].forEach((verb) => add(verb, inferCategory(verb)));
  if (has("CONTAINER")) ["OPEN", "CLOSE", "FILL", "EMPTY", "POUR", "SPILL"].forEach((verb) => add(verb, inferCategory(verb)));
  if (has("INSTRUMENT")) ["RING", "PLAY"].forEach((verb) => add(verb, inferCategory(verb)));
  if (has("BOOK")) ["OPEN", "CLOSE", "READ"].forEach((verb) => add(verb, inferCategory(verb)));
  if (has("PLANT")) ["GROW", "BLOOM"].forEach((verb) => add(verb, "Utility"));
  if (!moves.some((slot) => slot.category === "Attack")) add("BUMP", "Attack");
  if (!moves.some((slot) => slot.category === "Defense")) add("HIDE", "Defense");
  if (!moves.some((slot) => slot.category === "Movement")) add("MOVE", "Movement");
  return moves;
}

export function buildVerbs(): VerbDef[] {
  const all = new Set<string>();
  ["ATTACK", "BITE", "SCRATCH", "PECK", "DROP", "CHARGE", "THROW", "STING", "DIG", "SPLASH", "FALL", "GLARE", "HONK", "BUMP", "PUSH", "RING", "RUN", "WALK", "DODGE", "JUMP", "FLY", "SWIM", "ROLL", "BOUNCE", "DRIFT", "DANCE", "CLIMB", "CRAWL", "GLIDE", "DIVE", "FLOAT", "DRIVE", "TURN", "SPIN", "SLIDE", "SWAY", "HOVER", "PARK", "LAND", "MOVE", "HIDE", "LOOK", "NOTICE", "OBSERVE", "GLOW", "SHINE", "GLITTER", "SHAKE", "OPEN", "CLOSE", "FILL", "EMPTY", "POUR", "SPILL", "PLAY", "READ", "WRITE", "COUNT", "POINT", "SIT", "REST", "ASK", "ANSWER", "EXPLAIN", "DESCRIBE", "LEARN", "THINK", "REMEMBER", "SOLVE", "COMPARE", "MEASURE", "FIND", "DISCOVER", "DRAW", "COLOR", "HELP", "SHARE", "TEACH", "CARRY", "CALL", "SING", "EAT", "DRINK", "CLEAN", "WIPE", "WASH", "STACK", "SORT", "FOLD", "GROW", "BLOOM"].forEach((verb) => all.add(verb));
  return Array.from(all).map((verb) => fallbackVerb(verb, inferCategory(verb)));
}

export function fallbackVerb(verb: string, category: VerbCategory): VerbDef {
  const movementVerb = category === "Movement";
  return {
    verb,
    aliases: [],
    tags: [category === "Attack" ? "OFFENSE" : category.toUpperCase()],
    role: category,
    ppCost: category === "Attack" ? 4 : category === "Movement" ? 3 : 2,
    power: category === "Attack" ? 2 : 0,
    tacticalRangeCells: ["THROW", "GLARE"].includes(verb) ? 3 : ["SPLASH", "POUR", "HONK", "RING"].includes(verb) ? 2 : 1,
    tacticalMovementCells: movementVerb ? (["WALK", "DODGE", "HIDE", "PARK", "LAND"].includes(verb) ? 1 : ["FLY", "GLIDE", "DRIVE"].includes(verb) ? 3 : 2) : 0,
    tacticalDamageMultiplier: ["THROW", "GLARE"].includes(verb) ? 0.55 : ["SPLASH", "POUR", "HONK", "RING"].includes(verb) ? 0.75 : 1,
    cooldownSeconds: 0.5,
    movementVerb,
    third: [thirdPerson(verb)],
    past: [pastTense(verb)],
    progressive: [progressive(verb)]
  };
}

export function inferCategory(verb: string): VerbCategory {
  if (["ATTACK", "BITE", "SCRATCH", "PECK", "DROP", "CHARGE", "THROW", "STING", "DIG", "SPLASH", "FALL", "GLARE", "HONK", "BUMP", "PUSH", "RING", "POUR", "SPILL", "EMPTY"].includes(verb)) return "Attack";
  if (["RUN", "WALK", "DODGE", "JUMP", "FLY", "SWIM", "ROLL", "BOUNCE", "DRIFT", "DANCE", "CLIMB", "CRAWL", "GLIDE", "DIVE", "FLOAT", "DRIVE", "TURN", "SPIN", "SLIDE", "SWAY", "HOVER", "PARK", "LAND", "MOVE"].includes(verb)) return "Movement";
  if (["HIDE", "SIT", "REST", "LEARN", "THINK", "REMEMBER", "HELP", "CLEAN", "WIPE", "WASH"].includes(verb)) return "Defense";
  return "Utility";
}

export function tagBasedAdverbs(category: VerbCategory) {
  switch (category) {
    case "Attack": return ["GENTLY", "HEAVILY", "SHARPLY", "CAREFULLY", "FAST", "LOUDLY", "BRAVELY"];
    case "Defense": return ["SLOWLY", "CAREFULLY", "QUIETLY", "GENTLY", "SMOOTHLY", "SAFELY", "BRAVELY"];
    case "Movement": return ["FAST", "SLOWLY", "CAREFULLY", "SMOOTHLY", "HEAVILY", "EAGERLY", "SAFELY"];
    default: return ["GENTLY", "SLOWLY", "CAREFULLY", "QUIETLY", "SMOOTHLY", "CLEARLY", "EAGERLY"];
  }
}

export function adjective(name: string, multipliers: Partial<Record<"maxHp" | "attack" | "defense" | "speed" | "accuracy" | "power" | "evasion" | "pp", number>>, aliases: string[] = [], tags: string[] = []): ModifierDef {
  return modifier(name, "Adjective", aliases, tags, [], multipliers);
}

export function adverb(name: string, aliases: string[], verbTags: string[], multipliers: Partial<Record<"maxHp" | "attack" | "defense" | "speed" | "accuracy" | "power" | "evasion" | "pp", number>>): ModifierDef {
  return modifier(name, "Adverb", aliases, [], verbTags, multipliers);
}

export function modifier(name: string, role: "Adjective" | "Adverb", aliases: string[], nounTags: string[], verbTags: string[], multipliers: Partial<Record<"maxHp" | "attack" | "defense" | "speed" | "accuracy" | "power" | "evasion" | "pp", number>>): ModifierDef {
  return {
    modifier: name,
    role,
    aliases,
    allowedNounTags: nounTags,
    allowedVerbTags: verbTags,
    maxHpMultiplier: multipliers.maxHp ?? 1,
    attackMultiplier: multipliers.attack ?? 1,
    defenseMultiplier: multipliers.defense ?? 1,
    speedMultiplier: multipliers.speed ?? 1,
    accuracyMultiplier: multipliers.accuracy ?? 1,
    powerMultiplier: multipliers.power ?? 1,
    evasionMultiplier: multipliers.evasion ?? 1,
    ppCostMultiplier: multipliers.pp ?? 1
  };
}

export function nounAllowsVerb(noun: NounDef, verb: string) {
  return noun.moveSet.some((slot) => slot.verbId === normalize(verb));
}

export function nounAllowsAdverb(noun: NounDef, verb: string, adverbName: string) {
  const slot = noun.moveSet.find((move) => move.verbId === normalize(verb));
  return !slot || slot.allowedAdverbs.length === 0 || slot.allowedAdverbs.includes(normalize(adverbName));
}

export function verbAllowsAdverb(verb: VerbDef, adverbMod: ModifierDef) {
  return adverbMod.allowedVerbTags.length === 0 || adverbMod.allowedVerbTags.some((tag) => verb.tags.includes(tag));
}

export function modifierAllowsNoun(modifierDef: ModifierDef, noun: NounDef) {
  return modifierAllowsTags(modifierDef, noun.tags);
}

export function modifierAllowsTags(modifierDef: ModifierDef, tags: string[]) {
  return modifierDef.allowedNounTags.length === 0 || modifierDef.allowedNounTags.some((tag) => tags.includes(tag));
}

export function findNoun(value: string | undefined) {
  const token = normalize(value);
  return nouns.find((noun) => noun.canonicalNoun === token || noun.synonyms.some((synonym) => normalize(synonym) === token)) ?? null;
}

export function findVerb(value: string | undefined) {
  const token = normalize(value);
  return verbs.find((verb) => verb.verb === token) ?? null;
}

export function resolveVerb(value: string | undefined) {
  const token = normalize(value);
  return verbs.find((verb) => verb.verb === token || verb.aliases.includes(token) || verb.third.includes(token) || verb.past.includes(token) || verb.progressive.includes(token)) ?? null;
}

export function findModifier(value: string | undefined, role: "Adjective" | "Adverb") {
  const token = normalize(value);
  return modifiers.find((mod) => mod.role === role && (mod.modifier === token || mod.aliases.includes(token))) ?? null;
}

export function pickEnemyForSummon(noun: NounDef) {
  return findNoun(noun.canonicalNoun) ?? noun;
}

export function acceptedNounForms(noun: NounDef) {
  return [noun.canonicalNoun, ...noun.synonyms.map((synonym) => normalize(synonym))].filter(Boolean);
}

export function trueAliasesFor(canonicalNoun: string) {
  const aliases: Record<string, string[]> = {
    ANT: ["INSECT"],
    APPLE: ["FRUIT"],
    BAG: ["SACK", "POUCH"],
    BIN: ["DUSTBIN", "TRASHCAN"],
    BIRD: ["AVIAN"],
    BOAT: ["SHIP"],
    BOOK: ["VOLUME"],
    BOX: ["CRATE"],
    BROOM: ["BRUSH"],
    BUN: ["ROLL"],
    BUS: ["COACH"],
    CAN: ["TIN"],
    CAR: ["AUTO", "AUTOMOBILE"],
    CAT: ["KITTY", "FELINE", "KITTEN"],
    COW: ["CATTLE"],
    CUP: ["GOBLET"],
    DAD: ["FATHER", "PAPA"],
    DOG: ["MUTT", "POOCH", "CANINE", "HOUND"],
    DOOR: ["GATE"],
    DRUM: ["TOM"],
    FOX: ["VIXEN"],
    ICE: ["FROST"],
    JUG: ["PITCHER"],
    KID: ["CHILD"],
    LEG: ["LIMB"],
    MAN: ["PERSON"],
    MEN: ["PEOPLE"],
    MOON: ["LUNA"],
    MOP: ["SWAB"],
    MUG: ["CUPFUL"],
    PAN: ["SKILLET"],
    PIG: ["HOG", "SWINE"],
    PUP: ["PUPPY"],
    QUILT: ["BLANKET"],
    RAIN: ["SHOWER", "DRIZZLE"],
    RAT: ["MOUSE"],
    RING: ["BAND"],
    SOCK: ["STOCKING"],
    SPOON: ["LADLE"],
    STAR: ["ASTRA"],
    SUN: ["SUNLIGHT"],
    TAP: ["FAUCET"],
    TOY: ["PLAYTHING"],
    TREE: ["PLANT"],
    UMBRELLA: ["BROLLY"],
    VAN: ["MINIVAN"],
    VASE: ["URN"],
    WATCH: ["WRISTWATCH", "TIMEPIECE"],
    WIG: ["HAIRPIECE"],
    XRAY: ["X-RAY"]
  };
  return aliases[normalize(canonicalNoun)] ?? [];
}

export function toTacticalStats(stats: Omit<Stats, "accuracy" | "evasion">): Stats {
  return { ...stats, accuracy: 85, evasion: 10 };
}

export function applyModifierStats(stats: Stats, mod: ModifierDef): Stats {
  return {
    maxHp: Math.max(1, Math.round(stats.maxHp * mod.maxHpMultiplier)),
    attack: Math.max(1, Math.round(stats.attack * mod.attackMultiplier)),
    defense: Math.max(1, Math.round(stats.defense * mod.defenseMultiplier)),
    speed: Math.max(1, Math.round(stats.speed * mod.speedMultiplier)),
    accuracy: stats.accuracy,
    evasion: stats.evasion,
    maxPp: Math.max(1, Math.round(stats.maxPp / Math.max(0.05, mod.ppCostMultiplier)))
  };
}

export function resolveCooldownSeconds(category: VerbCategory, power: number, rangeCells: number, movementCells: number, unitSpeed: number, actionSpeed: number, verbCooldown: number) {
  let cooldown = Math.max(0.25, verbCooldown);
  if (category === "Attack") cooldown += 0.55 + power * 0.08 + Math.max(0, rangeCells - 1) * 0.18;
  else if (category === "Defense") cooldown += 0.35;
  else if (category === "Movement") cooldown += 0.2 + Math.max(0, movementCells - 1) * 0.08;
  else cooldown += 0.25;
  const speedRatio = clamp(unitSpeed / Math.max(1, actionSpeed), 0.65, 1.45);
  return clamp(cooldown * speedRatio, 0.35, 3);
}

export function resolveAdverbRangeBonus(mod: ModifierDef | null) {
  return mod && ["CAREFULLY", "CLOSELY"].includes(mod.modifier) ? 1 : 0;
}
