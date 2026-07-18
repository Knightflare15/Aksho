using System.Collections.Generic;
using UnityEngine;

public partial class CreatureCombatRegistry : MonoBehaviour
{
    public List<string> GetVoiceKeywords()
    {
        var result = new List<string>();
        foreach (NounDefinition noun in Nouns)
        {
            if (noun == null)
                continue;
            AddUnique(result, noun.canonicalNoun);
            if (noun.synonyms != null)
                foreach (string synonym in noun.synonyms)
                    AddUnique(result, synonym);
        }

        foreach (VerbActionDefinition verb in Verbs)
        {
            if (verb == null)
                continue;

            AddUnique(result, verb.verb);
            foreach (string form in verb.EnumerateAllCommandForms())
                AddUnique(result, form);
            if (verb.aliases != null)
                foreach (string alias in verb.aliases)
                    AddUnique(result, alias);
        }

        foreach (ModifierDefinition modifier in Modifiers)
            if (modifier != null)
                AddUnique(result, modifier.modifier);

        AddUnique(result, "I");
        AddUnique(result, "YOU");
        AddUnique(result, "HE");
        AddUnique(result, "SHE");
        AddUnique(result, "IT");
        AddUnique(result, "WE");
        AddUnique(result, "THEY");
        AddUnique(result, "AM");
        AddUnique(result, "IS");
        AddUnique(result, "ARE");
        AddVoicePhraseTemplates(result);

        return result;
    }

