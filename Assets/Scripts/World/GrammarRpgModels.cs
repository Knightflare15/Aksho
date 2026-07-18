using System;
using System.Collections.Generic;
using UnityEngine;

public enum GrammarPhrasePattern
{
    LetterOnly,
    NounOnly,
    DeterminerNoun,
    DeterminerAdjectiveNoun,
    AdjectiveNoun,
    VerbOnly,
    NounVerbPresent,
    PronounVerbPresent,
    VerbAdverb,
    PastTense,
    ProgressiveTense,
    FullSentence,
}

public enum GrammarConceptId
{
    None,
    Greetings,
    Alphabet,
    VowelsConsonants,
    SentenceStartEnd,
    BasicNouns,
    BasicVerbs,
    Articles,
    Pronouns,
    Plurals,
    Adjectives,
    BasicPrepositions,
}

public enum GrammarEncounterMode
{
    None,
    LetterRecognition,
    NounRecognition,
    TacticalCommand,
}

public enum TutorHintLevel
{
    None,
    DirectCorrection,
    RuleHint,
    MicroLesson,
}

public enum TutorRemediationStep
{
    None,
    Retry,
    GuidedRetry,
    ExampleDrill,
}

public enum GrammarBattleCurse
{
    None,
    I,
    You,
    HeSheIt,
    They,
    PastFog,
    NowMist,
}

public enum GrammarDialogueInputMode
{
    None,
    SpeakOnly,
    WriteOnly,
    SpeakOrWrite,
    SpeakAndWrite,
}

public enum GrammarDialogueMalfunctionType
{
    None,
    MissingWord,
    ScrambledSentence,
    PartialTranscript,
    HeardWrong,
}

public enum GrammarPracticeScaffoldMode
{
    AuthoredSubtitle,
    JumbledWords,
    FillInBlank,
    CorrectTranscript,
    PartialTranscript,
    NoSubtitleGym,
}

public enum TranslatorBuddyUseCase
{
    None,
    AuthoredLocalExplanation,
    AdaptiveHint,
    ResponseCoach,
    GrimoireCoach,
    TeacherReportOnly,
}

public enum BattleActionRole
{
    Attack,
    Defense,
    Mobility,
    Utility,
    Curse,
    Offense = Attack,
    Dodge = Mobility,
}

public enum GrammarNounRole
{
    Creature,
    Object,
    Place,
}

[Serializable]
public sealed class TutorFeedbackPlan
{
    public GrammarConceptId conceptId = GrammarConceptId.None;
    public string errorCategory = "";
    public string subskillId = "";
    public string observedResponse = "";
    public string whatWasWrong = "";
    public string why = "";
    public string correctedResponse = "";
    public string microLesson = "";
    public TutorHintLevel hintLevelShown = TutorHintLevel.None;
    public TutorRemediationStep remediationStep = TutorRemediationStep.None;
    public int missCount;
}

[Serializable]
public class GrammarRegionDefinition
{
    public string id;
    public string displayName;
    public GrammarConceptId conceptId = GrammarConceptId.None;
    public string grammarTopic;
    public string grammarFocus;
    public string focus;
    public int tier;
    public GrammarEncounterMode encounterMode = GrammarEncounterMode.None;
    public bool combatUnlocked;
    public TranslatorAssistMode assistMode = TranslatorAssistMode.Full;
    public GrammarPhrasePattern[] unlockedPhrasePatterns = Array.Empty<GrammarPhrasePattern>();
    public string[] vocabularyPool = Array.Empty<string>();
    public string[] currentNounFamilies = Array.Empty<string>();
    public string[] reviewNounFamilies = Array.Empty<string>();
    public string[] npcLessonIds = Array.Empty<string>();
    public string[] routePracticeIds = Array.Empty<string>();
    public string[] gymCheckIds = Array.Empty<string>();
    public string[] townNpcNames = Array.Empty<string>();
    public string[] routeNpcNames = Array.Empty<string>();
    public string gymLeaderName = "";
    public Color groundTint = default;
    public Color roadTint = default;
    public Color buildingTint = default;
    public Color accentTint = default;
    public GrammarBattleCurse[] newCurses = Array.Empty<GrammarBattleCurse>();
    public GrammarEncounterPoolDefinition[] wildEncounterPools = Array.Empty<GrammarEncounterPoolDefinition>();
    public GrammarEncounterPoolDefinition[] trainerBattlePools = Array.Empty<GrammarEncounterPoolDefinition>();
    public string[] masteryTags = Array.Empty<string>();

