using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public partial class MainMenuController : MonoBehaviour
{
    static Button MakeRuntimeButton(string name, Transform parent, string label, Vector2 anchoredPosition)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(190f, 48f);
        go.GetComponent<LayoutElement>().preferredHeight = 48f;
        var button = go.GetComponent<Button>();
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);
        MakeRuntimeLabel(go.transform, label, 17f);
        return button;
    }

    static TextMeshProUGUI MakeRuntimeLabel(Transform parent, string value, float size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = value;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        GameUiTheme.StyleText(label, size, parent.GetComponent<Button>() != null);
        go.GetComponent<LayoutElement>().preferredHeight = size + 12f;
        return label;
    }

    static Text MakeRuntimeLegacyLabel(Transform parent, string value, float size)
    {
        var go = new GameObject("LegacyLabel", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Text label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = value;
        label.fontSize = Mathf.RoundToInt(size);
        label.alignment = TextAnchor.MiddleCenter;
        label.color = GameUiTheme.TextMuted;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        go.GetComponent<LayoutElement>().preferredHeight = size + 28f;
        return label;
    }

    static Toggle MakeRuntimeToggle(Transform parent, string label, bool value)
    {
        var go = new GameObject("VibrationToggle", typeof(RectTransform), typeof(Toggle), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 42f;
        var toggle = go.GetComponent<Toggle>();

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(go.transform, false);
        var bgRt = background.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(0f, 0.5f);
        bgRt.sizeDelta = new Vector2(30f, 30f);
        bgRt.anchoredPosition = new Vector2(18f, 0f);
        background.GetComponent<Image>().color = GameUiTheme.PanelRaised;

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(background.transform, false);
        var checkRt = check.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.2f, 0.2f);
        checkRt.anchorMax = new Vector2(0.8f, 0.8f);
        checkRt.offsetMin = checkRt.offsetMax = Vector2.zero;
        check.GetComponent<Image>().color = GameUiTheme.Accent;
        toggle.targetGraphic = background.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = value;

        var text = MakeRuntimeLabel(go.transform, label, 18f);
        text.alignment = TextAlignmentOptions.Left;
        text.rectTransform.offsetMin = new Vector2(48f, 0f);
        return toggle;
    }

    static Slider MakeRuntimeSlider(Transform parent, float value)
    {
        var go = new GameObject("ShakeSlider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 38f;
        var slider = go.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = value;

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(go.transform, false);
        var bgRt = background.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.35f);
        bgRt.anchorMax = new Vector2(1f, 0.65f);
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = GameUiTheme.PanelRaised;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(background.transform, false);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = GameUiTheme.Accent;
        slider.fillRect = fillRt;
        slider.targetGraphic = fill.GetComponent<Image>();
        return slider;
    }

    static InputField MakeRuntimeInputField(
        string name,
        Transform parent,
        string placeholder,
        InputField.ContentType contentType = InputField.ContentType.Standard)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 48f;
        Image image = go.GetComponent<Image>();
        image.color = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        image.raycastTarget = true;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 48f);

        Text text = MakeLegacyInputText(go.transform, "Text", GameUiTheme.Text);
        Text placeholderText = MakeLegacyInputText(go.transform, "Placeholder", GameUiTheme.TextMuted);
        placeholderText.text = placeholder;
        placeholderText.fontStyle = FontStyle.Italic;

        InputField input = go.GetComponent<InputField>();
        input.targetGraphic = image;
        input.interactable = true;
        input.transition = Selectable.Transition.ColorTint;
        input.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        input.textComponent = text;
        input.placeholder = placeholderText;
        input.characterLimit = 160;
        input.lineType = InputField.LineType.SingleLine;
        input.contentType = contentType;
        input.caretColor = GameUiTheme.Accent;
        input.selectionColor = new Color(GameUiTheme.Accent.r, GameUiTheme.Accent.g, GameUiTheme.Accent.b, 0.35f);
        return input;
    }

    static Text MakeLegacyInputText(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(14f, 6f);
        rt.offsetMax = new Vector2(-14f, -6f);

        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 20;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }
}
