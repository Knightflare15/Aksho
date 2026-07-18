using System;
using System.Collections.Generic;
using System.Text;

[Serializable]
public sealed class VerbConjugationRow
{
    public string pronoun = "";
    public string present = "";
    public string past = "";
    public string progressive = "";
    public string future = "";
}

[Serializable]
public sealed class PronunciationGuideRow
{
    public string word = "";
    public string ipa = "";
    public string soundGuide = "";
    public string buddyHint = "";
    public string commonIssue = "";
}

[Serializable]
public sealed class GrammarGrimoirePage
{
    public GrammarConceptId conceptId = GrammarConceptId.None;
    public string title = "";
    public string summary = "";
    public string rule = "";
    public List<string> examples = new List<string>();
    public List<string> commonGoofs = new List<string>();
    public List<string> nouns = new List<string>();
    public List<string> adjectives = new List<string>();
    public List<string> functionWords = new List<string>();
    public List<PronunciationGuideRow> pronunciationGuides = new List<PronunciationGuideRow>();
    public List<VerbConjugationRow> conjugations = new List<VerbConjugationRow>();
}

public static class GrammarGrimoireCatalog
{
    static readonly Dictionary<GrammarConceptId, GrammarGrimoirePage> Pages = BuildPages();

    public static IReadOnlyDictionary<GrammarConceptId, GrammarGrimoirePage> AllPages => Pages;

    public static bool TryGetPage(GrammarConceptId conceptId, out GrammarGrimoirePage page)
    {
        return Pages.TryGetValue(conceptId, out page) && page != null;
    }

    public static List<GrammarGrimoirePage> GetUnlockedPages()
    {
        var pages = new List<GrammarGrimoirePage>();
        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;

        foreach (GrammarGrimoirePage page in Pages.Values)
        {
            if (page == null || page.conceptId == GrammarConceptId.None)
                continue;
            if (IsUnlocked(page.conceptId, progress))
                pages.Add(page);
        }

        return pages;
    }

    public static bool IsUnlocked(GrammarConceptId conceptId)
    {
        GrammarWorldProgressData progress = GrammarWorldProgressService.Instance != null
            ? GrammarWorldProgressService.Instance.Data
            : null;
        return IsUnlocked(conceptId, progress);
    }

    public static string BuildPageText(GrammarGrimoirePage page, string highlightKey = "")
    {
        if (page == null)
            return "This grammar page is not unlocked yet.";

        var text = new StringBuilder();
        text.Append("<color=#FFCF61><b>").Append(page.title).Append("</b></color>").AppendLine().AppendLine();
        text.Append(page.summary).AppendLine().AppendLine();
        if (!string.IsNullOrWhiteSpace(page.rule))
            text.Append("<b>Rule</b>: ").Append(Highlight(page.rule, highlightKey, "rule")).AppendLine().AppendLine();

        AppendSection(text, "Examples", page.examples, highlightKey, "example");
        AppendSection(text, "Common goofs", page.commonGoofs, highlightKey, "goof");
        AppendSection(text, "Nouns", page.nouns);
        AppendSection(text, "Adjectives", page.adjectives);
        AppendSection(text, "Function words", page.functionWords);

        if (page.pronunciationGuides != null && page.pronunciationGuides.Count > 0)
        {
            text.Append("<b>Pronunciation guide</b>").AppendLine();
            for (int i = 0; i < page.pronunciationGuides.Count; i++)
            {
                PronunciationGuideRow row = page.pronunciationGuides[i];
                if (row == null || string.IsNullOrWhiteSpace(row.word))
                    continue;
                var rowText = new StringBuilder();
                rowText.Append(row.word);
                if (!string.IsNullOrWhiteSpace(row.ipa))
                    rowText.Append(" ").Append(row.ipa);
                if (!string.IsNullOrWhiteSpace(row.soundGuide))
                    rowText.Append(": ").Append(row.soundGuide);
                if (!string.IsNullOrWhiteSpace(row.buddyHint))
                    rowText.Append(" - ").Append(row.buddyHint);
                if (!string.IsNullOrWhiteSpace(row.commonIssue))
                    rowText.Append(" Watch for: ").Append(row.commonIssue);
                text.Append(Highlight(rowText.ToString(), highlightKey, $"pronunciation:{i}")).AppendLine();
            }
            text.AppendLine();
        }

        if (page.conjugations != null && page.conjugations.Count > 0)
        {
            text.Append("<b>Verb forms</b>").AppendLine();
            for (int i = 0; i < page.conjugations.Count; i++)
            {
                VerbConjugationRow row = page.conjugations[i];
                if (row == null)
                    continue;
                var rowText = new StringBuilder();
                rowText.Append(row.pronoun)
                    .Append(": ")
                    .Append("Present: ")
                    .Append(row.present)
                    .Append(" / Past: ")
                    .Append(row.past)
                    .Append(" / Present continuous: ")
                    .Append(row.progressive)
                    .Append(" / Future: ")
                    .Append(row.future);
                text.Append(Highlight(rowText.ToString(), highlightKey, $"conjugation:{i}")).AppendLine();
            }
            text.AppendLine();
        }

        text.Append("<color=#9FB4D0>Buddy coach hook: this page can be sent to AI Buddy when the learner asks for a natural local-language explanation.</color>");
        return text.ToString();
    }