    public string EffectiveFocus => !string.IsNullOrWhiteSpace(grammarFocus) ? grammarFocus : focus ?? "";
}

[Serializable]
public sealed class BattleActionDefinition
{
    public string actionId = "bite";
    public string verb = "BITE";
    public BattleActionRole role = BattleActionRole.Attack;
    public List<string> compatibleNounFamilies = new List<string>();
    [Min(0)] public int basePower = 2;
    [Range(0f, 1f)] public float accuracy = 0.9f;
    [Min(0.05f)] public float speed = 1f;
    [Min(1)] public int ppCost = 2;
    [Min(0f)] public float cooldownSeconds = 0.7f;
    public GrammarPhrasePattern grammarPattern = GrammarPhrasePattern.VerbOnly;
    public List<string> pastTenseForms = new List<string>();
    public List<string> progressiveForms = new List<string>();
    public GrammarBattleCurse inflictedCurse = GrammarBattleCurse.None;

    public bool IsCompatibleWithNoun(string noun)
    {
        if (compatibleNounFamilies == null || compatibleNounFamilies.Count == 0)
            return true;

        string normalized = CreaturePhraseUtility.NormalizeToken(noun);
        foreach (string family in compatibleNounFamilies)
        {
            if (CreaturePhraseUtility.NormalizeToken(family) == normalized)
                return true;
        }

        return false;
    }
}

[Serializable]
public sealed class GrammarEncounterPoolDefinition
{
    public string poolId = "";
    public string displayName = "";
    public SemanticZoneKind zoneKind = SemanticZoneKind.Route;
    [Min(1)] public int enemyCount = 1;
    public List<string> nounFamilies = new List<string>();
    public List<GrammarPhrasePattern> practicePatterns = new List<GrammarPhrasePattern>();
    public List<string> masteryTags = new List<string>();
}

[Serializable]
public sealed class GrammarDialogueTaskDefinition
{
    public string taskId = "";
    public GrammarConceptId conceptId = GrammarConceptId.None;
    public string subskillId = "";
    [Tooltip("Optional in-world situation cue, such as a market stall or route sign, used to keep generated dialogue grounded in the current place.")]
    public string contextCue = "";
    [TextArea] public string npcLine = "";
    [TextArea] public string expectedResponse = "";
    public List<string> acceptedResponses = new List<string>();
    [Tooltip("Words that carry this task's grammar target. Fill-in-the-blank scaffolds prefer these over generic function words.")]
    public List<string> grammarFocusWords = new List<string>();
    [Tooltip("Plausible words that do not belong in the sentence-jumble answer.")]
    public List<string> jumbleDistractorWords = new List<string>();
    public GrammarDialogueInputMode inputMode = GrammarDialogueInputMode.SpeakOrWrite;
    public TranslatorAssistMode assistMode = TranslatorAssistMode.Full;
    public GrammarDialogueMalfunctionType malfunctionType = GrammarDialogueMalfunctionType.None;
    public GrammarPhrasePattern grammarPattern = GrammarPhrasePattern.LetterOnly;
    public GrammarPracticeScaffoldMode scaffoldMode = GrammarPracticeScaffoldMode.AuthoredSubtitle;
    public TranslatorBuddyUseCase buddyUseCase = TranslatorBuddyUseCase.AuthoredLocalExplanation;
    public bool allowAiHint = true;
    public bool openGrimoireOnWrongAnswer = true;
    [TextArea] public string teachingNote = "";
    [TextArea] public string localLanguageHint = "";
}

