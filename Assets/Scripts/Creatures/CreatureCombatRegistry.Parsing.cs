using System.Collections.Generic;
using UnityEngine;

public partial class CreatureCombatRegistry : MonoBehaviour
{
    bool TryParseDeterminerNoun(string determinerToken, string nounToken, out NounDefinition noun, out string canonicalText)
    {
        noun = null;
        canonicalText = "";
        string determiner = CreaturePhraseUtility.NormalizeToken(determinerToken);
        if (determiner != "A" && determiner != "AN" && determiner != "THE")
            return false;

        if (!TryGetNoun(nounToken, out noun))
            return false;

        string canonicalNoun = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
        if (determiner != "THE" && determiner != ResolveIndefiniteArticle(canonicalNoun))
            return false;

        canonicalText = $"{determiner} {canonicalNoun}";
        return true;
    }

    bool TryParseDeterminerAdjectiveNoun(
        string determinerToken,
        string adjectiveToken,
        string nounToken,
        out NounDefinition noun,
        out ModifierDefinition adjective,
        out string canonicalText)
    {
        noun = null;
        adjective = null;
        canonicalText = "";
        string determiner = CreaturePhraseUtility.NormalizeToken(determinerToken);
        if (determiner != "A" && determiner != "AN" && determiner != "THE")
            return false;

        if (!TryGetModifier(adjectiveToken, ModifierGrammarRole.Adjective, out adjective) ||
            !TryGetNoun(nounToken, out noun))
            return false;

        if (!noun.AllowsAdjective(adjective.modifier) || !adjective.AllowsForNoun(noun))
            return false;

        string canonicalAdjective = CreaturePhraseUtility.NormalizeToken(adjective.modifier);
        string canonicalNoun = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
        if (determiner != "THE" && determiner != ResolveIndefiniteArticle(canonicalAdjective))
            return false;

        canonicalText = $"{determiner} {canonicalAdjective} {canonicalNoun}";
        return true;
    }

    bool TryParseSimplePresentCommand(
        string subjectToken,
        string verbToken,
        out NounDefinition subjectNoun,
        out VerbActionDefinition verb,
        out string canonicalText,
        out string matchedForm)
    {
        subjectNoun = null;
        verb = null;
        canonicalText = "";
        matchedForm = "";

        string subject = CreaturePhraseUtility.NormalizeToken(subjectToken);
        string spokenVerb = CreaturePhraseUtility.NormalizeToken(verbToken);
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(spokenVerb))
            return false;

        bool thirdPersonSingular;
        bool subjectRecognized = TryResolvePresentSubject(subject, out subjectNoun, out thirdPersonSingular, out string canonicalSubject);
        if (!subjectRecognized)
            return false;

        foreach (VerbActionDefinition candidate in Verbs)
        {
            if (candidate == null)
                continue;

            if (subjectNoun != null && !subjectNoun.AllowsVerb(candidate.verb))
                continue;

            if (MatchesPresentVerb(candidate, spokenVerb, thirdPersonSingular))
            {
                verb = candidate;
                matchedForm = spokenVerb;
                canonicalText = $"{canonicalSubject} {CreaturePhraseUtility.NormalizeToken(spokenVerb)}";
                return true;
            }
        }