    public List<string> GetVoiceKeywordsForProgression(
        IEnumerable<GrammarPhrasePattern> allowedPatterns,
        IEnumerable<string> allowedVocabulary)
    {
        HashSet<GrammarPhrasePattern> patterns = BuildAllowedPatternSet(allowedPatterns);
        HashSet<string> vocabulary = BuildAllowedVocabularySet(allowedVocabulary);
        var result = new List<string>();

        bool allowNounOnly = AllowsPattern(patterns, GrammarPhrasePattern.NounOnly);
        bool allowVerbOnly = AllowsPattern(patterns, GrammarPhrasePattern.VerbOnly);
        bool allowVerbAdverb = AllowsPattern(patterns, GrammarPhrasePattern.VerbAdverb);
        bool allowNounVerb = AllowsPattern(patterns, GrammarPhrasePattern.NounVerbPresent);
        bool allowPronounVerb = AllowsPattern(patterns, GrammarPhrasePattern.PronounVerbPresent);
        bool allowDeterminerNoun = AllowsPattern(patterns, GrammarPhrasePattern.DeterminerNoun);
        bool allowAdjectiveNoun = AllowsPattern(patterns, GrammarPhrasePattern.AdjectiveNoun);
        bool allowDeterminerAdjectiveNoun = AllowsPattern(patterns, GrammarPhrasePattern.DeterminerAdjectiveNoun);

        foreach (NounDefinition noun in Nouns)
        {
            if (noun == null || !IsAllowedWord(vocabulary, noun.canonicalNoun))
                continue;

            AddUnique(result, noun.canonicalNoun);
            if (allowNounOnly && noun.synonyms != null)
                AddUniqueRange(result, noun.synonyms);

            if (allowDeterminerNoun)
            {
                if (IsAllowedWord(vocabulary, "THE"))
                    AddUnique(result, $"THE {noun.canonicalNoun}");
                if (IsAllowedWord(vocabulary, ResolveIndefiniteArticle(noun.canonicalNoun)))
                    AddUnique(result, $"{ResolveIndefiniteArticle(noun.canonicalNoun)} {noun.canonicalNoun}");
            }

            if (allowNounVerb)
            {
                foreach (VerbActionDefinition verb in Verbs)
                {
                    if (verb == null || !noun.AllowsVerb(verb.verb) || !IsAllowedWord(vocabulary, verb.verb))
                        continue;
                    AddUnique(result, $"{noun.canonicalNoun} {ResolvePreferredThirdPerson(verb)}");
                }
            }
        }

        foreach (VerbActionDefinition verb in Verbs)
        {
            if (verb == null || !IsAllowedWord(vocabulary, verb.verb))
                continue;

            if (allowVerbOnly || allowPronounVerb)
                AddUnique(result, verb.verb);
            if (allowNounVerb || allowPronounVerb)
                AddUnique(result, ResolvePreferredThirdPerson(verb));

            if (verb.aliases != null && allowVerbOnly)
                AddUniqueRange(result, verb.aliases);

            if (allowVerbAdverb)
            {
                foreach (ModifierDefinition modifier in Modifiers)
                {
                    if (modifier == null ||
                        modifier.role != ModifierGrammarRole.Adverb ||
                        !verb.AllowsAdverb(modifier.modifier) ||
                        !modifier.AllowsForVerb(verb) ||
                        !IsAllowedWord(vocabulary, modifier.modifier))
                        continue;

                    AddUnique(result, $"{verb.verb} {modifier.modifier}");
                }
            }
        }

        if (allowAdjectiveNoun || allowDeterminerAdjectiveNoun)
        {
            foreach (ModifierDefinition adjective in Modifiers)
            {
                if (adjective == null ||
                    adjective.role != ModifierGrammarRole.Adjective ||
                    !IsAllowedWord(vocabulary, adjective.modifier))
                    continue;

                foreach (NounDefinition noun in Nouns)
                {
                    if (noun == null ||
                        !noun.AllowsAdjective(adjective.modifier) ||
                        !adjective.AllowsForNoun(noun) ||
                        !IsAllowedWord(vocabulary, noun.canonicalNoun))
                        continue;

                    if (allowAdjectiveNoun)
                        AddUnique(result, $"{adjective.modifier} {noun.canonicalNoun}");

                    if (allowDeterminerAdjectiveNoun)
                    {
                        if (IsAllowedWord(vocabulary, "THE"))
                            AddUnique(result, $"THE {adjective.modifier} {noun.canonicalNoun}");
                        string article = ResolveIndefiniteArticle(adjective.modifier);
                        if (IsAllowedWord(vocabulary, article))
                            AddUnique(result, $"{article} {adjective.modifier} {noun.canonicalNoun}");
                    }
                }
            }
        }

        if (allowPronounVerb)
        {
            AddUniqueIfAllowed(result, vocabulary, "I");
            AddUniqueIfAllowed(result, vocabulary, "YOU");
            AddUniqueIfAllowed(result, vocabulary, "HE");
            AddUniqueIfAllowed(result, vocabulary, "SHE");
            AddUniqueIfAllowed(result, vocabulary, "IT");
            AddUniqueIfAllowed(result, vocabulary, "WE");
            AddUniqueIfAllowed(result, vocabulary, "THEY");

            foreach (VerbActionDefinition verb in Verbs)
            {
                if (verb == null || !IsAllowedWord(vocabulary, verb.verb))
                    continue;

                if (IsAllowedWord(vocabulary, "I"))
                    AddUnique(result, $"I {verb.verb}");
                if (IsAllowedWord(vocabulary, "YOU"))
                    AddUnique(result, $"YOU {verb.verb}");
                if (IsAllowedWord(vocabulary, "WE"))
                    AddUnique(result, $"WE {verb.verb}");
                if (IsAllowedWord(vocabulary, "THEY"))
                    AddUnique(result, $"THEY {verb.verb}");
                if (IsAllowedWord(vocabulary, "HE"))
                    AddUnique(result, $"HE {ResolvePreferredThirdPerson(verb)}");
                if (IsAllowedWord(vocabulary, "SHE"))
                    AddUnique(result, $"SHE {ResolvePreferredThirdPerson(verb)}");
                if (IsAllowedWord(vocabulary, "IT"))
                    AddUnique(result, $"IT {ResolvePreferredThirdPerson(verb)}");
            }
        }

        return result;
    }

