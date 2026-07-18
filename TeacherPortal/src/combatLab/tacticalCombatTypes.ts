export type CellType = "Empty" | "Box" | "Spikes" | "Wall" | "Roof" | "Bridge" | "Water" | "Tree" | "Rock";
export type VerbCategory = "Attack" | "Movement" | "Defense" | "Utility";
export type Curse = "None" | "I" | "You" | "HeSheIt" | "They" | "PastFog" | "NowMist";
export type EnemyPace = "Beginner" | "Standard" | "Advanced";

export interface Position {
  x: number;
  y: number;
}

export interface Stats {
  maxHp: number;
  attack: number;
  defense: number;
  speed: number;
  accuracy: number;
  evasion: number;
  maxPp: number;
}

export interface Unit {
  displayPhrase: string;
  noun: string;
  determiner: string;
  adjective: string;
  stats: Stats;
  currentHp: number;
  currentPp: number;
  position: Position;
  cooldowns: Record<string, number>;
}

export interface MoveSlot {
  verbId: string;
  category: VerbCategory;
  nounPowerOffset: number;
  allowedAdverbs: string[];
}

export interface NounDef {
  canonicalNoun: string;
  synonyms: string[];
  role: "Creature" | "Object" | "Place";
  tags: string[];
  baseStats: Omit<Stats, "accuracy" | "evasion">;
  allowedAdjectives: string[];
  moveSet: MoveSlot[];
}

export interface VerbDef {
  verb: string;
  aliases: string[];
  tags: string[];
  role: VerbCategory;
  ppCost: number;
  power: number;
  tacticalRangeCells: number;
  tacticalMovementCells: number;
  tacticalDamageMultiplier: number;
  cooldownSeconds: number;
  movementVerb: boolean;
  third: string[];
  past: string[];
  progressive: string[];
}

export interface ModifierDef {
  modifier: string;
  role: "Adjective" | "Adverb";
  aliases: string[];
  allowedNounTags: string[];
  allowedVerbTags: string[];
  maxHpMultiplier: number;
  attackMultiplier: number;
  defenseMultiplier: number;
  speedMultiplier: number;
  powerMultiplier: number;
  accuracyMultiplier: number;
  evasionMultiplier: number;
  ppCostMultiplier: number;
}

export interface ActionProfile {
  verb: string;
  adverb: string;
  preposition: string;
  direction: string;
  category: VerbCategory;
  power: number;
  speedScore: number;
  actionSpeed: number;
  ppCost: number;
  rangeCells: number;
  movementCells: number;
  shieldAmount: number;
  shieldDurationSeconds: number;
  cooldownSeconds: number;
  damageMultiplier: number;
  movementAction: boolean;
}

export interface BattleState {
  width: number;
  height: number;
  terrain: CellType[][];
  playerUnit: Unit | null;
  enemyUnit: Unit;
  playerFacing: Position;
  enemyFacing: Position;
  selectedAimCell: Position | null;
  activeShield: number;
  shieldExpiresAt: number;
  pendingEnemyAttack: PendingEnemyAttack | null;
  activeCurse: Curse;
  curseExpiresAt: number;
  nextEnemyDecisionAt: number;
  enemyDecisionCount: number;
  enemyPace: EnemyPace;
  clock: number;
  log: string[];
}

export interface PendingEnemyAttack {
  damage: number;
  attackSpeed: number;
  hitsAt: number;
}

export interface CommandResult {
  accepted: boolean;
  message: string;
  profile?: ActionProfile;
  damage: number;
  transformedPhrase?: string;
}

export interface BattleSnapshot {
  state: BattleState;
  noun: NounDef | null;
  enemy: NounDef | null;
  allowedVerbs: VerbDef[];
  allowedAdverbs: ModifierDef[];
  sampleCommands: string[];
  grammarNotes: string[];
}

export interface CommandPreview {
  ok: boolean;
  message: string;
  profile?: ActionProfile;
  movementPath?: Position[];
  destination?: Position;
  transformedPhrase?: string;
}

export interface EnemyPaceSummary {
  label: string;
  normalized: number;
  decisionIntervalSeconds: number;
  attackWindowSeconds: number;
}

export interface ParsedAction {
  subjectNoun: string;
  verb: VerbDef;
  adverb: ModifierDef | null;
  direction: string;
  preposition: string;
  objectToken: string;
  clock: number;
}

export interface MovementPlan {
  ok: boolean;
  message: string;
  path: Position[];
  destination?: Position;
  facing?: Position;
}

export interface CubePosition {
  x: number;
  y: number;
  z: number;
}
