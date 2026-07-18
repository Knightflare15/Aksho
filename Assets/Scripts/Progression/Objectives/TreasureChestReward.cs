using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class TreasureChestReward : MonoBehaviour
{
    public ChestMiniGameKind miniGameKind = ChestMiniGameKind.Counting;
    public int coinCount = 3;
    public string chestCategory = "RuntimeChest";
    public float interactionRadius = 3f;
    public CoinPickup visualCoinPrefab;
    public AudioClip[] numberPronunciationClips;

    bool opened;
    bool seenInInteractionRadius;
    bool ignoredRecorded;
    PlayerController player;
    Renderer[] renderers;

    public bool IsOpened => opened;

    public void Initialize(
        ChestMiniGameKind kind,
        int count,
        string category,
        CoinPickup coinPrefab,
        AudioClip[] pronunciationClips)
    {
        miniGameKind = kind;
        coinCount = Mathf.Clamp(count, 1, 10);
        chestCategory = string.IsNullOrWhiteSpace(category) ? "RuntimeChest" : category;
        visualCoinPrefab = coinPrefab;
        numberPronunciationClips = pronunciationClips;
        EnsureSetup();
    }

    void Awake()
    {
        EnsureSetup();
    }

    void Update()
    {
        if (opened || ChestMiniGameState.IsOpen || IsGameplayUiBlocking())
            return;

        if (player == null)
            player = FindAnyObjectByType<PlayerController>();
        if (player == null)
            return;

        bool inRange = Vector3.Distance(transform.position, player.transform.position) <= Mathf.Max(0.5f, interactionRadius);
        if (!inRange)
            return;

        seenInInteractionRadius = true;
        ChestInteractionPromptUI.EnsureExists().Show(this);
        if (WasInteractPressed())
            TryOpen();
    }

    void OnDestroy()
    {
        RecordIgnoredIfNeeded();
    }

    public void TryOpen()
    {
        if (opened || ChestMiniGameState.IsOpen || IsGameplayUiBlocking())
            return;

        opened = true;
        ApplyOpenedVisual();
        ChestInteractionPromptUI.EnsureExists().Hide();
        if (miniGameKind == ChestMiniGameKind.Color)
            ChestColorMiniGameUI.EnsureExists().Open(this);
        else
            ChestCountingMiniGameUI.EnsureExists().Open(this);
    }

    public AudioClip GetNumberClip(int number)
    {
        if (numberPronunciationClips == null || numberPronunciationClips.Length == 0)
            return null;

        int index = Mathf.Clamp(number, 1, 10) - 1;
        return index >= 0 && index < numberPronunciationClips.Length ? numberPronunciationClips[index] : null;
    }

    void RecordIgnoredIfNeeded()
    {
        if (ignoredRecorded || opened || !seenInInteractionRadius)
            return;

        ignoredRecorded = true;
        CurriculumSessionManager session = CurriculumSessionManager.Instance;
        if (session == null || !session.IsSchoolModeActive)
            return;

        if (miniGameKind == ChestMiniGameKind.Color)
        {
            session.RecordColorMiniGameAttempt(
                chestCategory,
                "",
                "",
                "",
                false,
                false,
                false,
                0,
                0f,
                "seen_ignored");
            return;
        }

        session.RecordCountingMiniGameAttempt(
            chestCategory,
            coinCount,
            0,
            "",
            false,
            false,
            false,
            0,
            0f,
            "seen_ignored");
    }

    void EnsureSetup()
    {
        coinCount = Mathf.Clamp(coinCount, 1, 10);
        renderers = GetComponentsInChildren<Renderer>(true);
        Collider collider = GetComponentInChildren<Collider>(true);
        if (collider == null)
            collider = gameObject.AddComponent<BoxCollider>();
        collider.isTrigger = false;
    }

    void ApplyOpenedVisual()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", new Color(0.42f, 0.28f, 0.16f, 1f));
            properties.SetColor("_Color", new Color(0.42f, 0.28f, 0.16f, 1f));
            renderer.SetPropertyBlock(properties);
        }
    }

    static bool WasInteractPressed()
    {
        if (Input.GetMouseButtonDown(1))
            return true;

        Mouse mouse = Mouse.current;
        return mouse != null && mouse.rightButton.wasPressedThisFrame;
    }

    static bool IsGameplayUiBlocking()
    {
        return PauseMenuController.IsPaused ||
               TemplateRecorderUI.IsOpen ||
               GrimoireUI.IsOpen ||
               RunEndScreenController.IsOpen;
    }
}
