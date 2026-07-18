using System;
using System.Collections.Generic;
using UnityEngine;


public sealed partial class TacticalGrammarBattler
{
    public TacticalGrammarBattler(CreatureCombatRegistry registry, int width = TacticalGrammarBattleState.DefaultSize, int height = TacticalGrammarBattleState.DefaultSize)
    {
        this.registry = registry;
        State = new TacticalGrammarBattleState
        {
            width = Mathf.Max(1, width),
            height = Mathf.Max(1, height),
            terrain = new TacticalBattleCellType[Mathf.Max(1, width), Mathf.Max(1, height)],
        };
    }

    public void SetTerrain(TacticalBattlePosition position, TacticalBattleCellType cellType)
    {
        if (State.IsInside(position))
            State.terrain[position.x, position.y] = cellType;
    }

    public void SetEnemyUnit(string noun, TacticalBattlePosition position)
    {
        State.enemyUnit = new TacticalBattleUnit
        {
            noun = CreaturePhraseUtility.NormalizeToken(noun),
            displayPhrase = noun,
            position = position,
            stats = TacticalBattleStats.FromCreatureStats(CreatureStatBlock.Default),
            currentHp = CreatureStatBlock.Default.maxHp,
            currentPp = CreatureStatBlock.Default.maxPp,
        };
    }

    public TacticalBattleCommandResult SummonPlayer(string phrase, TacticalBattlePosition position)
    {
        if (registry == null || !registry.TryParsePhrase(phrase, out CreaturePhraseParseResult parsed))
            return Fail("That summon phrase is not valid.");

        if (!IsPhraseAllowedByProgression(parsed, out string progressionError))
            return Fail(progressionError);

        if (parsed.kind != CreaturePhraseKind.NounSummon)
            return Fail("Use a noun phrase to summon a unit.");
        if (parsed.noun == null)
            return Fail("The summon noun could not be resolved.");
        if (!parsed.noun.IsCreatureNoun)
            return Fail($"{CreaturePhraseUtility.NormalizeToken(parsed.noun.canonicalNoun)} is a {parsed.noun.nounRole.ToString().ToLowerInvariant()} noun, not a creature summon.");
        if (!State.IsInside(position))
            return Fail("The summon position is outside the grid.");

        TacticalBattleStats stats = TacticalBattleStats.FromCreatureStats(parsed.noun.baseStats);
        string adjective = parsed.modifier != null ? parsed.modifier.modifier : "";
        CombatWordVarietyEvaluation adjectiveVariety = parsed.modifier != null
            ? wordVariety.Evaluate(CombatWordRole.Adjective, adjective)
            : new CombatWordVarietyEvaluation();
        if (parsed.modifier != null)
            stats = ApplyModifier(stats, parsed.modifier, adjectiveVariety.effectiveness);

        State.playerUnit = new TacticalBattleUnit
        {
            displayPhrase = parsed.canonicalText,
            noun = CreaturePhraseUtility.NormalizeToken(parsed.noun.canonicalNoun),
            determiner = parsed.command != null ? parsed.command.determiner : "",
            adjective = CreaturePhraseUtility.NormalizeToken(adjective),
            adjectiveEffectiveness = adjectiveVariety.effectiveness,
            stats = stats,
            currentHp = stats.maxHp,
            currentPp = stats.maxPp,
            position = position,
        };

        if (parsed.modifier != null)
            wordVariety.Record(CombatWordRole.Adjective, adjective);

        string summonFeedback = adjectiveVariety.BuildFeedback();

        return new TacticalBattleCommandResult
        {
            success = true,
            message = string.IsNullOrWhiteSpace(summonFeedback)
                ? $"Summoned {State.playerUnit.displayPhrase}."
                : $"Summoned {State.playerUnit.displayPhrase}. {summonFeedback}",
            finalPosition = position,
        };
    }

