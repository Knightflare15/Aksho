import type { BattleState, CellType, CubePosition, Position, Unit } from "./tacticalCombatTypes";
import { normalize } from "./tacticalCombatUtils";

export function emptyTerrain(): CellType[][] {
  return Array.from({ length: 5 }, () => Array.from({ length: 5 }, () => "Empty" as CellType));
}

export function terrainToken(cell: CellType) {
  return cell === "Empty" ? "" : cell.toUpperCase();
}

export function findTerrain(state: BattleState, objectToken: string) {
  const token = normalize(objectToken);
  for (let x = 0; x < state.width; x++) {
    for (let y = 0; y < state.height; y++) {
      if (terrainToken(state.terrain[x][y]) === token) return { x, y };
    }
  }
  return null;
}

export function resolvePrepositionTarget(state: BattleState, objectToken: string) {
  return findTerrain(state, objectToken) ?? state.enemyUnit.position;
}

export function isPreposition(token: string) {
  return ["BESIDE", "NEXT", "OVER", "UNDER", "AROUND", "BEHIND", "NEAR", "ACROSS", "INTO", "THROUGH", "TOWARD", "TOWARDS", "AWAY"].includes(normalize(token));
}

export function canonicalPreposition(token: string) {
  const normalized = normalize(token);
  if (normalized === "NEXT") return "BESIDE";
  if (normalized === "TOWARDS") return "TOWARD";
  return normalized;
}

export function isRelativeDirection(token: string) {
  return ["FORWARD", "FORWARDS", "AHEAD", "STRAIGHT", "BACK", "BACKWARD", "BACKWARDS", "LEFT", "RIGHT"].includes(normalize(token));
}

export function canonicalDirection(token: string) {
  const normalized = normalize(token);
  if (["FORWARDS", "AHEAD", "STRAIGHT"].includes(normalized)) return "FORWARD";
  if (["BACK", "BACKWARDS"].includes(normalized)) return "BACKWARD";
  return normalized;
}

export function canEnemyAttackPlayer(state: BattleState) {
  return Boolean(state.playerUnit) && hexDistance(state.enemyUnit.position, state.playerUnit!.position) === 1 && isInFacingArc(state.enemyUnit.position, state.playerUnit!.position, state.enemyFacing);
}

export function faceEnemyTowardPlayer(state: BattleState) {
  if (state.playerUnit) state.enemyFacing = directionTo(state.enemyUnit.position, state.playerUnit.position);
}

export function isPassableForUnit(state: BattleState, position: Position, unit: Unit) {
  if (!inside(state, position) || isBlocked(state, position) || isHazard(state, position)) return false;
  if (state.playerUnit && state.playerUnit !== unit && same(state.playerUnit.position, position)) return false;
  if (state.enemyUnit !== unit && same(state.enemyUnit.position, position)) return false;
  return true;
}

export function isPassableForPreposition(state: BattleState, position: Position, allowHazard: boolean) {
  if (!inside(state, position) || isBlocked(state, position) || occupied(state, position)) return false;
  return allowHazard || !isHazard(state, position);
}

export function cellAt(state: BattleState, position: Position): CellType {
  return inside(state, position) ? state.terrain[position.x][position.y] : "Wall";
}

export function isBlocked(state: BattleState, position: Position) {
  return ["Box", "Wall", "Tree", "Rock"].includes(cellAt(state, position));
}

export function isHazard(state: BattleState, position: Position) {
  return ["Spikes", "Water"].includes(cellAt(state, position));
}

export function occupied(state: BattleState, position: Position) {
  return Boolean((state.playerUnit && same(state.playerUnit.position, position)) || same(state.enemyUnit.position, position));
}

export function inside(state: BattleState, position: Position) {
  return position.x >= 0 && position.x < state.width && position.y >= 0 && position.y < state.height;
}

