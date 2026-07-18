import { ArrowLeft, Mic, MicOff, Pause, Play, RotateCcw, Swords, Wand2 } from "lucide-react";
import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import {
  advanceBattleTime,
  attackPreviewCells,
  availableSummons,
  createInitialBattle,
  executeCommand,
  enemyThreatCells,
  enemyPaceSummary,
  previewCommand,
  setEnemyPace as setBattleEnemyPace,
  startBattle,
  turnPlayer,
  type ActionProfile,
  type BattleSnapshot,
  type CellType,
  type Curse,
  type EnemyPace,
  type Position,
  type VerbCategory
} from "./tacticalCombatEngine";

const curseOptions: Curse[] = ["None", "I", "You", "HeSheIt", "They", "PastFog", "NowMist"];
const enemyPaceOptions: EnemyPace[] = ["Beginner", "Standard", "Advanced"];

interface BrowserSpeechRecognitionResult {
  readonly isFinal: boolean;
  readonly 0: { readonly transcript: string };
}

interface BrowserSpeechRecognitionEvent {
  readonly results: { readonly length: number; readonly [index: number]: BrowserSpeechRecognitionResult };
}

interface BrowserSpeechRecognition {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  maxAlternatives: number;
  onstart: (() => void) | null;
  onresult: ((event: BrowserSpeechRecognitionEvent) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start: () => void;
  stop: () => void;
  abort: () => void;
}

type BrowserSpeechRecognitionConstructor = new () => BrowserSpeechRecognition;

function speechRecognitionConstructor(): BrowserSpeechRecognitionConstructor | undefined {
  if (typeof window === "undefined") return undefined;
  const speechWindow = window as typeof window & {
    SpeechRecognition?: BrowserSpeechRecognitionConstructor;
    webkitSpeechRecognition?: BrowserSpeechRecognitionConstructor;
  };
  return speechWindow.SpeechRecognition ?? speechWindow.webkitSpeechRecognition;
}

export default function TacticalCombatVisualizer() {
  const [summonPhrase, setSummonPhrase] = useState("brave rat");
  const [command, setCommand] = useState("rat walks left");
  const [curse, setCurse] = useState<Curse>("None");
  const [enemyPace, setEnemyPace] = useState<EnemyPace>("Beginner");
  const [paused, setPaused] = useState(false);
  const [isListening, setIsListening] = useState(false);
  const [speechStatus, setSpeechStatus] = useState("");
  const [battle, setBattle] = useState<BattleSnapshot>(() => createInitialBattle());
  const recognitionRef = useRef<BrowserSpeechRecognition | null>(null);
  const speechAvailable = Boolean(speechRecognitionConstructor());
  const suggestions = useMemo(() => availableSummons(), []);
  const commandPreview = useMemo(() => previewCommand(battle, command, curse), [battle, command, curse]);
  const paceSummary = useMemo(() => enemyPaceSummary(battle), [battle]);
  const player = battle.state.playerUnit;
  const enemy = battle.state.enemyUnit;

  useEffect(() => {
    if (paused) return;
    let lastTick = performance.now();
    const timer = window.setInterval(() => {
      const now = performance.now();
      const deltaSeconds = Math.min(0.5, (now - lastTick) / 1000);
      lastTick = now;
      setBattle((current) => advanceBattleTime(current, deltaSeconds));
    }, 160);
    return () => window.clearInterval(timer);
  }, [paused]);

  useEffect(() => () => {
    try {
      recognitionRef.current?.abort();
    } catch {
      // Some embedded Chromium builds expose SpeechRecognition but throw on teardown.
    }
  }, []);

  const begin = (event?: FormEvent) => {
    event?.preventDefault();
    const next = startBattle(summonPhrase, enemyPace);
    setBattle(next);
    setCommand(next.sampleCommands[0]?.toLowerCase() ?? "");
  };

  const submitCommand = (event?: FormEvent) => {
    event?.preventDefault();
    if (!command.trim()) return;
    setBattle((current) => executeCommand(current, command, curse));
  };

  const listenForCommand = () => {
    const Recognition = speechRecognitionConstructor();
    if (!Recognition) {
      setSpeechStatus("Speech input is unavailable in this browser. Typing still works.");
      return;
    }

    setIsListening(true);
    setSpeechStatus("Opening microphone…");
    const previousRecognition = recognitionRef.current;
    recognitionRef.current = null;
    try {
      previousRecognition?.abort();
    } catch {
      // A failed prior recognizer must never block the next push-to-talk attempt.
    }
    let recognition: BrowserSpeechRecognition;
    try {
      recognition = new Recognition();
    } catch {
      setIsListening(false);
      setSpeechStatus("This browser exposes speech recognition but could not initialize it. Typing still works.");
      return;
    }
    recognitionRef.current = recognition;
    let recognitionStarted = false;
    const startupTimer = window.setTimeout(() => {
      if (recognitionStarted || recognitionRef.current !== recognition) return;
      recognition.abort();
      recognitionRef.current = null;
      setIsListening(false);
      setSpeechStatus("The browser did not open the microphone. Check microphone permission, then try again.");
    }, 4000);
    recognition.lang = "en-IN";
    recognition.continuous = false;
    recognition.interimResults = true;
    recognition.maxAlternatives = 1;
    recognition.onstart = () => {
      recognitionStarted = true;
      window.clearTimeout(startupTimer);
      setIsListening(true);
      setSpeechStatus("Listening… say the complete combat command.");
    };
    recognition.onresult = (event) => {
      let transcript = "";
      for (let index = 0; index < event.results.length; index += 1) {
        if (event.results[index].isFinal)
          transcript += `${event.results[index][0].transcript} `;
      }
      const spokenCommand = transcript.trim().toLowerCase();
      if (!spokenCommand) return;

      setCommand(spokenCommand);
      setBattle((current) => executeCommand(current, spokenCommand, curse));
      setSpeechStatus(`Resolved: “${spokenCommand}”`);
      recognition.stop();
    };
    recognition.onerror = (event) => {
      window.clearTimeout(startupTimer);
      const message = event.error === "not-allowed"
        ? "Microphone permission was denied. Allow it for localhost and try again."
        : event.error === "no-speech"
          ? "No speech was heard. Try again and speak after Listening appears."
          : `Speech input failed (${event.error}). Typing still works.`;
      setSpeechStatus(message);
    };
    recognition.onend = () => {
      window.clearTimeout(startupTimer);
      setIsListening(false);
      if (recognitionRef.current === recognition) recognitionRef.current = null;
    };

    try {
      recognition.start();
    } catch {
      window.clearTimeout(startupTimer);
      setIsListening(false);
      setSpeechStatus("Speech input could not start. Wait a moment and try again.");
    }
  };

  const useSample = (sample: string) => {
    setCommand(sample.toLowerCase());
    setBattle((current) => executeCommand(current, sample, curse));
  };

  const faceCell = (position: Position) => {
    setBattle((current) => turnPlayer(current, position));
  };

  const changeEnemyPace = (pace: EnemyPace) => {
    setEnemyPace(pace);
    setBattle((current) => setBattleEnemyPace(current, pace));
  };

  return (
    <main className="combatLabShell">
      <section className="combatLabTopbar">
        <a className="combatBackLink" href="/">
          <ArrowLeft size={18} />
          Portal
        </a>
        <div>
          <p className="eyebrow">Local Battle Lab</p>
          <h1>Tactical Grammar Combat Visualizer</h1>
        </div>
        <button className="secondaryButton combatResetButton" onClick={() => begin()}>
          <RotateCcw size={17} />
          Reset
        </button>
      </section>

      <section className="combatLabLayout">
        <aside className="combatPanel combatSetupPanel">
          <form onSubmit={begin} className="combatForm">
            <label>
              Summon phrase
              <input
                list="combat-summons"
                value={summonPhrase}
                onChange={(event) => setSummonPhrase(event.target.value)}
                placeholder="brave rat"
              />
            </label>
            <datalist id="combat-summons">
              {suggestions.map((suggestion) => <option key={suggestion} value={suggestion.toLowerCase()} />)}
            </datalist>
            <button className="primaryButton">
              <Wand2 size={18} />
              Start Battle
            </button>
          </form>

          <div className="combatStatGrid">
            {player && <UnitCard title="Player" name={player.displayPhrase} hp={player.currentHp} maxHp={player.stats.maxHp} pp={player.currentPp} maxPp={player.stats.maxPp} stats={player.stats} />}
            <UnitCard title="Enemy" name={enemy.displayPhrase} hp={enemy.currentHp} maxHp={enemy.stats.maxHp} pp={enemy.currentPp} maxPp={enemy.stats.maxPp} stats={enemy.stats} />
          </div>

          <div className="combatMetaBox">
            <h2>Battle Rules Used</h2>
            {battle.grammarNotes.map((note) => <p key={note}>{note}</p>)}
          </div>
        </aside>

        <section className="combatBoardPanel">
          <div className="combatRealtimeStrip">
            <span className="combatLiveBadge"><b /> {paused ? "Paused" : "Live"}</span>
            <span>{battle.state.clock.toFixed(1)}s</span>
            <span>Facing {player ? directionLabel(player.position, battle.state.playerFacing) : "-"}</span>
            <span>Curse: {activeCurseStatus(battle)}</span>
            <button className="secondaryButton" type="button" onClick={() => setPaused((value) => !value)}>
              {paused ? <Play size={15} /> : <Pause size={15} />}
              {paused ? "Resume" : "Pause"}
            </button>
          </div>
          <div className="combatEnemyPacePanel">
            <label>
              Enemy pace
              <select value={enemyPace} onChange={(event) => changeEnemyPace(event.target.value as EnemyPace)}>
                {enemyPaceOptions.map((option) => <option key={option}>{option}</option>)}
              </select>
            </label>
            <div className="combatEnemyPaceReadout">
              <div className="combatEnemyPaceHeader">
                <strong>{paceSummary.label}</strong>
                <span>~{paceSummary.decisionIntervalSeconds.toFixed(1)}s decisions · ~{paceSummary.attackWindowSeconds.toFixed(1)}s reaction</span>
              </div>
              <div className="combatEnemyPaceGauge" role="meter" aria-label="Enemy combat pace" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(paceSummary.normalized * 100)}>
                <span style={{ width: `${Math.max(8, paceSummary.normalized * 100)}%` }} />
              </div>
            </div>
          </div>
          <Board
            battle={battle}
            previewProfile={commandPreview.profile}
            movementPath={commandPreview.movementPath}
            movementDestination={commandPreview.destination}
            onFaceCell={faceCell}
          />
          <form className="combatCommandBar" onSubmit={submitCommand}>
            <label>
              Typed command
              <input value={command} onChange={(event) => setCommand(event.target.value)} placeholder="rat bites" />
            </label>
            <label>
              Debug curse override
              <select value={curse} onChange={(event) => setCurse(event.target.value as Curse)}>
                {curseOptions.map((option) => <option key={option} value={option}>{option === "None" ? "Enemy-controlled" : curseLabel(option)}</option>)}
              </select>
            </label>
            <div className="combatSpeechControl">
              <button className="secondaryButton" type="button" onClick={listenForCommand} disabled={!speechAvailable || isListening}>
                {speechAvailable ? <Mic size={18} /> : <MicOff size={18} />}
                {isListening ? "Listening…" : "Speak & resolve"}
              </button>
              <small aria-live="polite">
                {speechStatus || (speechAvailable ? "Uses this Windows browser's speech recognition (English–India)." : "Speech recognition is unavailable here.")}
              </small>
            </div>
            <button className="primaryButton">
              <Swords size={18} />
              Resolve
            </button>
          </form>
          <div className={`combatPreview ${commandPreview.ok ? "" : "isInvalid"}`}>
            {commandPreview.ok && commandPreview.profile
              ? `Preview: ${commandPreview.profile.verb}${commandPreview.profile.adverb ? ` ${commandPreview.profile.adverb}` : ""} · ${commandPreview.profile.category} · range ${commandPreview.profile.rangeCells} · move ${commandPreview.profile.movementCells} · PP ${commandPreview.profile.ppCost}${commandPreview.destination ? ` · lands (${commandPreview.destination.x}, ${commandPreview.destination.y})` : ""}`
              : commandPreview.message}
          </div>

          {battle.state.pendingEnemyAttack && (
            <div className="combatIncoming">
              Incoming attack: {battle.state.pendingEnemyAttack.damage} damage, speed {battle.state.pendingEnemyAttack.attackSpeed.toFixed(1)}, lands at {battle.state.pendingEnemyAttack.hitsAt.toFixed(1)}s.
            </div>
          )}
        </section>

        <aside className="combatPanel combatMovePanel">
          <section>
            <h2>Permissible Verbs</h2>
            <div className="moveList">
              {battle.allowedVerbs.map((verb) => (
                <button key={verb.verb} className={`moveChip ${categoryClass(verb.role)}`} onClick={() => setCommand(`${player?.noun.toLowerCase() ?? "rat"} ${verb.third[0].toLowerCase()}`)}>
                  <span>{verb.verb}</span>
                  <small>{verb.role} · PP {verb.ppCost} · R{verb.tacticalRangeCells}</small>
                </button>
              ))}
            </div>
          </section>

          <section>
            <h2>Try These</h2>
            <div className="sampleList">
              {battle.sampleCommands.map((sample) => (
                <button key={sample} onClick={() => useSample(sample)}>{sample}</button>
              ))}
            </div>
          </section>

          <section>
            <h2>Log</h2>
            <div className="combatLog">
              {battle.state.log.map((entry) => <p key={entry}>{entry}</p>)}
            </div>
          </section>
        </aside>
      </section>
    </main>
  );
}

