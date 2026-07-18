using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum GrammarVoiceCombatState
{
    Idle,
    Listening,
    Processing,
    Heard,
    Accepted,
    Retry,
    Unavailable,
}

[DisallowMultipleComponent]
public sealed class GrammarVoiceCombatController : MonoBehaviour
{
    public VoiceUnlockRecognizer recognizer;
    public CreatureCombatRegistry registry;
    public CreatureCombatController creatureCombat;
    public TextMeshProUGUI statusLabel;
    [Min(0.2f)] public float releaseResultGraceSeconds = 1.25f;

    public GrammarVoiceCombatState State { get; private set; } = GrammarVoiceCombatState.Idle;
    public bool IsCombatEncounterActive => IsEncounterActive();
    public string LastTranscript { get; private set; } = "";
    public event Action<GrammarVoiceCombatState, string> OnStateChanged;

    Coroutine releaseRoutine;
    bool holdActive;
    VoiceUnlockRecognizer.RecognitionEvent? bufferedResult;

    void Awake()
    {
        creatureCombat ??= GetComponent<CreatureCombatController>() ?? gameObject.AddComponent<CreatureCombatController>();
        registry ??= GetComponent<CreatureCombatRegistry>() ?? gameObject.AddComponent<CreatureCombatRegistry>();
        recognizer ??= GetComponent<VoiceUnlockRecognizer>() ?? gameObject.AddComponent<VoiceUnlockRecognizer>();
        creatureCombat.registry = registry;
        ConfigureVocabulary();
        EnsureStatusUi();
    }

    void OnEnable()
    {
        recognizer.OnRecognitionResolved += HandleRecognition;
        recognizer.OnDisplayStateChanged += HandleRecognizerState;
        creatureCombat.OnStatus += HandleCombatStatus;
    }

    void OnDisable()
    {
        if (recognizer != null)
        {
            recognizer.OnRecognitionResolved -= HandleRecognition;
            recognizer.OnDisplayStateChanged -= HandleRecognizerState;
            recognizer.StopListening();
        }
        if (creatureCombat != null)
            creatureCombat.OnStatus -= HandleCombatStatus;
    }

    public void BeginHold()
    {
        if (!CanListen(out string reason))
        {
            SetState(GrammarVoiceCombatState.Retry, reason);
            return;
        }

        if (releaseRoutine != null)
        {
            StopCoroutine(releaseRoutine);
            releaseRoutine = null;
        }
        ConfigureVocabulary();
        LastTranscript = "";
        bufferedResult = null;
        holdActive = true;
        recognizer.StartListening(VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen);
        SetState(recognizer.IsAvailable ? GrammarVoiceCombatState.Listening : GrammarVoiceCombatState.Unavailable,
            recognizer.IsAvailable ? "Listening..." : "Microphone unavailable.");
    }

