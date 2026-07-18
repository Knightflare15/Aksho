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


public sealed class NpcWordTile : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    NpcDialogueUI owner;
    public string Word { get; private set; } = "";
    public DialogueJumbleWordState FeedbackState { get; private set; } = DialogueJumbleWordState.Unused;

    public void Configure(NpcDialogueUI tileOwner, string word)
    {
        owner = tileOwner;
        Word = word ?? "";

        GameObject labelObject = new GameObject("Word", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(transform, false);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 4f);
        rect.offsetMax = new Vector2(-8f, -4f);
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = Word;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        GameUiTheme.StyleText(label, 20f, true);
    }

    public void SetFeedback(DialogueJumbleWordState state)
    {
        FeedbackState = state;
        Image image = GetComponent<Image>();
        if (image == null)
            return;

        image.color = state switch
        {
            DialogueJumbleWordState.CorrectPosition => new Color(0.16f, 0.62f, 0.30f, 1f),
            DialogueJumbleWordState.Distractor => new Color(0.34f, 0.35f, 0.38f, 1f),
            DialogueJumbleWordState.PresentElsewhere => new Color(0.22f, 0.40f, 0.58f, 1f),
            _ => new Color(0.22f, 0.40f, 0.58f, 1f),
        };
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        owner?.StartTileDrag(this);
    }

    public void OnDrag(PointerEventData eventData)
    {
        owner?.DragTile(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        owner?.FinishTileDrag(this, eventData);
    }
}
