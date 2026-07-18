using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class WorldGoalHud : MonoBehaviour
{
    TextMeshProUGUI compactLabel;
    TextMeshProUGUI detailLabel;
    GameObject detailPanel;
    Coroutine celebrationRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!GrammarWorldRuntimeBootstrap.IsGrammarWorldTemplateSceneName(scene.name) ||
            FindAnyObjectByType<WorldGoalHud>() != null)
            return;
        new GameObject("WorldGoalHudController").AddComponent<WorldGoalHud>();
    }

    void Awake() => BuildUi();

    void OnEnable()
    {
        WorldGoalTracker tracker = WorldGoalTracker.EnsureExists();
        tracker.OnGoalChanged += Refresh;
        tracker.OnRewardClaimed += HandleRewardClaimed;
        Refresh();
    }

    void OnDisable()
    {
        if (WorldGoalTracker.Instance == null)
            return;
        WorldGoalTracker.Instance.OnGoalChanged -= Refresh;
        WorldGoalTracker.Instance.OnRewardClaimed -= HandleRewardClaimed;
    }

    void Refresh()
    {
        CurriculumSessionManager session = CurriculumSessionManager.Instance;
        WorldGoalAssignment goal = session?.CurrentWorldGoal;
        bool visible = session?.HasStudentSession == true && goal != null;
        compactLabel.transform.parent.gameObject.SetActive(visible);
        if (!visible)
        {
            detailPanel.SetActive(false);
            return;
        }

        string target = HumanizeGoal(goal.targetGymId);
        WorldGoalStatus status = WorldGoalTracker.Instance.Status;
        compactLabel.text = status switch
        {
            WorldGoalStatus.RewardClaimed => $"Weekly goal complete  +{goal.rewardCoins} coins",
            WorldGoalStatus.CompletedLate => "Weekly goal completed late",
            WorldGoalStatus.Expired => $"Weekly goal expired  {target}",
            _ => $"Weekly goal  {target}  +{goal.rewardCoins}",
        };
        detailLabel.text = $"Clear {target}\nDue {goal.dueDate}\nReward +{goal.rewardCoins} coins\nStatus: {StatusText(status)}";
    }

    void HandleRewardClaimed(WorldGoalClaimResult result)
    {
        if (result == null || result.rewardCoins <= 0 || result.alreadyClaimed)
            return;
        if (celebrationRoutine != null)
            StopCoroutine(celebrationRoutine);
        celebrationRoutine = StartCoroutine(ShowReward(result.rewardCoins));
    }

    IEnumerator ShowReward(int amount)
    {
        detailPanel.SetActive(true);
        detailLabel.text = $"Weekly goal complete!\n+{amount} coins";
        yield return new WaitForSecondsRealtime(4f);
        detailPanel.SetActive(false);
        celebrationRoutine = null;
        Refresh();
    }

    void BuildUi()
    {
        GameObject canvasRoot = new GameObject("WorldGoalCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot.transform.SetParent(transform, false);
        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 220;
        CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject compact = new GameObject("WeeklyGoalButton", typeof(RectTransform), typeof(Image), typeof(Button));
        compact.transform.SetParent(canvasRoot.transform, false);
        RectTransform rect = compact.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -24f);
        rect.sizeDelta = new Vector2(480f, 58f);
        compact.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.15f, 0.94f);
        compact.GetComponent<Button>().onClick.AddListener(() => detailPanel.SetActive(!detailPanel.activeSelf));
        compactLabel = MakeLabel(compact.transform, 22f);

        detailPanel = new GameObject("WeeklyGoalDetails", typeof(RectTransform), typeof(Image));
        detailPanel.transform.SetParent(canvasRoot.transform, false);
        RectTransform detailRect = detailPanel.GetComponent<RectTransform>();
        detailRect.anchorMin = detailRect.anchorMax = new Vector2(1f, 1f);
        detailRect.pivot = new Vector2(1f, 1f);
        detailRect.anchoredPosition = new Vector2(-24f, -92f);
        detailRect.sizeDelta = new Vector2(480f, 190f);
        detailPanel.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.10f, 0.97f);
        detailLabel = MakeLabel(detailPanel.transform, 21f);
        detailLabel.alignment = TextAlignmentOptions.TopLeft;
        detailLabel.rectTransform.offsetMin = new Vector2(22f, 16f);
        detailLabel.rectTransform.offsetMax = new Vector2(-22f, -16f);
        detailPanel.SetActive(false);
    }

    static TextMeshProUGUI MakeLabel(Transform parent, float size)
    {
        GameObject root = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(14f, 4f);
        rect.offsetMax = new Vector2(-14f, -4f);
        TextMeshProUGUI label = root.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    static string StatusText(WorldGoalStatus status) => status switch
    {
        WorldGoalStatus.RewardClaimed => "reward claimed",
        WorldGoalStatus.CompletedOnTime => "claiming reward",
        WorldGoalStatus.CompletedLate => "completed late",
        WorldGoalStatus.Expired => "expired",
        _ => "in progress",
    };

    static string HumanizeGoal(string areaId)
    {
        string[] parts = (areaId ?? "").Split(':');
        string topic = parts.Length > 1 ? parts[1] : "next";
        return topic switch
        {
            "GREETINGSANDSURVIVALENGLISH" => "Welcome Village Gym",
            "VOWELSANDCONSONANTS" => "Vowel Valley Gym",
            "SENTENCESTARTANDFULLSTOP" => "Sentence Square Gym",
            "NOUNS" => "Nounfield Gym",
            "VERBS" => "Verb Village Gym",
            "ARTICLES" => "Article Arcade Gym",
            "PRONOUNS" => "Pronoun Port Gym",
            "PLURALS" => "Plural Plains Gym",
            "ADJECTIVES" => "Adjective Grove Gym",
            "BASICPREPOSITIONS" => "Preposition Park Gym",
            _ => char.ToUpperInvariant(topic.Length > 0 ? topic[0] : 'G') + (topic.Length > 1 ? topic.Substring(1).ToLowerInvariant() : "ym"),
        };
    }
}