    void AddVoicePhraseTemplates(List<string> result)
    {
        foreach (NounDefinition noun in Nouns)
        {
            if (noun == null)
                continue;

            string nounToken = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
            foreach (ModifierDefinition modifier in Modifiers)
            {
                if (modifier == null ||
                    modifier.role != ModifierGrammarRole.Adjective ||
                    !noun.AllowsAdjective(modifier.modifier) ||
                    !modifier.AllowsForNoun(noun))
                    continue;

                AddUnique(result, $"{modifier.modifier} {nounToken}");
                AddUnique(result, $"THE {modifier.modifier} {nounToken}");
                AddUnique(result, $"{ResolveIndefiniteArticle(modifier.modifier)} {modifier.modifier} {nounToken}");
            }

            AddUnique(result, $"THE {nounToken}");
            AddUnique(result, $"{ResolveIndefiniteArticle(nounToken)} {nounToken}");

            foreach (VerbActionDefinition verb in Verbs)
            {
                if (verb == null || !noun.AllowsVerb(verb.verb))
                    continue;

                AddUnique(result, $"{nounToken} {ResolvePreferredThirdPerson(verb)}");
                AddUnique(result, $"{Pluralize(nounToken)} {verb.verb}");
                AddUnique(result, $"{nounToken} {ResolvePreferredPast(verb)}");
                AddUnique(result, $"{nounToken} IS {ResolvePreferredProgressive(verb)}");
            }
        }

        foreach (VerbActionDefinition verb in Verbs)
        {
            if (verb == null)
                continue;

            AddUnique(result, $"I {verb.verb}");
            AddUnique(result, $"YOU {verb.verb}");
            AddUnique(result, $"WE {verb.verb}");
            AddUnique(result, $"THEY {verb.verb}");
            AddUnique(result, $"HE {ResolvePreferredThirdPerson(verb)}");
            AddUnique(result, $"SHE {ResolvePreferredThirdPerson(verb)}");
            AddUnique(result, $"IT {ResolvePreferredThirdPerson(verb)}");
            AddUnique(result, $"I AM {ResolvePreferredProgressive(verb)}");
            AddUnique(result, $"HE IS {ResolvePreferredProgressive(verb)}");
            AddUnique(result, $"THEY ARE {ResolvePreferredProgressive(verb)}");

            foreach (ModifierDefinition modifier in Modifiers)
            {
                if (modifier != null &&
                    modifier.role == ModifierGrammarRole.Adverb &&
                    verb.AllowsAdverb(modifier.modifier) &&
                    modifier.AllowsForVerb(verb))
                {
                    AddUnique(result, $"{verb.verb} {modifier.modifier}");
                }
            }
        }
    }

    static HashSet<GrammarPhrasePattern> BuildAllowedPatternSet(IEnumerable<GrammarPhrasePattern> allowedPatterns)
    {
        var result = new HashSet<GrammarPhrasePattern>();
        if (allowedPatterns != null)
        {
            foreach (GrammarPhrasePattern pattern in allowedPatterns)
                result.Add(pattern);
        }

        if (result.Count > 0)
            return result;

        result.Add(GrammarPhrasePattern.NounOnly);
        result.Add(GrammarPhrasePattern.VerbOnly);
        result.Add(GrammarPhrasePattern.VerbAdverb);
        result.Add(GrammarPhrasePattern.NounVerbPresent);
        result.Add(GrammarPhrasePattern.DeterminerNoun);
        result.Add(GrammarPhrasePattern.PronounVerbPresent);
        result.Add(GrammarPhrasePattern.AdjectiveNoun);
        result.Add(GrammarPhrasePattern.DeterminerAdjectiveNoun);
        return result;
    }

    static HashSet<string> BuildAllowedVocabularySet(IEnumerable<string> allowedVocabulary)
    {
        var result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (allowedVocabulary == null)
            return result;

        foreach (string value in allowedVocabulary)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(value);
            if (!string.IsNullOrEmpty(normalized))
                result.Add(normalized);
        }

        return result;
    }

    static bool AllowsPattern(HashSet<GrammarPhrasePattern> allowedPatterns, GrammarPhrasePattern pattern)
    {
        return allowedPatterns == null || allowedPatterns.Count == 0 || allowedPatterns.Contains(pattern);
    }

    static bool IsAllowedWord(HashSet<string> allowedVocabulary, params string[] words)
    {
        if (allowedVocabulary == null || allowedVocabulary.Count == 0)
            return true;

        foreach (string word in words)
        {
            string normalized = CreaturePhraseUtility.NormalizeToken(word);
            if (string.IsNullOrEmpty(normalized))
                continue;
            if (!allowedVocabulary.Contains(normalized))
                return false;
        }

        return true;
    }

    static void AddUniqueIfAllowed(List<string> values, HashSet<string> allowedVocabulary, string value)
    {
        if (IsAllowedWord(allowedVocabulary, value))
            AddUnique(values, value);
    }
}