public static class GrammarRpgContentValidation
{
    public static List<string> ValidateRegions(
        IEnumerable<GrammarRegionDefinition> regions,
        CreatureCombatRegistry registry = null,
        IReadOnlyDictionary<string, GrammarDialogueTaskDefinition> dialogueTasks = null)
    {
        var issues = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedDialogueTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (regions == null)
        {
            issues.Add("No grammar regions are defined.");
            return issues;
        }

        foreach (GrammarRegionDefinition region in regions)
        {
            if (region == null)
            {
                issues.Add("A grammar region entry is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(region.id))
                issues.Add($"Tier {region.tier} is missing an id.");
            if (region.groundTint.a <= 0f)
                issues.Add($"{region.id} is missing a ground tint.");
            if (region.roadTint.a <= 0f)
                issues.Add($"{region.id} is missing a road tint.");
            if (region.buildingTint.a <= 0f)
                issues.Add($"{region.id} is missing a building tint.");
            if (region.accentTint.a <= 0f)
                issues.Add($"{region.id} is missing an accent tint.");
            else if (!ids.Add(region.id))
                issues.Add($"Grammar region id '{region.id}' is duplicated.");

            if (string.IsNullOrWhiteSpace(region.displayName))
                issues.Add($"Region '{region.id}' is missing a display name.");
            if (region.tier <= 0)
                issues.Add($"Region '{region.id}' has an invalid tier.");
            if (region.unlockedPhrasePatterns == null || region.unlockedPhrasePatterns.Length == 0)
                issues.Add($"Region '{region.id}' does not unlock any phrase patterns.");
            if (region.masteryTags == null || region.masteryTags.Length == 0)
                issues.Add($"Region '{region.id}' does not define teacher-visible mastery tags.");

            ValidateNonEmptyIds(region.npcLessonIds, "NPC lesson", region, issues);
            ValidateNonEmptyIds(region.routePracticeIds, "route practice", region, issues);
            ValidateNonEmptyIds(region.gymCheckIds, "gym check", region, issues);
            ValidateNonEmptyIds(region.masteryTags, "mastery tag", region, issues);
            ValidateDialogueReferences(region.npcLessonIds, "NPC lesson", region, registry, dialogueTasks, validatedDialogueTaskIds, issues);
            ValidateDialogueReferences(region.routePracticeIds, "route practice", region, registry, dialogueTasks, validatedDialogueTaskIds, issues);
            ValidateDialogueReferences(region.gymCheckIds, "gym check", region, registry, dialogueTasks, validatedDialogueTaskIds, issues);
            ValidateDialogueCoverage(region, dialogueTasks, issues);
            ValidateVocabulary(region, registry, issues);
            ValidateEncounterPools(region.wildEncounterPools, "wild encounter", region, registry, issues);
            ValidateEncounterPools(region.trainerBattlePools, "trainer battle", region, registry, issues);
            ValidateCombatScope(region, issues);
        }

        return issues;
    }

    static void ValidateNonEmptyIds(string[] values, string label, GrammarRegionDefinition region, List<string> issues)
    {
        if (values == null)
            return;

        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                issues.Add($"Region '{region.id}' contains an empty {label} id.");
        }
    }

    static void ValidateDialogueReferences(
        string[] values,
        string label,
        GrammarRegionDefinition region,
        CreatureCombatRegistry registry,
        IReadOnlyDictionary<string, GrammarDialogueTaskDefinition> dialogueTasks,
        HashSet<string> validatedDialogueTaskIds,
        List<string> issues)
    {
        if (dialogueTasks == null || values == null)
            return;

        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            string taskId = value.Trim();
            if (!dialogueTasks.TryGetValue(taskId, out GrammarDialogueTaskDefinition task) || task == null)
            {
                issues.Add($"Region '{region.id}' references unknown {label} dialogue task '{taskId}'.");
                continue;
            }

            if (validatedDialogueTaskIds.Add(taskId))
                ValidateDialogueTask(task, registry, issues);
        }
    }