function Board(props: {
  battle: BattleSnapshot;
  previewProfile?: ActionProfile;
  movementPath?: Position[];
  movementDestination?: Position;
  onFaceCell: (position: Position) => void;
}) {
  const { state } = props.battle;
  const player = state.playerUnit;
  const attackCells = attackPreviewCells(props.battle, props.previewProfile);
  const enemyArc = enemyThreatCells(props.battle);

  return (
    <div className="combatBoardWrap">
      <div className="combatBoard" aria-label="5 by 5 tactical hex battle grid">
        {Array.from({ length: state.height }).map((_, rowIndex) => {
          const y = state.height - 1 - rowIndex;
          return (
            <div key={`row:${y}`} className={`combatBoardRow ${(y & 1) !== 0 ? "isOddHexRow" : ""}`}>
              {Array.from({ length: state.width }).map((_, x) => {
                const position = { x, y };
                const terrain = state.terrain[x][y];
                const hasPlayer = Boolean(player && player.position.x === x && player.position.y === y);
                const hasEnemy = state.enemyUnit.position.x === x && state.enemyUnit.position.y === y;
                const inPlayerArc = attackCells.some((cell) => cell.x === x && cell.y === y);
                const inEnemyArc = enemyArc.some((cell) => cell.x === x && cell.y === y);
                const inMovementPath = Boolean(props.movementPath?.some((cell) => cell.x === x && cell.y === y));
                const isMovementDestination = Boolean(props.movementDestination && props.movementDestination.x === x && props.movementDestination.y === y);
                const isAimCell = Boolean(state.selectedAimCell && state.selectedAimCell.x === x && state.selectedAimCell.y === y);
                return (
                  <button
                    key={`${x}:${y}`}
                    className={[
                      "combatCell",
                      terrainClass(terrain),
                      inPlayerArc ? "inPlayerArc" : "",
                      inEnemyArc ? "inEnemyArc" : "",
                      inMovementPath ? "inMovementPath" : "",
                      isMovementDestination ? "isMovementDestination" : "",
                      isAimCell ? "isAimCell" : "",
                      hasPlayer ? "hasPlayer" : "",
                      hasEnemy ? "hasEnemy" : ""
                    ].filter(Boolean).join(" ")}
                    onClick={() => props.onFaceCell(position)}
                    title={`(${x}, ${y}) ${terrain}`}
                  >
                    <span className="cellCoords">{x},{y}</span>
                    {hasPlayer && <span className="unitToken playerToken">P{directionGlyph(player!.position, state.playerFacing)}</span>}
                    {hasEnemy && <span className="unitToken enemyToken">E{directionGlyph(state.enemyUnit.position, state.enemyFacing)}</span>}
                    {isAimCell && !hasPlayer && !hasEnemy && <span className="aimToken">◎</span>}
                    {!hasPlayer && !hasEnemy && terrain !== "Empty" && <span className="terrainToken">{terrainGlyph(terrain)}</span>}
                  </button>
                );
              })}
            </div>
          );
        })}
      </div>
      <div className="combatLegend">
        <span><b className="legendSwatch playerSwatch" /> Attack cone</span>
        <span><b className="legendSwatch movementSwatch" /> Movement route</span>
        <span><b className="legendSwatch enemySwatch" /> Enemy arc</span>
        <span><b className="legendSwatch blockSwatch" /> Blocks line</span>
        <span><b className="legendSwatch hazardSwatch" /> Hazard</span>
      </div>
    </div>
  );
}