    public static string BuildBuddyExcerpt(GrammarGrimoirePage page)
    {
        if (page == null)
            return "";

        var text = new StringBuilder();
        text.Append(page.title).Append(": [rule] ").Append(page.rule);
        AppendAnchoredExcerpt(text, "example", page.examples);
        AppendAnchoredExcerpt(text, "goof", page.commonGoofs);
        if (page.pronunciationGuides != null)
        {
            for (int i = 0; i < page.pronunciationGuides.Count; i++)
            {
                PronunciationGuideRow row = page.pronunciationGuides[i];
                if (row == null || string.IsNullOrWhiteSpace(row.word)) continue;
                text.Append(" [pronunciation:").Append(i).Append("] ")
                    .Append(row.word);
                if (!string.IsNullOrWhiteSpace(row.ipa))
                    text.Append(" ").Append(row.ipa);
                if (!string.IsNullOrWhiteSpace(row.soundGuide))
                    text.Append(": ").Append(row.soundGuide);
                if (!string.IsNullOrWhiteSpace(row.commonIssue))
                    text.Append("; watch for ").Append(row.commonIssue);
            }
        }
        if (page.conjugations != null)
        {
            for (int i = 0; i < page.conjugations.Count; i++)
            {
                VerbConjugationRow row = page.conjugations[i];
                if (row == null) continue;
                text.Append(" [conjugation:").Append(i).Append("] ")
                    .Append(row.pronoun)
                    .Append(": Present: ").Append(row.present)
                    .Append(" / Past: ").Append(row.past)
                    .Append(" / Present continuous: ").Append(row.progressive)
                    .Append(" / Future: ").Append(row.future);
            }
        }
        return text.ToString();
    }

    public static bool IsValidHighlightKey(GrammarGrimoirePage page, string key)
    {
        if (page == null || string.IsNullOrWhiteSpace(key)) return false;
        if (string.Equals(key, "rule", StringComparison.OrdinalIgnoreCase)) return !string.IsNullOrWhiteSpace(page.rule);
        return TryValidateIndexedKey(key, "example", page.examples?.Count ?? 0) ||
               TryValidateIndexedKey(key, "goof", page.commonGoofs?.Count ?? 0) ||
               TryValidateIndexedKey(key, "pronunciation", page.pronunciationGuides?.Count ?? 0) ||
               TryValidateIndexedKey(key, "conjugation", page.conjugations?.Count ?? 0);
    }