    static void ValidateDialogueCoverage(
        GrammarRegionDefinition region,
        IReadOnlyDictionary<string, GrammarDialogueTaskDefinition> dialogueTasks,
        List<string> issues)
    {
        if (region == null || dialogueTasks == null)
            return;

        List<GrammarDialogueTaskDefinition> townTasks = ResolveRegionTasks(region.npcLessonIds, dialogueTasks);
        List<GrammarDialogueTaskDefinition> routeTasks = ResolveRegionTasks(region.routePracticeIds, dialogueTasks);
        List<GrammarDialogueTaskDefinition> gymTasks = ResolveRegionTasks(region.gymCheckIds, dialogueTasks);

        EnsureMinimumZoneCount(region, "town", townTasks, 4, issues);
        EnsureMinimumZoneCount(region, "route", routeTasks, 4, issues);
        EnsureMinimumZoneCount(region, "gym", gymTasks, 3, issues);

        ValidateZoneSupport(region, "town", townTasks, TranslatorAssistMode.Full, issues);
        ValidateZoneSupport(region, "route", routeTasks, TranslatorAssistMode.Partial, issues);
        ValidateZoneSupport(region, "gym", gymTasks, TranslatorAssistMode.Off, issues);

        EnsureAnyInputMode(region, "town", townTasks, issues, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueInputMode.SpeakOrWrite);
        EnsureAnyInputMode(region, "route", routeTasks, issues, GrammarDialogueInputMode.WriteOnly);
        EnsureAnyInputMode(region, "gym", gymTasks, issues, GrammarDialogueInputMode.SpeakOnly, GrammarDialogueInputMode.WriteOnly, GrammarDialogueInputMode.SpeakOrWrite);

        EnsureMinimumRouteVariety(region, routeTasks, issues);
        EnsurePrimaryPatternCoverage(region, "town", townTasks, issues);
        EnsurePrimaryPatternCoverage(region, "gym", gymTasks, issues);

        if (region.encounterMode != GrammarEncounterMode.None)
        {
            EnsureAnyInputMode(region, "route or gym", CombineTasks(routeTasks, gymTasks), issues, GrammarDialogueInputMode.SpeakOrWrite, GrammarDialogueInputMode.SpeakAndWrite);
        }
    }

    static List<GrammarDialogueTaskDefinition> ResolveRegionTasks(
        string[] taskIds,
        IReadOnlyDictionary<string, GrammarDialogueTaskDefinition> dialogueTasks)
    {
        var resolved = new List<GrammarDialogueTaskDefinition>();
        if (taskIds == null || dialogueTasks == null)
            return resolved;

        foreach (string taskId in taskIds)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                continue;
            if (dialogueTasks.TryGetValue(taskId.Trim(), out GrammarDialogueTaskDefinition task) && task != null)
                resolved.Add(task);
        }

