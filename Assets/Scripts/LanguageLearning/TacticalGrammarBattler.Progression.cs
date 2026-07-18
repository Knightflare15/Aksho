using System;
using System.Collections.Generic;
using UnityEngine;


public sealed partial class TacticalGrammarBattler
{
    bool IsPhraseAllowedByProgression(CreaturePhraseParseResult parsed, out string error)
    {
        error = "";
        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        if (!ShouldApplyProgressionGate(progress))
            return true;

        if (!progress.IsGrammarPhrasePatternUnlocked(parsed.pattern))
        {
            error = $"That grammar form is not unlocked yet: {parsed.pattern}.";
            return false;
        }

        if (parsed.noun != null && !progress.IsVocabularyUnlocked(parsed.noun.canonicalNoun))
        {
            error = $"The word {parsed.noun.canonicalNoun} is not unlocked yet.";
            return false;
        }
        if (parsed.verb != null && !progress.IsVocabularyUnlocked(parsed.verb.verb))
        {
            error = $"The verb {parsed.verb.verb} is not unlocked yet.";
            return false;
        }
        if (parsed.modifier != null && !progress.IsVocabularyUnlocked(parsed.modifier.modifier))
        {
            error = $"The word {parsed.modifier.modifier} is not unlocked yet.";
            return false;
        }

        return true;
    }

    bool IsPhraseAllowedByProgression(ParsedActionPhrase action, out string error)
    {
        error = "";
        GrammarWorldProgressService progress = GrammarWorldProgressService.Instance;
        if (!ShouldApplyProgressionGate(progress))
            return true;

        GrammarPhrasePattern pattern = !string.IsNullOrWhiteSpace(action.preposition) || !string.IsNullOrWhiteSpace(action.direction)
            ? GrammarPhrasePattern.FullSentence
            : action.adverbModifier != null
                ? GrammarPhrasePattern.VerbAdverb
                : !string.IsNullOrWhiteSpace(action.subjectNoun)
                    ? GrammarPhrasePattern.NounVerbPresent
                    : GrammarPhrasePattern.VerbOnly;

        if (!progress.IsGrammarPhrasePatternUnlocked(pattern))
        {
            error = $"That grammar form is not unlocked yet: {pattern}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(action.subjectNoun) &&
            !progress.IsVocabularyUnlocked(action.subjectNoun))
        {
            error = $"The word {action.subjectNoun} is not unlocked yet.";
            return false;
        }
        if (action.verbDefinition != null &&
            !progress.IsVocabularyUnlocked(action.verbDefinition.verb))
        {
            error = $"The verb {action.verbDefinition.verb} is not unlocked yet.";
            return false;
        }
        if (action.adverbModifier != null &&
            !progress.IsVocabularyUnlocked(action.adverbModifier.modifier))
        {
            error = $"The word {action.adverbModifier.modifier} is not unlocked yet.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(action.preposition) &&
            !progress.IsVocabularyUnlocked(action.preposition))
        {
            error = $"The preposition {action.preposition} is not unlocked yet.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(action.direction) &&
            !progress.IsVocabularyUnlocked(action.direction))
        {
            error = $"The direction word {action.direction} is not unlocked yet.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(action.objectToken) &&
            !progress.IsVocabularyUnlocked(action.objectToken))
        {
            error = $"The word {action.objectToken} is not unlocked yet.";
            return false;
        }

        return true;
    }

    static bool ShouldApplyProgressionGate(GrammarWorldProgressService progress)
    {
        if (progress == null || progress.Data == null || string.IsNullOrWhiteSpace(progress.Data.currentAreaId))
            return false;

        string[] parts = progress.Data.currentAreaId.Split(':');
        if (parts.Length < 3 ||
            (!string.Equals(parts[0], SemanticZoneKind.Route.ToString(), StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(parts[0], SemanticZoneKind.Gym.ToString(), StringComparison.OrdinalIgnoreCase)))
            return false;

        int tier = int.TryParse(parts[2], out int parsedTier) ? parsedTier : 1;
        NaturalGrammarRegion region = NaturalGrammarProgression.Resolve("", tier);
        return region != null && region.combatUnlocked;
    }

    struct ParsedActionPhrase
    {
        public string subjectNoun;
        public string verb;
        public VerbActionDefinition verbDefinition;
        public ModifierDefinition adverbModifier;
        public string direction;
        public string preposition;
        public string objectToken;
    }
}
