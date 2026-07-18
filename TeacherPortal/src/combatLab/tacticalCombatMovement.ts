import type { ActionProfile, BattleState, CubePosition, MovementPlan, ParsedAction, Position, Unit } from "./tacticalCombatTypes";
import {
  canBypassObstacle,
  canFlyOverTerrain,
  cellAt,
  cubeAdd,
  cubeScale,
  cubeSub,
  faceEnemyTowardPlayer,
  findTerrain,
  fromCube,
  hexDirectionFromTo,
  hexDistance,
  inside,
  isBlocked,
  isDodgeVerb,
  isHazard,
  isPassableForPreposition,
  isPassableForUnit,
  key,
  neighbors,
  occupied,
  offsetDeltaToCubeDirection,
  relativeCubeDirection,
  relativeFacing,
  resolvePrepositionTarget,
  same,
  terrainToken,
  toCube
} from "./tacticalCombatGeometry";

export function planMovement(state: BattleState, action: ParsedAction, profile: ActionProfile): MovementPlan {
  const player = state.playerUnit!;
  if (profile.verb === "TURN") {
    if (!action.direction) return { ok: false, message: "Say turn left, turn right, turn forward, or turn backward.", path: [] };
    return {
      ok: true,
      message: "",
      path: [],
      destination: { ...player.position },
      facing: relativeFacing(player.position, state.playerFacing, action.direction)
    };
  }

  if (action.preposition) return resolvePrepositionMove(state, action, profile);
  if (isDodgeVerb(profile.verb) && !action.direction && !state.selectedAimCell) {
    return moveAwayFromEnemy(state, profile.movementCells);
  }

  const step = relativeCubeDirection(player.position, state.playerFacing, action.direction || "FORWARD");
  return moveLinear(state, profile, step);
}

export function resolvePrepositionMove(state: BattleState, action: ParsedAction, profile: ActionProfile): MovementPlan {
  switch (action.preposition) {
    case "BESIDE": return moveBeside(state, action.objectToken, profile.movementCells);
    case "AROUND": return moveAroundLandmark(state, action.objectToken, profile.movementCells);
    case "OVER": return moveOver(state, action, profile);
    case "BEHIND": return moveBehind(state, action.objectToken, profile.movementCells);
    case "UNDER": return moveNearTerrain(state, action.objectToken, profile.movementCells, "Under only works for authored cover or obstacle cases.");
    case "NEAR": return moveNear(state, action.objectToken, profile.movementCells);
    case "TOWARD": return moveTowardOrAway(state, action.objectToken, profile, false);
    case "AWAY": return moveTowardOrAway(state, action.objectToken, profile, true);
    case "ACROSS": return moveNearTerrain(state, action.objectToken || "BRIDGE", profile.movementCells, "There is no clear route across that terrain.");
    case "INTO": return moveNearTerrain(state, action.objectToken, profile.movementCells, "There is no safe space to move into.", action.objectToken === "WATER");
    case "THROUGH": return moveNearTerrain(state, action.objectToken, profile.movementCells, "There is no passable route through that space.");
    default: return { ok: false, message: `The preposition '${action.preposition}' is not implemented yet.`, path: [] };
  }
}

export function moveBeside(state: BattleState, objectToken: string, movementCells: number): MovementPlan {
  const target = resolvePrepositionTarget(state, objectToken);
  if (!target) return { ok: false, message: "There is no open adjacent cell beside that target.", path: [] };
  const candidates = neighbors(target).filter((next) => hexDistance(next, target) === 1 && isPassableForPreposition(state, next, false));
  return chooseLandmarkDestination(state, candidates, movementCells, "There is no reachable open cell beside that target.");
}

export function moveOver(state: BattleState, action: ParsedAction, profile: ActionProfile): MovementPlan {
  if (!canBypassObstacle(profile.verb) || profile.movementCells < 2) {
    return { ok: false, message: `${profile.verb} cannot clear an obstacle. Use JUMP, FLY, or GLIDE with at least 2 movement.`, path: [] };
  }

  const player = state.playerUnit!;
  const authoredObstacle = findTerrain(state, action.objectToken);
  const step = authoredObstacle
    ? hexDirectionFromTo(player.position, authoredObstacle)
    : relativeCubeDirection(player.position, state.playerFacing, action.direction || "FORWARD");
  const middle = fromCube(cubeAdd(toCube(player.position), step));
  const landing = fromCube(cubeAdd(toCube(player.position), cubeScale(step, 2)));
  if (!inside(state, middle) || !inside(state, landing)) return { ok: false, message: "There is no valid obstacle or hazard to move over.", path: [] };
  if (action.objectToken && terrainToken(cellAt(state, middle)) !== action.objectToken) return { ok: false, message: `The ${action.objectToken.toLowerCase()} is not directly ahead. Face it first.`, path: [] };
  if (!(isBlocked(state, middle) || isHazard(state, middle))) return { ok: false, message: "OVER needs an obstacle or hazard in the first cell.", path: [] };
  if (isBlocked(state, landing) || isHazard(state, landing) || occupied(state, landing)) return { ok: false, message: "The landing cell beyond the obstacle is not safe.", path: [] };
  return { ok: true, destination: landing, path: [middle, landing], message: "" };
}

