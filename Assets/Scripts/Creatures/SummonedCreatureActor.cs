using System.Collections.Generic;
using UnityEngine;

public class SummonedCreatureActor : MonoBehaviour
{
    sealed class RuntimeMoveState
    {
        public string verbId;
        public int maxPp;
        public int currentPp;
        public NounMoveSlot slot;
    }

    public Animator animator;
    public Transform visualRoot;
    public float commandMoveSeconds = 0.25f;
    public float fallbackVisualScale = 0.65f;

    public NounDefinition Definition { get; private set; }
    public ModifierDefinition Adjective { get; private set; }
    public float AdjectiveEffectiveness { get; private set; } = 1f;
    public CreatureStatBlock Stats { get; private set; }
    public int CurrentHp { get; private set; }
    public int CurrentPp { get; private set; }
    public int LastPpSpent { get; private set; }
    public int LastDamageDealt { get; private set; }
    public string LastActionOutcome { get; private set; } = "";
    public float LastVarietyEffectiveness { get; private set; } = 1f;
    public string LastVarietyFeedback { get; private set; } = "";
    public float NextCommandAt { get; private set; }
    public string CanonicalNoun => Definition != null ? CreaturePhraseUtility.NormalizeToken(Definition.canonicalNoun) : "";
    public bool IsDefeated => CurrentHp <= 0;

    readonly HashSet<string> focusVocabulary = new HashSet<string>();
    readonly Dictionary<string, RuntimeMoveState> moveStates = new Dictionary<string, RuntimeMoveState>();
    readonly CombatWordVarietyTracker commandVariety = new CombatWordVarietyTracker();
    PlayerLearningProfile learningProfile;
    int legacyFallbackPp;

    public void Configure(NounDefinition definition, ModifierDefinition adjective)
    {
        Configure(definition, adjective, null, null);
    }

    public void Configure(NounDefinition definition, ModifierDefinition adjective, IEnumerable<string> focusWords)
    {
        Configure(definition, adjective, null, focusWords);
    }

    public void Configure(
        NounDefinition definition,
        ModifierDefinition adjective,
        PlayerLearningProfile profile = null,
        IEnumerable<string> focusWords = null,
        float adjectiveEffectiveness = 1f)
    {
        Definition = definition;
        Adjective = adjective;
        AdjectiveEffectiveness = Mathf.Clamp(adjectiveEffectiveness, 0.1f, 1f);
        learningProfile = profile;
        Stats = ApplyAdjective(
            definition != null ? definition.baseStats : CreatureStatBlock.Default,
            adjective,
            AdjectiveEffectiveness);
        CurrentHp = Stats.maxHp;
        LastPpSpent = 0;
        LastDamageDealt = 0;
        LastActionOutcome = "summoned";
        LastVarietyEffectiveness = 1f;
        LastVarietyFeedback = "";
        commandVariety.Clear();
        SetFocusVocabulary(focusWords);
        BuildMoveStates();
        RefreshDisplayedPpTotals();
        EnsureFallbackVisual();
    }

    public void ApplyStartingPpMultiplier(float multiplier)
    {
        float clamped = Mathf.Clamp(multiplier, 0.1f, 1f);
        if (clamped >= 0.995f)
            return;

        if (moveStates.Count > 0)
        {
            foreach (RuntimeMoveState state in moveStates.Values)
                state.currentPp = Mathf.Clamp(Mathf.FloorToInt(state.maxPp * clamped), 1, state.maxPp);
        }
        else
        {
            legacyFallbackPp = Mathf.Clamp(Mathf.FloorToInt(Stats.maxPp * clamped), 1, Stats.maxPp);
        }

        RefreshDisplayedPpTotals();
        LastActionOutcome = "summoned_unstable";
    }

    public bool CanUseVerb(VerbActionDefinition verb, out string reason)
    {
        reason = "";
        if (Definition == null)
        {
            reason = "No creature is summoned.";
            return false;
        }
        if (verb == null)
        {
            reason = "That verb is not known yet.";
            return false;
        }
        if (!Definition.AllowsVerb(verb.verb))
        {
            reason = $"{CanonicalNoun} cannot {CreaturePhraseUtility.NormalizeToken(verb.verb).ToLowerInvariant()} yet.";
            return false;
        }
        if (Time.time < NextCommandAt)
        {
            reason = "Creature is still acting.";
            return false;
        }
        if (IsDefeated)
        {
            reason = $"{CanonicalNoun} is knocked out.";
            return false;
        }
        return true;
    }