    public void EndHold()
    {
        if (State != GrammarVoiceCombatState.Listening && State != GrammarVoiceCombatState.Heard)
            return;
        holdActive = false;
        if (bufferedResult.HasValue)
        {
            VoiceUnlockRecognizer.RecognitionEvent result = bufferedResult.Value;
            bufferedResult = null;
            ResolveRecognition(result);
            return;
        }
        SetState(GrammarVoiceCombatState.Processing, "Processing...");
        recognizer.FinishListeningAttempt();
        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);
        releaseRoutine = StartCoroutine(FinishAfterGrace());
    }

    public void Cancel()
    {
        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        holdActive = false;
        bufferedResult = null;
        recognizer?.StopListening();
        SetState(GrammarVoiceCombatState.Idle, "Hold to speak");
    }

    IEnumerator FinishAfterGrace()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.2f, releaseResultGraceSeconds));
        releaseRoutine = null;
        if (State == GrammarVoiceCombatState.Processing)
        {
            recognizer.StopListening();
            SetState(GrammarVoiceCombatState.Retry, "I didn't catch that. Hold and try again.");
        }
    }

    void HandleRecognition(VoiceUnlockRecognizer.RecognitionEvent result)
    {
        if (result.Mode != VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen)
            return;
        if (holdActive)
        {
            bufferedResult = result;
            recognizer.StopListening(true);
            LastTranscript = string.IsNullOrWhiteSpace(result.RawText) ? result.Text : result.RawText;
            SetState(GrammarVoiceCombatState.Heard, string.IsNullOrWhiteSpace(LastTranscript)
                ? "Release to retry"
                : $"Heard: {LastTranscript}\nRelease to act");
            return;
        }
        ResolveRecognition(result);
    }

    void ResolveRecognition(VoiceUnlockRecognizer.RecognitionEvent result)
    {
        if (releaseRoutine != null)
        {
            StopCoroutine(releaseRoutine);
            releaseRoutine = null;
        }

        recognizer.StopListening(true);
        string heard = string.IsNullOrWhiteSpace(result.RawText) ? result.Text : result.RawText;
        LastTranscript = heard ?? "";
        if (!result.Recognized || string.IsNullOrWhiteSpace(result.Text))
        {
            SetState(GrammarVoiceCombatState.Retry, "I didn't catch that. Hold and try again.");
            return;
        }

        SetState(GrammarVoiceCombatState.Heard, $"Heard: {LastTranscript}");
        bool handled = creatureCombat.TryHandlePhrase(result.Text, recognizer.LastPronunciationInsight);
        if (!handled)
            SetState(GrammarVoiceCombatState.Retry, $"Heard: {LastTranscript}\nThat is not a battle command yet.");
    }

    void HandleCombatStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        SetState(IsFailureMessage(message) ? GrammarVoiceCombatState.Retry : GrammarVoiceCombatState.Accepted,
            string.IsNullOrWhiteSpace(LastTranscript) ? message : $"Heard: {LastTranscript}\n{message}");
    }

    void HandleRecognizerState(VoiceUnlockRecognizer.VoiceDisplayState state)
    {
        if (state == VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied ||
            state == VoiceUnlockRecognizer.VoiceDisplayState.Unavailable ||
            state == VoiceUnlockRecognizer.VoiceDisplayState.Error)
            SetState(GrammarVoiceCombatState.Unavailable, recognizer.StatusMessage);
    }

    bool CanListen(out string reason)
    {
        reason = "";
        if (PauseMenuController.IsPaused || ChestMiniGameState.IsOpen || RunEndScreenController.IsOpen)
        {
            reason = "Voice combat is paused.";
            return false;
        }

        EnemyWaveDirector director = FindAnyObjectByType<EnemyWaveDirector>();
        bool active = director != null &&
            (director.CurrentPhase == EncounterPhase.Combat || director.CurrentPhase == EncounterPhase.PillarDefense);
        if (!active)
        {
            reason = "Enter a combat encounter to use voice commands.";
            return false;
        }
        if (recognizer == null || !recognizer.IsAvailable)
        {
            reason = "Microphone unavailable.";
            return false;
        }
        return true;
    }

    void ConfigureVocabulary()
    {
        if (registry == null || recognizer == null)
            return;
        List<string> phrases = registry.GetVoiceKeywords();
        recognizer.ConfigureKeywords(phrases, phrases.Count > 0 ? phrases[0] : "", false);
        recognizer.CombatPronunciationInsightEnabled = CurriculumSessionManager.Instance?.HasStudentSession == true;
    }

    void SetState(GrammarVoiceCombatState state, string message)
    {
        State = state;
        if (statusLabel != null)
        {
            statusLabel.text = message ?? "";
            statusLabel.gameObject.SetActive(state != GrammarVoiceCombatState.Idle || IsEncounterActive());
        }
        OnStateChanged?.Invoke(state, message ?? "");
    }

    static bool IsFailureMessage(string message)
    {
        string value = message.ToLowerInvariant();
        return value.Contains("locked") || value.Contains("try") || value.Contains("cannot") ||
               value.Contains("does not") || value.Contains("summon a noun") || value.Contains("cooling down");
    }

    static bool IsEncounterActive()
    {
        EnemyWaveDirector director = FindAnyObjectByType<EnemyWaveDirector>();
        return director != null && (director.CurrentPhase == EncounterPhase.Combat || director.CurrentPhase == EncounterPhase.PillarDefense);
    }

    void EnsureStatusUi()
    {
        if (statusLabel != null)
            return;
        GameObject canvasRoot = new GameObject("GrammarVoiceCombatHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject labelRoot = new GameObject("VoiceStatus", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelRoot.transform.SetParent(canvasRoot.transform, false);
        RectTransform rect = labelRoot.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 34f);
        rect.sizeDelta = new Vector2(820f, 92f);
        statusLabel = labelRoot.GetComponent<TextMeshProUGUI>();
        statusLabel.font = TMP_Settings.defaultFontAsset;
        statusLabel.fontSize = 26f;
        statusLabel.alignment = TextAlignmentOptions.Center;
        statusLabel.color = Color.white;
        statusLabel.textWrappingMode = TextWrappingModes.Normal;
        statusLabel.gameObject.SetActive(false);
    }
}