    public TacticalBattleCommandResult ExecutePlayerCommand(string phrase, float currentTime = -1f)
    {
        if (State.playerUnit == null)
            return Fail("Summon a unit before issuing commands.");

        if (!TryParseActionPhrase(phrase, out ParsedActionPhrase action))
            return Fail("That battle command is not supported.");

        if (!SubjectsMatch(action.subjectNoun))
            return Fail("That subject does not match the current player unit.");

        if (!IsPhraseAllowedByProgression(action, out string progressionError))
            return Fail(progressionError);

        if (registry != null &&
            registry.TryGetNoun(State.playerUnit.noun, out NounDefinition nounDefinition))
        {
            if (action.verbDefinition != null && !nounDefinition.AllowsVerb(action.verbDefinition.verb))
                return Fail($"{State.playerUnit.noun} cannot use {action.verbDefinition.verb}.");

            if (action.adverbModifier != null &&
                action.verbDefinition != null &&
                (!action.verbDefinition.AllowsAdverb(action.adverbModifier.modifier) ||
                 !action.adverbModifier.AllowsForVerb(action.verbDefinition) ||
                 !nounDefinition.AllowsAdverb(action.verbDefinition.verb, action.adverbModifier.modifier)))
            {
                return Fail($"{action.adverbModifier.modifier} does not fit {action.verbDefinition.verb}.");
            }
        }

        TacticalBattleActionProfile profile = BuildActionProfile(action);
        if (currentTime >= 0f && IsActionCoolingDown(profile, currentTime, out float secondsRemaining))
            return Fail($"{profile.verb} is recovering for {secondsRemaining:0.0}s.", profile);
        if (State.playerUnit.currentPp < profile.ppCost)
            return Fail("Not enough PP for that action.");

        if (profile.movementAction)
        {
            TacticalBattlePosition destination;
            string movementError;
            var movementPath = new List<TacticalBattlePosition>();
            bool moved;
            bool heldPosition = false;

            if (CreaturePhraseUtility.NormalizeToken(profile.verb) == "TURN")
            {
                moved = TryTurnRelative(action.direction, out movementError);
                destination = State.playerUnit.position;
            }
            else if (!string.IsNullOrWhiteSpace(action.preposition))
            {
                moved = TryResolveMovement(action, profile, out destination, out movementError);
                if (moved && action.preposition != "OVER" &&
                    !CanReachWithinBudget(State.playerUnit.position, destination, profile.movementCells, out int requiredCells))
                {
                    moved = false;
                    movementError = requiredCells >= 0
                        ? $"{profile.verb} moves {profile.movementCells} cell(s), but that destination needs {requiredCells}."
                        : $"{profile.verb} cannot reach that destination safely.";
                }
                if (moved)
                    TryBuildSafePath(State.playerUnit.position, destination, out movementPath);
            }
            else if (IsDodgeVerb(profile.verb) && string.IsNullOrWhiteSpace(action.direction) && !hasSelectedTacticalCell)
            {
                moved = TryMoveAwayFromEnemy(profile.movementCells, out destination, out movementError);
                if (moved)
                    TryBuildSafePath(State.playerUnit.position, destination, out movementPath);
            }
            else
            {
                moved = TryMoveInFacingDirection(profile, action.direction, out destination, out movementPath, out movementError);
            }

            // A directionless movement verb is still a valid language action
            // when the unit has already reached the edge of the board. Keep
            // explicit directions, selected cells, dodges, and prepositions
            // strict because those commands promise a particular destination.
            if (!moved &&
                string.IsNullOrWhiteSpace(action.preposition) &&
                string.IsNullOrWhiteSpace(action.direction) &&
                !hasSelectedTacticalCell &&
                !IsDodgeVerb(profile.verb) &&
                CreaturePhraseUtility.NormalizeToken(profile.verb) != "TURN")
            {
                heldPosition = true;
                moved = true;
                destination = State.playerUnit.position;
                movementPath.Clear();
            }

            if (!moved)
                return Fail(movementError, profile);

            SpendPlayerPp(profile.ppCost);
            StartActionCooldown(profile, currentTime);
            RecordSuccessfulAction(profile);
            if (CreaturePhraseUtility.NormalizeToken(profile.verb) != "TURN")
                State.playerUnit.position = destination;
            ClearSelectedTacticalCell();
            return new TacticalBattleCommandResult
            {
                success = true,
                message = AppendVarietyFeedback(heldPosition
                    ? $"{profile.verb} found no safe forward cell, so the unit held position."
                    : CreaturePhraseUtility.NormalizeToken(profile.verb) == "TURN"
                    ? $"Turned {CreaturePhraseUtility.NormalizeToken(action.direction).ToLowerInvariant()}."
                    : !string.IsNullOrWhiteSpace(action.preposition)
                        ? $"Moved to ({destination.x}, {destination.y}) using {action.preposition.ToLowerInvariant()}."
                        : $"Moved {(string.IsNullOrWhiteSpace(action.direction) ? "forward" : action.direction.ToLowerInvariant())} to ({destination.x}, {destination.y}) with {profile.verb}.", profile),
                actionProfile = profile,
                finalPosition = destination,
                movementPath = movementPath,
            };
        }

        if (profile.UtilityAction)
        {
            SpendPlayerPp(profile.ppCost);
            StartActionCooldown(profile, currentTime);
            RecordSuccessfulAction(profile);
            string boostMessage = ApplyUtilityBoost(profile);
            return new TacticalBattleCommandResult
            {
                success = true,
                message = AppendVarietyFeedback(boostMessage, profile),
                actionProfile = profile,
                finalPosition = State.playerUnit.position,
            };
        }

        SpendPlayerPp(profile.ppCost);
        StartActionCooldown(profile, currentTime);
        RecordSuccessfulAction(profile);
        return new TacticalBattleCommandResult
        {
            success = true,
            message = AppendVarietyFeedback($"Resolved {profile.verb}{(string.IsNullOrWhiteSpace(profile.adverb) ? "" : $" {profile.adverb}")}.", profile),
            actionProfile = profile,
            finalPosition = State.playerUnit.position,
        };
    }