    public bool TryUseVerb(VerbActionDefinition verb, ModifierDefinition adverb, SpellTarget target, out string resultMessage)
    {
        LastPpSpent = 0;
        LastDamageDealt = 0;
        LastActionOutcome = "";
        LastVarietyEffectiveness = 1f;
        LastVarietyFeedback = "";

        if (!CanUseVerb(verb, out resultMessage))
        {
            LastActionOutcome = "command_unavailable";
            return false;
        }

        if (adverb != null)
        {
            if (!verb.AllowsAdverb(adverb.modifier) || !adverb.AllowsForVerb(verb) || !Definition.AllowsAdverb(verb.verb, adverb.modifier))
            {
                LastActionOutcome = "invalid_adverb";
                resultMessage = $"{CreaturePhraseUtility.NormalizeToken(adverb.modifier)} does not fit {CreaturePhraseUtility.NormalizeToken(verb.verb)} for {CanonicalNoun}.";
                return false;
            }
        }

        RuntimeMoveState moveState = ResolveMoveState(verb);
        CombatWordVarietyEvaluation variety = commandVariety.EvaluateAction(
            verb.verb,
            adverb != null ? adverb.modifier : "");
        float actionEffectiveness = variety.effectiveness;
        int ppCost = ResolvePpCost(verb, adverb, moveState, variety.ppCostMultiplier);
        int currentPp = moveState != null ? moveState.currentPp : legacyFallbackPp;
        if (currentPp < ppCost)
        {
            LastPpSpent = 0;
            LastActionOutcome = "not_enough_pp";
            resultMessage = $"{CanonicalNoun} needs {ppCost} PP for {CreaturePhraseUtility.NormalizeToken(verb.verb)}, but has {currentPp}.";
            return false;
        }

        SpendPp(moveState, ppCost);
        LastPpSpent = ppCost;
        LastVarietyEffectiveness = actionEffectiveness;
        LastVarietyFeedback = variety.BuildFeedback();
        commandVariety.RecordAction(verb.verb, adverb != null ? adverb.modifier : "");
        float cooldownMultiplier = adverb != null ? Mathf.Max(1f, adverb.ppCostMultiplier) : 1f;
        NextCommandAt = Time.time + Mathf.Max(0f, verb.cooldownSeconds) * cooldownMultiplier * variety.cooldownMultiplier;
        if (animator != null && !string.IsNullOrWhiteSpace(verb.animationTrigger))
            animator.SetTrigger(verb.animationTrigger);

        if (verb.role == BattleActionRole.Defense || verb.role == BattleActionRole.Dodge)
        {
            float range = Mathf.Max(0.5f, verb.range) * ResolveSpeedMultiplier(adverb, actionEffectiveness);
            MoveDefensively(range);
            LastActionOutcome = verb.role == BattleActionRole.Dodge ? "dodge_ready" : "guard_ready";
            resultMessage = $"{CanonicalNoun} used {CreaturePhraseUtility.NormalizeToken(verb.verb)} to {FormatRole(verb.role)}{FormatVarietyFeedback(variety)}.";
            return true;
        }

        if (verb.role == BattleActionRole.Utility)
        {
            LastActionOutcome = "utility_boost";
            resultMessage = ApplyUtilityBoost(verb, adverb, actionEffectiveness) + FormatVarietyFeedback(variety) + ".";
            return true;
        }

        if (target == null || target.IsDefeated)
        {
            LastActionOutcome = "no_target";
            resultMessage = $"{CanonicalNoun} used {CreaturePhraseUtility.NormalizeToken(verb.verb)}, but there is no target{FormatVarietyFeedback(variety)}.";
            return true;
        }

        float distance = Vector3.Distance(transform.position, target.GetAimPoint());
        float effectiveRange = Mathf.Max(0.1f, verb.range) * ResolveSpeedMultiplier(adverb, actionEffectiveness);
        if (distance > effectiveRange || verb.movementVerb)
            MoveTowardTarget(target, effectiveRange);

        float hitChance = ResolveHitChance(verb, adverb, actionEffectiveness, moveState);
        if (Random.value > hitChance)
        {
            LastActionOutcome = "missed";
            resultMessage = $"{CanonicalNoun} used {CreaturePhraseUtility.NormalizeToken(verb.verb)}, but missed ({Mathf.RoundToInt(hitChance * 100f)}% accuracy){FormatVarietyFeedback(variety)}.";
            return true;
        }

        int damage = ResolveDamage(verb, adverb, actionEffectiveness, moveState);
        bool hit = target.ReceiveCreatureAction(CanonicalNoun, verb.verb, damage);
        SpawnVerbEffect(verb, target);
        LastActionOutcome = hit ? "hit" : "wrong_noun_family";
        LastDamageDealt = hit ? damage : 0;

        resultMessage = hit
            ? $"{CanonicalNoun} used {CreaturePhraseUtility.NormalizeToken(verb.verb)} for {damage}{FormatVarietyFeedback(variety)}."
            : $"{CanonicalNoun} is the wrong noun family for this enemy{FormatVarietyFeedback(variety)}.";
        return hit;
    }

