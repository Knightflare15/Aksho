using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class TacticalGrammarBattleController : MonoBehaviour
{
    public static bool TryBuildConjunctionClauses(
        CreatureCombatRegistry registry,
        string phrase,
        out string conjunction,
        out List<string> clauses,
        out string rejectionMessage)
    {
        conjunction = "";
        clauses = new List<string>();
        rejectionMessage = "";
        List<string> tokens = CreaturePhraseUtility.Tokenize(phrase);
        if (tokens.Count == 0)
            return false;

        int conjunctionIndex = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!IsSupportedConjunction(tokens[i]))
                continue;
            if (conjunctionIndex >= 0)
            {
                conjunction = CreaturePhraseUtility.NormalizeToken(tokens[i]);
                rejectionMessage = "Use one conjunction at a time in this battle sentence.";
                return true;
            }

            conjunctionIndex = i;
            conjunction = CreaturePhraseUtility.NormalizeToken(tokens[i]);
        }

        if (conjunctionIndex < 0)
            return false;
        if (conjunctionIndex == 0 || conjunctionIndex >= tokens.Count - 1)
        {
            rejectionMessage = $"Put {conjunction} between two useful clauses.";
            return true;
        }

        List<string> first = tokens.GetRange(0, conjunctionIndex);
        List<string> second = tokens.GetRange(conjunctionIndex + 1, tokens.Count - conjunctionIndex - 1);
        if (!TryReadSubjectPrefix(registry, first, out List<string> subjectPrefix, out int firstVerbIndex) ||
            !StartsWithVerb(registry, first, firstVerbIndex))
        {
            rejectionMessage = "Start the conjunction sentence with a creature and an action.";
            return true;
        }

        if (conjunction == "BECAUSE")
        {
            if (!TryReadSubjectPrefix(registry, second, out List<string> reasonSubjectPrefix, out int reasonVerbIndex) ||
                !StartsWithVerb(registry, second, reasonVerbIndex))
            {
                rejectionMessage = "After BECAUSE, give a reason with a subject and a verb.";
                return true;
            }

            if (!SubjectPrefixesMatch(registry, subjectPrefix, reasonSubjectPrefix))
            {
                rejectionMessage = "In battle, the BECAUSE reason should stay with the same word family.";
                return true;
            }

            clauses.Add(string.Join(" ", first));
            clauses.Add(string.Join(" ", second));
            return true;
        }

        if (!TryExpandActionClause(registry, second, subjectPrefix, out List<string> expandedSecond))
        {
            rejectionMessage = conjunction == "AND"
                ? "After AND, add another verb for the same creature, like THE RAT JUMPS AND BITES."
                : "After OR, add another possible verb for the same creature, like THE RAT JUMPS OR BITES.";
            return true;
        }

        clauses.Add(string.Join(" ", first));
        clauses.Add(string.Join(" ", expandedSecond));
        return true;
    }

    static bool IsSupportedConjunction(string token)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(token);
        return normalized == "AND" || normalized == "OR" || normalized == "BECAUSE";
    }

    static bool IsDeterminerToken(string token)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(token);
        return normalized == "THE" || normalized == "A" || normalized == "AN";
    }

    static bool TryReadSubjectPrefix(CreatureCombatRegistry registry, List<string> tokens, out List<string> subjectPrefix, out int nextIndex)
    {
        subjectPrefix = new List<string>();
        nextIndex = 0;
        if (tokens == null || tokens.Count == 0 || registry == null)
            return false;

        if (tokens.Count >= 2 && IsDeterminerToken(tokens[0]) && registry.TryGetNoun(tokens[1], out _))
        {
            subjectPrefix.Add(tokens[0]);
            subjectPrefix.Add(tokens[1]);
            nextIndex = 2;
            return true;
        }

        if (registry.TryGetNoun(tokens[0], out _))
        {
            subjectPrefix.Add(tokens[0]);
            nextIndex = 1;
            return true;
        }

        return false;
    }

    static bool StartsWithSubject(CreatureCombatRegistry registry, List<string> tokens)
    {
        return TryReadSubjectPrefix(registry, tokens, out _, out _);
    }

    static bool StartsWithVerb(CreatureCombatRegistry registry, List<string> tokens, int index = 0)
    {
        return registry != null &&
               tokens != null &&
               index >= 0 &&
               index < tokens.Count &&
               registry.TryGetVerb(tokens[index], out _);
    }

    static bool TryExpandActionClause(CreatureCombatRegistry registry, List<string> tokens, List<string> subjectPrefix, out List<string> expanded)
    {
        expanded = new List<string>();
        if (tokens == null || tokens.Count == 0)
            return false;

        if (StartsWithSubject(registry, tokens))
        {
            expanded.AddRange(tokens);
            return true;
        }

        if (!StartsWithVerb(registry, tokens))
            return false;

        expanded.AddRange(subjectPrefix);
        expanded.AddRange(tokens);
        return true;
    }

    static bool SubjectPrefixesMatch(CreatureCombatRegistry registry, List<string> firstSubjectPrefix, List<string> secondSubjectPrefix)
    {
        return TryResolveSubjectNoun(registry, firstSubjectPrefix, out string firstNoun) &&
               TryResolveSubjectNoun(registry, secondSubjectPrefix, out string secondNoun) &&
               string.Equals(firstNoun, secondNoun, StringComparison.OrdinalIgnoreCase);
    }

    static bool TryResolveSubjectNoun(CreatureCombatRegistry registry, List<string> subjectPrefix, out string noun)
    {
        noun = "";
        if (registry == null || subjectPrefix == null || subjectPrefix.Count == 0)
            return false;

        string nounToken = subjectPrefix.Count >= 2 && IsDeterminerToken(subjectPrefix[0])
            ? subjectPrefix[1]
            : subjectPrefix[0];
        if (!registry.TryGetNoun(nounToken, out NounDefinition definition) || definition == null)
            return false;

        noun = CreaturePhraseUtility.NormalizeToken(definition.canonicalNoun);
        return !string.IsNullOrWhiteSpace(noun);
    }
}
