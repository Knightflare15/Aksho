#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

public sealed class NpcBuddySeparationTests
{
    [Test]
    public void SpokenNpcChoicesContainExpectedAnswerAndConceptDistractors()
    {
        var line = new LocalizedDialogueLine
        {
            lineId = "choice-test",
            dialogueTaskId = "welcome-greet",
            conceptId = GrammarConceptId.Greetings,
            grammarPattern = GrammarPhrasePattern.FullSentence,
            expectedEnglishResponse = "Hello",
            useSpokenAnswerChoices = true,
        };

        var choices = NpcSpokenAnswerChoiceBuilder.Build(line);

        Assert.That(choices.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(choices.Any(value => VoiceUnlockRecognizer.NormalizeKeyword(value) == "HELLO"), Is.True);
        Assert.That(choices.Select(VoiceUnlockRecognizer.NormalizeKeyword).Distinct().Count(), Is.EqualTo(choices.Count));
    }

    [Test]
    public void GrimoireOnlyHighlightsCatalogApprovedAnchors()
    {
        Assert.That(GrammarGrimoireCatalog.TryGetPage(GrammarConceptId.Articles, out GrammarGrimoirePage page), Is.True);
        Assert.That(GrammarGrimoireCatalog.IsValidHighlightKey(page, "rule"), Is.True);
        Assert.That(GrammarGrimoireCatalog.IsValidHighlightKey(page, "example:0"), Is.True);
        Assert.That(GrammarGrimoireCatalog.IsValidHighlightKey(page, "example:999"), Is.False);
        Assert.That(GrammarGrimoireCatalog.BuildPageText(page, "rule"), Does.Contain("<mark="));
    }

    [Test]
    public void BuddyConversationIsItsOwnNonPronunciationInputMode()
    {
        Assert.That(VoiceUnlockRecognizer.VoiceInputMode.BuddyConversation,
            Is.Not.EqualTo(VoiceUnlockRecognizer.VoiceInputMode.Manual));
    }

    [Test]
    public void BuddySpeechSegmentsPreserveMultilingualPlaybackOrder()
    {
        var response = new TranslatorBuddyHintResponse
        {
            speechText = "Legacy fallback",
            responseLanguage = "hi",
            speechSegments = new List<BuddySpeechSegment>
            {
                new BuddySpeechSegment { language = "hi", text = "बहुत अच्छा!" },
                new BuddySpeechSegment { language = "en", text = "Now try again." },
                new BuddySpeechSegment { language = "hi", text = "धीरे बोलो।" },
            }
        };

        List<BuddySpeechSegment> resolved = BuddySpeechSequence.Resolve(response, "en");

        Assert.That(resolved.Select(segment => segment.language), Is.EqualTo(new[] { "hi", "en", "hi" }));
        Assert.That(resolved.Select(segment => segment.text), Is.EqualTo(new[]
        {
            "बहुत अच्छा!", "Now try again.", "धीरे बोलो।"
        }));
    }

    [Test]
    public void BuddySpeechSegmentsRejectUnsupportedLanguageAndUseLegacyFallback()
    {
        var response = new TranslatorBuddyHintResponse
        {
            speechText = "Try the clue again.",
            responseLanguage = "xx",
            speechSegments = new List<BuddySpeechSegment>
            {
                new BuddySpeechSegment { language = "xx", text = "Untrusted segment" },
            }
        };

        List<BuddySpeechSegment> resolved = BuddySpeechSequence.Resolve(response, "en");

        Assert.That(resolved.Count, Is.EqualTo(1));
        Assert.That(resolved[0].language, Is.EqualTo("en"));
        Assert.That(resolved[0].text, Is.EqualTo("Try the clue again."));
    }

    [Test]
    public void BuddySpeechSegmentsRejectMarkupAndExcessiveSegmentCounts()
    {
        var markedUp = new TranslatorBuddyHintResponse
        {
            speechText = "Safe fallback",
            speechSegments = new List<BuddySpeechSegment>
            {
                new BuddySpeechSegment { language = "en", text = "<speak>Unsafe</speak>" },
            }
        };
        var excessive = new TranslatorBuddyHintResponse
        {
            speechText = "Bounded fallback",
            speechSegments = Enumerable.Range(0, BuddySpeechSequence.MaximumSegments + 1)
                .Select(_ => new BuddySpeechSegment { language = "en", text = "Hi" })
                .ToList()
        };

        Assert.That(BuddySpeechSequence.Resolve(markedUp)[0].text, Is.EqualTo("Safe fallback"));
        Assert.That(BuddySpeechSequence.Resolve(excessive)[0].text, Is.EqualTo("Bounded fallback"));
    }

    [Test]
    public void BuddySpeechSegmentsAcceptSingleLanguageResponsesAndRejectEmptyOrOverlongText()
    {
        var englishOnly = new TranslatorBuddyHintResponse
        {
            speechSegments = new List<BuddySpeechSegment>
            {
                new BuddySpeechSegment { language = "en-US", text = "Try once more." },
            }
        };
        var hindiOnly = new TranslatorBuddyHintResponse
        {
            speechSegments = new List<BuddySpeechSegment>
            {
                new BuddySpeechSegment { language = "hi", text = "फिर से कोशिश करो।" },
            }
        };
        var empty = new TranslatorBuddyHintResponse
        {
            speechText = "Empty fallback",
            speechSegments = new List<BuddySpeechSegment>
            {
                new BuddySpeechSegment { language = "en", text = "   " },
            }
        };
        var overlong = new TranslatorBuddyHintResponse
        {
            speechText = "Length fallback",
            speechSegments = new List<BuddySpeechSegment>
            {
                new BuddySpeechSegment
                {
                    language = "en",
                    text = new string('a', BuddySpeechSequence.MaximumSegmentCharacters + 1),
                },
            }
        };

        Assert.That(BuddySpeechSequence.Resolve(englishOnly).Single().language, Is.EqualTo("en"));
        Assert.That(BuddySpeechSequence.Resolve(hindiOnly).Single().language, Is.EqualTo("hi"));
        Assert.That(BuddySpeechSequence.Resolve(empty).Single().text, Is.EqualTo("Empty fallback"));
        Assert.That(BuddySpeechSequence.Resolve(overlong).Single().text, Is.EqualTo("Length fallback"));
    }

#if UNITY_EDITOR_WIN
    [Test]
    public void WindowsLocalTtsRuntimeHasAnInstalledVoice()
    {
        string powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        Assert.That(File.Exists(powerShellPath), Is.True, "Windows PowerShell is unavailable.");

        const string script =
            "$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Speech; " +
            "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
            "try { [Console]::Out.WriteLine($s.GetInstalledVoices().Count) } finally { $s.Dispose() }";
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            Arguments = $"-NoLogo -NoProfile -NonInteractive -Sta -WindowStyle Hidden -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using (Process process = Process.Start(startInfo))
        {
            Assert.That(process, Is.Not.Null, "Unity could not start the local Windows TTS worker.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            Assert.That(process.WaitForExit(10000), Is.True, "The local Windows TTS worker timed out.");
            Assert.That(process.ExitCode, Is.EqualTo(0), error);
            Assert.That(int.TryParse(output.Trim(), out int voiceCount), Is.True, output);
            Assert.That(voiceCount, Is.GreaterThan(0), "Windows has no local TTS voice installed.");
        }
    }
#endif
}
#endif