        return false;
    }

    bool TryParsePastCommand(
        string subjectToken,
        string verbToken,
        out NounDefinition subjectNoun,
        out VerbActionDefinition verb,
        out string canonicalText,
        out string matchedForm)
    {
        subjectNoun = null;
        verb = null;
        canonicalText = "";
        matchedForm = "";

        string subject = CreaturePhraseUtility.NormalizeToken(subjectToken);
        string spokenVerb = CreaturePhraseUtility.NormalizeToken(verbToken);
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(spokenVerb))
            return false;

        if (!TryResolvePresentSubject(subject, out subjectNoun, out _, out string canonicalSubject))
            return false;

        foreach (VerbActionDefinition candidate in Verbs)
        {
            if (candidate == null)
                continue;
            if (subjectNoun != null && !subjectNoun.AllowsVerb(candidate.verb))
                continue;
            if (!MatchesPastVerb(candidate, spokenVerb))
                continue;

            verb = candidate;
            matchedForm = spokenVerb;
            canonicalText = $"{canonicalSubject} {spokenVerb}";
            return true;
        }

        return false;
    }

    bool TryParseProgressiveCommand(
        string subjectToken,
        string auxiliaryToken,
        string verbToken,
        out NounDefinition subjectNoun,
        out VerbActionDefinition verb,
        out string canonicalText,
        out string matchedForm)
    {
        subjectNoun = null;
        verb = null;
        canonicalText = "";
        matchedForm = "";

        string subject = CreaturePhraseUtility.NormalizeToken(subjectToken);
        string auxiliary = CreaturePhraseUtility.NormalizeToken(auxiliaryToken);
        string spokenVerb = CreaturePhraseUtility.NormalizeToken(verbToken);
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(auxiliary) || string.IsNullOrEmpty(spokenVerb))
            return false;

        if (!TryResolvePresentSubject(subject, out subjectNoun, out bool thirdPersonSingular, out string canonicalSubject))
            return false;

        if (!MatchesProgressiveAuxiliary(subject, thirdPersonSingular, auxiliary))
            return false;

        foreach (VerbActionDefinition candidate in Verbs)
        {
            if (candidate == null)
                continue;
            if (subjectNoun != null && !subjectNoun.AllowsVerb(candidate.verb))
                continue;
            if (!MatchesProgressiveVerb(candidate, spokenVerb))
                continue;

            verb = candidate;
            matchedForm = spokenVerb;
            canonicalText = $"{canonicalSubject} {auxiliary} {spokenVerb}";
            return true;
        }

        return false;
    }

    bool TryResolvePresentSubject(
        string subject,
        out NounDefinition noun,
        out bool thirdPersonSingular,
        out string canonicalSubject)
    {
        noun = null;
        thirdPersonSingular = false;
        canonicalSubject = subject;

        switch (subject)
        {
            case "I":
            case "YOU":
            case "WE":
            case "THEY":
                thirdPersonSingular = false;
                return true;
            case "HE":
            case "SHE":
            case "IT":
                thirdPersonSingular = true;
                return true;
        }

        if (TryGetNoun(subject, out noun))
        {
            thirdPersonSingular = true;
            canonicalSubject = CreaturePhraseUtility.NormalizeToken(noun.canonicalNoun);
            return true;
        }

        if (TryGetSingularFromPluralSubject(subject, out noun))
        {
            thirdPersonSingular = false;
            canonicalSubject = subject;
            return true;
        }

        return false;
    }

    bool TryGetSingularFromPluralSubject(string subject, out NounDefinition noun)
    {
        noun = null;
        if (string.IsNullOrWhiteSpace(subject) || subject.Length <= 1 || !subject.EndsWith("S"))
            return false;

        string singular = subject.EndsWith("IES") && subject.Length > 3
            ? subject.Substring(0, subject.Length - 3) + "Y"
            : subject.EndsWith("ES") && subject.Length > 2
                ? subject.Substring(0, subject.Length - 2)
                : subject.Substring(0, subject.Length - 1);

        return TryGetNoun(singular, out noun);
    }

    static bool MatchesPresentVerb(VerbActionDefinition verb, string spokenVerb, bool thirdPersonSingular)
    {
        return thirdPersonSingular
            ? MatchesAnyForm(verb.GetThirdPersonSingularForms(), spokenVerb)
            : MatchesAnyForm(verb.GetPluralPresentForms(), spokenVerb);
    }

    static bool MatchesPastVerb(VerbActionDefinition verb, string spokenVerb)
    {
        return MatchesAnyForm(verb.GetPastTenseForms(), spokenVerb);
    }

    static bool MatchesProgressiveVerb(VerbActionDefinition verb, string spokenVerb)
    {
        return MatchesAnyForm(verb.GetProgressiveForms(), spokenVerb);
    }

    static bool MatchesAnyForm(IEnumerable<string> forms, string spokenVerb)
    {
        if (forms == null)
            return false;

        string normalized = CreaturePhraseUtility.NormalizeToken(spokenVerb);
        foreach (string form in forms)
        {
            if (CreaturePhraseUtility.NormalizeToken(form) == normalized)
                return true;
        }

        return false;
    }

    static bool MatchesProgressiveAuxiliary(string subject, bool thirdPersonSingular, string auxiliary)
    {
        string normalizedSubject = CreaturePhraseUtility.NormalizeToken(subject);
        string normalizedAuxiliary = CreaturePhraseUtility.NormalizeToken(auxiliary);
        if (normalizedSubject == "I")
            return normalizedAuxiliary == "AM";
        if (normalizedSubject == "YOU" || normalizedSubject == "WE" || normalizedSubject == "THEY")
            return normalizedAuxiliary == "ARE";
        return thirdPersonSingular && normalizedAuxiliary == "IS";
    }

    static string ResolvePreferredPast(VerbActionDefinition verb)
    {
        if (verb != null)
        {
            foreach (string form in verb.GetPastTenseForms())
                return CreaturePhraseUtility.NormalizeToken(form);
        }

        return "";
    }

    static string ResolvePreferredThirdPerson(VerbActionDefinition verb)
    {
        if (verb != null)
        {
            foreach (string form in verb.GetThirdPersonSingularForms())
                return CreaturePhraseUtility.NormalizeToken(form);
        }

        return "";
    }

    static string ResolvePreferredProgressive(VerbActionDefinition verb)
    {
        if (verb != null)
        {
            foreach (string form in verb.GetProgressiveForms())
                return CreaturePhraseUtility.NormalizeToken(form);
        }

        return "";
    }

    static string ResolveIndefiniteArticle(string nounToken)
    {
        string token = CreaturePhraseUtility.NormalizeToken(nounToken);
        if (string.IsNullOrEmpty(token))
            return "A";

        char first = token[0];
        return first == 'A' || first == 'E' || first == 'I' || first == 'O' || first == 'U'
            ? "AN"
            : "A";
    }

    static string Pluralize(string noun)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(noun);
        if (string.IsNullOrEmpty(normalized))
            return "";
        if (normalized.EndsWith("S"))
            return normalized;
        if (normalized.EndsWith("Y") && normalized.Length > 1 && !"AEIOU".Contains(normalized[normalized.Length - 2].ToString()))
            return normalized.Substring(0, normalized.Length - 1) + "IES";
        if (normalized.EndsWith("CH") || normalized.EndsWith("SH") || normalized.EndsWith("X") || normalized.EndsWith("Z"))
            return normalized + "ES";
        return normalized + "S";
    }

    static bool IsPronounSubject(string subject)
    {
        switch (CreaturePhraseUtility.NormalizeToken(subject))
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
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (!string.IsNullOrEmpty(normalized) && !values.Contains(normalized))
            values.Add(normalized);
    }

    static void AddUniqueRange(List<string> values, IEnumerable<string> source)
    {
        if (source == null)
            return;
        foreach (string value in source)
            AddUnique(values, value);
    }
}