    static TacticalBattleStats ApplyModifier(
        TacticalBattleStats stats,
        ModifierDefinition modifier,
        float effectiveness)
    {
        float amount = Mathf.Clamp(effectiveness, 0.1f, 1f);
        return new TacticalBattleStats
        {
            maxHp = Mathf.Max(1, Mathf.RoundToInt(stats.maxHp * ScaleModifier(modifier.maxHpMultiplier, amount))),
            attack = Mathf.Max(1, Mathf.RoundToInt(stats.attack * ScaleModifier(modifier.attackMultiplier, amount))),
            defense = Mathf.Max(1, Mathf.RoundToInt(stats.defense * ScaleModifier(modifier.defenseMultiplier, amount))),
            speed = Mathf.Max(1, Mathf.RoundToInt(stats.speed * ScaleModifier(modifier.speedMultiplier, amount))),
            accuracy = stats.accuracy,
            evasion = stats.evasion,
            maxPp = Mathf.Max(1, Mathf.RoundToInt(stats.maxPp / Mathf.Max(0.05f, modifier.ppCostMultiplier))),
        };
    }

    static float ScaleModifier(float multiplier, float effectiveness)
    {
        return 1f + (Mathf.Max(0.05f, multiplier) - 1f) * Mathf.Clamp01(effectiveness);
    }

