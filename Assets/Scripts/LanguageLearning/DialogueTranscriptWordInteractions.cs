using System;
using System.Collections.Generic;

/// <summary>Tokenization shared by tappable transcripts and sentence jumbles.</summary>
public static class DialogueTranscriptTokenizer
{
    public static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool isToken = i < text.Length &&
                           (char.IsLetterOrDigit(text[i]) || text[i] == '\'' || text[i] == '\u2019');
            if (isToken && start < 0) start = i;
            if (isToken || start < 0) continue;
            result.Add(text.Substring(start, i - start));
            start = -1;
        }
        return result;
    }

    public static string NormalizeWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var characters = new List<char>();
        foreach (char character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character == '\'' || character == '\u2019')
                characters.Add(char.ToLowerInvariant(character == '\u2019' ? '\'' : character));
        }
        return new string(characters.ToArray());
    }
}

/// <summary>Provider boundary for token-level dialogue audio and meanings.</summary>
public interface IDialogueWordInteractionService
{
    void Speak(string text);
    string GetMeaning(string text, GrammarConceptId conceptId);
}

public static class DialogueWordInteractionServiceFactory
{
    public static Func<IDialogueWordInteractionService> OverrideFactory { get; set; }

    public static IDialogueWordInteractionService Create() =>
        OverrideFactory != null ? OverrideFactory() : new DeviceDialogueWordInteractionService();
}

sealed class DeviceDialogueWordInteractionService : IDialogueWordInteractionService
{
    public void Speak(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            PronunciationSpeaker.EnsureExists().Speak(text.Trim());
    }

    public string GetMeaning(string text, GrammarConceptId conceptId) =>
        DialogueWordMeaningCatalog.GetMeaning(text, conceptId);
}

public static class DialogueWordMeaningCatalog
{
    static readonly Dictionary<string, string> Meanings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "hello", "a greeting used when you meet someone" },
        { "goodbye", "a polite word used when leaving" },
        { "please", "a polite word used when asking" },
        { "thank", "to show that you are grateful" },
        { "a", "an article used before one noun that begins with a consonant sound" },
        { "an", "an article used before one noun that begins with a vowel sound" },
        { "the", "an article for a particular or already-known noun" },
        { "i", "the pronoun a speaker uses for themself" },
        { "you", "the pronoun used for the person being spoken to" },
        { "he", "a pronoun for one boy or man" },
        { "she", "a pronoun for one girl or woman" },
        { "it", "a pronoun for one animal, place, or thing already known" },
        { "we", "a pronoun meaning the speaker and at least one other person" },
        { "they", "a pronoun for more than one person, animal, or thing" },
        { "big", "large in size" },
        { "small", "little in size" },
        { "under", "in a lower place than something" },
        { "over", "above or across something" },
        { "behind", "at the back of something" },
        { "beside", "next to something" },
        { "near", "not far away" },
        { "bite", "to cut or grip with the teeth" },
        { "run", "to move quickly on foot" },
        { "jump", "to push off the ground into the air" },
        { "fly", "to move through the air" },
        { "swim", "to move through water" },
        { "rat", "a small animal with a long tail" },
        { "mouse", "a very small animal related to a rat" },
        { "owl", "a bird with large eyes that is often awake at night" },
        { "mall", "a large building or area with many shops" },
        { "roof", "the top covering of a building" },
        { "bridge", "a structure that carries a path over water, a road, or a gap" },
        { "yesterday", "the day before today" },
    };

    public static string GetMeaning(string text, GrammarConceptId conceptId)
    {
        string normalized = VoiceUnlockRecognizer.NormalizeKeyword(text);
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        if (Meanings.TryGetValue(normalized, out string meaning)) return meaning;
        if (normalized.IndexOf(' ') >= 0) return "a complete reply that fits this conversation";

        return conceptId switch
        {
            GrammarConceptId.BasicNouns => "a naming word in this conversation",
            GrammarConceptId.BasicVerbs => "an action word in this conversation",
            GrammarConceptId.Articles => "a word used before a noun",
            GrammarConceptId.Pronouns => "a word that replaces a noun",
            GrammarConceptId.Plurals => "a word form that can show more than one",
            GrammarConceptId.Adjectives => "a describing word",
            GrammarConceptId.BasicPrepositions => "a word that shows place or direction",
            _ => "a word from this conversation",
        };
    }
}