function UnitCard(props: {
  title: string;
  name: string;
  hp: number;
  maxHp: number;
  pp: number;
  maxPp: number;
  stats: { attack: number; defense: number; speed: number };
}) {
  return (
    <article className="unitCard">
      <p className="eyebrow">{props.title}</p>
      <h2>{props.name}</h2>
      <div className="meterLine"><span>HP</span><Meter value={props.hp} max={props.maxHp} /></div>
      <div className="meterLine"><span>PP</span><Meter value={props.pp} max={props.maxPp} /></div>
      <div className="unitStats">
        <span>ATK {props.stats.attack}</span>
        <span>DEF {props.stats.defense}</span>
        <span>SPD {props.stats.speed}</span>
      </div>
    </article>
  );
}

function Meter(props: { value: number; max: number }) {
  const percent = Math.max(0, Math.min(100, (props.value / Math.max(1, props.max)) * 100));
  return (
    <span className="meter">
      <span style={{ width: `${percent}%` }} />
      <em>{props.value}/{props.max}</em>
    </span>
  );
}

interface CubePosition {
  x: number;
  y: number;
  z: number;
}

function offsetDeltaToCubeDirection(origin: Position, offsetDelta: Position): CubePosition {
  const adjacent = offsetDelta.x === 0 && offsetDelta.y === 0
    ? { x: origin.x + 1, y: origin.y }
    : { x: origin.x + offsetDelta.x, y: origin.y + offsetDelta.y };
  const delta = cubeSub(toCube(adjacent), toCube(origin));
  if (delta.x === 0 && delta.y === 0 && delta.z === 0) return { x: 1, y: -1, z: 0 };
  return delta;
}