        return resolved;
    }

    static List<GrammarDialogueTaskDefinition> CombineTasks(
        List<GrammarDialogueTaskDefinition> first,
        List<GrammarDialogueTaskDefinition> second)
    {
        var combined = new List<GrammarDialogueTaskDefinition>();
        if (first != null)
            combined.AddRange(first);
        if (second != null)
            combined.AddRange(second);
        return combined;
    }

    static void ValidateZoneSupport(
        GrammarRegionDefinition region,
        string zoneLabel,
        List<GrammarDialogueTaskDefinition> tasks,
        TranslatorAssistMode expectedAssistMode,
        List<string> issues)
    {
        if (tasks == null)
            return;

        foreach (GrammarDialogueTaskDefinition task in tasks)
        {
            if (task == null)
                continue;
            if (task.conceptId != GrammarConceptId.None && task.conceptId != region.conceptId)
            {
                issues.Add($"Region '{region.id}' {zoneLabel} task '{task.taskId}' uses concept '{task.conceptId}' instead of '{region.conceptId}'.");
            }
            if (task.assistMode != expectedAssistMode)
            {
                issues.Add($"Region '{region.id}' {zoneLabel} task '{task.taskId}' should use {expectedAssistMode} Buddy support.");
            }
            if (expectedAssistMode == TranslatorAssistMode.Off &&
                task.grammarPattern != GrammarPhrasePattern.LetterOnly &&
                task.grammarPattern != GrammarPhrasePattern.NounOnly &&
                PromptContainsExactAnswer(task.npcLine, task.expectedResponse))
            {
                issues.Add($"Region '{region.id}' {zoneLabel} task '{task.taskId}' exposes its exact assessment answer in the prompt.");
            }
        }
    }

    static bool PromptContainsExactAnswer(string prompt, string answer)
    {
        string normalizedPrompt = NormalizeAssessmentText(prompt);
        string normalizedAnswer = NormalizeAssessmentText(answer);
        return normalizedAnswer.Length >= 4 &&
               $" {normalizedPrompt} ".Contains($" {normalizedAnswer} ");
    }

    static string NormalizeAssessmentText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var builder = new System.Text.StringBuilder(value.Length);
        bool lastWasSpace = true;
        foreach (char character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    static void EnsureAnyInputMode(
        GrammarRegionDefinition region,
        string zoneLabel,
        List<GrammarDialogueTaskDefinition> tasks,
        List<string> issues,
        params GrammarDialogueInputMode[] expectedModes)
    {
        if (tasks == null || expectedModes == null || expectedModes.Length == 0)
            return;

        foreach (GrammarDialogueTaskDefinition task in tasks)
        {
            if (task == null)
                continue;
            foreach (GrammarDialogueInputMode mode in expectedModes)
            {
                if (task.inputMode == mode)
                    return;
            }
        }

        issues.Add($"Region '{region.id}' {zoneLabel} tasks are missing a {string.Join(" or ", expectedModes)} interaction.");
    }

    static void EnsureMinimumZoneCount(
        GrammarRegionDefinition region,
        string zoneLabel,
        List<GrammarDialogueTaskDefinition> tasks,
        int minimumCount,
        List<string> issues)
    {
        if (region == null || tasks == null)
            return;

        if (tasks.Count < minimumCount)
            issues.Add($"Region '{region.id}' {zoneLabel} needs at least {minimumCount} authored tasks for the Class 1-2 slice.");
    }

    static void EnsureMinimumRouteVariety(
        GrammarRegionDefinition region,
        List<GrammarDialogueTaskDefinition> routeTasks,
        List<string> issues)
    {
        if (routeTasks == null || routeTasks.Count == 0)
            return;

        var malfunctions = new HashSet<GrammarDialogueMalfunctionType>();
        foreach (GrammarDialogueTaskDefinition task in routeTasks)
        {
            if (task == null)
                continue;
            if (task.malfunctionType != GrammarDialogueMalfunctionType.None)
                malfunctions.Add(task.malfunctionType);
        }

        if (malfunctions.Count < 3)
            issues.Add($"Region '{region.id}' route practice needs at least three remediation-style malfunction patterns.");
    }

    static void EnsurePrimaryPatternCoverage(
        GrammarRegionDefinition region,
        string zoneLabel,
        List<GrammarDialogueTaskDefinition> tasks,
        List<string> issues)
    {
        if (region == null || tasks == null || tasks.Count == 0)
            return;

        GrammarPhrasePattern primaryPattern = ResolvePrimaryPattern(region);
        foreach (GrammarDialogueTaskDefinition task in tasks)
        {
            if (task != null && task.grammarPattern == primaryPattern)
                return;
        }

        issues.Add($"Region '{region.id}' {zoneLabel} tasks do not cover the region's primary pattern '{primaryPattern}'.");
    }

    static GrammarPhrasePattern ResolvePrimaryPattern(GrammarRegionDefinition region)
    {
        if (region == null || region.unlockedPhrasePatterns == null || region.unlockedPhrasePatterns.Length == 0)
            return GrammarPhrasePattern.LetterOnly;
        return region.unlockedPhrasePatterns[region.unlockedPhrasePatterns.Length - 1];
    }

    static void ValidateDialogueTask(
        GrammarDialogueTaskDefinition task,
        CreatureCombatRegistry registry,
        List<string> issues)
    {
        if (task == null)
            return;

        if (string.IsNullOrWhiteSpace(task.taskId))
            issues.Add("A dialogue task is missing an id.");
        if (string.IsNullOrWhiteSpace(task.npcLine))
            issues.Add($"Dialogue task '{task.taskId}' is missing an NPC line.");
        if (string.IsNullOrWhiteSpace(task.expectedResponse))
            issues.Add($"Dialogue task '{task.taskId}' is missing an expected response.");

        if (registry == null || !ShouldParseDialogueAnswer(task.grammarPattern))
            return;

        var responses = new List<string>();
        AddUnique(responses, task.expectedResponse);
        if (task.acceptedResponses != null)
        {
            foreach (string response in task.acceptedResponses)
                AddUnique(responses, response);
        }

        foreach (string response in responses)
        {
            if (AllowsSinglePronounIntro(task.grammarPattern, response))
                continue;

            if (!registry.TryParsePhrase(response, out CreaturePhraseParseResult parsed))
            {
                issues.Add($"Dialogue task '{task.taskId}' response '{response}' does not parse as {task.grammarPattern}.");
                continue;
            }

            if (parsed.pattern != task.grammarPattern)
            {
                issues.Add($"Dialogue task '{task.taskId}' response '{response}' parses as {parsed.pattern}, expected {task.grammarPattern}.");
                continue;
            }

            if (parsed.noun != null &&
                parsed.verb != null &&
                !parsed.noun.AllowsVerb(parsed.verb.verb))
            {
                string noun = CreaturePhraseUtility.NormalizeToken(parsed.noun.canonicalNoun);
                string verb = CreaturePhraseUtility.NormalizeToken(parsed.verb.verb);
                issues.Add($"Dialogue task '{task.taskId}' response '{response}' uses verb '{verb}' with incompatible noun '{noun}'.");
            }
        }
    }

    static bool ShouldParseDialogueAnswer(GrammarPhrasePattern pattern)
    {
        switch (pattern)
        {
            case GrammarPhrasePattern.NounOnly:
            case GrammarPhrasePattern.DeterminerNoun:
            case GrammarPhrasePattern.DeterminerAdjectiveNoun:
            case GrammarPhrasePattern.AdjectiveNoun:
            case GrammarPhrasePattern.VerbOnly:
            case GrammarPhrasePattern.NounVerbPresent:
            case GrammarPhrasePattern.PronounVerbPresent:
            case GrammarPhrasePattern.VerbAdverb:
            case GrammarPhrasePattern.PastTense:
            case GrammarPhrasePattern.ProgressiveTense:
                return true;
            default:
                return false;
        }
    }

    static bool AllowsSinglePronounIntro(GrammarPhrasePattern pattern, string response)
    {
        if (pattern != GrammarPhrasePattern.PronounVerbPresent)
            return false;

        List<string> tokens = CreaturePhraseUtility.Tokenize(response);
        return tokens.Count == 1 && IsKnownPronoun(tokens[0]);
    }

    static bool IsKnownPronoun(string token)
    {
        switch (CreaturePhraseUtility.NormalizeToken(token))
        {
            case "I":
            case "YOU":
            case "HE":
            case "SHE":
            case "IT":
            case "WE":
            case "THEY":
                return true;
            default:
                return false;
        }
    }

    static void AddUnique(List<string> values, string value)
    {
        if (values == null || string.IsNullOrWhiteSpace(value))
            return;

        string normalized = value.Trim();
        if (!values.Contains(normalized))
            values.Add(normalized);
    }

    static void ValidateVocabulary(GrammarRegionDefinition region, CreatureCombatRegistry registry, List<string> issues)
    {
        if (registry == null || region.vocabularyPool == null)
            return;

        HashSet<string> voiceKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string keyword in registry.GetVoiceKeywords())
            if (!string.IsNullOrWhiteSpace(keyword))
                voiceKeywords.Add(CreaturePhraseUtility.NormalizeToken(keyword));

        foreach (string value in region.vocabularyPool)
        {
            string token = CreaturePhraseUtility.NormalizeToken(value);
            if (string.IsNullOrEmpty(token))
            {
                issues.Add($"Region '{region.id}' contains empty vocabulary.");
                continue;
            }

            if (registry.TryGetNoun(token, out _) ||
                registry.TryGetVerb(token, out _) ||
                registry.TryGetModifier(token, ModifierGrammarRole.Adjective, out _) ||
                registry.TryGetModifier(token, ModifierGrammarRole.Adverb, out _))
                continue;

            if (voiceKeywords.Contains(token))
                continue;

            if (IsPluralOfKnownNoun(token, registry))
                continue;

            if (IsKnownFunctionWord(token))
                continue;

            issues.Add($"Region '{region.id}' references unknown vocabulary '{token}'.");
        }
    }

    static void ValidateEncounterPools(
        GrammarEncounterPoolDefinition[] pools,
        string label,
        GrammarRegionDefinition region,
        CreatureCombatRegistry registry,
        List<string> issues)
    {
        if (pools == null)
            return;

        if (region.encounterMode == GrammarEncounterMode.TacticalCommand && pools.Length < 2)
            issues.Add($"Tactical region '{region.id}' should define at least two {label} pools.");

        for (int i = 0; i < pools.Length; i++)
        {
            GrammarEncounterPoolDefinition pool = pools[i];
            if (pool == null)
            {
                issues.Add($"Region '{region.id}' has a null {label} pool at index {i}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pool.poolId))
                issues.Add($"Region '{region.id}' has a {label} pool without an id.");
            if (pool.enemyCount <= 0)
                issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' has invalid enemy count.");
            if (pool.nounFamilies == null || pool.nounFamilies.Count == 0)
                issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' has no noun families.");
            if (pool.masteryTags == null || pool.masteryTags.Count == 0)
                issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' has no mastery tags.");
            if (region.encounterMode != GrammarEncounterMode.None && (pool.practicePatterns == null || pool.practicePatterns.Count == 0))
                issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' has no practice patterns.");

            if (pool.practicePatterns != null)
            {
                foreach (GrammarPhrasePattern pattern in pool.practicePatterns)
                {
                    if (!RegionSupportsCombatPattern(region, pattern))
                        issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' uses pattern '{pattern}' before the region unlocks it.");
                    if (!IsAllowedFirstSliceCombatPattern(pattern))
                        issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' uses out-of-scope Class 3-5 combat pattern '{pattern}'.");
                }
            }

            if (registry == null || pool.nounFamilies == null)
                continue;

            foreach (string noun in pool.nounFamilies)
            {
                string normalized = CreaturePhraseUtility.NormalizeToken(noun);
                if (!registry.TryGetNoun(normalized, out NounDefinition nounDefinition))
                {
                    issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' references unknown noun family '{normalized}'.");
                    continue;
                }

                if (!nounDefinition.IsCreatureNoun)
                    issues.Add($"Region '{region.id}' {label} pool '{pool.poolId}' uses non-creature noun '{normalized}' as an encounter noun.");
            }
        }
    }

    static void ValidateCombatScope(GrammarRegionDefinition region, List<string> issues)
    {
        if (region == null)
            return;

        if (region.combatUnlocked && region.encounterMode != GrammarEncounterMode.TacticalCommand)
            issues.Add($"Region '{region.id}' has tactical combat unlocked outside TacticalCommand mode.");
        if (!region.combatUnlocked && region.encounterMode == GrammarEncounterMode.TacticalCommand)
            issues.Add($"Region '{region.id}' uses TacticalCommand mode without combatUnlocked.");

        if (region.encounterMode == GrammarEncounterMode.None)
        {
            if (region.wildEncounterPools != null && region.wildEncounterPools.Length > 0)
                issues.Add($"Non-encounter region '{region.id}' should not define wild encounter pools.");
            if (region.trainerBattlePools != null && region.trainerBattlePools.Length > 0)
                issues.Add($"Non-encounter region '{region.id}' should not define trainer battle pools.");
        }

        if (region.unlockedPhrasePatterns != null)
        {
            foreach (GrammarPhrasePattern pattern in region.unlockedPhrasePatterns)
            {
                if (!IsAllowedFirstSlicePattern(pattern))
                    issues.Add($"Region '{region.id}' unlocks out-of-scope Class 3-5 pattern '{pattern}'.");
            }
        }

        if (region.newCurses != null)
        {
            foreach (GrammarBattleCurse curse in region.newCurses)
            {
                if (!IsAllowedFirstSliceCurse(curse))
                    issues.Add($"Region '{region.id}' uses out-of-scope Class 3-5 curse '{curse}'.");
            }
        }
    }

    static bool RegionSupportsCombatPattern(GrammarRegionDefinition region, GrammarPhrasePattern pattern)
    {
        if (region == null || region.unlockedPhrasePatterns == null)
            return false;

        foreach (GrammarPhrasePattern candidate in region.unlockedPhrasePatterns)
        {
            if (candidate == pattern)
                return true;
        }

        return false;
    }

    static bool IsAllowedFirstSlicePattern(GrammarPhrasePattern pattern)
    {
        return pattern switch
        {
            GrammarPhrasePattern.LetterOnly => true,
            GrammarPhrasePattern.FullSentence => true,
            _ => IsAllowedFirstSliceCombatPattern(pattern),
        };
    }

    static bool IsAllowedFirstSliceCombatPattern(GrammarPhrasePattern pattern)
    {
        return pattern switch
        {
            GrammarPhrasePattern.NounOnly => true,
            GrammarPhrasePattern.VerbOnly => true,
            GrammarPhrasePattern.NounVerbPresent => true,
            GrammarPhrasePattern.FullSentence => true,
            GrammarPhrasePattern.DeterminerNoun => true,
            GrammarPhrasePattern.PronounVerbPresent => true,
            GrammarPhrasePattern.AdjectiveNoun => true,
            GrammarPhrasePattern.DeterminerAdjectiveNoun => true,
            _ => false,
        };
    }

    static bool IsAllowedFirstSliceCurse(GrammarBattleCurse curse)
    {
        return curse switch
        {
            GrammarBattleCurse.None => true,
            GrammarBattleCurse.I => true,
            GrammarBattleCurse.You => true,
            GrammarBattleCurse.HeSheIt => true,
            GrammarBattleCurse.They => true,
            _ => false,
        };
    }

    static bool IsKnownFunctionWord(string token)
    {
        switch (CreaturePhraseUtility.NormalizeToken(token))
        {
            case "A":
            case "AN":
            case "THE":
            case "I":
            case "YOU":
            case "HE":
            case "SHE":
            case "IT":
            case "WE":
            case "THEY":
            case "IS":
            case "ARE":
            case "AM":
            case "WAS":
            case "WERE":
            case "WILL":
            case "HELLO":
            case "GOODBYE":
            case "YES":
            case "NO":
            case "PLEASE":
            case "THANK":
            case "MY":
            case "NAME":
            case "READY":
            case "BESIDE":
            case "BEHIND":
            case "OVER":
            case "UNDER":
            case "NEAR":
            case "THROUGH":
                return true;
            default:
                string normalized = CreaturePhraseUtility.NormalizeToken(token);
                return normalized.Length == 1 && normalized[0] >= 'A' && normalized[0] <= 'Z';
        }
    }

    static bool IsPluralOfKnownNoun(string token, CreatureCombatRegistry registry)
    {
        if (registry == null || string.IsNullOrWhiteSpace(token))
            return false;

        string normalized = CreaturePhraseUtility.NormalizeToken(token);
        string singular = normalized.EndsWith("IES", StringComparison.Ordinal)
            ? normalized.Substring(0, normalized.Length - 3) + "Y"
            : normalized.EndsWith("ES", StringComparison.Ordinal)
                ? normalized.Substring(0, normalized.Length - 2)
                : normalized.EndsWith("S", StringComparison.Ordinal)
                    ? normalized.Substring(0, normalized.Length - 1)
                    : "";
        return !string.IsNullOrEmpty(singular) && registry.TryGetNoun(singular, out _);
    }
}
