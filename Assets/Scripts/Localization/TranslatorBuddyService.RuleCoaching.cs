using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public partial class TranslatorBuddyService : MonoBehaviour
{
    static string BuildFullResponsePrompt(LocalizedDialogueLine dialogueLine, string response)
    {
        return string.IsNullOrWhiteSpace(response) ? "" : $"Say: \"{response}\"";
    }

    static string BuildPartialPromptText(LocalizedDialogueLine dialogueLine, string response)
    {
        if (dialogueLine == null)
            return response ?? "";

        if (dialogueLine.malfunctionType == GrammarDialogueMalfunctionType.MissingWord)
        {
            string authoredBlank = ExtractAuthoredBlankPrompt(ResolveNpcLine(dialogueLine));
            if (!string.IsNullOrWhiteSpace(authoredBlank))
                return authoredBlank;
        }

        if (string.IsNullOrWhiteSpace(response) || response.Trim().IndexOf(' ') < 0)
            return BuildRouteConceptClue(dialogueLine);

        string fumbled = FumbleResponse(
            response,
            dialogueLine.lineId,
            dialogueLine.malfunctionType,
            dialogueLine.conceptId,
            dialogueLine.grammarPattern,
            dialogueLine.grammarFocusWords);
        return string.Equals(
            VoiceUnlockRecognizer.NormalizeKeyword(fumbled),
            VoiceUnlockRecognizer.NormalizeKeyword(response),
            StringComparison.Ordinal)
            ? BuildRouteConceptClue(dialogueLine)
            : fumbled;
    }

    static string BuildRouteConceptClue(LocalizedDialogueLine dialogueLine)
    {
        if (dialogueLine == null)
            return "Use the grammar clue and try again.";

        return dialogueLine.conceptId switch
        {
            GrammarConceptId.Alphabet => "Think about the letter shape and its alphabet position.",
            GrammarConceptId.VowelsConsonants => "Decide whether the sound is a vowel or a consonant.",
            GrammarConceptId.BasicNouns => "Look for the naming word.",
            GrammarConceptId.BasicVerbs => "Look for the action word.",
            GrammarConceptId.Articles => "Choose the article by listening to the next sound.",
            GrammarConceptId.Pronouns => "Choose the word that replaces the naming word.",
            GrammarConceptId.Plurals => "Check whether the noun means one or more than one.",
            GrammarConceptId.Adjectives => "Put the describing word with the noun.",
            GrammarConceptId.BasicPrepositions => "Find the word that shows where something is.",
            _ => "Use the grammar clue and try again.",
        };
    }

    static string BuildTeachingLead(LocalizedDialogueLine dialogueLine)
    {
        if (dialogueLine == null)
            return "";
        if (!string.IsNullOrWhiteSpace(dialogueLine.teachingNote))
            return dialogueLine.teachingNote.Trim();

        return dialogueLine.conceptId switch
        {
            GrammarConceptId.Greetings => "Use the whole survival phrase clearly so people understand you right away.",
            GrammarConceptId.Alphabet => "Watch the letter form and its place in the alphabet.",
            GrammarConceptId.VowelsConsonants => "Vowels are a, e, i, o, u. The other letters here are consonants.",
            GrammarConceptId.SentenceStartEnd => "A sentence starts with a capital letter and ends with a full stop.",
            GrammarConceptId.BasicNouns => "A noun names a person, place, animal, or thing.",
            GrammarConceptId.BasicVerbs => "A verb is an action word.",
            GrammarConceptId.Articles => "Articles come before nouns. Use a before a consonant sound, an before a vowel sound, and the for a specific noun.",
            GrammarConceptId.Pronouns => "Pronouns replace nouns so the sentence stays smooth.",
            GrammarConceptId.Plurals => "Plural nouns show more than one.",
            GrammarConceptId.Adjectives => "Adjectives describe nouns and usually come before the noun here.",
            GrammarConceptId.BasicPrepositions => "Prepositions show where something is, like in, on, under, or behind.",
            _ => "",
        };
    }

    static string BuildWhatWasWrong(LocalizedDialogueLine dialogueLine, string rejectionReason)
    {
        if (dialogueLine == null)
            return "The answer did not match yet.";

        return rejectionReason switch
        {
            "missing_word_mismatch" => "A needed word is missing from the answer.",
            "scrambled_sentence_mismatch" => "The words are not in the right order yet.",
            "heard_wrong_correction_mismatch" => "The wrong word needs to be corrected.",
            "partial_transcript_mismatch" => "The answer is still incomplete.",
            "input_mode_write_required" => "This check is asking for a handwritten answer.",
            "input_mode_speak_required" => "This check is asking for a spoken answer.",
            "gym_answer_mismatch" => BuildConceptMismatch(dialogueLine.conceptId),
            _ => BuildConceptMismatch(dialogueLine.conceptId),
        };
    }

    static string BuildWhy(LocalizedDialogueLine dialogueLine, string rejectionReason)
    {
        if (dialogueLine == null)
            return "";

        if (rejectionReason == "input_mode_write_required")
            return "The goal here is to show you can write the English form yourself.";
        if (rejectionReason == "input_mode_speak_required")
            return "The goal here is to show you can say the English form aloud.";

        return dialogueLine.conceptId switch
        {
            GrammarConceptId.Greetings => "Greetings work best as clear whole phrases people already know.",
            GrammarConceptId.Alphabet => "Capital and small letters can look different, but they still name the same letter.",
            GrammarConceptId.VowelsConsonants => "English has five vowels: a, e, i, o, and u. The other letters are consonants.",
            GrammarConceptId.SentenceStartEnd => "Readers need a capital at the start and a full stop at the end to see the sentence boundary.",
            GrammarConceptId.BasicNouns => "Nouns are naming words for people, places, animals, and things.",
            GrammarConceptId.BasicVerbs => "Verbs show what the subject does.",
            GrammarConceptId.Articles => "Use a before a consonant sound, an before a vowel sound, and the when the noun is specific.",
            GrammarConceptId.Pronouns => "Pronouns stand in for nouns so you do not repeat the full name every time.",
            GrammarConceptId.Plurals => "Plural endings change the noun to show more than one.",
            GrammarConceptId.Adjectives => "The adjective comes with the noun to describe it more clearly.",
            GrammarConceptId.BasicPrepositions => "Prepositions change the location meaning of the sentence.",
            _ => "",
        };
    }

    static string BuildMicroLesson(GrammarConceptId conceptId)
    {
        return conceptId switch
        {
            GrammarConceptId.Articles => "Rule: a rat, an owl, the cat.",
            GrammarConceptId.Pronouns => "Rule: rat becomes it, Aryan becomes he, Mira and Ira become they.",
            GrammarConceptId.Plurals => "Rule: rat/rats, box/boxes, puppy/puppies.",
            GrammarConceptId.Adjectives => "Rule: the describing word comes before the noun, like big rat or small cat.",
            GrammarConceptId.BasicPrepositions => "Rule: in the box, on the box, under the box, behind the box.",
            GrammarConceptId.SentenceStartEnd => "Rule: start tall and end clean, like I am ready.",
            GrammarConceptId.VowelsConsonants => "Rule: vowels are a, e, i, o, u.",
            _ => "",
        };
    }

    static string BuildPracticeExamples(GrammarConceptId conceptId)
    {
        return conceptId switch
        {
            GrammarConceptId.Articles => "a rat / an owl / the dog",
            GrammarConceptId.Pronouns => "I bite / he bites / they bite",
            GrammarConceptId.Plurals => "cat / cats / puppies",
            GrammarConceptId.Adjectives => "big rat / small bird",
            GrammarConceptId.BasicPrepositions => "rat in the box / rat under the table",
            _ => "",
        };
    }

    static string BuildConceptMismatch(GrammarConceptId conceptId)
    {
        return conceptId switch
        {
            GrammarConceptId.Articles => "The article in front of the noun is not right yet.",
            GrammarConceptId.Pronouns => "The pronoun command is not in the right form yet.",
            GrammarConceptId.Plurals => "The noun number form is not right yet.",
            GrammarConceptId.Adjectives => "The describing word is missing or in the wrong place.",
            GrammarConceptId.BasicPrepositions => "The location word is not right yet.",
            GrammarConceptId.BasicVerbs => "The action word is not right yet.",
            GrammarConceptId.BasicNouns => "The naming word is not right yet.",
            GrammarConceptId.SentenceStartEnd => "The sentence form is not complete yet.",
            _ => "The answer does not match the grammar target yet.",
        };
    }

    static string BuildShortWhy(string why)
    {
        if (string.IsNullOrWhiteSpace(why))
            return "";

        string trimmed = why.Trim();
        if (trimmed.EndsWith("."))
            return $"Why: {trimmed}";
        return $"Why: {trimmed}.";
    }

    static string ResolveErrorCategory(
        string rejectionReason,
        PronunciationInsightResult? pronunciationInsight,
        HandwritingDiagnosticSummary handwritingDiagnostics)
    {
        if (handwritingDiagnostics != null && !string.IsNullOrWhiteSpace(handwritingDiagnostics.primaryHint))
            return $"handwriting_{(handwritingDiagnostics.tags != null && handwritingDiagnostics.tags.Count > 0 ? handwritingDiagnostics.tags[0] : "shape")}";

        if (pronunciationInsight.HasValue && IsActionablePronunciationInsight(pronunciationInsight.Value))
            return $"pronunciation_{pronunciationInsight.Value.HintKey}";

        return string.IsNullOrWhiteSpace(rejectionReason) ? "response_mismatch" : rejectionReason;
    }

    static string BuildPronunciationNote(PronunciationInsightResult insight)
    {
        if (!IsActionablePronunciationInsight(insight))
            return "";

        string focus = !string.IsNullOrWhiteSpace(insight.FocusSegment.Spelling)
            ? insight.FocusSegment.Spelling.ToLowerInvariant()
            : "that sound";
        return $"Sound note: watch {focus} and say it more clearly.";
    }

    static bool IsActionablePronunciationInsight(PronunciationInsightResult insight)
    {
        return (!string.IsNullOrWhiteSpace(insight.ProviderName) ||
                !string.IsNullOrWhiteSpace(insight.TargetWord) ||
                !string.IsNullOrWhiteSpace(insight.RawRecognizedText)) &&
               (insight.AttemptedTarget || !string.IsNullOrWhiteSpace(insight.Message));
    }

    string BuildConceptCounterKey(GrammarConceptId conceptId)
    {
        return conceptId == GrammarConceptId.None ? "" : conceptId.ToString();
    }

    string BuildSubskillCounterKey(LocalizedDialogueLine dialogueLine)
    {
        if (dialogueLine == null)
            return "";
        string concept = BuildConceptCounterKey(dialogueLine.conceptId);
        string subskill = !string.IsNullOrWhiteSpace(dialogueLine.subskillId)
            ? dialogueLine.subskillId.Trim()
            : dialogueLine.dialogueTaskId ?? "";
        return string.IsNullOrWhiteSpace(concept) || string.IsNullOrWhiteSpace(subskill)
            ? concept
            : $"{concept}|{subskill}";
    }

    static int RegisterMiss(Dictionary<string, int> counts, string key)
    {
        if (counts == null || string.IsNullOrWhiteSpace(key))
            return 1;

        counts.TryGetValue(key, out int current);
        current++;
        counts[key] = current;
        return current;
    }

    static string ExtractAuthoredBlankPrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("____"))
            return "";

        string prompt = text.Trim();
        int colon = prompt.LastIndexOf(':');
        if (colon >= 0 && prompt.IndexOf("____", colon, StringComparison.Ordinal) >= 0)
            prompt = prompt.Substring(colon + 1).Trim();

        return prompt;
    }

    static string BuildPartialResponsePrompt(string fumbledResponse, GrammarDialogueMalfunctionType malfunctionType)
    {
        string prompt = string.IsNullOrWhiteSpace(fumbledResponse) ? "____" : fumbledResponse.Trim();
        return malfunctionType switch
        {
            GrammarDialogueMalfunctionType.MissingWord => $"Signal broken. Fill the blank: \"{prompt}\"",
            GrammarDialogueMalfunctionType.ScrambledSentence => $"Signal scrambled. Unscramble: \"{prompt}\"",
            GrammarDialogueMalfunctionType.HeardWrong => $"Signal wrong. Correct it: \"{prompt}\"",
            GrammarDialogueMalfunctionType.PartialTranscript => $"Signal broken. Complete what you heard: \"{prompt}\"",
            _ => $"Signal broken. Maybe say: \"{prompt}\"",
        };
    }

    public static string FumbleResponse(
        string response,
        string salt = "",
        GrammarDialogueMalfunctionType malfunctionType = GrammarDialogueMalfunctionType.PartialTranscript,
        GrammarConceptId conceptId = GrammarConceptId.None,
        GrammarPhrasePattern grammarPattern = GrammarPhrasePattern.FullSentence,
        IEnumerable<string> grammarFocusWords = null)
    {
        string normalized = string.IsNullOrWhiteSpace(response) ? "" : response.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        if (malfunctionType == GrammarDialogueMalfunctionType.MissingWord)
        {
            return DialogueFillInBlankScaffold.Build(
                normalized,
                conceptId,
                grammarPattern,
                salt,
                grammarFocusWords);
        }

        string[] words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return ScrambleWord(words.Length == 0 ? normalized : words[0]);

        int seed = 17;
        foreach (char c in normalized)
            seed = unchecked(seed * 31 + c);
        if (!string.IsNullOrWhiteSpace(salt))
            foreach (char c in salt)
                seed = unchecked(seed * 31 + c);

        var rng = new System.Random(Mathf.Abs(seed == int.MinValue ? 0 : seed));
        var result = new List<string>(words);

        if (malfunctionType == GrammarDialogueMalfunctionType.HeardWrong && result.Count > 0)
        {
            int wrongIndex = rng.Next(0, result.Count);
            result[wrongIndex] = ScrambleWord(result[wrongIndex]);
            return string.Join(" ", result);
        }

        if (result.Count > 2 && malfunctionType == GrammarDialogueMalfunctionType.PartialTranscript)
            result.RemoveAt(rng.Next(0, result.Count));

        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        if (malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence)
            return string.Join(" ", result);

        int fumbleIndex = rng.Next(0, result.Count);
        result[fumbleIndex] = ScrambleWord(result[fumbleIndex]);
        return string.Join(" ", result);
    }

    static string ResolveNpcLine(LocalizedDialogueLine line)
    {
        if (line == null)
            return "";
        return string.IsNullOrWhiteSpace(line.npcLine) ? line.sourceText : line.npcLine;
    }

    static string ScrambleWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length <= 3)
            return word;

        char[] chars = word.ToCharArray();
        int left = 1;
        int right = chars.Length - 2;
        (chars[left], chars[right]) = (chars[right], chars[left]);
        return new string(chars);
    }
}
