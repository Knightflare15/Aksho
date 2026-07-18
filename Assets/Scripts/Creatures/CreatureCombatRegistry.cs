using System.Collections.Generic;
using UnityEngine;

public partial class CreatureCombatRegistry : MonoBehaviour
{
    const string DefaultCatalogResourcePath = "CreatureCombatCatalog_Main";

    public CreatureCombatCatalog catalog;

    CreatureCombatCatalog runtimeDefaultCatalog;

    public IReadOnlyList<NounDefinition> Nouns => GetCatalog().nouns;
    public IReadOnlyList<VerbActionDefinition> Verbs => GetCatalog().verbs;
    public IReadOnlyList<ModifierDefinition> Modifiers => GetCatalog().modifiers;

    void Awake()
    {
        EnsureCatalog();
    }

    void OnValidate()
    {
        EnsureCatalog();
    }

    public bool TryParsePhrase(string phrase, out CreaturePhraseParseResult result)
    {
        result = new CreaturePhraseParseResult
        {
            kind = CreaturePhraseKind.None,
            pattern = GrammarPhrasePattern.LetterOnly,
            originalText = phrase ?? "",
            command = new CreatureGrammarCommand(),
        };

        List<string> tokens = CreaturePhraseUtility.Tokenize(phrase);
        if (tokens.Count == 0)
            return false;

        if (tokens.Count == 1)
        {
            if (TryGetNoun(tokens[0], out NounDefinition noun))
            {
                result.kind = CreaturePhraseKind.NounSummon;
                result.pattern = GrammarPhrasePattern.NounOnly;
                result.noun = noun;
                result.canonicalText = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
                result.command = BuildCommand(
                    noun: noun.canonicalNoun,
                    subject: noun.canonicalNoun,
                    canonicalText: result.canonicalText);
                return true;
            }

            if (TryGetVerb(tokens[0], out VerbActionDefinition verb))
            {
                result.kind = CreaturePhraseKind.VerbCommand;
                result.pattern = GrammarPhrasePattern.VerbOnly;
                result.verb = verb;
                result.matchedVerbForm = CreaturePhraseUtility.NormalizeToken(tokens[0]);
                result.canonicalText = CreaturePhraseUtility.NormalizeToken(verb.verb);
                result.command = BuildCommand(
                    verb: verb.verb,
                    canonicalText: result.canonicalText,
                    tense: CreatureCommandTense.Bare);
                return true;
            }

            return false;
        }

        if (tokens.Count == 2)
        {
            if (TryParseDeterminerNoun(tokens[0], tokens[1], out NounDefinition determinedNoun, out string determinerCanonicalText))
            {
                result.kind = CreaturePhraseKind.NounSummon;
                result.pattern = GrammarPhrasePattern.DeterminerNoun;
                result.noun = determinedNoun;
                result.subject = tokens[0];
                result.canonicalText = determinerCanonicalText;
                result.command = BuildCommand(
                    subject: determinedNoun.canonicalNoun,
                    determiner: tokens[0],
                    noun: determinedNoun.canonicalNoun,
                    canonicalText: determinerCanonicalText);
                return true;
            }

            if (TryGetModifier(tokens[0], ModifierGrammarRole.Adjective, out ModifierDefinition adjective) &&
                TryGetNoun(tokens[1], out NounDefinition modifiedNoun) &&
                modifiedNoun.AllowsAdjective(adjective.modifier) &&
                adjective.AllowsForNoun(modifiedNoun))
            {
                result.kind = CreaturePhraseKind.NounSummon;
                result.pattern = GrammarPhrasePattern.AdjectiveNoun;
                result.noun = modifiedNoun;
                result.modifier = adjective;
                result.canonicalText = $"{CreaturePhraseUtility.NormalizeToken(adjective.modifier)} {CreaturePhraseUtility.NormalizeToken(modifiedNoun.canonicalNoun)}";
                result.command = BuildCommand(
                    subject: modifiedNoun.canonicalNoun,
                    noun: modifiedNoun.canonicalNoun,
                    adjectives: new[] { adjective.modifier },
                    canonicalText: result.canonicalText);
                return true;
            }

            if (TryGetVerb(tokens[0], out VerbActionDefinition modifiedVerb) &&
                TryGetModifier(tokens[1], ModifierGrammarRole.Adverb, out ModifierDefinition adverb))
            {
                result.kind = CreaturePhraseKind.VerbCommand;
                result.pattern = GrammarPhrasePattern.VerbAdverb;
                result.verb = modifiedVerb;
                result.modifier = adverb;
                result.matchedVerbForm = CreaturePhraseUtility.NormalizeToken(tokens[0]);
                result.canonicalText = $"{CreaturePhraseUtility.NormalizeToken(modifiedVerb.verb)} {CreaturePhraseUtility.NormalizeToken(adverb.modifier)}";
                result.command = BuildCommand(
                    verb: modifiedVerb.verb,
                    adverb: adverb.modifier,
                    canonicalText: result.canonicalText,
                    tense: CreatureCommandTense.Bare);
                return true;
            }

            if (TryParseSimplePresentCommand(tokens[0], tokens[1], out NounDefinition subjectNoun, out VerbActionDefinition presentVerb, out string canonicalText, out string matchedForm))
            {
                result.kind = CreaturePhraseKind.VerbCommand;
                result.pattern = IsPronounSubject(tokens[0])
                    ? GrammarPhrasePattern.PronounVerbPresent
                    : GrammarPhrasePattern.NounVerbPresent;
                result.noun = subjectNoun;
                result.verb = presentVerb;
                result.subject = tokens[0];
                result.matchedVerbForm = matchedForm;
                result.canonicalText = canonicalText;
                result.command = BuildCommand(
                    subject: tokens[0],
                    pronoun: IsPronounSubject(tokens[0]) ? tokens[0] : "",
                    noun: subjectNoun != null ? subjectNoun.canonicalNoun : "",
                    verb: presentVerb != null ? presentVerb.verb : "",
                    canonicalText: canonicalText,
                    tense: CreatureCommandTense.Present);
                return true;
            }

            if (TryParsePastCommand(tokens[0], tokens[1], out NounDefinition pastSubjectNoun, out VerbActionDefinition pastVerb, out string pastCanonicalText, out string pastMatchedForm))
            {
                result.kind = CreaturePhraseKind.VerbCommand;
                result.pattern = GrammarPhrasePattern.PastTense;
                result.noun = pastSubjectNoun;
                result.verb = pastVerb;
                result.subject = tokens[0];
                result.matchedVerbForm = pastMatchedForm;
                result.canonicalText = pastCanonicalText;
                result.command = BuildCommand(
                    subject: tokens[0],
                    pronoun: IsPronounSubject(tokens[0]) ? tokens[0] : "",
                    noun: pastSubjectNoun != null ? pastSubjectNoun.canonicalNoun : "",
                    verb: pastVerb != null ? pastVerb.verb : "",
                    canonicalText: pastCanonicalText,
                    tense: CreatureCommandTense.Past);
                return true;
            }
        }

        if (tokens.Count == 3)
        {
            if (TryParseDeterminerAdjectiveNoun(tokens[0], tokens[1], tokens[2], out NounDefinition determinedModifiedNoun, out ModifierDefinition determinedAdjective, out string determinedModifiedCanonicalText))
            {
                result.kind = CreaturePhraseKind.NounSummon;
                result.pattern = GrammarPhrasePattern.DeterminerAdjectiveNoun;
                result.noun = determinedModifiedNoun;
                result.modifier = determinedAdjective;
                result.subject = tokens[0];
                result.canonicalText = determinedModifiedCanonicalText;
                result.command = BuildCommand(
                    subject: determinedModifiedNoun.canonicalNoun,
                    determiner: tokens[0],
                    noun: determinedModifiedNoun.canonicalNoun,
                    adjectives: new[] { determinedAdjective.modifier },
                    canonicalText: determinedModifiedCanonicalText);
                return true;
            }

            if (TryParseProgressiveCommand(tokens[0], tokens[1], tokens[2], out NounDefinition progressiveSubjectNoun, out VerbActionDefinition progressiveVerb, out string progressiveCanonicalText, out string progressiveMatchedForm))
            {
                result.kind = CreaturePhraseKind.VerbCommand;
                result.pattern = GrammarPhrasePattern.ProgressiveTense;
                result.noun = progressiveSubjectNoun;
                result.verb = progressiveVerb;
                result.subject = tokens[0];
                result.matchedVerbForm = progressiveMatchedForm;
                result.canonicalText = progressiveCanonicalText;
                result.command = BuildCommand(
                    subject: tokens[0],
                    pronoun: IsPronounSubject(tokens[0]) ? tokens[0] : "",
                    noun: progressiveSubjectNoun != null ? progressiveSubjectNoun.canonicalNoun : "",
                    verb: progressiveVerb != null ? progressiveVerb.verb : "",
                    canonicalText: progressiveCanonicalText,
                    tense: CreatureCommandTense.Progressive);
                return true;
            }
        }

        return false;
    }

