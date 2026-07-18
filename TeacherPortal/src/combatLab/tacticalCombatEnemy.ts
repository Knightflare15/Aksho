import type { ActionProfile, BattleState, Curse, EnemyPace, EnemyPaceSummary } from "./tacticalCombatTypes";
import { canEnemyAttackPlayer, faceEnemyTowardPlayer } from "./tacticalCombatGeometry";
import { advanceEnemy, retreatEnemy } from "./tacticalCombatMovement";
import { clamp, curseName, lerp, pushLog } from "./tacticalCombatUtils";

export function resolveEnemyResponse(state: BattleState, playerDamagedEnemy: boolean, playerAction?: ActionProfile): string {
  const enemy = state.enemyUnit;
  if (enemy.currentHp <= 0) {
    state.pendingEnemyAttack = null;
    return `Enemy ${enemy.noun} is defeated.`;
  }
  if (playerDamagedEnemy) {
    state.pendingEnemyAttack = null;
    const moved = retreatEnemy(state);
    state.nextEnemyDecisionAt = Math.max(state.nextEnemyDecisionAt, state.clock + enemyThinkInterval(state));
    return moved > 0 ? `Enemy ${enemy.noun} recoiled ${moved} cell(s) to recover.` : `Enemy ${enemy.noun} reels but has nowhere safe to recover.`;
  }
  if (state.pendingEnemyAttack && playerAction?.movementAction) {
    if (state.clock > state.pendingEnemyAttack.hitsAt) return "";
    if (playerAction.actionSpeed <= state.pendingEnemyAttack.attackSpeed) {
      return `${playerAction.verb} was too slow. ${resolvePendingEnemyAttack(state, false)}`;
    }
    if (canEnemyAttackPlayer(state)) return `${playerAction.verb} moved, but not out of the attack arc.`;
    state.pendingEnemyAttack = null;
    state.nextEnemyDecisionAt = state.clock + enemyThinkInterval(state);
    return `${playerAction.verb} beat the attack speed and dodged out of the arc.`;
  }
  if (state.pendingEnemyAttack) return `Enemy ${enemy.noun}'s attack is still incoming.`;
  return "";
}

export function resolveRealtimeEnemyDecision(state: BattleState): string {
  const enemy = state.enemyUnit;
  if (!state.playerUnit || enemy.currentHp <= 0) return "";
  state.enemyDecisionCount += 1;

  if (state.activeCurse === "None" && state.enemyDecisionCount % 3 === 0) {
    const curse = enemyCurseForDecision(state.enemyDecisionCount);
    state.activeCurse = curse;
    state.curseExpiresAt = isPronounCurse(curse) ? state.clock + 30 : Number.MAX_SAFE_INTEGER;
    return `Enemy ${enemy.noun} inflicted ${curseName(curse)}. ${isPronounCurse(curse) ? "It governs every command for 30 seconds." : "It governs every command for the rest of this battle."}`;
  }

  faceEnemyTowardPlayer(state);
  if (canEnemyAttackPlayer(state)) {
    const attackSpeed = Math.max(1, enemy.stats.speed);
    state.pendingEnemyAttack = {
      damage: Math.max(1, enemy.stats.attack),
      attackSpeed,
      hitsAt: state.clock + enemyAttackWindowSeconds(state, attackSpeed)
    };
    return `Enemy ${enemy.noun} is winding up an attack: ${state.pendingEnemyAttack.damage} damage, speed ${attackSpeed.toFixed(1)}, ${(state.pendingEnemyAttack.hitsAt - state.clock).toFixed(1)}s to dodge or shield.`;
  }
  const moved = advanceEnemy(state, 1);
  if (moved > 0) return `Enemy ${enemy.noun} advanced ${moved} cell(s) to (${enemy.position.x}, ${enemy.position.y}).`;
  return `Enemy ${enemy.noun} waits behind the hazards.`;
}