    public bool TakeDamage(int incomingDamage, out string resultMessage)
    {
        int damage = Mathf.Max(0, incomingDamage);
        if (damage <= 0)
        {
            resultMessage = "";
            return false;
        }

        CurrentHp = Mathf.Max(0, CurrentHp - damage);
        if (CurrentHp <= 0)
        {
            LastActionOutcome = "knocked_out";
            resultMessage = $"{CanonicalNoun} was knocked out.";
        }
        else
        {
            LastActionOutcome = "damaged";
            resultMessage = $"{CanonicalNoun} took {damage} damage ({CurrentHp}/{Stats.maxHp} HP).";
        }

        return true;
    }

    public bool TryGetMovePp(string verbId, out int currentPp, out int maxPp)
    {
        RuntimeMoveState state = ResolveMoveState(verbId);
        if (state != null)
        {
            currentPp = state.currentPp;
            maxPp = state.maxPp;
            return true;
        }

        currentPp = legacyFallbackPp;
        maxPp = Stats.maxPp;
        return !string.IsNullOrWhiteSpace(verbId);
    }

    public void ResetCommandVariety()
    {
        commandVariety.Clear();
        LastVarietyEffectiveness = 1f;
        LastVarietyFeedback = "";
    }

    static string FormatRole(BattleActionRole role)
    {
        return role switch
        {
            BattleActionRole.Dodge => "dodge",
            BattleActionRole.Defense => "defend",
            _ => "act",
        };
    }

