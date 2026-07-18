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


public class GrammarScenePortal : MonoBehaviour
{
    public string displayName = "Next Area";
    public string targetSceneName = "";
    public string targetAreaId = "";
    public bool requiresCurrentAreaCompleted;
    public bool carryGrammarRouteContext = true;
    public SemanticZoneKind sourceSceneKind = SemanticZoneKind.Town;
    public string sourceGrammarTopic = "";
    [Min(1)] public int sourceGrammarTopicTier = 1;
    public List<string> sourceCurrentNounFamilies = new List<string>();
    public List<string> sourceReviewNounFamilies = new List<string>();

    public void ConfigureRouteContext(
        SemanticZoneKind sceneKind,
        string grammarTopic,
        int grammarTopicTier,
        IEnumerable<string> currentNounFamilies,
        IEnumerable<string> reviewNounFamilies)
    {
        sourceSceneKind = sceneKind;
        sourceGrammarTopic = grammarTopic ?? "";
        sourceGrammarTopicTier = Mathf.Max(1, grammarTopicTier);
        sourceCurrentNounFamilies = currentNounFamilies != null ? new List<string>(currentNounFamilies) : new List<string>();
        sourceReviewNounFamilies = reviewNounFamilies != null ? new List<string>(reviewNounFamilies) : new List<string>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        if (requiresCurrentAreaCompleted &&
            !GrammarWorldProgressService.Instance.IsCurrentAreaObjectiveCompleted())
        {
            Debug.Log($"[GrammarScenePortal] Complete this area before travelling to {displayName}.", this);
            return;
        }

        string resolvedSceneName = !string.IsNullOrWhiteSpace(targetSceneName)
            ? targetSceneName
            : SceneManager.GetActiveScene().name;
        if (string.IsNullOrWhiteSpace(resolvedSceneName))
            return;

        if (carryGrammarRouteContext)
        {
            GrammarRouteContext.Instance.CaptureFromScene(
                sourceSceneKind,
                sourceGrammarTopic,
                sourceGrammarTopicTier,
                sourceCurrentNounFamilies,
                sourceReviewNounFamilies);
        }

        if (!string.IsNullOrWhiteSpace(targetAreaId))
            GrammarWorldProgressService.Instance.PrepareAreaTransition(targetAreaId, resolvedSceneName);

        SceneManager.LoadScene(resolvedSceneName);
    }
}