export function neighbors(position: Position) {
  const odd = (position.y & 1) !== 0;
  return [
    { x: position.x + 1, y: position.y },
    { x: position.x - 1, y: position.y },
    { x: position.x + (odd ? 1 : 0), y: position.y + 1 },
    { x: position.x + (odd ? 0 : -1), y: position.y + 1 },
    { x: position.x + (odd ? 1 : 0), y: position.y - 1 },
    { x: position.x + (odd ? 0 : -1), y: position.y - 1 }
  ];
}

export function hasClearLine(state: BattleState, from: Position, to: Position): { ok: boolean; blocker?: CellType; position?: Position } {
  const direction = tryGetHexLineDirection(from, to);
  if (!direction) return { ok: true };
  let currentCube = cubeAdd(toCube(from), direction);
  let current = fromCube(currentCube);
  while (!same(current, to)) {
    if (isBlocked(state, current) || isHazard(state, current)) return { ok: false, blocker: cellAt(state, current), position: current };
    currentCube = cubeAdd(currentCube, direction);
    current = fromCube(currentCube);
  }
  return { ok: true };
}

export function isInFacingArc(from: Position, to: Position, facing: Position) {
  const fromCube = toCube(from);
  const toCubePosition = toCube(to);
  const facingCube = offsetDeltaToCubeDirection(from, normalizeDirection(facing));
  const vector = cubeSub(toCubePosition, fromCube);
  if (vector.x === 0 && vector.y === 0 && vector.z === 0) return false;
  const dot = vector.x * facingCube.x + vector.y * facingCube.y + vector.z * facingCube.z;
  return dot > 0;
}

export function directionTo(from: Position, to: Position) {
  return normalizeDirection(hexDirectionToOffsetDelta(from, to));
}

export function normalizeDirection(direction: Position) {
  return { x: Math.sign(direction.x), y: Math.sign(direction.y) };
}

const hexDirections: CubePosition[] = [
  { x: 1, y: -1, z: 0 },
  { x: 0, y: -1, z: 1 },
  { x: -1, y: 0, z: 1 },
  { x: -1, y: 1, z: 0 },
  { x: 0, y: 1, z: -1 },
  { x: 1, y: 0, z: -1 }
];

export function relativeCubeDirection(origin: Position, facing: Position, relativeDirection: string): CubePosition {
  const current = offsetDeltaToCubeDirection(origin, normalizeDirection(facing));
  const currentIndex = Math.max(0, hexDirections.findIndex((direction) => sameCube(direction, current)));
  const turn = relativeDirection === "LEFT" ? 1 : relativeDirection === "RIGHT" ? -1 : relativeDirection === "BACKWARD" ? 3 : 0;
  return hexDirections[(currentIndex + turn + hexDirections.length) % hexDirections.length];
}

export function relativeFacing(origin: Position, facing: Position, relativeDirection: string): Position {
  const adjacent = fromCube(cubeAdd(toCube(origin), relativeCubeDirection(origin, facing, relativeDirection)));
  return { x: adjacent.x - origin.x, y: adjacent.y - origin.y };
}

export function directionName(origin: Position, facing: Position) {
  const current = offsetDeltaToCubeDirection(origin, normalizeDirection(facing));
  const index = Math.max(0, hexDirections.findIndex((direction) => sameCube(direction, current)));
  return ["east", "north-east", "north-west", "west", "south-west", "south-east"][index];
}

export function sameCube(a: CubePosition, b: CubePosition) {
  return a.x === b.x && a.y === b.y && a.z === b.z;
}

export function hexDistance(a: Position, b: Position) {
  const ac = toCube(a);
  const bc = toCube(b);
  return Math.max(Math.abs(ac.x - bc.x), Math.abs(ac.y - bc.y), Math.abs(ac.z - bc.z));
}