export function moveBehind(state: BattleState, objectToken: string, movementCells: number): MovementPlan {
  const objectPosition = findTerrain(state, objectToken);
  if (!objectPosition) return { ok: false, message: "There is no valid cover position behind that object.", path: [] };
  const direction = hexDirectionFromTo(state.enemyUnit.position, objectPosition);
  const candidate = fromCube(cubeAdd(toCube(objectPosition), direction));
  if (!isPassableForPreposition(state, candidate, false)) return { ok: false, message: "There is no valid cover position behind that object.", path: [] };
  return chooseLandmarkDestination(state, [candidate], movementCells, "That cover is outside this verb's movement range.");
}

export function moveNear(state: BattleState, objectToken: string, movementCells: number): MovementPlan {
  const target = resolvePrepositionTarget(state, objectToken);
  if (!target) return { ok: false, message: "There is no open cell near that target.", path: [] };
  const candidates: Position[] = [];
  for (let x = 0; x < state.width; x++) {
    for (let y = 0; y < state.height; y++) {
      const candidate = { x, y };
      const distance = hexDistance(candidate, target);
      if (distance >= 1 && distance <= 2 && isPassableForPreposition(state, candidate, false)) candidates.push(candidate);
    }
  }
  return chooseLandmarkDestination(state, candidates, movementCells, "There is no reachable open cell near that target.");
}

export function moveNearTerrain(state: BattleState, objectToken: string, movementCells: number, message: string, allowHazard = false): MovementPlan {
  const terrain = findTerrain(state, objectToken);
  if (!terrain) return { ok: false, message, path: [] };
  const candidates = [terrain, ...neighbors(terrain)].filter((candidate) => isPassableForPreposition(state, candidate, allowHazard && same(candidate, terrain)));
  return chooseLandmarkDestination(state, candidates, movementCells, message);
}

export function moveTowardOrAway(state: BattleState, objectToken: string, profile: ActionProfile, away: boolean): MovementPlan {
  const target = resolvePrepositionTarget(state, objectToken);
  if (!target) return { ok: false, message: `There is no ${objectToken || "target"} to move ${away ? "away from" : "toward"}.`, path: [] };
  let step = hexDirectionFromTo(state.playerUnit!.position, target);
  if (away) step = cubeScale(step, -1);
  return moveLinear(state, profile, step);
}

export function moveAroundLandmark(state: BattleState, objectToken: string, movementCells: number): MovementPlan {
  const landmark = findTerrain(state, objectToken);
  if (!landmark) return { ok: false, message: "Name the obstacle to move around, such as AROUND THE WALL.", path: [] };
  const forward = relativeCubeDirection(state.playerUnit!.position, state.playerFacing, "FORWARD");
  const projected = fromCube(cubeAdd(toCube(state.playerUnit!.position), cubeScale(forward, Math.max(1, movementCells))));
  const candidates = neighbors(landmark)
    .filter((cell) => isPassableForPreposition(state, cell, false))
    .sort((a, b) => hexDistance(a, projected) - hexDistance(b, projected));
  return chooseLandmarkDestination(state, candidates, movementCells, "No safe route around that obstacle fits this verb's movement range.");
}

export function chooseLandmarkDestination(state: BattleState, candidates: Position[], movementCells: number, message: string): MovementPlan {
  const origin = state.playerUnit!.position;
  const facing = offsetDeltaToCubeDirection(origin, state.playerFacing);
  const ranked = candidates
    .map((destination) => {
      const path = shortestSafePath(state, origin, destination);
      const vector = cubeSub(toCube(destination), toCube(origin));
      const alignment = vector.x * facing.x + vector.y * facing.y + vector.z * facing.z;
      return { destination, path, alignment };
    })
    .filter((candidate) => candidate.path && candidate.path.length <= Math.max(0, movementCells))
    .sort((a, b) => b.alignment - a.alignment || a.path!.length - b.path!.length || key(a.destination).localeCompare(key(b.destination)));
  const best = ranked[0];
  return best ? { ok: true, destination: best.destination, path: best.path!, message: "" } : { ok: false, message, path: [] };
}

export function moveLinear(state: BattleState, profile: ActionProfile, step: CubePosition): MovementPlan {
  const origin = state.playerUnit!.position;
  let currentCube = toCube(origin);
  let destination: Position | undefined;
  const path: Position[] = [];
  const aerial = canFlyOverTerrain(profile.verb);
  const leap = profile.verb === "JUMP";

  for (let index = 0; index < Math.max(0, profile.movementCells); index++) {
    currentCube = cubeAdd(currentCube, step);
    const next = fromCube(currentCube);
    if (!inside(state, next) || occupied(state, next)) break;
    if (isBlocked(state, next) || isHazard(state, next)) {
      path.push(next);
      if (aerial) continue;
      if (leap && index + 1 < profile.movementCells) {
        currentCube = cubeAdd(currentCube, step);
        const landing = fromCube(currentCube);
        if (!inside(state, landing) || isBlocked(state, landing) || isHazard(state, landing) || occupied(state, landing)) break;
        path.push(landing);
        destination = landing;
        index += 1;
        continue;
      }
      break;
    }
    path.push(next);
    destination = next;
  }

  if (!destination) return { ok: false, message: `No safe cell lies ${profile.direction.toLowerCase() || "forward"} within ${profile.verb}'s movement range.`, path: [] };
  return { ok: true, destination, path, message: "" };
}