    TacticalBattleActionProfile BuildActionProfile(ParsedActionPhrase action)
    {
        float powerMultiplier = 1f;
        float defenseMultiplier = 1f;
        float speedMultiplier = 1f;
        float ppCostMultiplier = 1f;
        int nounPowerOffset = 0;

        if (action.adverbModifier != null)
        {
            powerMultiplier *= action.adverbModifier.powerMultiplier;
            defenseMultiplier *= action.adverbModifier.defenseMultiplier;
            speedMultiplier *= action.adverbModifier.speedMultiplier;
            ppCostMultiplier *= action.adverbModifier.ppCostMultiplier;
        }

        if (registry != null &&
            registry.TryGetNoun(State.playerUnit.noun, out NounDefinition nounDefinition))
        {
            NounMoveSlot slot = nounDefinition.ResolveMoveSlot(action.verbDefinition != null ? action.verbDefinition.verb : action.verb);
            if (slot != null)
            {
                nounPowerOffset = slot.nounPowerOffset;
            }
        }

        CreatureVerbCategory category = action.verbDefinition != null
            ? CreatureVerbCategoryUtility.InferCategory(action.verbDefinition, action.verbDefinition.verb)
            : CreatureVerbCategoryUtility.InferCategory(null, action.verb);
        if (registry != null &&
            registry.TryGetNoun(State.playerUnit.noun, out nounDefinition))
        {
            NounMoveSlot slot = nounDefinition.ResolveMoveSlot(action.verbDefinition != null ? action.verbDefinition.verb : action.verb);
            if (slot != null)
                category = slot.ResolveCategory(action.verbDefinition);
        }

        int rangeCells = Mathf.Clamp(
            (action.verbDefinition != null ? action.verbDefinition.tacticalRangeCells : MinAttackRangeCells) +
            ResolveAdverbRangeBonus(action.adverbModifier),
            MinAttackRangeCells,
            MaxAttackRangeCells);
        int movementCells = action.verbDefinition != null
            ? action.verbDefinition.tacticalMovementCells
            : 0;
        if (category == CreatureVerbCategory.Movement && movementCells <= 0)
            movementCells = 1;

        string verbToken = action.verbDefinition != null ? action.verbDefinition.verb : action.verb;
        string adverbToken = action.adverbModifier != null ? action.adverbModifier.modifier : "";
        CombatWordVarietyEvaluation variety = wordVariety.EvaluateAction(verbToken, adverbToken);
        int basePpCost = Mathf.Max(1, Mathf.RoundToInt(
            (action.verbDefinition != null ? action.verbDefinition.ppCost : 1) * ppCostMultiplier));
        int shieldAmount = category == CreatureVerbCategory.Defense
            ? Mathf.Max(1, Mathf.RoundToInt(State.playerUnit.stats.defense * defenseMultiplier * variety.effectiveness))
            : 0;
        float baseCooldown = ResolveCooldownSeconds(
            category,
            Mathf.Max(0, Mathf.RoundToInt(((action.verbDefinition != null ? action.verbDefinition.power : 0) + nounPowerOffset) * powerMultiplier)),
            rangeCells,
            movementCells,
            State.playerUnit.stats.speed,
            Mathf.Max(1f, State.playerUnit.stats.speed * speedMultiplier),
            action.verbDefinition != null ? action.verbDefinition.cooldownSeconds : 0.5f);

        return new TacticalBattleActionProfile
        {
            verb = verbToken,
            adverb = adverbToken,
            preposition = action.preposition,
            direction = action.direction,
            category = category,
            power = Mathf.Max(0, Mathf.RoundToInt(((action.verbDefinition != null ? action.verbDefinition.power : 0) + nounPowerOffset) * powerMultiplier)),
            accuracy = 1f,
            speedScore = Mathf.Max(1f, State.playerUnit.stats.speed * speedMultiplier),
            actionSpeed = Mathf.Max(1f, State.playerUnit.stats.speed * speedMultiplier),
            ppCost = Mathf.Max(1, Mathf.CeilToInt(basePpCost * variety.ppCostMultiplier)),
            rangeCells = rangeCells,
            movementCells = Mathf.Clamp(movementCells, 0, MaxMovementCells),
            shieldAmount = shieldAmount,
            shieldDurationSeconds = category == CreatureVerbCategory.Defense
                ? Mathf.Max(1f, BaseShieldDurationSeconds * Mathf.Clamp(defenseMultiplier, 0.5f, 2f))
                : 0f,
            cooldownSeconds = baseCooldown * variety.cooldownMultiplier,
            damageMultiplier = action.verbDefinition != null
                ? Mathf.Clamp(action.verbDefinition.tacticalDamageMultiplier, 0.1f, 1f)
                : 1f,
            movementAction = category == CreatureVerbCategory.Movement,
            varietyEffectiveness = variety.effectiveness,
            varietyRepeatLevel = variety.repeatLevel,
            varietyFeedback = variety.BuildFeedback(),
        };
    }

