using System;
using UnityEngine;

public sealed class ManualSpeechRecognitionServiceStub : MonoBehaviour, ISpeechRecognitionService
{
    [TextArea] public string nextTranscript = "";
    public bool available = true;
    public string providerName = "Manual Speech Stub";

    public bool IsAvailable => available;
    public string ProviderName => providerName;

    public string CaptureTranscript(string prompt)
    {
        Debug.Log($"[ManualSpeechRecognitionServiceStub] Prompt: {prompt}");
        return nextTranscript ?? "";
    }
}

public sealed class DialogueAudioPlaybackServiceStub : MonoBehaviour, IAudioPlaybackService
{
    public AudioSource audioSource;
    public bool loadResourcesClips = true;

    public void PlayDialogue(DialogueLine line)
    {
        if (line == null)
            return;

        AudioClip clip = line.audioClip;
        if (clip == null && loadResourcesClips && LooksLikeResourcesPath(line.audioPathPlaceholder))
            clip = Resources.Load<AudioClip>(line.audioPathPlaceholder);

        if (audioSource != null && clip != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }

        Debug.Log($"[DialogueAudioPlaybackServiceStub] Play dialogue '{line.dialogueId}' clip='{(clip != null ? clip.name : line.audioPathPlaceholder)}'");
    }

    static bool LooksLikeResourcesPath(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains("/");
    }
}

public sealed class PlayerPrefsLearnerProgressPersistence : MonoBehaviour, ILearnerProgressPersistence
{
    const string KeyPrefix = "language_learning_progress_";

    public void Save(string learnerId, LearnerProgressSummary summary)
    {
        if (string.IsNullOrWhiteSpace(learnerId) || summary == null)
            return;

        PlayerPrefs.SetString(KeyPrefix + learnerId.Trim(), JsonUtility.ToJson(summary));
        PlayerPrefs.Save();
    }

    public LearnerProgressSummary Load(string learnerId)
    {
        if (string.IsNullOrWhiteSpace(learnerId))
            return new LearnerProgressSummary();

        string key = KeyPrefix + learnerId.Trim();
        if (!PlayerPrefs.HasKey(key))
            return new LearnerProgressSummary();

        string json = PlayerPrefs.GetString(key, "");
        return string.IsNullOrWhiteSpace(json)
            ? new LearnerProgressSummary()
            : JsonUtility.FromJson<LearnerProgressSummary>(json) ?? new LearnerProgressSummary();
    }
}

[DisallowMultipleComponent]
public sealed class GrammarNpcLearningBridge : MonoBehaviour
{
    public GrammarNpc grammarNpc;
    public NPCInteractionManager interactionManager;
    public bool logLifecycle = true;

    DialogueSessionState session;

    void Awake()
    {
        grammarNpc ??= GetComponent<GrammarNpc>();
        interactionManager ??= GetComponent<NPCInteractionManager>() ?? FindAnyObjectByType<NPCInteractionManager>();
        if (interactionManager == null)
        {
            GameObject runtime = new GameObject("NPCInteractionManager_Runtime");
            interactionManager = runtime.AddComponent<NPCInteractionManager>();
        }
    }

    [ContextMenu("Begin Learning Interaction")]
    public void BeginLearningInteraction()
    {
        grammarNpc ??= GetComponent<GrammarNpc>();
        if (grammarNpc == null || interactionManager == null)
            return;

        LocalizedDialogueLine line = grammarNpc.GetCurrentLine();
        session = interactionManager.StartLocalizedDialogue(grammarNpc.npcId, grammarNpc.displayName, line);
        if (session == null)
        {
            if (logLifecycle)
                Debug.LogWarning("[GrammarNpcLearningBridge] Could not start a learning interaction.", this);
            return;
        }

        if (logLifecycle)
        {
            Debug.Log($"[GrammarNpcLearningBridge] Started learning interaction for '{grammarNpc.displayName}'.", this);
            Debug.Log($"[GrammarNpcLearningBridge] Support={session.presentation.supportLevel} Task={session.presentation.taskType} Display='{session.presentation.displayText}'", this);
        }
    }

