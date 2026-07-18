using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plug-in contract that DrawController calls into.
/// Implement this on SandboxMode and ChallengeMode.
///
/// DrawController holds one IDrawMode reference and knows nothing about
/// which mode is active — swap at runtime by assigning DrawController.ActiveMode.
/// </summary>
public interface IDrawMode
{
    /// <summary>
    /// Called once when draw mode is entered.
    /// Use to display the prompt UI, reset state, etc.
    /// </summary>
    void OnEnter();

    /// <summary>
    /// Called once when draw mode exits (cancel or submit).
    /// Use to hide prompt UI.
    /// </summary>
    void OnExit();

    /// <summary>
    /// Called by DrawController after PDollar recognises a letter.
    /// result    = the full PDollar result (name + score).
    /// strokes   = current visual GameObjects (for recolouring).
    /// Returns the final accepted character to append to the word
    /// (the mode may override it — e.g. Sandbox always accepts).
    /// Return '\0' to signal "reject and let the player retry".
    /// </summary>
    char OnLetterConfirmed(PDollarRecognizer.RecognitionResult result,
                           System.Collections.Generic.List<UnityEngine.GameObject> strokes);

    /// <summary>
    /// Called when the full word is submitted.
    /// word = the string built from accepted letters.
    /// </summary>
    void OnWordSubmitted(string word);

    /// <summary>
    /// The hint string DrawController should display while drawing.
    /// Updated each frame so ChallengeMode can show "Write: C _ _" etc.
    /// </summary>
    string CurrentHint { get; }
}

/// <summary>
/// Optional draw-mode behavior for flows that consume the submitted word
/// themselves instead of sending it to the normal spell/combat handler.
/// </summary>
public interface IDrawSubmissionBehavior
{
    bool KeepDrawingSessionAfterSubmit { get; }
    bool ConsumeWordAction { get; }
}

// A drawing task can require spelling only one part of an accepted spoken
// phrase (for example RAT from "BIG RAT") while still passing the complete
// phrase to the world action that follows.
public interface IDrawWordActionPhraseProvider
{
    string ResolveWordActionPhrase(string drawnWord);
}

public interface IRawLetterEvaluator
{
    char OnLetterConfirmed(
        PDollarRecognizer.RecognitionResult result,
        System.Collections.Generic.List<UnityEngine.GameObject> strokeVisuals,
        System.Collections.Generic.List<System.Collections.Generic.List<UnityEngine.Vector2>> strokes);
}

public interface IExpectedLetterRecognitionContext
{
    bool TryGetExpectedLetterRecognition(out char expectedLetter, out float scoreThreshold);
}

/// <summary>
/// Adapts the existing letter-by-letter handwriting pipeline to an NPC answer.
/// Spaces and punctuation are ignored by the recognizer, while the dialogue
/// answer remains responsible for deciding whether the reconstructed response
/// is correct.
/// </summary>
public sealed class NpcHandwritingMode : IDrawMode, IRawLetterEvaluator, IExpectedLetterRecognitionContext, IDrawSubmissionBehavior
{
    const float RecognitionThreshold = PDollarRecognizer.SCORE_THRESHOLD;

    string targetLetters = "";
    int letterIndex;
    bool keepSessionAfterSubmit;
    Action<bool, string> submissionHandler;
    Action exitHandler;

    public string CurrentHint => letterIndex < targetLetters.Length
        ? $"Draw the letter '{targetLetters[letterIndex]}' and confirm it."
        : "Press Enter or Submit to check your drawing.";

    public bool KeepDrawingSessionAfterSubmit => keepSessionAfterSubmit;
    public bool ConsumeWordAction => true;

    public void Configure(string expectedAnswer, Action<bool, string> onSubmitted, Action onExited = null)
    {
        targetLetters = NormalizeLetters(expectedAnswer);
        submissionHandler = onSubmitted;
        exitHandler = onExited;
        keepSessionAfterSubmit = false;
        letterIndex = 0;
    }

    public void OnEnter()
    {
        letterIndex = 0;
        keepSessionAfterSubmit = false;
    }

    public void OnExit()
    {
        exitHandler?.Invoke();
        submissionHandler = null;
        exitHandler = null;
        targetLetters = "";
        letterIndex = 0;
        keepSessionAfterSubmit = false;
    }

    public char OnLetterConfirmed(PDollarRecognizer.RecognitionResult result, List<GameObject> strokeVisuals)
    {
        return OnLetterConfirmed(result, strokeVisuals, null);
    }

    public char OnLetterConfirmed(
        PDollarRecognizer.RecognitionResult result,
        List<GameObject> strokeVisuals,
        List<List<Vector2>> rawStrokes)
    {
        if (letterIndex >= targetLetters.Length)
            return '\0';

        char expected = char.ToUpperInvariant(targetLetters[letterIndex]);
        HandwritingAssessmentDecision decision = HandwritingAcceptancePolicy.Evaluate(
            expected,
            result,
            rawStrokes,
            default);
        if (!decision.Accepted)
            return '\0';

        letterIndex++;
        return expected;
    }

    public bool TryGetExpectedLetterRecognition(out char expectedLetter, out float scoreThreshold)
    {
        expectedLetter = letterIndex < targetLetters.Length
            ? char.ToUpperInvariant(targetLetters[letterIndex])
            : '\0';
        scoreThreshold = RecognitionThreshold;
        return expectedLetter != '\0';
    }

    public void OnWordSubmitted(string word)
    {
        string submittedLetters = NormalizeLetters(word);
        bool correct = submittedLetters == targetLetters && !string.IsNullOrEmpty(targetLetters);
        keepSessionAfterSubmit = !correct;
        submissionHandler?.Invoke(correct, submittedLetters);
    }

    static string NormalizeLetters(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetter(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
