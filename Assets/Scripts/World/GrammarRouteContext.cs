using System.Collections.Generic;
using UnityEngine;

public sealed class GrammarRouteContext : MonoBehaviour
{
    static GrammarRouteContext instance;

    public SemanticZoneKind sourceSceneKind = SemanticZoneKind.Town;
    public string sourceGrammarTopic = "";
    [Min(1)] public int sourceGrammarTopicTier = 1;
    public List<string> currentNounFamilies = new List<string>();
    public List<string> reviewNounFamilies = new List<string>();

    public static GrammarRouteContext Instance
    {
        get
        {
            if (instance != null)
                return instance;

            GrammarRouteContext existing = FindAnyObjectByType<GrammarRouteContext>();
            if (existing != null)
            {
                instance = existing;
                return instance;
            }

            GameObject go = new GameObject("GrammarRouteContext");
            instance = go.AddComponent<GrammarRouteContext>();
            PreserveAcrossScenes(go);
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        PreserveAcrossScenes(gameObject);
    }

    static void PreserveAcrossScenes(GameObject target)
    {
        if (Application.isPlaying && target != null)
            DontDestroyOnLoad(target);
    }

    public void CaptureFromScene(
        SemanticZoneKind sceneKind,
        string grammarTopic,
        int grammarTopicTier,
        IEnumerable<string> currentNouns,
        IEnumerable<string> reviewNouns)
    {
        sourceSceneKind = sceneKind;
        sourceGrammarTopic = grammarTopic ?? "";
        sourceGrammarTopicTier = Mathf.Max(1, grammarTopicTier);
        currentNounFamilies = NormalizeList(currentNouns);
        reviewNounFamilies = NormalizeList(reviewNouns);
    }

    public List<string> ResolveEncounterNounFamilies(
        CreatureCombatRegistry registry,
        SemanticZoneKind fallbackSceneKind,
        string fallbackGrammarTopic,
        int fallbackGrammarTopicTier,
        IEnumerable<string> explicitFamilies)
    {
        List<string> explicitList = NormalizeList(explicitFamilies);
        if (explicitList.Count > 0)
            return explicitList;

        int tier = Mathf.Max(1, fallbackGrammarTopicTier);
        List<string> current = new List<string>();
        List<string> review = new List<string>();

        bool shouldUseCapturedRouteContext =
            fallbackSceneKind == SemanticZoneKind.Route &&
            (!string.IsNullOrWhiteSpace(sourceGrammarTopic) || currentNounFamilies.Count > 0 || reviewNounFamilies.Count > 0);
        if (shouldUseCapturedRouteContext)
        {
            tier = Mathf.Max(1, sourceGrammarTopicTier);
            current.AddRange(currentNounFamilies);
            review.AddRange(reviewNounFamilies);
        }

        if (current.Count == 0)
            AddNounsByUnlockLevel(current, registry, tier, exactLevelOnly: true);
        if (review.Count == 0)
            AddNounsByUnlockLevel(review, registry, tier, exactLevelOnly: false);

        var result = new List<string>();
        AddUniqueRange(result, current);
        AddUniqueRange(result, review);
        KeepCreatureNounsOnly(result, registry);
        if (result.Count == 0)
            AddNounsByUnlockLevel(result, registry, tier, exactLevelOnly: false, includeCurrentLevel: true);
        if (result.Count == 0 && registry != null)
        {
            foreach (NounDefinition noun in registry.Nouns)
            {
                if (noun != null && noun.IsCreatureNoun)
                {
                    AddUnique(result, noun.canonicalNoun);
                    break;
                }
            }
        }

        return result;
    }

    static void AddNounsByUnlockLevel(
        List<string> target,
        CreatureCombatRegistry registry,
        int tier,
        bool exactLevelOnly,
        bool includeCurrentLevel = false)
    {
        if (target == null || registry == null)
            return;

        int maxLevel = Mathf.Max(1, tier);
        foreach (NounDefinition noun in registry.Nouns)
        {
            if (noun == null)
                continue;
            if (!noun.IsCreatureNoun)
                continue;

            int unlock = Mathf.Max(1, noun.unlockLevel);
            bool include = exactLevelOnly
                ? unlock == maxLevel
                : includeCurrentLevel ? unlock <= maxLevel : unlock < maxLevel;
            if (include)
                AddUnique(target, noun.canonicalNoun);
        }
    }

    static void KeepCreatureNounsOnly(List<string> target, CreatureCombatRegistry registry)
    {
        if (target == null || registry == null)
            return;

        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!registry.TryGetNoun(target[i], out NounDefinition noun) || noun == null || !noun.IsCreatureNoun)
                target.RemoveAt(i);
        }
    }

    static List<string> NormalizeList(IEnumerable<string> values)
    {
        var result = new List<string>();
        AddUniqueRange(result, values);
        return result;
    }

    static void AddUniqueRange(List<string> target, IEnumerable<string> values)
    {
        if (target == null || values == null)
            return;

        foreach (string value in values)
            AddUnique(target, value);
    }

    static void AddUnique(List<string> target, string value)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(value);
        if (!string.IsNullOrEmpty(normalized) && !target.Contains(normalized))
            target.Add(normalized);
    }
}
