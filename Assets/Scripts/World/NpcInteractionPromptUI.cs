using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;


public sealed class NpcInteractionPromptUI : MonoBehaviour
{
    static NpcInteractionPromptUI instance;

    Canvas canvas;
    RectTransform panel;
    TextMeshProUGUI label;
    Button button;
    GrammarNpc target;
    int shownFrame = -1;

    public static NpcInteractionPromptUI EnsureExists()
    {
        if (instance != null)
            return instance;

        NpcInteractionPromptUI existing = FindAnyObjectByType<NpcInteractionPromptUI>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        GameObject go = new GameObject("NpcInteractionPromptUI");
        instance = go.AddComponent<NpcInteractionPromptUI>();
        DontDestroyOnLoad(go);
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        Build();
        Hide();
    }

    void LateUpdate()
    {
        if (shownFrame != Time.frameCount)
            Hide();
    }

    public void Show(GrammarNpc npc)
    {
        if (npc == null)
            return;

        EnsureBuilt();
        target = npc;
        shownFrame = Time.frameCount;
        if (panel != null)
            panel.gameObject.SetActive(true);
        if (label != null)
            label.text = $"Talk to {npc.displayName}";
    }

    public void Hide()
    {
        target = null;
        if (panel != null)
            panel.gameObject.SetActive(false);
    }

    void HandlePressed()
    {
        target?.Talk();
    }

    void EnsureBuilt()
    {
        if (canvas == null || panel == null)
            Build();
    }

    void Build()
    {
        if (canvas != null)
            return;

        GameObject root = new GameObject("NpcInteractionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 421;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelGo = new GameObject("Prompt", typeof(RectTransform), typeof(Image), typeof(Button));
        panelGo.transform.SetParent(root.transform, false);
        panel = panelGo.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0f);
        panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 276f);
        panel.sizeDelta = new Vector2(360f, 72f);

        button = panelGo.GetComponent<Button>();
        button.onClick.AddListener(HandlePressed);
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(panelGo.transform, false);
        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        GameUiTheme.StyleText(label, 21f, true);
    }
}