function toCube(position: Position): CubePosition {
  const x = position.x - Math.floor((position.y - (position.y & 1)) / 2);
  const z = position.y;
  const y = -x - z;
  return { x, y, z };
}

function cubeSub(a: CubePosition, b: CubePosition): CubePosition {
  return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z };
}

function curseLabel(curse: Curse) {
  switch (curse) {
    case "I": return "Pronoun: I";
    case "You": return "Pronoun: you";
    case "HeSheIt": return "Pronoun: he/she/it";
    case "They": return "Pronoun: they";
    case "PastFog": return "Past Fog";
    case "NowMist": return "Now Mist";
    default: return "No active curse";
  }
}

function activeCurseStatus(battle: BattleSnapshot) {
  const active = battle.state.activeCurse;
  if (active === "None") return curseLabel(active);
  if (active === "PastFog" || active === "NowMist") return `${curseLabel(active)} · until battle ends`;
  return `${curseLabel(active)} · ${Math.max(0, battle.state.curseExpiresAt - battle.state.clock).toFixed(0)}s`;
}

function categoryClass(category: VerbCategory) {
  return {
    Attack: "attackMove",
    Movement: "movementMove",
    Defense: "defenseMove",
    Utility: "utilityMove"
  }[category];
}

function terrainClass(terrain: CellType) {
  return terrain === "Empty" ? "" : `terrain${terrain}`;
}

