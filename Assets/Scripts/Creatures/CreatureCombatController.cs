using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CreatureCombatRegistry))]
public class CreatureCombatController : MonoBehaviour
{
    [Header("References")]
    public CreatureCombatRegistry registry;
    public PlayerAimAssist aimAssist;
    public PlayerLearningProfile learningProfile;
    public Transform summonOrigin;
    public TacticalGrammarBattleController tacticalBattle;
    public EnemyWaveDirector waveDirector;

    [Header("Creature Combat")]
    public bool enabledForPhrases = true;
    public float summonForwardOffset = 1.8f;
    public float summonSideOffset = 0.8f;
    public float summonCooldownSeconds = 1.5f;
    public float adjectiveSwapFatigueWindowSeconds = 8f;
    [Range(0f, 0.8f)] public float adjectiveSwapPpPenalty = 0.35f;
    public float baseDefenseWindowSeconds = 1.15f;
    public GrammarBattleCurse activeCurse = GrammarBattleCurse.None;

    SummonedCreatureActor activeCreature;
    float nextSummonAt;
    float dodgeWindowUntil = -999f;
    float defenseWindowUntil = -999f;
    int defenseReduction;
    float lastAdjectiveSwapAt = -999f;
    int repeatedAdjectiveSwapCount;
    string previousSummonedNounToken = "";
    string previousSummonedAdjectiveToken = "";
    string lastEnemyNounFamily = "";
    string lastEnemyActionVerb = "";
    string lastEnemyGrammarCommand = "";
    string lastEnemyGrammarPattern = "";
    readonly CombatWordVarietyTracker summonVariety = new CombatWordVarietyTracker();

    public SummonedCreatureActor ActiveCreature => activeCreature;
    public event Action<string> OnStatus;

    // Reset is invoked when this component is attached in the editor, including
    // when Unity adds it to satisfy WordActionHandler's RequireComponent. Awake
    // is not guaranteed to run in that EditMode lifecycle, so establish the
    // required local dependency here as well as in the runtime resolver below.
    void Reset()
    {
        if (registry == null)
            registry = GetComponent<CreatureCombatRegistry>();
    }