    static float ResolveCooldownSeconds(
        CreatureVerbCategory category,
        int power,
        int rangeCells,
        int movementCells,
        float unitSpeed,
        float actionSpeed,
        float verbCooldown)
    {
        float cooldown = Mathf.Max(0.25f, verbCooldown);
        switch (category)
        {
            case CreatureVerbCategory.Attack:
                cooldown += 0.55f + power * 0.08f + Mathf.Max(0, rangeCells - 1) * 0.18f;
                break;
            case CreatureVerbCategory.Defense:
                cooldown += 0.35f;
                break;
            case CreatureVerbCategory.Movement:
                cooldown += 0.2f + Mathf.Max(0, movementCells - 1) * 0.08f;
                break;
            default:
                cooldown += 0.25f;
                break;
        }

        float speedRatio = Mathf.Clamp(unitSpeed / Mathf.Max(1f, actionSpeed), 0.65f, 1.45f);
        return Mathf.Clamp(cooldown * speedRatio, 0.35f, 3f);
    }

    static int ResolveAdverbRangeBonus(ModifierDefinition adverb)
    {
        if (adverb == null)
            return 0;

        switch (CreaturePhraseUtility.NormalizeToken(adverb.modifier))
        {
            case "CAREFULLY":
            case "CLOSELY":
                return 1;
            default:
                return 0;
        }
    }

    bool IsActionCoolingDown(TacticalBattleActionProfile profile, float currentTime, out float secondsRemaining)
    {
        secondsRemaining = 0f;
        if (profile == null || State.playerUnit == null || State.playerUnit.verbCooldownReadyAt == null)
            return false;

        string key = CreaturePhraseUtility.NormalizeToken(profile.verb);
        if (string.IsNullOrEmpty(key) || !State.playerUnit.verbCooldownReadyAt.TryGetValue(key, out float readyAt))
            return false;

        secondsRemaining = Mathf.Max(0f, readyAt - currentTime);
        return secondsRemaining > 0.01f;
    }

    void StartActionCooldown(TacticalBattleActionProfile profile, float currentTime)
    {
        if (currentTime < 0f || profile == null || State.playerUnit == null)
            return;

        string key = CreaturePhraseUtility.NormalizeToken(profile.verb);
        if (string.IsNullOrEmpty(key))
            return;

        State.playerUnit.verbCooldownReadyAt ??= new Dictionary<string, float>();
        State.playerUnit.verbCooldownReadyAt[key] = currentTime + Mathf.Max(0f, profile.cooldownSeconds);
    }

    void RecordSuccessfulAction(TacticalBattleActionProfile profile)
    {
        if (profile == null)
            return;
        wordVariety.RecordAction(profile.verb, profile.adverb);
    }

    static string AppendVarietyFeedback(string message, TacticalBattleActionProfile profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.varietyFeedback))
            return message;
        return $"{message} {profile.varietyFeedback}";
    }

    string ApplyUtilityBoost(TacticalBattleActionProfile profile)
    {
        int boost = profile != null && profile.varietyEffectiveness >= 0.75f ? 1 : 0;
        if (boost <= 0)
            return $"{profile?.verb ?? "That utility word"} is too familiar to raise stats again.";

        string adverb = CreaturePhraseUtility.NormalizeToken(profile.adverb);
        string verb = CreaturePhraseUtility.NormalizeToken(profile.verb);

        if (adverb == "FAST" || adverb == "EAGERLY" || verb == "LOOK" || verb == "NOTICE" || verb == "OBSERVE")
        {
            State.playerUnit.stats.speed += boost;
            return $"{profile.verb} sharpened movement. SPD +{boost}.";
        }

        if (adverb == "HEAVILY" || adverb == "LOUDLY" || adverb == "BRAVELY" || verb == "GLOW" || verb == "SHINE")
        {
            State.playerUnit.stats.attack += boost;
            return $"{profile.verb} built power. ATK +{boost}.";
        }

        State.playerUnit.stats.defense += boost;
        return $"{profile.verb} steadied the summon. DEF +{boost}.";
    }
}