    static CreatureGrammarCommand BuildCommand(
        string subject = "",
        string determiner = "",
        string pronoun = "",
        string noun = "",
        string verb = "",
        string adverb = "",
        string directObject = "",
        string preposition = "",
        string conjunction = "",
        string tenseObject = "",
        string canonicalText = "",
        CreatureCommandTense tense = CreatureCommandTense.None,
        string[] adjectives = null)
    {
        return new CreatureGrammarCommand
        {
            subject = CreaturePhraseUtility.NormalizeToken(subject),
            determiner = CreaturePhraseUtility.NormalizeToken(determiner),
            pronoun = CreaturePhraseUtility.NormalizeToken(pronoun),
            noun = CreaturePhraseUtility.NormalizeToken(noun),
            verb = CreaturePhraseUtility.NormalizeToken(verb),
            adverb = CreaturePhraseUtility.NormalizeToken(adverb),
            directObject = CreaturePhraseUtility.NormalizeToken(directObject),
            preposition = CreaturePhraseUtility.NormalizeToken(preposition),
            conjunction = CreaturePhraseUtility.NormalizeToken(conjunction),
            tenseObject = CreaturePhraseUtility.NormalizeToken(tenseObject),
            canonicalText = canonicalText ?? "",
            tense = tense,
            adjectives = NormalizeWords(adjectives),
        };
    }