    void OnValidate()
    {
        if (registry == null)
            registry = GetComponent<CreatureCombatRegistry>();
    }

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        SubscribeToEncounterLifecycle();
    }

    void OnDisable()
    {
        UnsubscribeFromEncounterLifecycle();
    }

    public bool TryHandlePhrase(string phrase, PronunciationInsightResult? pronunciationInsight = null)
    {
        if (!enabledForPhrases)
            return false;

        ResolveReferences();
        if (tacticalBattle != null && tacticalBattle.TryHandlePhrase(phrase, pronunciationInsight))
            return true;

        if (TryHandleConjunctionPhrase(phrase, pronunciationInsight))
            return true;

        return TryHandleSinglePhrase(phrase, pronunciationInsight, "", false);
    }

    bool TryHandleSinglePhrase(string phrase, PronunciationInsightResult? pronunciationInsight, string commandConjunction, bool allowTactical)
    {
        if (!enabledForPhrases)
            return false;

        ResolveReferences();
        if (allowTactical && tacticalBattle != null && tacticalBattle.TryHandlePhrase(phrase, pronunciationInsight))
            return true;

        if (registry == null || !registry.TryParsePhrase(phrase, out CreaturePhraseParseResult parsed))
            return false;

        if (!IsPhrasePatternUnlocked(parsed.pattern, out string lockedMessage))
        {
            List<string> encounterMasteryTags = ResolveActiveEncounterMasteryTags();
            GrammarConceptId conceptId = ResolveConceptForPattern(parsed.pattern);
            string tutorialMessage = BuildBattleTutorialMessage(conceptId, lockedMessage, parsed);
            PublishStatus(tutorialMessage);
            learningProfile?.RecordSpokenCast(parsed.canonicalText, false, false, 0f, null, Array.Empty<byte>(), false, false);
            CurriculumSessionManager.Instance?.RecordGrammarBattleEvent(
                parsed.originalText,
                parsed.pattern,
                activeCurse,
                parsed.verb != null ? parsed.verb.verb : "",
                parsed.verb != null ? parsed.verb.role.ToString() : "",
                false,
                "grammar_pattern_locked",
                conceptId: conceptId,
                errorCategory: "grammar_pattern_locked",
                hintLevelShown: TutorHintLevel.RuleHint.ToString(),
                remediationStep: TutorRemediationStep.GuidedRetry.ToString(),
                correctedResponse: parsed.canonicalText,
                enemyNounFamily: lastEnemyNounFamily,
                enemyActionVerb: lastEnemyActionVerb,
                enemyGrammarCommand: lastEnemyGrammarCommand,
                enemyGrammarPattern: lastEnemyGrammarPattern,
                commandConjunction: commandConjunction,
                encounterMasteryTags: encounterMasteryTags,
                pronunciationInsight: pronunciationInsight);
            return true;
        }

        if (!IsPhraseVocabularyUnlocked(parsed, out string vocabularyMessage))
        {
            List<string> encounterMasteryTags = ResolveActiveEncounterMasteryTags();
            GrammarConceptId conceptId = ResolveConceptForPattern(parsed.pattern);
            string tutorialMessage = BuildBattleTutorialMessage(conceptId, vocabularyMessage, parsed);
            PublishStatus(tutorialMessage);
            learningProfile?.RecordSpokenCast(parsed.canonicalText, false, false, 0f, null, Array.Empty<byte>(), false, false);
            CurriculumSessionManager.Instance?.RecordGrammarBattleEvent(
                parsed.originalText,
                parsed.pattern,
                activeCurse,
                parsed.verb != null ? parsed.verb.verb : "",
                parsed.verb != null ? parsed.verb.role.ToString() : "",
                false,
                "vocabulary_locked",
                conceptId: conceptId,
                errorCategory: "vocabulary_locked",
                hintLevelShown: TutorHintLevel.RuleHint.ToString(),
                remediationStep: TutorRemediationStep.GuidedRetry.ToString(),
                correctedResponse: parsed.canonicalText,
                enemyNounFamily: lastEnemyNounFamily,
                enemyActionVerb: lastEnemyActionVerb,
                enemyGrammarCommand: lastEnemyGrammarCommand,
                enemyGrammarPattern: lastEnemyGrammarPattern,
                commandConjunction: commandConjunction,
                encounterMasteryTags: encounterMasteryTags,
                pronunciationInsight: pronunciationInsight);
            return true;
        }

        if (!IsPhraseAllowedByCurse(parsed, out string curseMessage))
        {
            List<string> encounterMasteryTags = ResolveActiveEncounterMasteryTags();
            GrammarConceptId conceptId = ResolveConceptForPattern(parsed.pattern);
            string tutorialMessage = BuildBattleTutorialMessage(conceptId, curseMessage, parsed);
            PublishStatus(tutorialMessage);
            learningProfile?.RecordBattleCommand(
                parsed.noun != null ? parsed.noun.canonicalNoun : "",
                parsed.verb != null ? parsed.verb.verb : "",
                parsed.modifier != null && parsed.modifier.role == ModifierGrammarRole.Adjective ? parsed.modifier.modifier : "",
                parsed.modifier != null && parsed.modifier.role == ModifierGrammarRole.Adverb ? parsed.modifier.modifier : "",
                parsed.command != null ? parsed.command.tense : CreatureCommandTense.None,
                parsed.command != null ? parsed.command.pronoun : "",
                false,
                true);
            learningProfile?.RecordSpokenCast(parsed.canonicalText, false, false, 0f, null, Array.Empty<byte>(), false, false);
            CurriculumSessionManager.Instance?.RecordGrammarBattleEvent(
                parsed.originalText,
                parsed.pattern,
                activeCurse,
                parsed.verb != null ? parsed.verb.verb : "",
                parsed.verb != null ? parsed.verb.role.ToString() : "",
                false,
                curseMessage,
                conceptId: conceptId,
                errorCategory: "curse_requirement",
                hintLevelShown: TutorHintLevel.RuleHint.ToString(),
                remediationStep: TutorRemediationStep.GuidedRetry.ToString(),
                correctedResponse: BuildCurseCorrection(parsed),
                enemyNounFamily: lastEnemyNounFamily,
                enemyActionVerb: lastEnemyActionVerb,
                enemyGrammarCommand: lastEnemyGrammarCommand,
                enemyGrammarPattern: lastEnemyGrammarPattern,
                commandConjunction: commandConjunction,
                encounterMasteryTags: encounterMasteryTags,
                pronunciationInsight: pronunciationInsight);
            return true;
        }

        GrammarBattleCurse curseAtInput = activeCurse;
        List<string> activeEncounterMasteryTags = ResolveActiveEncounterMasteryTags();
        switch (parsed.kind)
        {
            case CreaturePhraseKind.NounSummon:
                bool summoned = Summon(parsed.noun, parsed.modifier);
                string summonOutcome = summoned && activeCreature != null && !string.IsNullOrWhiteSpace(activeCreature.LastActionOutcome)
                    ? activeCreature.LastActionOutcome
                    : summoned ? "summoned" : "summon_rejected";
                learningProfile?.RecordBattleCommand(
                    parsed.noun != null ? parsed.noun.canonicalNoun : "",
                    "",
                    parsed.modifier != null ? parsed.modifier.modifier : "",
                    "",
                    parsed.command != null ? parsed.command.tense : CreatureCommandTense.None,
                    parsed.command != null ? parsed.command.pronoun : "",
                    summoned);
                learningProfile?.RecordSpokenCast(parsed.noun != null ? parsed.noun.canonicalNoun : parsed.canonicalText, summoned, false, 0f, null, Array.Empty<byte>(), false, false);
                CurriculumSessionManager.Instance?.RecordGrammarBattleEvent(
                    parsed.originalText,
                    parsed.pattern,
                    curseAtInput,
                    "",
                    "Summon",
                    summoned,
                    summonOutcome,
                    conceptId: ResolveConceptForPattern(parsed.pattern),
                    errorCategory: summoned ? "" : summonOutcome,
                    correctedResponse: parsed.canonicalText,
                    enemyNounFamily: lastEnemyNounFamily,
                    enemyActionVerb: lastEnemyActionVerb,
                    enemyGrammarCommand: lastEnemyGrammarCommand,
                    enemyGrammarPattern: lastEnemyGrammarPattern,
                    commandConjunction: commandConjunction,
                    encounterMasteryTags: activeEncounterMasteryTags,
                    pronunciationInsight: pronunciationInsight);
                return true;
            case CreaturePhraseKind.VerbCommand:
                if (parsed.noun != null &&
                    (activeCreature == null ||
                     !registry.AreInSameNounFamily(activeCreature.CanonicalNoun, parsed.noun.canonicalNoun)))
                {
                    if (!Summon(parsed.noun, null))
                        return true;
                }
                bool success = Command(parsed.verb, parsed.modifier);
                learningProfile?.RecordBattleCommand(
                    activeCreature != null ? activeCreature.CanonicalNoun : (parsed.noun != null ? parsed.noun.canonicalNoun : ""),
                    parsed.verb != null ? parsed.verb.verb : "",
                    activeCreature != null && activeCreature.Adjective != null ? activeCreature.Adjective.modifier : "",
                    parsed.modifier != null ? parsed.modifier.modifier : "",
                    parsed.command != null ? parsed.command.tense : CreatureCommandTense.None,
                    parsed.command != null ? parsed.command.pronoun : "",
                    success);
                int ppSpent = activeCreature != null ? activeCreature.LastPpSpent : 0;
                int damageDealt = activeCreature != null ? activeCreature.LastDamageDealt : 0;
                string actionOutcome = activeCreature != null && !string.IsNullOrWhiteSpace(activeCreature.LastActionOutcome)
                    ? activeCreature.LastActionOutcome
                    : success ? "command_success" : "command_failed";
                CurriculumSessionManager.Instance?.RecordGrammarBattleEvent(
                    parsed.originalText,
                    parsed.pattern,
                    curseAtInput,
                    parsed.verb != null ? parsed.verb.verb : "",
                    parsed.verb != null ? parsed.verb.role.ToString() : "",
                    success,
                    actionOutcome,
                    damageDealt: damageDealt,
                    ppSpent: ppSpent,
                    conceptId: ResolveConceptForPattern(parsed.pattern),
                    errorCategory: success ? "" : actionOutcome,
                    correctedResponse: parsed.canonicalText,
                    enemyNounFamily: lastEnemyNounFamily,
                    enemyActionVerb: lastEnemyActionVerb,
                    enemyGrammarCommand: lastEnemyGrammarCommand,
                    enemyGrammarPattern: lastEnemyGrammarPattern,
                    commandPreposition: parsed.command != null ? parsed.command.preposition : "",
                    commandConjunction: commandConjunction,
                    encounterMasteryTags: activeEncounterMasteryTags,
                    pronunciationInsight: pronunciationInsight);
                if (activeCurse != GrammarBattleCurse.None)
                    ClearGrammarCurse();
                return true;
            default:
                return false;
        }
    }

    bool TryHandleConjunctionPhrase(string phrase, PronunciationInsightResult? pronunciationInsight)
    {
        if (!TacticalGrammarBattleController.TryBuildConjunctionClauses(registry, phrase, out string conjunction, out List<string> clauses, out string rejectionMessage))
            return false;

        if (clauses.Count == 0)
        {
            PublishStatus(rejectionMessage);
            return true;
        }

        if (conjunction == "BECAUSE")
        {
            bool handled = TryHandleSinglePhrase(clauses[0], pronunciationInsight, conjunction, false);
            if (handled)
                PublishStatus("Used BECAUSE to explain the battle action.");
            return handled;
        }

        if (conjunction == "OR")
        {
            foreach (string clause in clauses)
            {
                if (TryHandleSinglePhrase(clause, pronunciationInsight, conjunction, false))
                {
                    PublishStatus("Chose one OR battle action.");
                    return true;
                }
            }

            PublishStatus("Neither OR battle action worked.");
            return true;
        }

        bool firstHandled = TryHandleSinglePhrase(clauses[0], pronunciationInsight, conjunction, false);
        bool secondHandled = TryHandleSinglePhrase(clauses[1], pronunciationInsight, conjunction, false);
        if (firstHandled && secondHandled)
            PublishStatus("Chained verbs with AND.");
        return firstHandled || secondHandled;
    }

    public void ApplyGrammarCurse(GrammarBattleCurse curse)
    {
        activeCurse = curse;
        if (curse != GrammarBattleCurse.None)
            PublishStatus($"Grammar curse: {FormatCurse(curse)}.");
    }

    public void ClearGrammarCurse()
    {
        if (activeCurse == GrammarBattleCurse.None)
            return;
        activeCurse = GrammarBattleCurse.None;
        PublishStatus("Grammar curse cleared.");
    }

    public void ClearActiveCreature()
    {
        ClearActiveCreature(preserveSummonContext: false);
    }

    void ClearActiveCreature(bool preserveSummonContext)
    {
        if (activeCreature != null)
            DisposeCreature(activeCreature.gameObject);
        activeCreature = null;
        if (!preserveSummonContext)
        {
            previousSummonedNounToken = "";
            previousSummonedAdjectiveToken = "";
            repeatedAdjectiveSwapCount = 0;
        }
    }

    public void ResetCombatVariety()
    {
        summonVariety.Clear();
        repeatedAdjectiveSwapCount = 0;
        previousSummonedNounToken = "";
        previousSummonedAdjectiveToken = "";
        lastAdjectiveSwapAt = -999f;
        activeCreature?.ResetCommandVariety();
    }

    static void DisposeCreature(GameObject creature)
    {
        if (creature == null)
            return;

        if (Application.isPlaying)
            Destroy(creature);
        else
            DestroyImmediate(creature);
    }

    public void NoteEnemyGrammarAction(EnemyAttackDefinition attack)
    {
        if (attack == null)
            return;

        lastEnemyNounFamily = CreaturePhraseUtility.NormalizeToken(attack.grammarNounFamily);
        lastEnemyActionVerb = CreaturePhraseUtility.NormalizeToken(attack.grammarVerb);
        lastEnemyGrammarCommand = attack.grammarCommand ?? "";
        lastEnemyGrammarPattern = attack.grammarPattern.ToString();
    }

    bool Summon(NounDefinition noun, ModifierDefinition adjective)
    {
        if (noun == null)
            return false;

        if (!noun.IsCreatureNoun)
        {
            PublishStatus($"{CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun)} is a {noun.nounRole.ToString().ToLowerInvariant()} noun, not a creature summon.");
            return false;
        }

        if (adjective != null && (!noun.AllowsAdjective(adjective.modifier) || !adjective.AllowsForNoun(noun)))
        {
            PublishStatus($"{CreaturePhraseUtility.NormalizeToken(adjective.modifier)} does not fit {CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun)}.");
            return false;
        }

        string nounToken = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
        string adjectiveToken = adjective != null ? CreaturePhraseUtility.NormalizeToken(adjective.modifier) : "";
        if (activeCreature != null &&
            !activeCreature.IsDefeated &&
            registry.AreInSameNounFamily(activeCreature.CanonicalNoun, nounToken) &&
            ResolveActiveAdjectiveToken(activeCreature) == adjectiveToken)
        {
            PublishStatus($"Already using {FormatSummonName(adjectiveToken, nounToken)}. Command it with a verb instead.");
            return false;
        }

        if (Time.time < nextSummonAt)
        {
            PublishStatus("Summon is cooling down. Use your active noun first.");
            return false;
        }

        CombatWordVarietyEvaluation adjectiveVariety = string.IsNullOrEmpty(adjectiveToken)
            ? new CombatWordVarietyEvaluation()
            : summonVariety.Evaluate(CombatWordRole.Adjective, adjectiveToken);
        float startingPpMultiplier = ResolveAdjectiveSwapStartingPpMultiplier(nounToken, adjectiveToken);
        ClearActiveCreature(preserveSummonContext: true);
        Vector3 position = ResolveSummonPosition();
        Quaternion rotation = Quaternion.LookRotation(ResolveForward(), Vector3.up);
        SummonedCreatureActor prefab = noun.prefabOverride;
        activeCreature = prefab != null
            ? Instantiate(prefab, position, rotation)
            : new GameObject($"{CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun)}_Summon").AddComponent<SummonedCreatureActor>();
        activeCreature.transform.SetPositionAndRotation(position, rotation);
        activeCreature.Configure(
            noun,
            adjective,
            learningProfile,
            ResolveFocusVocabulary(),
            adjectiveVariety.effectiveness);
        activeCreature.ApplyStartingPpMultiplier(startingPpMultiplier);
        if (!string.IsNullOrEmpty(adjectiveToken))
            summonVariety.Record(CombatWordRole.Adjective, adjectiveToken);
        previousSummonedNounToken = nounToken;
        previousSummonedAdjectiveToken = adjectiveToken;
        nextSummonAt = Time.time + Mathf.Max(0f, summonCooldownSeconds);

        string varietyFeedback = adjectiveVariety.BuildFeedback();
        string summonMessage = startingPpMultiplier < 0.995f
            ? $"Summoned {FormatSummonName(adjectiveToken, nounToken)} with unstable adjective energy. Starting PP reduced."
            : $"Summoned {FormatSummonName(adjectiveToken, nounToken)}.";
        PublishStatus(string.IsNullOrWhiteSpace(varietyFeedback)
            ? summonMessage
            : $"{summonMessage} {varietyFeedback}");
        return true;
    }

    float ResolveAdjectiveSwapStartingPpMultiplier(string nounToken, string adjectiveToken)
    {
        if (string.IsNullOrEmpty(adjectiveToken))
        {
            repeatedAdjectiveSwapCount = 0;
            return 1f;
        }

        bool changingAdjectiveOnSameNoun =
            !string.IsNullOrEmpty(previousSummonedNounToken) &&
            previousSummonedNounToken == nounToken &&
            previousSummonedAdjectiveToken != adjectiveToken;
        if (!changingAdjectiveOnSameNoun)
        {
            repeatedAdjectiveSwapCount = 0;
            // Adjectives intentionally trade some starting PP for their stat
            // effect. This keeps BIG/SMALL summons meaningful even before a
            // player begins swapping modifiers in one encounter.
            return Mathf.Clamp(1f - adjectiveSwapPpPenalty * 0.5f, 0.5f, 1f);
        }

        if (Time.time - lastAdjectiveSwapAt <= Mathf.Max(0f, adjectiveSwapFatigueWindowSeconds))
            repeatedAdjectiveSwapCount++;
        else
            repeatedAdjectiveSwapCount = 0;

        lastAdjectiveSwapAt = Time.time;
        float penalty = Mathf.Clamp01(adjectiveSwapPpPenalty + repeatedAdjectiveSwapCount * 0.15f);
        return Mathf.Clamp(1f - penalty, 0.25f, 1f);
    }

    static string ResolveActiveAdjectiveToken(SummonedCreatureActor creature)
    {
        return creature != null && creature.Adjective != null
            ? CreaturePhraseUtility.NormalizeToken(creature.Adjective.modifier)
            : "";
    }

    static string FormatSummonName(string adjectiveToken, string nounToken)
    {
        return string.IsNullOrEmpty(adjectiveToken)
            ? nounToken
            : $"{adjectiveToken} {nounToken}";
    }

    bool Command(VerbActionDefinition verb, ModifierDefinition adverb)
    {
        if (activeCreature == null)
        {
            PublishStatus("Summon a noun first.");
            return false;
        }

        SpellTarget target = ResolveTarget();
        bool success = activeCreature.TryUseVerb(verb, adverb, target, out string message);
        if (success)
            RegisterDefensiveResponse(verb, adverb);
        PublishStatus(message);

        string verbText = verb != null ? verb.verb : "";
        learningProfile?.RecordSpokenCast(verbText, success, false, 0f, null, Array.Empty<byte>(), false, false);
        return success;
    }

    void RegisterDefensiveResponse(VerbActionDefinition verb, ModifierDefinition adverb)
    {
        if (verb == null || activeCreature == null)
            return;

        float adverbSpeed = adverb != null ? Mathf.Max(0.05f, adverb.speedMultiplier) : 1f;
        float adverbEvasion = adverb != null ? Mathf.Max(0.05f, adverb.evasionMultiplier) : 1f;
        float varietyEffectiveness = Mathf.Clamp(activeCreature.LastVarietyEffectiveness, 0.1f, 1f);
        float duration = Mathf.Clamp(baseDefenseWindowSeconds * adverbSpeed * adverbEvasion * varietyEffectiveness, 0.35f, 2.25f);
        if (verb.role == BattleActionRole.Dodge)
        {
            dodgeWindowUntil = Mathf.Max(dodgeWindowUntil, Time.time + duration);
            PublishStatus($"{activeCreature.CanonicalNoun} is ready to dodge the next hit.");
            return;
        }

        if (verb.role == BattleActionRole.Defense)
        {
            float adverbDefense = adverb != null ? Mathf.Max(0.05f, adverb.defenseMultiplier) : 1f;
            defenseReduction = Mathf.Max(1, Mathf.RoundToInt(activeCreature.Stats.defense * adverbDefense * varietyEffectiveness));
            defenseWindowUntil = Mathf.Max(defenseWindowUntil, Time.time + duration);
            PublishStatus($"{activeCreature.CanonicalNoun} is guarding against the next hit.");
        }
    }

    public bool TryMitigateIncomingDamage(int incomingDamage, string source, out int mitigatedDamage, out string mitigationMessage)
    {
        mitigatedDamage = Mathf.Max(0, incomingDamage);
        mitigationMessage = "";
        if (incomingDamage <= 0)
            return false;

        ResolveReferences();
        if (tacticalBattle != null &&
            tacticalBattle.TryMitigateIncomingDamage(incomingDamage, out mitigatedDamage, out mitigationMessage))
            return true;

        if (Time.time <= dodgeWindowUntil)
        {
            dodgeWindowUntil = -999f;
            mitigatedDamage = 0;
            mitigationMessage = "dodged";
            PublishStatus("Dodged the incoming attack.");
            return true;
        }

        if (Time.time <= defenseWindowUntil && defenseReduction > 0)
        {
            defenseWindowUntil = -999f;
            int reduced = Mathf.Max(0, incomingDamage - defenseReduction);
            if (reduced != incomingDamage)
            {
                // A guarding creature keeps the hit away from the learner.
                // The unblocked remainder is absorbed by the creature instead
                // of leaking through to player HP, so defense remains a clear
                // protective command rather than a partial UI-only effect.
                if (reduced > 0 && activeCreature != null)
                {
                    activeCreature.TakeDamage(reduced, out string creatureMessage);
                    if (!string.IsNullOrWhiteSpace(creatureMessage))
                        PublishStatus(creatureMessage);
                }

                mitigatedDamage = 0;
                mitigationMessage = reduced > 0
                    ? $"blocked {incomingDamage - reduced} and absorbed {reduced}"
                    : $"blocked {incomingDamage}";
                PublishStatus($"Blocked {incomingDamage - reduced} damage.");
                return true;
            }
        }

        if (activeCreature != null)
        {
            bool tookDamage = activeCreature.TakeDamage(incomingDamage, out string creatureMessage);
            if (tookDamage)
            {
                mitigatedDamage = 0;
                mitigationMessage = activeCreature.IsDefeated ? "active_noun_knocked_out" : "active_noun_absorbed_hit";
                PublishStatus(creatureMessage);
                if (activeCreature.IsDefeated)
                {
                    PublishStatus($"{activeCreature.CanonicalNoun} can no longer fight. Summon another noun.");
                    nextSummonAt = Time.time;
                }
                return true;
            }
        }

        return false;
    }

    IEnumerable<string> ResolveFocusVocabulary()
    {
        var words = new List<string>();
        WorldGoalAssignment goal = CurriculumSessionManager.Instance != null ? CurriculumSessionManager.Instance.CurrentWorldGoal : null;
        AddWords(words, goal?.focusVocabulary);

        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null ? GrammarWorldProgressService.Instance.Data : null;
        GrammarMapAreaState area = null;
        if (progress != null && !string.IsNullOrWhiteSpace(progress.currentAreaId) && progress.areas != null)
            area = progress.areas.Find(candidate => candidate != null && candidate.areaId == progress.currentAreaId);

        if (area != null)
        {
            NaturalGrammarRegion region = NaturalGrammarProgression.Resolve(area.grammarTopic, area.grammarTopicTier);
            AddWords(words, region?.vocabularyPool);
        }

        return words;
    }

    bool IsPhrasePatternUnlocked(GrammarPhrasePattern pattern, out string message)
    {
        message = "";
        GrammarWorldProgressService progressService = ResolveActiveProgressionService();
        if (progressService == null || progressService.IsGrammarPhrasePatternUnlocked(pattern))
            return true;

        message = $"{FormatPattern(pattern)} is still locked. Reach the town that teaches it first.";
        return false;
    }

    bool IsPhraseVocabularyUnlocked(CreaturePhraseParseResult parsed, out string message)
    {
        message = "";
        GrammarWorldProgressService progressService = ResolveActiveProgressionService();
        if (progressService == null)
            return true;

        var requiredWords = new List<string>();
        AddRequiredVocabulary(requiredWords, parsed);
        foreach (string word in requiredWords)
        {
            if (progressService.IsVocabularyUnlocked(word))
                continue;

            message = $"{word} is still locked. Reach the town that teaches it first.";
            return false;
        }

        return true;
    }

    GrammarWorldProgressService ResolveActiveProgressionService()
    {
        GrammarWorldProgressService service = FindAnyObjectByType<GrammarWorldProgressService>();
        if (service == null)
            return null;

        // A live tactical board is an explicit battle context even when it is
        // hosted in a dedicated battle scene rather than a Town/Route/Gym scene.
        if (tacticalBattle != null && tacticalBattle.IsActive)
            return service;

        GrammarWorldProgressData progress = service.Data;
        if (progress == null || string.IsNullOrWhiteSpace(progress.currentAreaId))
            return null;

        string currentAreaId = GrammarWorldProgressService.CanonicalizeAreaId(progress.currentAreaId);
        GrammarSceneController[] sceneControllers = FindObjectsByType<GrammarSceneController>(
            FindObjectsInactive.Exclude);
        foreach (GrammarSceneController controller in sceneControllers)
        {
            if (controller == null || !controller.isActiveAndEnabled)
                continue;

            string controllerAreaId = GrammarWorldProgressService.CanonicalizeAreaId(
                GrammarWorldProgressService.ResolveAreaId(controller));
            if (string.Equals(controllerAreaId, currentAreaId, StringComparison.OrdinalIgnoreCase))
                return service;
        }

        // A stale persistent service must not lock an isolated test, preview,
        // or non-world sandbox that has no scene owning its current area.
        return null;
    }

    static void AddRequiredVocabulary(List<string> words, CreaturePhraseParseResult parsed)
    {
        if (words == null)
            return;

        AddWords(words, parsed.noun != null ? new[] { parsed.noun.canonicalNoun } : null);
        AddWords(words, parsed.verb != null ? new[] { parsed.verb.verb } : null);
        AddWords(words, parsed.modifier != null ? new[] { parsed.modifier.modifier } : null);

        string subject = CreaturePhraseUtility.NormalizeToken(parsed.subject);
        if (string.IsNullOrEmpty(subject))
            return;

        bool subjectIsNoun = parsed.noun != null &&
            subject == CreaturePhraseUtility.NormalizeToken(parsed.noun.canonicalNoun);
        if (!subjectIsNoun)
            AddWords(words, new[] { subject });
    }

    static void AddWords(List<string> target, IEnumerable<string> values)
    {
        if (target == null || values == null)
            return;

        foreach (string value in values)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(value);
            if (!string.IsNullOrEmpty(normalized) && !target.Contains(normalized))
                target.Add(normalized);
        }
    }

    static List<string> ResolveActiveEncounterMasteryTags()
    {
        EnemyWaveDirector director = FindAnyObjectByType<EnemyWaveDirector>();
        WaveDescriptor descriptor = director != null ? director.CurrentWaveDescriptor : null;
        return descriptor != null && descriptor.masteryTags != null
            ? new List<string>(descriptor.masteryTags)
            : null;
    }

    bool IsPhraseAllowedByCurse(CreaturePhraseParseResult parsed, out string message)
    {
        message = "";
        switch (activeCurse)
        {
            case GrammarBattleCurse.None:
                return true;
            case GrammarBattleCurse.I:
                return RequirePronounSubject(parsed, "I", "The curse forces I. Say something like: I bite.", out message);
            case GrammarBattleCurse.You:
                return RequirePronounSubject(parsed, "YOU", "The curse forces you. Say something like: you bite.", out message);
            case GrammarBattleCurse.HeSheIt:
                if (parsed.pattern == GrammarPhrasePattern.PronounVerbPresent &&
                    (parsed.subject == "HE" || parsed.subject == "SHE" || parsed.subject == "IT"))
                    return true;
                message = "The curse forces he/she/it. Use the third-person form, like: he bites.";
                return false;
            case GrammarBattleCurse.They:
                return RequirePronounSubject(parsed, "THEY", "The curse forces they. Say something like: they bite.", out message);
            case GrammarBattleCurse.PastFog:
                if (parsed.pattern == GrammarPhrasePattern.PastTense)
                    return true;
                message = "Past Fog is active. Use past tense, like: rat bit.";
                return false;
            case GrammarBattleCurse.NowMist:
                if (parsed.pattern == GrammarPhrasePattern.ProgressiveTense)
                    return true;
                message = "Now Mist is active. Use am/is/are + -ing, like: rat is biting.";
                return false;
            default:
                return true;
        }
    }

    static bool RequirePronounSubject(CreaturePhraseParseResult parsed, string requiredSubject, string failureMessage, out string message)
    {
        if (parsed.pattern == GrammarPhrasePattern.PronounVerbPresent &&
            CreaturePhraseUtility.NormalizeToken(parsed.subject) == CreaturePhraseUtility.NormalizeToken(requiredSubject))
        {
            message = "";
            return true;
        }

        message = failureMessage;
        return false;
    }

    static string FormatPattern(GrammarPhrasePattern pattern)
    {
        return pattern switch
        {
            GrammarPhrasePattern.NounOnly => "Noun summons",
            GrammarPhrasePattern.DeterminerNoun => "Article + noun summons",
            GrammarPhrasePattern.DeterminerAdjectiveNoun => "Article + adjective + noun summons",
            GrammarPhrasePattern.AdjectiveNoun => "Adjective + noun summons",
            GrammarPhrasePattern.VerbOnly => "Verb commands",
            GrammarPhrasePattern.NounVerbPresent => "Noun + verb present commands",
            GrammarPhrasePattern.PronounVerbPresent => "Pronoun + verb present commands",
            GrammarPhrasePattern.VerbAdverb => "Verb + adverb commands",
            GrammarPhrasePattern.PastTense => "Past-tense commands",
            GrammarPhrasePattern.ProgressiveTense => "Present continuous commands",
            GrammarPhrasePattern.FullSentence => "Full sentence commands",
            _ => "That grammar pattern",
        };
    }

    static string FormatCurse(GrammarBattleCurse curse)
    {
        return curse switch
        {
            GrammarBattleCurse.I => "use I",
            GrammarBattleCurse.You => "use you",
            GrammarBattleCurse.HeSheIt => "use he/she/it",
            GrammarBattleCurse.They => "use they",
            GrammarBattleCurse.PastFog => "use past tense",
            GrammarBattleCurse.NowMist => "use am/is/are + -ing",
            _ => "none",
        };
    }

    static GrammarConceptId ResolveConceptForPattern(GrammarPhrasePattern pattern)
    {
        return pattern switch
        {
            GrammarPhrasePattern.NounOnly => GrammarConceptId.BasicNouns,
            GrammarPhrasePattern.VerbOnly => GrammarConceptId.BasicVerbs,
            GrammarPhrasePattern.NounVerbPresent => GrammarConceptId.BasicVerbs,
            GrammarPhrasePattern.DeterminerNoun => GrammarConceptId.Articles,
            GrammarPhrasePattern.PronounVerbPresent => GrammarConceptId.Pronouns,
            GrammarPhrasePattern.AdjectiveNoun => GrammarConceptId.Adjectives,
            GrammarPhrasePattern.DeterminerAdjectiveNoun => GrammarConceptId.Adjectives,
            _ => GrammarConceptId.None,
        };
    }

    static string BuildBattleTutorialMessage(GrammarConceptId conceptId, string baseMessage, CreaturePhraseParseResult parsed)
    {
        string clean = parsed.command != null && !string.IsNullOrWhiteSpace(parsed.command.canonicalText)
            ? parsed.command.canonicalText
            : parsed.canonicalText;
        string why = conceptId switch
        {
            GrammarConceptId.Articles => "Remember: a before a consonant sound, an before a vowel sound, and the for a specific noun.",
            GrammarConceptId.Pronouns => "Remember: the pronoun must match the command form, like he bites or they bite.",
            GrammarConceptId.Adjectives => "Remember: the adjective comes with the noun, like big rat.",
            GrammarConceptId.BasicVerbs => parsed.pattern == GrammarPhrasePattern.NounVerbPresent
                ? "Remember: say the creature and the action together, like rat bites or bird flies."
                : "Remember: after the noun is ready, use one action verb.",
            GrammarConceptId.BasicNouns => "Remember: start by saying the noun you want to summon.",
            _ => "",
        };
        if (string.IsNullOrWhiteSpace(why))
            return string.IsNullOrWhiteSpace(clean) ? baseMessage : $"{baseMessage} Try: {clean}.";
        return string.IsNullOrWhiteSpace(clean)
            ? $"{baseMessage} {why}"
            : $"{baseMessage} {why} Try: {clean}.";
    }

    string BuildCurseCorrection(CreaturePhraseParseResult parsed)
    {
        if (parsed.command != null && !string.IsNullOrWhiteSpace(parsed.command.canonicalText))
            return parsed.command.canonicalText;

        return activeCurse switch
        {
            GrammarBattleCurse.I => "I bite",
            GrammarBattleCurse.You => "You bite",
            GrammarBattleCurse.HeSheIt => "He bites",
            GrammarBattleCurse.They => "They bite",
            GrammarBattleCurse.PastFog => "Rat bit",
            GrammarBattleCurse.NowMist => "Rat is biting",
            _ => parsed.canonicalText,
        };
    }

    SpellTarget ResolveTarget()
    {
        ResolveReferences();
        if (aimAssist != null && aimAssist.TryGetSelectedTarget(out SpellTarget selected))
            return selected;

        SpellTarget[] targets = FindObjectsByType<SpellTarget>(FindObjectsInactive.Exclude);
        SpellTarget best = null;
        float bestDistance = float.MaxValue;
        Vector3 origin = transform.position;
        foreach (SpellTarget target in targets)
        {
            if (target == null || target.IsDefeated)
                continue;
            float distance = Vector3.Distance(origin, target.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
            }
        }

        return best;
    }

    Vector3 ResolveSummonPosition()
    {
        Transform origin = summonOrigin != null ? summonOrigin : transform;
        Vector3 forward = ResolveForward();
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        return origin.position + forward * summonForwardOffset + side * summonSideOffset;
    }

    Vector3 ResolveForward()
    {
        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 forward = camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
                return forward.normalized;
        }

        Vector3 ownForward = transform.forward;
        ownForward.y = 0f;
        return ownForward.sqrMagnitude > 0.001f ? ownForward.normalized : Vector3.forward;
    }

    void ResolveReferences()
    {
        registry ??= GetComponent<CreatureCombatRegistry>() ?? GetComponentInParent<CreatureCombatRegistry>() ?? FindAnyObjectByType<CreatureCombatRegistry>();
        if (registry == null)
            registry = gameObject.AddComponent<CreatureCombatRegistry>();
        tacticalBattle ??= GetComponent<TacticalGrammarBattleController>() ?? GetComponentInParent<TacticalGrammarBattleController>() ?? FindAnyObjectByType<TacticalGrammarBattleController>();
        aimAssist ??= GetComponent<PlayerAimAssist>() ?? GetComponentInParent<PlayerAimAssist>() ?? FindAnyObjectByType<PlayerAimAssist>();
        learningProfile ??= GetComponent<PlayerLearningProfile>() ?? GetComponentInParent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
        waveDirector ??= GetComponent<EnemyWaveDirector>() ?? GetComponentInParent<EnemyWaveDirector>() ?? FindAnyObjectByType<EnemyWaveDirector>();
        if (isActiveAndEnabled)
            SubscribeToEncounterLifecycle();
    }

    void SubscribeToEncounterLifecycle()
    {
        if (waveDirector == null)
            return;
        waveDirector.OnWaveStarted -= HandleWaveStarted;
        waveDirector.OnEncounterEnded -= HandleEncounterEnded;
        waveDirector.OnWaveStarted += HandleWaveStarted;
        waveDirector.OnEncounterEnded += HandleEncounterEnded;
    }

    void UnsubscribeFromEncounterLifecycle()
    {
        if (waveDirector == null)
            return;
        waveDirector.OnWaveStarted -= HandleWaveStarted;
        waveDirector.OnEncounterEnded -= HandleEncounterEnded;
    }

    void HandleWaveStarted(WaveDescriptor descriptor)
    {
        ResetCombatVariety();
    }

    void HandleEncounterEnded(WaveDescriptor descriptor, EncounterOutcome outcome)
    {
        ResetCombatVariety();
    }

    void PublishStatus(string message)
    {
        OnStatus?.Invoke(message);
        if (!string.IsNullOrWhiteSpace(message))
            Debug.Log($"[CreatureCombat] {message}", this);
    }
}
