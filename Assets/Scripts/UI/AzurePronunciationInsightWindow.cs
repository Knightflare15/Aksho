using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Standalone, non-dialogue window for the asynchronous Azure result.</summary>
public sealed class AzurePronunciationInsightWindow : MonoBehaviour
{
    static AzurePronunciationInsightWindow instance;
    Canvas canvas;
    GameObject panel;
    TextMeshProUGUI text;

    public static AzurePronunciationInsightWindow EnsureExists()
    {
        if (instance != null) return instance;
        var go = new GameObject("AzurePronunciationInsightWindow");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AzurePronunciationInsightWindow>();
        return instance;
    }

    void Awake()
    {
        Build();
        CurriculumSessionManager.EnsureExists().OnAzurePronunciationInsightReady += Show;
    }

    void Build()
    {
        var root = new GameObject("AzureInsightCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

        panel = new GameObject("AzureInsightPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-32f, -32f); rect.sizeDelta = new Vector2(620f, 500f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.05f, 0.04f, 0.96f);
        text = MakeText(panel.transform, "Azure Pronunciation\nWaiting for assessment...", 21f);
        panel.SetActive(false);
    }

    static TextMeshProUGUI MakeText(Transform parent, string value, float size)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = value; label.fontSize = size; label.color = Color.white;
        label.alignment = TextAlignmentOptions.TopLeft; label.textWrappingMode = TextWrappingModes.Normal;
        var rect = label.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(18f, 18f); rect.offsetMax = new Vector2(-18f, -18f);
        return label;
    }

    void Show(PronunciationInsightRecord insight)
    {
        if (insight == null || text == null) return;
        text.text = FormatInsight(insight);
        panel.SetActive(true);
    }

    static string FormatInsight(PronunciationInsightRecord insight)
    {
        string target = FirstNonEmpty(insight.targetWord, insight.confirmedWord, "your response");
        string heard = insight.rawRecognizedText ?? "";
        var output = new StringBuilder();
        output.Append("Azure Pronunciation\n\n");
        output.Append("Target: ").Append(target).Append("\n");
        output.Append("Overall: ").Append(Mathf.RoundToInt(Mathf.Clamp01(insight.score) * 100f)).Append("%\n");
        if (!string.IsNullOrWhiteSpace(heard))
            output.Append("Heard: ").Append(heard).Append("\n");
        output.Append("\n<b>Phonetic breakdown</b>\n");

        int shown = 0;
        if (insight.segments != null)
        {
            foreach (PhoneticSegmentRecord segment in insight.segments)
            {
                if (segment == null || shown >= 14)
                    continue;
                AppendSegment(output, segment.spelling, segment.heardSound, segment.status, segment.confidence);
                shown++;
            }
        }

        if (shown == 0 && insight.phonemeAlignment != null)
        {
            foreach (PhonemeAlignmentRecord alignment in insight.phonemeAlignment)
            {
                if (alignment == null || shown >= 14)
                    continue;
                AppendSegment(output, alignment.expected, alignment.observed, alignment.status, alignment.confidence);
                shown++;
            }
        }

        if (shown == 0)
            output.Append("Azure returned no individual phonemes for this attempt.\n");
        else if ((insight.segments?.Count ?? insight.phonemeAlignment?.Count ?? 0) > shown)
            output.Append("…more sounds are available in the saved assessment.\n");

        if (!string.IsNullOrWhiteSpace(insight.message))
            output.Append("\n").Append(insight.message);
        return output.ToString();
    }

    static void AppendSegment(StringBuilder output, string expected, string observed, string status, float confidence)
    {
        bool matched = string.Equals(status, "Matched", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(status, "matched", StringComparison.OrdinalIgnoreCase);
        bool missing = string.Equals(status, "Missing", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(status, "missing", StringComparison.OrdinalIgnoreCase);
        string icon = matched ? "✓" : missing ? "!" : "~";
        string color = matched ? "#71E39B" : missing ? "#FF7777" : "#FFD166";
        string expectedSound = string.IsNullOrWhiteSpace(expected) ? "—" : expected;
        string heardSound = string.IsNullOrWhiteSpace(observed) ? "—" : observed;
        output.Append("<color=").Append(color).Append('>').Append(icon).Append("</color> /")
            .Append(expectedSound).Append("/ → /").Append(heardSound).Append("/  ")
            .Append(Mathf.RoundToInt(Mathf.Clamp01(confidence) * 100f)).Append("%\n");
    }

    static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        return "";
    }

    public void ShowPending(string targetPhrase)
    {
        if (text == null) return;
        string target = string.IsNullOrWhiteSpace(targetPhrase) ? "your response" : targetPhrase;
        text.text = $"Azure Pronunciation\n\nChecking: {target}\nAssessment in progress...";
        panel.SetActive(true);
    }
}