    public DialogueTurnResult SubmitAnswer(string answer)
    {
        if (interactionManager == null || interactionManager.dialogueManager == null || session == null)
        {
            return new DialogueTurnResult
            {
                answerResult = new AnswerCheckResult
                {
                    feedbackMessage = "No learning interaction is active.",
                    detectedErrorType = AnswerErrorType.UnsupportedTask,
                },
            };
        }

        DialogueTurnResult result = interactionManager.dialogueManager.SubmitAnswer(answer);
        if (logLifecycle)
            Debug.Log($"[GrammarNpcLearningBridge] Answer='{answer}' Correct={result.answerResult.isCorrect} Feedback='{result.answerResult.feedbackMessage}'", this);
        if (result.hint != null && logLifecycle)
            Debug.Log($"[GrammarNpcLearningBridge] Hint: {result.hint.text}", this);

        if (result.interactionComplete)
        {
            LocalizedDialogueLine acceptedLine = grammarNpc != null ? grammarNpc.GetCurrentLine() : null;
            grammarNpc?.HandleDialogueResponseAccepted(acceptedLine);
            session = null;
        }

        return result;
    }
}

[DisallowMultipleComponent]
public sealed class TacticalGrammarBattleDemoRunner : MonoBehaviour
{
    public bool runOnStart;
    public CreatureCombatRegistry creatureCombatRegistry;

    void Start()
    {
        if (runOnStart)
            RunDemo();
    }

    [ContextMenu("Run Tactical Grammar Battle Demo")]
    public void RunDemo()
    {
        creatureCombatRegistry ??= GetComponent<CreatureCombatRegistry>();
        if (creatureCombatRegistry == null)
        {
            creatureCombatRegistry = gameObject.AddComponent<CreatureCombatRegistry>();
            creatureCombatRegistry.catalog = CreatureCombatCatalog.CreateRuntimeDefault();
        }

        var battler = new TacticalGrammarBattler(creatureCombatRegistry);
        battler.SetTerrain(new TacticalBattlePosition(1, 1), TacticalBattleCellType.Box);
        battler.SetTerrain(new TacticalBattlePosition(3, 1), TacticalBattleCellType.Spikes);
        battler.SetTerrain(new TacticalBattlePosition(3, 2), TacticalBattleCellType.Spikes);
        battler.SetTerrain(new TacticalBattlePosition(1, 3), TacticalBattleCellType.Roof);
        battler.SetEnemyUnit("TURTLE", new TacticalBattlePosition(4, 1));

        TacticalBattleCommandResult summon = battler.SummonPlayer("a big rat", new TacticalBattlePosition(0, 2));
        Debug.Log($"[TacticalGrammarBattleDemo] {summon.message}");
        if (battler.State.playerUnit != null)
        {
            Debug.Log($"[TacticalGrammarBattleDemo] Stats HP={battler.State.playerUnit.stats.maxHp} ATK={battler.State.playerUnit.stats.attack} DEF={battler.State.playerUnit.stats.defense} SPD={battler.State.playerUnit.stats.speed}");
        }

        TacticalBattleCommandResult move = battler.ExecutePlayerCommand("the rat runs around the spikes");
        Debug.Log($"[TacticalGrammarBattleDemo] {move.message}");

        TacticalBattleCommandResult cover = battler.ExecutePlayerCommand("the rat hides behind the box");
        Debug.Log($"[TacticalGrammarBattleDemo] {cover.message}");

        TacticalBattleCommandResult attack = battler.ExecutePlayerCommand("the rat scratches fast");
        Debug.Log($"[TacticalGrammarBattleDemo] {attack.message} speed={attack.actionProfile.speedScore:0.00} pp={attack.actionProfile.ppCost}");
    }
}
