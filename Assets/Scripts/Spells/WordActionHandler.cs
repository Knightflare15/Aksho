using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Routes spoken/written battle words into grammar creature combat. Legacy spell pages remain for non-grammar modes.
/// </summary>
[RequireComponent(typeof(CreatureCombatController))]
public partial class WordActionHandler : MonoBehaviour
{
    public const int DefaultSpellbookSlotCount = 4;
    const float CombatVoiceRearmSeconds = 1f;
    const float DelayedPronunciationReviewWindowSeconds = 30f;

    [Header("Legacy Template Reference")]
    public CheeseEnemyTarget cheeseEnemyTarget;

    [Header("Legacy Word-Spell Compatibility")]
    [Tooltip("Disabled for the production grammar-creature game. Enable only in a deliberately maintained legacy/sandbox scene.")]
    public bool legacyWordSpellCastingEnabled;

    [Header("Legacy Projectile Combat")]
    public Transform spellOrigin;
    public SpellRegistry spellRegistry;

    [Header("Creature Combat")]
    public bool useCreatureCombat = true;
    public CreatureCombatController creatureCombat;
    public TacticalGrammarBattleController tacticalBattle;

    [Header("Spellbook")]
    [Min(1)] public int spellbookSlotCount = DefaultSpellbookSlotCount;
    public SpellbookSlot[] spellbookSlots;

    [Header("Voice Casting")]
    [Tooltip("Enables hold-attack voice casting for loaded spellbook pages.")]
    public bool automaticVoiceCasting = true;
    [Min(0f)] public float voiceCastInitialDelay = 0.25f;
    [Min(1f)] public float voiceCastSuccessRetryDelay = CombatVoiceRearmSeconds;
    [Min(0.1f)] public float voiceCastErrorRetryDelay = 1.4f;

    private ChallengeMode challengeMode;
    private PlayerController playerController;
    private DrawController drawController;

    private int selectedSlotIndex;
    private string statusHint = "";
    private float statusHintUntil;
    private RunProgressionManager runProgression;
    private VoiceUnlockRecognizer voiceCastRecognizer;
    private PlayerLearningProfile learningProfile;
    private PlayerAimAssist aimAssist;
    private PhoneticDisplayState phoneticDisplayState;
    private float castListeningStartedAt;
    private string configuredCastPageSignature = "";
    private string configuredCastKeywords = "";
    private readonly Dictionary<string, string> castAliasesToCanonical =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    private float nextContinuousListenAt;
    private bool attackButtonHeld;
    private float lastAttackVoiceDebugAt = -999f;
    private float lastConsumedVoiceActivityAt = -1f;
    private string lastSuccessfulVoiceCastWord = "";
    private bool lastSuccessfulVoiceCastWasArea;
    private float lastSuccessfulVoiceCastAt = -1f;
    private SpellTarget[] cachedTargets = System.Array.Empty<SpellTarget>();
    private float nextTargetRefreshAt = -1f;
    private readonly SpellbookSlot dormantSpellbookSlot = new SpellbookSlot();

    public int LoadedShots => legacyWordSpellCastingEnabled ? SelectedSlot.currentAmmo : 0;
    public int LoadedShotCapacity => legacyWordSpellCastingEnabled ? SelectedSlot.maxAmmo : 0;
    public string LoadedSpellWord => !legacyWordSpellCastingEnabled
        ? ""
        : SelectedSlot.pageType == SpellbookPageType.Letter ? SelectedSlot.pageLetter : SelectedSlot.spellWord;
    public int SelectedSlotIndex => selectedSlotIndex;
    public int SlotCount => legacyWordSpellCastingEnabled && spellbookSlots != null ? spellbookSlots.Length : 0;
    public string StatusHint => Time.unscaledTime <= statusHintUntil ? statusHint : "";
    public string ConfiguredCastKeywords => configuredCastKeywords;
    public string LiveVoiceGuess =>
        IsListeningForCast && voiceCastRecognizer != null
            ? voiceCastRecognizer.LastRecognizedText
            : "";
    public bool IsListeningForCast => voiceCastRecognizer != null &&
                                      voiceCastRecognizer.IsListening &&
                                      voiceCastRecognizer.ActiveMode == VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen;
    public bool IsAttackListenActive => voiceCastRecognizer != null &&
                                        voiceCastRecognizer.ActiveMode == VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen;
    public bool IsAttackCastHeld => attackButtonHeld;
    public bool IsGrammarBattleFlowActive => useCreatureCombat && creatureCombat != null && creatureCombat.enabledForPhrases;
    public string ActiveCreatureSummary
    {
        get
        {
            if (tacticalBattle != null && tacticalBattle.IsActive)
                return "Tactical grid active";

            SummonedCreatureActor active = creatureCombat != null ? creatureCombat.ActiveCreature : null;
            if (active == null)
                return "No creature summoned";

            string noun = active.CanonicalNoun;
            CreatureStatBlock stats = active.Stats;
            return $"{noun.ToUpperInvariant()}  PP {active.CurrentPp}/{stats.maxPp}  ATK {stats.attack}  DEF {stats.defense}  SPD {stats.speed}";
        }
    }
    public string TacticalBoardSummary => tacticalBattle != null && tacticalBattle.IsActive ? tacticalBattle.BuildHudSummary() : "";
    public float AttackListenDelayRemaining => Mathf.Max(0f, nextContinuousListenAt - Time.unscaledTime);
    public string AttackListenStatusDetail =>
        voiceCastRecognizer == null
            ? "voice recognizer missing"
            : !voiceCastRecognizer.IsAvailable
                ? "voice unavailable"
                : voiceCastRecognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.PermissionDenied
                    ? "permission denied"
                    : voiceCastRecognizer.CurrentDisplayState == VoiceUnlockRecognizer.VoiceDisplayState.Unavailable
                        ? "voice unavailable"
                        : AttackListenDelayRemaining > 0.01f
                            ? "arming"
                            : IsListeningForCast
                                ? "listening"
                                : "starting";
    public SpellbookSlot SelectedSlot
    {
        get
        {
            EnsureSpellbookSlots();
            if (!legacyWordSpellCastingEnabled)
                return dormantSpellbookSlot;
            if (spellbookSlots == null || spellbookSlots.Length == 0)
                return dormantSpellbookSlot;
            return spellbookSlots[Mathf.Clamp(selectedSlotIndex, 0, spellbookSlots.Length - 1)];
        }
    }


}