function terrainGlyph(terrain: CellType) {
  return {
    Box: "B",
    Spikes: "^",
    Wall: "W",
    Roof: "R",
    Bridge: "=",
    Water: "~",
    Tree: "T",
    Rock: "O",
    Empty: ""
  }[terrain];
}

function directionGlyph(origin: Position, direction: Position) {
  return {
    east: "→",
    "north-east": "↗",
    "north-west": "↖",
    west: "←",
    "south-west": "↙",
    "south-east": "↘"
  }[directionLabel(origin, direction)] ?? "";
}

function directionLabel(origin: Position, direction: Position) {
  const facingCube = offsetDeltaToCubeDirection(origin, { x: Math.sign(direction.x), y: Math.sign(direction.y) });
  const directions: CubePosition[] = [
    { x: 1, y: -1, z: 0 }, { x: 0, y: -1, z: 1 }, { x: -1, y: 0, z: 1 },
    { x: -1, y: 1, z: 0 }, { x: 0, y: 1, z: -1 }, { x: 1, y: 0, z: -1 }
  ];
  const index = Math.max(0, directions.findIndex((candidate) => candidate.x === facingCube.x && candidate.y === facingCube.y && candidate.z === facingCube.z));
  return ["east", "north-east", "north-west", "west", "south-west", "south-east"][index];
}
