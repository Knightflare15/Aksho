using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Sandbox draw mode — zero pressure, always positive.
///
/// • Every letter attempt is accepted (even '?' unrecognised ones become '?').
/// • Only correct-colour feedback fires (green flash, small happy wobble).
/// • No screen shake, no red, no wrong-feedback haptics.
/// • Word submission routes to WordActionHandler as normal.
///
/// Attach to the same GameObject as DrawController.
/// Assign in DrawController.ActiveMode via the Inspector or at runtime.
/// </summary>
public class SandboxMode : MonoBehaviour, IDrawMode
{
    [Header("UI")]
    [Tooltip("Optional label to show the mode name / encouragement")]
    public TextMeshProUGUI promptLabel;

    private FeedbackManager feedback;
    private CurriculumSessionManager curriculum;
    private string _hint = "Draw anything! Right-click to confirm each letter.";

    public string CurrentHint => _hint;

    void Awake()
    {
        feedback = GetComponent<FeedbackManager>();
        curriculum = CurriculumSessionManager.EnsureExists();
    }

    // ── IDrawMode ──────────────────────────────────────────────────────────

    public void OnEnter()
    {
        if (promptLabel)
        {
            promptLabel.gameObject.SetActive(true);
            promptLabel.text = "✏️  Sandbox — write whatever you like!";
        }
        _hint = "Draw unlocked letters, Right-click to confirm, Enter to cast!";
    }

    public void OnExit()
    {
        if (promptLabel)
            promptLabel.gameObject.SetActive(false);
    }

    public char OnLetterConfirmed(PDollarRecognizer.RecognitionResult result,
                                   List<GameObject> strokes)
    {
        // Always accept — green flash regardless of score
        if (feedback != null)
            feedback.PlayCorrectFeedback(strokes);

        char letter = (result.name != "Unknown" && result.name.Length > 0)
                      ? result.name[0] : '?';

        if (letter != '?' && curriculum != null && !curriculum.IsSandboxLetterUnlocked(letter))
        {
            _hint = $"'{char.ToUpperInvariant(letter)}' is still locked. Try an unlocked letter.";
            return '?';
        }

        _hint = letter == '?' ? "Hmm, I couldn't read that — but that's OK! Try again."
                              : $"Nice '{letter}'! Keep going or press Enter ✨";

        return letter;
    }

    public void OnWordSubmitted(string word)
    {
        if (curriculum != null && !curriculum.IsSandboxWordUnlocked(word))
        {
            _hint = $"\"{word}\" is still locked. Try one of your unlocked words.";
            return;
        }

        if (feedback != null)
            feedback.PlaySuccessFeedback();

        _hint = $"You wrote \"{word}\"! Amazing! 🎉";
        Debug.Log($"[SandboxMode] Word submitted: '{word}'");
    }
}
