using System;
using System.Collections.Generic;
using UnityEngine;

public static class GeneratedGrammarGrimoireData
{
    const string ResourcePath = "Grammar/generated-grimoire-pages";

    static List<GrammarGrimoirePage> cachedPages;

    public static List<GrammarGrimoirePage> BuildPages()
    {
        EnsureLoaded();
        return new List<GrammarGrimoirePage>(cachedPages);
    }

    static void EnsureLoaded()
    {
        if (cachedPages != null)
            return;

        cachedPages = new List<GrammarGrimoirePage>();
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            return;

        GeneratedGrammarGrimoirePageFile file = JsonUtility.FromJson<GeneratedGrammarGrimoirePageFile>(asset.text);
        if (file?.pages == null)
            return;

        foreach (GeneratedGrammarGrimoirePage source in file.pages)
        {
            GrammarGrimoirePage page = source?.ToPage();
            if (page != null)
                cachedPages.Add(page);
        }
    }

    [Serializable]
    sealed class GeneratedGrammarGrimoirePageFile
    {
        public List<GeneratedGrammarGrimoirePage> pages = new List<GeneratedGrammarGrimoirePage>();
    }
}

[Serializable]
public sealed class GeneratedGrammarGrimoirePage
{
    public string id = "";
    public string conceptId = "";
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

    public GrammarGrimoirePage ToPage()
    {
        if (string.IsNullOrWhiteSpace(conceptId) ||
            !Enum.TryParse(conceptId.Trim(), true, out GrammarConceptId parsedConcept) ||
            parsedConcept == GrammarConceptId.None)
            return null;

        return new GrammarGrimoirePage
        {
            conceptId = parsedConcept,
            title = title ?? "",
            summary = summary ?? "",
            rule = rule ?? "",
            examples = new List<string>(examples ?? new List<string>()),
            commonGoofs = new List<string>(commonGoofs ?? new List<string>()),
            nouns = new List<string>(nouns ?? new List<string>()),
            adjectives = new List<string>(adjectives ?? new List<string>()),
            functionWords = new List<string>(functionWords ?? new List<string>()),
            pronunciationGuides = new List<PronunciationGuideRow>(pronunciationGuides ?? new List<PronunciationGuideRow>()),
            conjugations = new List<VerbConjugationRow>(conjugations ?? new List<VerbConjugationRow>()),
        };
    }
}