    static bool IsUnlocked(GrammarConceptId conceptId, GrammarWorldProgressData progress)
    {
        if (conceptId == GrammarConceptId.None)
            return false;
        if (progress == null || progress.unlockedConceptIds == null || progress.unlockedConceptIds.Count == 0)
            return conceptId == GrammarConceptId.Greetings;

        string key = conceptId.ToString();
        foreach (string unlocked in progress.unlockedConceptIds)
        {
            if (string.Equals(unlocked, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static void AppendSection(StringBuilder text, string title, List<string> values, string highlightKey = "", string keyPrefix = "")
    {
        if (values == null || values.Count == 0)
            return;

        text.Append("<b>").Append(title).Append("</b>").AppendLine();
        for (int i = 0; i < values.Count; i++)
        {
            string value = values[i];
            if (!string.IsNullOrWhiteSpace(value))
                text.Append("- ").Append(Highlight(value.Trim(), highlightKey, $"{keyPrefix}:{i}")).AppendLine();
        }
        text.AppendLine();
    }

    static void AppendAnchoredExcerpt(StringBuilder text, string prefix, List<string> values)
    {
        if (values == null) return;
        for (int i = 0; i < values.Count; i++)
            if (!string.IsNullOrWhiteSpace(values[i]))
                text.Append(" [").Append(prefix).Append(':').Append(i).Append("] ").Append(values[i].Trim());
    }

    static string Highlight(string value, string requestedKey, string valueKey)
    {
        return string.Equals(requestedKey?.Trim(), valueKey, StringComparison.OrdinalIgnoreCase)
            ? $"<mark=#E0A92F66><color=#FFF1A6><b>{value}</b></color></mark>"
            : value;
    }

    static bool TryValidateIndexedKey(string key, string prefix, int count)
    {
        string marker = prefix + ":";
        if (!key.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(key.Substring(marker.Length), out int index) && index >= 0 && index < count;
    }

    static Dictionary<GrammarConceptId, GrammarGrimoirePage> BuildPages()
    {
        var pages = new Dictionary<GrammarConceptId, GrammarGrimoirePage>();
        foreach (GrammarGrimoirePage page in GeneratedGrammarGrimoireData.BuildPages())
            Add(pages, page);
        return pages;
    }

    static GrammarGrimoirePage Page(
        GrammarConceptId conceptId,
        string title,
        string summary,
        string rule,
        IEnumerable<string> examples = null,
        IEnumerable<string> commonGoofs = null,
        IEnumerable<string> nouns = null,
        IEnumerable<string> adjectives = null,
        IEnumerable<string> functionWords = null,
        IEnumerable<PronunciationGuideRow> pronunciationGuides = null,
        IEnumerable<VerbConjugationRow> conjugations = null)
    {
        return new GrammarGrimoirePage
        {
            conceptId = conceptId,
            title = title,
            summary = summary,
            rule = rule,
            examples = Copy(examples),
            commonGoofs = Copy(commonGoofs),
            nouns = Copy(nouns),
            adjectives = Copy(adjectives),
            functionWords = Copy(functionWords),
            pronunciationGuides = pronunciationGuides != null ? new List<PronunciationGuideRow>(pronunciationGuides) : new List<PronunciationGuideRow>(),
            conjugations = conjugations != null ? new List<VerbConjugationRow>(conjugations) : new List<VerbConjugationRow>(),
        };
    }

    static List<string> Copy(IEnumerable<string> values)
    {
        var result = new List<string>();
        if (values == null)
            return result;
        foreach (string value in values)
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value.Trim());
        return result;
    }

    static void Add(Dictionary<GrammarConceptId, GrammarGrimoirePage> pages, GrammarGrimoirePage page)
    {
        if (page != null)
            pages[page.conceptId] = page;
    }

    static PronunciationGuideRow Pron(string word, string ipa, string soundGuide, string buddyHint, string commonIssue)
    {
        return new PronunciationGuideRow
        {
            word = word,
            ipa = ipa,
            soundGuide = soundGuide,
            buddyHint = buddyHint,
            commonIssue = commonIssue,
        };
    }

    static List<PronunciationGuideRow> PronunciationRows(params PronunciationGuideRow[] rows)
    {
        var result = new List<PronunciationGuideRow>();
        if (rows == null)
            return result;
        foreach (PronunciationGuideRow row in rows)
        {
            if (row != null && !string.IsNullOrWhiteSpace(row.word))
                result.Add(row);
        }
        return result;
    }

    static List<VerbConjugationRow> BuildCoreVerbRows()
    {
        var rows = new List<VerbConjugationRow>();
        // Keep the highest-value irregular verb first so its complete tense
        // table is also present in the bounded Buddy excerpt.
        string[] verbs = { "go", "bite", "run", "jump", "scratch", "fly", "peck", "swim" };
        string[] pronouns = { "I", "you", "he/she/it", "we", "they" };
        foreach (string verb in verbs)
        {
            foreach (string pronoun in pronouns)
            {
                bool thirdPerson = pronoun == "he/she/it";
                string basePhrase = $"{pronoun} {(thirdPerson ? ThirdPerson(verb) : verb)}";
                rows.Add(new VerbConjugationRow
                {
                    pronoun = $"{pronoun} + {verb}",
                    present = basePhrase,
                    past = $"{pronoun} {Past(verb)}",
                    progressive = $"{pronoun} {(thirdPerson ? "is" : pronoun == "I" ? "am" : "are")} {Progressive(verb)}",
                    future = $"{pronoun} will {verb}",
                });
            }
        }
        return rows;
    }

    static string ThirdPerson(string verb)
    {
        if (verb == "fly")
            return "flies";
        if (verb == "go")
            return "goes";
        if (verb.EndsWith("ch", StringComparison.Ordinal) || verb.EndsWith("sh", StringComparison.Ordinal) || verb.EndsWith("x", StringComparison.Ordinal))
            return verb + "es";
        return verb + "s";
    }

    static string Past(string verb)
    {
        return verb switch
        {
            "bite" => "bit",
            "go" => "went",
            "run" => "ran",
            "fly" => "flew",
            "swim" => "swam",
            "scratch" => "scratched",
            "peck" => "pecked",
            _ => verb + "ed",
        };
    }

    static string Progressive(string verb)
    {
        return verb switch
        {
            "bite" => "biting",
            "go" => "going",
            "run" => "running",
            "swim" => "swimming",
            "fly" => "flying",
            _ => verb + "ing",
        };
    }
}