export function tryGetHexLineDirection(from: Position, to: Position): CubePosition | null {
  const start = toCube(from);
  const end = toCube(to);
  const delta = cubeSub(end, start);
  const distance = hexDistance(from, to);
  if (distance <= 0) return null;
  const straight =
    (delta.x === 0 && delta.y === -delta.z) ||
    (delta.y === 0 && delta.x === -delta.z) ||
    (delta.z === 0 && delta.x === -delta.y);
  if (!straight) return null;
  return { x: delta.x / distance, y: delta.y / distance, z: delta.z / distance };
}

export function hexDirectionToOffsetDelta(from: Position, to: Position): Position {
  const start = toCube(from);
  const direction = hexDirectionFromTo(from, to);
  const adjacent = fromCube(cubeAdd(start, direction));
  return { x: adjacent.x - from.x, y: adjacent.y - from.y };
}

export function hexDirectionFromTo(from: Position, to: Position): CubePosition {
  const start = toCube(from);
  const end = toCube(to);
  const distance = hexDistance(from, to);
  if (distance <= 0) return { x: 0, y: 0, z: 0 };
  const delta = cubeSub(end, start);
  return roundCube(delta.x / distance, delta.y / distance, delta.z / distance);
}

export function offsetDeltaToCubeDirection(origin: Position, offsetDelta: Position): CubePosition {
  const adjacent = offsetDelta.x === 0 && offsetDelta.y === 0
    ? { x: origin.x + 1, y: origin.y }
    : { x: origin.x + offsetDelta.x, y: origin.y + offsetDelta.y };
  const delta = cubeSub(toCube(adjacent), toCube(origin));
  if (delta.x === 0 && delta.y === 0 && delta.z === 0) return { x: 1, y: -1, z: 0 };
  return delta;
}

export function toCube(position: Position): CubePosition {
  const x = position.x - Math.floor((position.y - (position.y & 1)) / 2);
  const z = position.y;
  const y = -x - z;
  return { x, y, z };
}

export function fromCube(cube: CubePosition): Position {
  return {
    x: cube.x + Math.floor((cube.z - (cube.z & 1)) / 2),
    y: cube.z
  };
}

export function cubeAdd(a: CubePosition, b: CubePosition): CubePosition {
  return { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z };
}

export function cubeSub(a: CubePosition, b: CubePosition): CubePosition {
  return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z };
}

export function cubeScale(cube: CubePosition, scale: number): CubePosition {
  return { x: cube.x * scale, y: cube.y * scale, z: cube.z * scale };
}

export function roundCube(x: number, y: number, z: number): CubePosition {
  let rx = Math.round(x);
  let ry = Math.round(y);
  let rz = Math.round(z);
  const xDiff = Math.abs(rx - x);
  const yDiff = Math.abs(ry - y);
  const zDiff = Math.abs(rz - z);

  if (xDiff > yDiff && xDiff > zDiff) rx = -ry - rz;
  else if (yDiff > zDiff) ry = -rx - rz;
  else rz = -rx - ry;

  return { x: rx, y: ry, z: rz };
}

export function same(a: Position, b: Position) {
  return a.x === b.x && a.y === b.y;
}

export function key(position: Position) {
  return `${position.x}:${position.y}`;
}

export function isDodgeVerb(verb: string) {
  return ["DODGE", "JUMP", "BLINK"].includes(verb);
}

export function canFlyOverTerrain(verb: string) {
  return ["FLY", "GLIDE", "HOVER"].includes(normalize(verb));
}

export function canBypassObstacle(verb: string) {
  return canFlyOverTerrain(verb) || ["JUMP", "VAULT", "LEAP"].includes(normalize(verb));
}

export function facingArcCells(state: BattleState, origin: Position, facing: Position, range: number) {
  const cells: Position[] = [];
  for (let x = 0; x < state.width; x++) {
    for (let y = 0; y < state.height; y++) {
      const cell = { x, y };
      if (same(cell, origin) || hexDistance(cell, origin) > range || !isInFacingArc(origin, cell, facing)) continue;
      if (hasClearLine(state, origin, cell).ok) cells.push(cell);
    }
  }
  return cells;
}