export function moveAlongSafePath(state: BattleState, movementCells: number) {
  let current = state.playerUnit!.position;
  for (let step = 0; step < Math.max(0, movementCells); step++) {
    let best = current;
    let bestDistance = hexDistance(best, state.enemyUnit.position);
    for (const next of neighbors(current)) {
      if (!isPassableForUnit(state, next, state.playerUnit!)) continue;
      const distance = hexDistance(next, state.enemyUnit.position);
      if (distance < bestDistance) {
        best = next;
        bestDistance = distance;
      }
    }
    if (same(best, current)) break;
    current = best;
    if (bestDistance <= 1) break;
  }
  return same(current, state.playerUnit!.position)
    ? { ok: false, message: "No safe path was found." }
    : { ok: true, destination: current, message: "" };
}

export function moveAwayFromEnemy(state: BattleState, movementCells: number) {
  let current = state.playerUnit!.position;
  const path: Position[] = [];
  for (let step = 0; step < Math.max(0, movementCells); step++) {
    let best = current;
    let bestDistance = hexDistance(current, state.enemyUnit.position);
    for (const next of neighbors(current)) {
      if (!isPassableForUnit(state, next, state.playerUnit!)) continue;
      const distance = hexDistance(next, state.enemyUnit.position);
      if (distance > bestDistance) {
        best = next;
        bestDistance = distance;
      }
    }
    if (same(best, current)) break;
    current = best;
    path.push(current);
  }
  return same(current, state.playerUnit!.position)
    ? { ok: false, message: "No dodge space was found.", path: [] }
    : { ok: true, destination: current, message: "", path };
}

export function advanceEnemy(state: BattleState, movementCells: number) {
  const path = shortestUnitPathToRange(state, state.enemyUnit, state.playerUnit!.position, 1);
  if (!path || path.length === 0) return 0;
  const moved = Math.min(Math.max(0, movementCells), path.length);
  state.enemyUnit.position = path[moved - 1];
  faceEnemyTowardPlayer(state);
  return moved;
}

export function shortestUnitPathToRange(state: BattleState, unit: Unit, target: Position, range: number): Position[] | null {
  const start = unit.position;
  if (hexDistance(start, target) <= range) return [];
  const queue = [start];
  const previous: Record<string, Position | null> = { [key(start)]: null };
  while (queue.length) {
    const current = queue.shift()!;
    for (const next of neighbors(current)) {
      if (!isPassableForUnit(state, next, unit) || previous[key(next)] !== undefined) continue;
      previous[key(next)] = current;
      if (hexDistance(next, target) <= range) {
        const path: Position[] = [next];
        let cursor = current;
        while (!same(cursor, start)) {
          path.push(cursor);
          cursor = previous[key(cursor)]!;
        }
        path.reverse();
        return path;
      }
      queue.push(next);
    }
  }
  return null;
}

export function retreatEnemy(state: BattleState) {
  let current = state.enemyUnit.position;
  let moved = 0;
  for (let step = 0; step < 3; step++) {
    let best = current;
    let bestDistance = hexDistance(current, state.playerUnit!.position);
    for (const next of neighbors(current)) {
      if (!isPassableForUnit(state, next, state.enemyUnit)) continue;
      const distance = hexDistance(next, state.playerUnit!.position);
      if (distance > bestDistance) {
        best = next;
        bestDistance = distance;
      }
    }
    if (same(best, current)) break;
    current = best;
    moved += 1;
  }
  state.enemyUnit.position = current;
  faceEnemyTowardPlayer(state);
  return moved;
}

export function shortestSafePath(state: BattleState, start: Position, destination: Position): Position[] | null {
  if (same(start, destination)) return [];
  const queue = [start];
  const previous: Record<string, Position | null> = { [key(start)]: null };
  while (queue.length) {
    const current = queue.shift()!;
    for (const next of neighbors(current)) {
      if (!inside(state, next) || isBlocked(state, next) || isHazard(state, next)) continue;
      if (occupied(state, next) && !same(next, destination)) continue;
      if (previous[key(next)] !== undefined) continue;
      previous[key(next)] = current;
      if (same(next, destination)) {
        const path: Position[] = [next];
        let cursor = current;
        while (!same(cursor, start)) {
          path.push(cursor);
          cursor = previous[key(cursor)]!;
        }
        path.reverse();
        return path;
      }
      queue.push(next);
    }
  }
  return null;
}