    void BuildMoveStates()
    {
        moveStates.Clear();
        legacyFallbackPp = Stats.maxPp;

        if (Definition == null || Definition.moveSet == null || Definition.moveSet.Count == 0)
            return;

        foreach (NounMoveSlot slot in Definition.moveSet)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.verbId))
                continue;

            int maxPp = ResolveMoveMaxPp(slot);
            string key = CreaturePhraseUtility.NormalizeToken(slot.verbId);
            moveStates[key] = new RuntimeMoveState
            {
                verbId = key,
                maxPp = maxPp,
                currentPp = maxPp,
                slot = slot,
            };
        }
    }

    int ResolveMoveMaxPp(NounMoveSlot slot)
    {
        if (slot == null)
            return Mathf.Max(1, Stats.maxPp);

        float supportMultiplier = learningProfile != null
            ? learningProfile.GetBattleMovePpMultiplier(CanonicalNoun, slot.verbId, slot.masteryBias, slot.mistakeBias)
            : 1f;
        float focusMultiplier = MatchesFocusVocabulary(slot.verbId) ? 1.15f : 1f;
        int resolved = Mathf.RoundToInt(Mathf.Max(1, slot.baseMaxPp) * supportMultiplier * focusMultiplier);
        return Mathf.Max(Mathf.Max(1, slot.minMaxPp), resolved);
    }

    RuntimeMoveState ResolveMoveState(VerbActionDefinition verb)
    {
        return verb != null ? ResolveMoveState(verb.verb) : null;
    }

    RuntimeMoveState ResolveMoveState(string verbId)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(verbId);
        if (string.IsNullOrEmpty(normalized))
            return null;

        moveStates.TryGetValue(normalized, out RuntimeMoveState state);
        return state;
    }

    void SpendPp(RuntimeMoveState moveState, int ppCost)
    {
        if (moveState != null)
        {
            moveState.currentPp = Mathf.Max(0, moveState.currentPp - ppCost);
        }
        else
        {
            legacyFallbackPp = Mathf.Max(0, legacyFallbackPp - ppCost);
        }

        RefreshDisplayedPpTotals();
    }

    void RefreshDisplayedPpTotals()
    {
        if (moveStates.Count == 0)
        {
            CurrentPp = legacyFallbackPp;
            Stats = new CreatureStatBlock
            {
                maxHp = Stats.maxHp,
                attack = Stats.attack,
                defense = Stats.defense,
                speed = Stats.speed,
                maxPp = Mathf.Max(1, legacyFallbackPp <= 0 ? Stats.maxPp : Stats.maxPp),
            };
            return;
        }

        int totalCurrent = 0;
        int totalMax = 0;
        foreach (RuntimeMoveState state in moveStates.Values)
        {
            totalCurrent += Mathf.Max(0, state.currentPp);
            totalMax += Mathf.Max(1, state.maxPp);
        }

        CurrentPp = totalCurrent;
        Stats = new CreatureStatBlock
        {
            maxHp = Stats.maxHp,
            attack = Stats.attack,
            defense = Stats.defense,
            speed = Stats.speed,
            maxPp = Mathf.Max(1, totalMax),
        };
    }

    int ResolvePpCost(
        VerbActionDefinition verb,
        ModifierDefinition adverb,
        RuntimeMoveState moveState,
        float varietyCostMultiplier)
    {
        float multiplier = adverb != null ? Mathf.Max(0.05f, adverb.ppCostMultiplier) : 1f;
        multiplier *= ResolveFocusPpMultiplier(verb);
        multiplier *= Mathf.Max(1f, varietyCostMultiplier);
        if (moveState != null && moveState.slot != null && moveState.slot.nounPowerOffset > 0)
            multiplier *= 1f + moveState.slot.nounPowerOffset * 0.03f;
        return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, verb.ppCost) * multiplier));
    }

    float ResolveFocusPpMultiplier(VerbActionDefinition verb)
    {
        if (verb == null || focusVocabulary.Count == 0)
            return 1f;

        if (MatchesFocusVocabulary(verb))
            return 0.65f;

        return 1.35f;
    }

    bool MatchesFocusVocabulary(VerbActionDefinition verb)
    {
        if (verb == null)
            return false;

        return MatchesFocusVocabulary(verb.verb) ||
               MatchesAnyFocus(verb.aliases) ||
               MatchesAnyFocus(verb.GetThirdPersonSingularForms()) ||
               MatchesAnyFocus(verb.GetPastTenseForms()) ||
               MatchesAnyFocus(verb.GetProgressiveForms());
    }

    bool MatchesFocusVocabulary(string verbId)
    {
        return focusVocabulary.Contains(CreaturePhraseUtility.NormalizeToken(verbId));
    }

    bool MatchesAnyFocus(IEnumerable<string> words)
    {
        if (words == null)
            return false;

        foreach (string word in words)
            if (focusVocabulary.Contains(CreaturePhraseUtility.NormalizeToken(word)))
                return true;

        return false;
    }

    void SetFocusVocabulary(IEnumerable<string> focusWords)
    {
        focusVocabulary.Clear();
        if (focusWords == null)
            return;

        foreach (string word in focusWords)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(word);
            if (!string.IsNullOrEmpty(normalized))
                focusVocabulary.Add(normalized);
        }
    }

    int ResolveDamage(VerbActionDefinition verb, ModifierDefinition adverb, float actionEffectiveness, RuntimeMoveState moveState)
    {
        int powerOffset = moveState != null && moveState.slot != null ? moveState.slot.nounPowerOffset : 0;
        float multiplier = adverb != null ? Mathf.Max(0.05f, adverb.powerMultiplier) : 1f;
        multiplier *= Mathf.Clamp(actionEffectiveness, 0.1f, 1f);
        return Mathf.Max(1, Mathf.RoundToInt((Stats.attack + Mathf.Max(0, verb.power) + powerOffset) * multiplier));
    }

    float ResolveHitChance(VerbActionDefinition verb, ModifierDefinition adverb, float actionEffectiveness, RuntimeMoveState moveState)
    {
        float baseAccuracy = verb != null ? Mathf.Clamp(verb.accuracy, 0.05f, 1f) : 0.5f;
        float slotAccuracy = moveState != null && moveState.slot != null ? moveState.slot.accuracyOffset : 0f;
        float statAccuracy = Mathf.Clamp(0.82f + Stats.speed * 0.025f, 0.65f, 1.15f);
        float adverbAccuracy = adverb != null ? Mathf.Max(0.05f, adverb.accuracyMultiplier) : 1f;
        float varietyAccuracy = Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(actionEffectiveness));
        return Mathf.Clamp((baseAccuracy + slotAccuracy) * statAccuracy * adverbAccuracy * varietyAccuracy, 0.05f, 0.98f);
    }

    float ResolveSpeedMultiplier(ModifierDefinition adverb, float actionEffectiveness)
    {
        float varietySpeed = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(actionEffectiveness));
        return (Stats.speed / 5f) * (adverb != null ? Mathf.Max(0.05f, adverb.speedMultiplier) : 1f) * varietySpeed;
    }

    static string FormatVarietyFeedback(CombatWordVarietyEvaluation evaluation)
    {
        if (evaluation == null || !evaluation.IsDiminished)
            return "";
        return $" ({evaluation.BuildFeedback()})";
    }

    string ApplyUtilityBoost(
        VerbActionDefinition verb,
        ModifierDefinition adverb,
        float actionEffectiveness)
    {
        string verbToken = CreaturePhraseUtility.NormalizeToken(verb != null ? verb.verb : "");
        string adverbToken = CreaturePhraseUtility.NormalizeToken(adverb != null ? adverb.modifier : "");
        int boost = actionEffectiveness >= 0.75f ? 1 : 0;
        if (boost <= 0)
            return $"{CanonicalNoun} used {verbToken}, but that familiar utility word no longer raises a stat";

        CreatureStatBlock updated = Stats;
        string boostedStat;
        if (adverbToken == "FAST" || adverbToken == "EAGERLY" || verbToken == "LOOK" || verbToken == "NOTICE" || verbToken == "OBSERVE")
        {
            updated.speed += boost;
            boostedStat = "SPD";
        }
        else if (adverbToken == "HEAVILY" || adverbToken == "LOUDLY" || adverbToken == "BRAVELY" || verbToken == "GLOW" || verbToken == "SHINE")
        {
            updated.attack += boost;
            boostedStat = "ATK";
        }
        else
        {
            updated.defense += boost;
            boostedStat = "DEF";
        }

        Stats = updated;
        return $"{CanonicalNoun} used {verbToken} for utility. {boostedStat} +{boost}";
    }

    void MoveTowardTarget(SpellTarget target, float effectiveRange)
    {
        Vector3 destination = target.GetAimPoint();
        Vector3 direction = destination - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            return;

        float moveDistance = Mathf.Min(direction.magnitude, Mathf.Max(0.5f, effectiveRange * 0.55f));
        transform.position += direction.normalized * moveDistance;
        transform.LookAt(new Vector3(destination.x, transform.position.y, destination.z));
    }

    void MoveDefensively(float effectiveRange)
    {
        Vector3 sidestep = transform.right;
        if (sidestep.sqrMagnitude < 0.01f)
            sidestep = Vector3.right;

        float moveDistance = Mathf.Clamp(effectiveRange * 0.35f, 0.4f, 2.5f);
        transform.position += sidestep.normalized * moveDistance;
    }

    void SpawnVerbEffect(VerbActionDefinition verb, SpellTarget target)
    {
        if (verb == null || verb.effectPrefab == null || target == null)
            return;

        GameObject effect = Instantiate(verb.effectPrefab, target.GetAimPoint(), Quaternion.identity);
        DisposeTemporaryObject(effect, 2f);
    }

    void EnsureFallbackVisual()
    {
        if (visualRoot != null || GetComponentInChildren<Renderer>() != null)
            return;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Placeholder Creature Cube";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * Mathf.Max(0.1f, fallbackVisualScale);
        visualRoot = visual.transform;
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
            DisposeTemporaryObject(collider);
    }

    static void DisposeTemporaryObject(UnityEngine.Object target, float delay = 0f)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            if (delay > 0f)
                Destroy(target, delay);
            else
                Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    static CreatureStatBlock ApplyAdjective(
        CreatureStatBlock baseStats,
        ModifierDefinition adjective,
        float adjectiveEffectiveness)
    {
        CreatureStatBlock stats = baseStats.Clamp();
        if (adjective == null)
            return stats;

        float effectiveness = Mathf.Clamp(adjectiveEffectiveness, 0.1f, 1f);
        stats.maxHp = Mathf.Max(1, Mathf.RoundToInt(stats.maxHp * ScaleModifier(adjective.maxHpMultiplier, effectiveness)));
        stats.attack = Mathf.Max(1, Mathf.RoundToInt(stats.attack * ScaleModifier(adjective.attackMultiplier, effectiveness)));
        stats.defense = Mathf.Max(1, Mathf.RoundToInt(stats.defense * ScaleModifier(adjective.defenseMultiplier, effectiveness)));
        stats.speed = Mathf.Max(1, Mathf.RoundToInt(stats.speed * ScaleModifier(adjective.speedMultiplier, effectiveness)));
        stats.maxPp = Mathf.Max(1, stats.maxPp);
        return stats;
    }

    static float ScaleModifier(float multiplier, float effectiveness)
    {
        return 1f + (Mathf.Max(0.05f, multiplier) - 1f) * Mathf.Clamp01(effectiveness);
    }
}