    static string[] NormalizeWords(IEnumerable<string> words)
    {
        if (words == null)
            return System.Array.Empty<string>();

        var normalized = new List<string>();
        foreach (string word in words)
        {
            string token = CreaturePhraseUtility.NormalizeToken(word);
            if (!string.IsNullOrEmpty(token))
                normalized.Add(token);
        }
        return normalized.ToArray();
    }

    public bool TryGetNoun(string value, out NounDefinition definition)
    {
        foreach (NounDefinition noun in Nouns)
        {
            if (noun != null && noun.Matches(value))
            {
                definition = noun;
                return true;
            }
        }

        definition = null;
        return false;
    }

    public bool TryGetVerb(string value, out VerbActionDefinition definition)
    {
        foreach (VerbActionDefinition verb in Verbs)
        {
            if (verb == null)
                continue;

            if (verb.Matches(value) || MatchesAnyForm(verb.EnumerateAllCommandForms(), value))
            {
                definition = verb;
                return true;
            }
        }

        definition = null;
        return false;
    }

    public bool TryGetModifier(string value, ModifierGrammarRole role, out ModifierDefinition definition)
    {
        foreach (ModifierDefinition modifier in Modifiers)
        {
            if (modifier != null && modifier.role == role && modifier.Matches(value))
            {
                definition = modifier;
                return true;
            }
        }

        definition = null;
        return false;
    }

    public bool TryResolveCanonicalNoun(string value, out string canonicalNoun)
    {
        if (TryGetNoun(value, out NounDefinition definition) && definition != null)
        {
            canonicalNoun = CreaturePhraseUtility.NormalizeToken(definition.canonicalNoun);
            return !string.IsNullOrEmpty(canonicalNoun);
        }

        canonicalNoun = CreaturePhraseUtility.NormalizeToken(value);
        return !string.IsNullOrEmpty(canonicalNoun);
    }

    public bool AreInSameNounFamily(string first, string second)
    {
        if (!TryResolveCanonicalNoun(first, out string firstCanonical) ||
            !TryResolveCanonicalNoun(second, out string secondCanonical))
        {
            return false;
        }

        return string.Equals(
            firstCanonical,
            secondCanonical,
            System.StringComparison.OrdinalIgnoreCase);
    }

    public List<string> GetNounFamilyForms(string value)
    {
        var result = new List<string>();
        if (!TryGetNoun(value, out NounDefinition definition) || definition == null)
            return result;

        foreach (string form in definition.AcceptedForms())
            AddUnique(result, form);
        return result;
    }

    public static string ResolveCanonicalNounInScene(string value)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (string.IsNullOrEmpty(normalized))
            return "";

        CreatureCombatRegistry registry = FindAnyObjectByType<CreatureCombatRegistry>();
        if (registry != null && registry.TryResolveCanonicalNoun(normalized, out string canonical))
            return canonical;

        return normalized;
    }

    CreatureCombatCatalog GetCatalog()
    {
        EnsureCatalog();
        return catalog != null ? catalog : runtimeDefaultCatalog;
    }

    void EnsureCatalog()
    {
        if (catalog == null)
            catalog = Resources.Load<CreatureCombatCatalog>(DefaultCatalogResourcePath);
        if (catalog == null)
            runtimeDefaultCatalog ??= CreatureCombatCatalog.CreateRuntimeDefault();
    }
}
