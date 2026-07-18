using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.Networking;

public class UpgradeShopController : MonoBehaviour
{
    [Serializable]
    sealed class PurchaseRequest
    {
        public string schoolId = "";
        public string studentId = "";
        public string itemId = "";
    }

    [Serializable]
    sealed class PurchaseResult
    {
        public bool ok = false;
        public string itemId = "";
        public int charged = 0;
        public bool alreadyOwned = false;
        public int walletBalance = 0;
    }

    [Serializable]
    sealed class PurchaseEnvelope
    {
        public PurchaseResult result = new PurchaseResult();
    }

    [Header("Authored UI")]
    [SerializeField] private UpgradeShopView authoredView;
    [SerializeField] private UpgradeShopView authoredViewPrefab;
    [SerializeField] private bool allowRuntimeUiFallback = true;

    private WorldSessionManager worldSession;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI coinsLabel;
    private TextMeshProUGUI messageLabel;
    private readonly List<Button> itemButtons = new List<Button>();
    private List<CosmeticShopItem> items = new List<CosmeticShopItem>();
    private bool purchasePending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapIfShopScene()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) =>
        {
            WorldSessionManager manager = WorldSessionManager.Instance;
            if (manager == null || string.IsNullOrWhiteSpace(manager.shopSceneName))
                return;

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != manager.shopSceneName)
                return;

            if (FindAnyObjectByType<UpgradeShopController>() != null)
                return;

            new GameObject("CosmeticShopController").AddComponent<UpgradeShopController>();
        };
    }

    void Awake()
    {
        worldSession = WorldSessionManager.EnsureExists();
        EnsureEventSystem();
        UnlockCursorForShop();
        BuildUi();
        RefreshItems();
    }

    void OnEnable()
    {
        CosmeticInventoryStore.OnCosmeticsChanged += HandleCosmeticsChanged;
        PersistentCoinWallet.OnCoinsChanged += HandleCoinsChanged;
    }

    void OnDisable()
    {
        CosmeticInventoryStore.OnCosmeticsChanged -= HandleCosmeticsChanged;
        PersistentCoinWallet.OnCoinsChanged -= HandleCoinsChanged;
    }

    void BuildUi()
    {
        if (TryBindAuthoredUi())
            return;

        if (!allowRuntimeUiFallback)
        {
            Debug.LogError("[UpgradeShopController] No authored UpgradeShopView is assigned and runtime UI fallback is disabled.", this);
            return;
        }

        Debug.LogWarning("[UpgradeShopController] Using runtime-generated cosmetic shop UI. Assign an UpgradeShopView prefab for production.", this);
        var canvasGo = new GameObject("ShopCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(canvasGo.transform, false);
        var backdropRt = backdrop.GetComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.06f, 1f);

        titleLabel = MakeLabel("Title", canvasGo.transform, new Vector2(0f, -72f), 48f, FontStyles.Bold, TextAlignmentOptions.Center);
        titleLabel.text = "Cosmetic Shop";
        coinsLabel = MakeLabel("Coins", canvasGo.transform, new Vector2(0f, -132f), 30f, FontStyles.Bold, TextAlignmentOptions.Center);
        messageLabel = MakeLabel("Message", canvasGo.transform, new Vector2(0f, -812f), 24f, FontStyles.Normal, TextAlignmentOptions.Center);

        var itemsRoot = new GameObject("Cosmetics", typeof(RectTransform), typeof(GridLayoutGroup));
        itemsRoot.transform.SetParent(canvasGo.transform, false);
        var itemsRt = itemsRoot.GetComponent<RectTransform>();
        itemsRt.anchorMin = new Vector2(0.5f, 0.5f);
        itemsRt.anchorMax = new Vector2(0.5f, 0.5f);
        itemsRt.pivot = new Vector2(0.5f, 0.5f);
        itemsRt.anchoredPosition = new Vector2(0f, -28f);
        itemsRt.sizeDelta = new Vector2(1360f, 560f);

        var grid = itemsRoot.GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.cellSize = new Vector2(308f, 236f);
        grid.spacing = new Vector2(28f, 28f);
        grid.childAlignment = TextAnchor.MiddleCenter;

        int buttonCount = Mathf.Max(8, CosmeticCatalog.GetShopItems().Count);
        for (int i = 0; i < buttonCount; i++)
            itemButtons.Add(MakeItemButton(itemsRoot.transform, i));

        Button backButton = MakeCommandButton(canvasGo.transform, "Back", new Vector2(0f, 74f), new Vector2(360f, 74f));
        backButton.onClick.AddListener(HandleBackPressed);
        RefreshCoins();
    }

    bool TryBindAuthoredUi()
    {
        UpgradeShopView view = authoredView;
        if (view == null)
            view = GetComponentInChildren<UpgradeShopView>(true);
        if (view == null)
            view = FindAnyObjectByType<UpgradeShopView>(FindObjectsInactive.Include);
        if (view == null && authoredViewPrefab != null)
            view = Instantiate(authoredViewPrefab, transform);

        if (view == null)
            return false;

        authoredView = view;
        if (!view.HasMinimumBindings())
        {
            Debug.LogWarning("[UpgradeShopController] Authored UpgradeShopView is missing one or more required bindings. Falling back to runtime UI.", view);
            return false;
        }

        view.gameObject.SetActive(true);
        titleLabel = view.titleLabel;
        coinsLabel = view.coinsLabel;
        messageLabel = view.messageLabel;
        itemButtons.Clear();
        itemButtons.AddRange(view.offerButtons.FindAll(button => button != null));

        titleLabel.text = "Cosmetic Shop";
        view.continueButton.onClick.RemoveAllListeners();
        view.continueButton.onClick.AddListener(HandleBackPressed);
        TextMeshProUGUI continueLabel = view.continueButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (continueLabel != null)
            continueLabel.text = "Back";
        RefreshCoins();
        return true;
    }

    void RefreshItems()
    {
        items = CosmeticCatalog.GetShopItems();
        for (int i = 0; i < itemButtons.Count; i++)
        {
            Button button = itemButtons[i];
            bool hasItem = i < items.Count;
            button.gameObject.SetActive(hasItem);
            if (!hasItem)
                continue;

            CosmeticShopItem item = items[i];
            bool unlocked = CosmeticInventoryStore.IsUnlocked(item);
            bool equipped = CosmeticInventoryStore.IsEquipped(item);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                string category = item.kind == CosmeticItemKind.Skin ? "Skin" : "Companion";
                string action = equipped ? "Equipped" : unlocked ? "Equip" : $"{item.price} coins";
                label.text = $"{item.displayName}\n<size=20>{category}</size>\n<size=19>{item.description}</size>\n\n{action}";
            }

            button.interactable = !purchasePending && !equipped && (unlocked || PersistentCoinWallet.Coins >= item.price);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => TryBuyOrEquip(item));
        }
    }

    void TryBuyOrEquip(CosmeticShopItem item)
    {
        CurriculumSessionManager session = CurriculumSessionManager.Instance;
        if (session != null && session.HasStudentSession)
        {
            if (!purchasePending)
                StartCoroutine(PurchaseAuthenticatedCosmetic(session, item));
            return;
        }

        if (CosmeticInventoryStore.TryBuyOrEquip(item, out string message))
        {
            messageLabel.text = message;
            RefreshCoins();
            RefreshItems();
            return;
        }

        messageLabel.text = message;
    }

    IEnumerator PurchaseAuthenticatedCosmetic(CurriculumSessionManager session, CosmeticShopItem item)
    {
        if (session == null || item == null)
            yield break;
        string studentId = session.activeStudentId;
        string endpoint = (session.firebaseFunctionsBaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            messageLabel.text = "Shop connection is not configured.";
            yield break;
        }

        purchasePending = true;
        messageLabel.text = CosmeticInventoryStore.IsUnlocked(item) ? "Equipping..." : "Purchasing...";
        RefreshItems();
        var payload = new PurchaseRequest
        {
            schoolId = session.activeSchoolId,
            studentId = studentId,
            itemId = item.id,
        };
        string body = "{\"data\":" + JsonUtility.ToJson(payload) + "}";
        using UnityWebRequest request = new UnityWebRequest(endpoint + "/purchaseCosmetic", UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 20,
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {session.studentIdToken}");
        yield return request.SendWebRequest();

        CurriculumSessionManager current = CurriculumSessionManager.Instance;
        if (current == null || !string.Equals(current.activeStudentId, studentId, StringComparison.Ordinal))
        {
            purchasePending = false;
            yield break;
        }

        PurchaseEnvelope envelope = request.result == UnityWebRequest.Result.Success
            ? JsonUtility.FromJson<PurchaseEnvelope>(request.downloadHandler.text)
            : null;
        if (envelope?.result?.ok == true)
        {
            WorldEconomyService.EnsureExists().MirrorServerBalance(envelope.result.walletBalance);
            CosmeticInventoryStore.ApplyServerPurchase(item);
            messageLabel.text = envelope.result.alreadyOwned
                ? $"Equipped {item.displayName}."
                : $"Bought {item.displayName} for {envelope.result.charged} coins.";
        }
        else
        {
            messageLabel.text = request.responseCode == 400 || request.responseCode == 412
                ? "Purchase could not be completed. Check your coin balance."
                : "Shop connection failed. Nothing was charged.";
        }

        purchasePending = false;
        RefreshCoins();
        RefreshItems();
    }

    void HandleCoinsChanged(int _)
    {
        RefreshCoins();
        RefreshItems();
    }

    void HandleCosmeticsChanged()
    {
        RefreshItems();
    }

    void RefreshCoins()
    {
        if (coinsLabel != null)
            coinsLabel.text = $"Coins {PersistentCoinWallet.Coins}";
    }

    void HandleBackPressed()
    {
        (worldSession ?? WorldSessionManager.EnsureExists()).ReturnToMainMenu();
    }

    Button MakeItemButton(Transform parent, int index)
    {
        var go = new GameObject($"Cosmetic{index + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.19f, 0.96f);

        var label = MakeChildLabel(go.transform, "Label", 25f, FontStyles.Bold);
        label.rectTransform.offsetMin = new Vector2(18f, 16f);
        label.rectTransform.offsetMax = new Vector2(-18f, -16f);
        return go.GetComponent<Button>();
    }

    Button MakeCommandButton(Transform parent, string text, Vector2 anchoredPosition, Vector2 size)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.95f, 0.78f, 0.30f, 1f);

        var label = MakeChildLabel(go.transform, "Label", 30f, FontStyles.Bold);
        label.text = text;
        label.color = Color.black;
        return go.GetComponent<Button>();
    }

    TextMeshProUGUI MakeLabel(string name, Transform parent, Vector2 anchoredPosition, float fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(1100f, 60f);

        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    TextMeshProUGUI MakeChildLabel(Transform parent, string name, float fontSize, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    void EnsureEventSystem()
    {
        EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
            return;
        }

        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
        {
            StandaloneInputModule standalone = eventSystem.GetComponent<StandaloneInputModule>();
            if (standalone != null)
                Destroy(standalone);

            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }

    void UnlockCursorForShop()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

public sealed class UpgradeShopView : MonoBehaviour
{
    [Header("Labels")]
    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI coinsLabel;
    public TextMeshProUGUI messageLabel;

    [Header("Controls")]
    public Button continueButton;
    public List<Button> offerButtons = new List<Button>();

    public bool HasMinimumBindings()
    {
        return titleLabel != null &&
               coinsLabel != null &&
               continueButton != null &&
               offerButtons != null &&
               offerButtons.Count > 0;
    }
}