export function enemyThinkInterval(state: BattleState) {
  const pace = enemyPaceNormalized(state.enemyPace);
  const baseInterval = lerp(4.8, 2.2, pace);
  const speedScale = clamp(6 / Math.max(1, state.enemyUnit.stats.speed), 0.7, 1.35);
  return clamp(baseInterval * speedScale, 1.8, 6.5);
}

export function enemyOpeningGraceSeconds(state: BattleState) {
  return lerp(3.2, 1.8, enemyPaceNormalized(state.enemyPace));
}

export function enemyAttackWindowSeconds(state: BattleState, attackSpeed: number) {
  const advancedWindow = clamp(6.5 - 0.35 * Math.max(1, attackSpeed), 3.25, 5.5);
  return clamp(advancedWindow * lerp(1.6, 1, enemyPaceNormalized(state.enemyPace)), 3.25, 8.8);
}

export function enemyPaceNormalized(pace: EnemyPace) {
  if (pace === "Advanced") return 1;
  if (pace === "Standard") return 0.55;
  return 0.1;
}

export function enemyPaceSummaryFromState(state: BattleState): EnemyPaceSummary {
  const normalized = enemyPaceNormalized(state.enemyPace);
  return {
    label: state.enemyPace === "Beginner" ? "Gentle" : state.enemyPace === "Standard" ? "Steady" : "Fast",
    normalized,
    decisionIntervalSeconds: enemyThinkInterval(state),
    attackWindowSeconds: enemyAttackWindowSeconds(state, Math.max(1, state.enemyUnit.stats.speed))
  };
}

export function enemyCurseForDecision(decision: number): Curse {
  const curses: Curse[] = ["HeSheIt", "PastFog", "NowMist", "They", "I", "You"];
  return curses[Math.floor(decision / 3 - 1) % curses.length];
}

export function isPronounCurse(curse: Curse) {
  return curse === "I" || curse === "You" || curse === "HeSheIt" || curse === "They";
}

export function resolvePendingEnemyAttackIfReady(state: BattleState) {
  if (state.pendingEnemyAttack && state.clock >= state.pendingEnemyAttack.hitsAt) {
    pushLog(state, resolvePendingEnemyAttack(state, true));
    state.nextEnemyDecisionAt = state.clock + enemyThinkInterval(state);
  }
}

export function resolvePendingEnemyAttack(state: BattleState, requireArc: boolean): string {
  const attack = state.pendingEnemyAttack;
  if (!attack || !state.playerUnit || state.enemyUnit.currentHp <= 0) {
    state.pendingEnemyAttack = null;
    return "";
  }
  state.pendingEnemyAttack = null;
  if (requireArc && !canEnemyAttackPlayer(state)) return "Enemy attack missed because the target left the attack arc.";
  let damage = attack.damage;
  if (state.activeShield > 0 && state.clock <= state.shieldExpiresAt) {
    const blocked = Math.min(state.activeShield, damage);
    state.activeShield = Math.max(0, state.activeShield - blocked);
    damage = Math.max(0, damage - blocked);
    state.playerUnit.currentHp = Math.max(0, state.playerUnit.currentHp - damage);
    return damage <= 0
      ? `Enemy attack hit the shield and dealt no HP damage. ${buildEnemyRetreatMessage(state)}`
      : `Enemy attack dealt ${damage} after shield blocked ${blocked}. ${buildEnemyRetreatMessage(state)}`;
  }
  state.playerUnit.currentHp = Math.max(0, state.playerUnit.currentHp - damage);
  return `Enemy attack landed for ${damage}. ${buildEnemyRetreatMessage(state)}`;
}

export function buildEnemyRetreatMessage(state: BattleState) {
  if (state.enemyUnit.currentHp <= 0) return "";
  const moved = retreatEnemy(state);
  return moved > 0 ? `Then it stepped back ${moved} cell(s) to recover.` : "It could not find a safe recovery cell.";
}
