using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class TacticalGrammarBattleController : MonoBehaviour
{
    void ApplyTacticalCurse(GrammarBattleCurse curse)
    {
        activeTacticalCurse = curse;
        activeCurseExpiresAt = curse == GrammarBattleCurse.None
            ? -999f
            : IsPronounCurse(curse)
                ? Time.time + Mathf.Max(5f, pronounCurseDurationSeconds)
                : float.PositiveInfinity;
    }

    void ClearTacticalCurse()
    {
        activeTacticalCurse = GrammarBattleCurse.None;
        activeCurseExpiresAt = -999f;
    }

    void UpdateActiveTacticalCurse()
    {
        if (!IsPronounCurse(activeTacticalCurse) || Time.time < activeCurseExpiresAt)
            return;

        string faded = FormatCurse(activeTacticalCurse);
        ClearTacticalCurse();
        Publish($"{faded} faded after 30 seconds.");
        RefreshBoardVisuals();
    }

    static bool IsPronounCurse(GrammarBattleCurse curse)
    {
        return curse == GrammarBattleCurse.I ||
               curse == GrammarBattleCurse.You ||
               curse == GrammarBattleCurse.HeSheIt ||
               curse == GrammarBattleCurse.They;
    }

    static GrammarBattleCurse ResolveEnemyCurse(int curseIndex)
    {
        GrammarBattleCurse[] cycle =
        {
            GrammarBattleCurse.HeSheIt,
            GrammarBattleCurse.PastFog,
            GrammarBattleCurse.NowMist,
            GrammarBattleCurse.They,
            GrammarBattleCurse.I,
            GrammarBattleCurse.You,
        };
        return cycle[Mathf.Abs(curseIndex - 1) % cycle.Length];
    }

    bool TryApplyTacticalCurseGate(
        string phrase,
        GrammarBattleCurse curse,
        out string transformedPhrase,
        out string error)
    {
        transformedPhrase = phrase ?? "";
        error = "";
        if (curse == GrammarBattleCurse.None)
            return true;

        List<string> tokens = CreaturePhraseUtility.Tokenize(phrase);
        if (!TryFindVerbForm(tokens, out VerbActionDefinition verb, out int verbIndex, out string matchedVerb))
        {
            error = "The curse needs a usable action verb.";
            return false;
        }

        string subject = tokens.Count > 0 ? tokens[0] : "";
        switch (curse)
        {
            case GrammarBattleCurse.I:
                if (subject != "I")
                {
                    error = "The curse forces I. Say something like: I run forward.";
                    return false;
                }
                break;
            case GrammarBattleCurse.You:
                if (subject != "YOU")
                {
                    error = "The curse forces you. Say something like: you run forward.";
                    return false;
                }
                break;
            case GrammarBattleCurse.HeSheIt:
                if ((subject != "HE" && subject != "SHE" && subject != "IT") || !MatchesVerbForm(verb.GetThirdPersonSingularForms(), matchedVerb))
                {
                    error = "The curse forces he/she/it with the third-person verb form, like: he runs forward.";
                    return false;
                }
                break;
            case GrammarBattleCurse.They:
                if (subject != "THEY")
                {
                    error = "The curse forces they. Say something like: they run forward.";
                    return false;
                }
                break;
            case GrammarBattleCurse.PastFog:
                if (!MatchesVerbForm(verb.GetPastTenseForms(), matchedVerb))
                {
                    error = "Past Fog is active. Use past tense, like: rat ran forward.";
                    return false;
                }
                break;
            case GrammarBattleCurse.NowMist:
                bool hasAuxiliary = tokens.Count > 1 && (tokens[1] == "AM" || tokens[1] == "IS" || tokens[1] == "ARE");
                if (!hasAuxiliary || !MatchesVerbForm(verb.GetProgressiveForms(), matchedVerb))
                {
                    error = "Now Mist is active. Use am/is/are + -ing, like: rat is running forward.";
                    return false;
                }
                break;
        }

        var transformed = new List<string>
        {
            State.playerUnit.noun,
            CreaturePhraseUtility.NormalizeToken(verb.verb),
        };
        for (int index = verbIndex + 1; index < tokens.Count; index++)
            transformed.Add(tokens[index]);
        transformedPhrase = string.Join(" ", transformed);
        return true;
    }

    bool TryFindVerbForm(
        List<string> tokens,
        out VerbActionDefinition verbDefinition,
        out int verbIndex,
        out string matchedVerb)
    {
        verbDefinition = null;
        verbIndex = -1;
        matchedVerb = "";
        if (registry == null || tokens == null)
            return false;

        for (int index = 0; index < tokens.Count; index++)
        {
            string token = CreaturePhraseUtility.NormalizeToken(tokens[index]);
            foreach (VerbActionDefinition candidate in registry.Verbs)
            {
                if (candidate == null || (!candidate.Matches(token) && !MatchesVerbForm(candidate.EnumerateAllCommandForms(), token)))
                    continue;
                verbDefinition = candidate;
                verbIndex = index;
                matchedVerb = token;
                return true;
            }
        }
        return false;
    }

    static bool MatchesVerbForm(IEnumerable<string> forms, string token)
    {
        if (forms == null)
            return false;
        string normalized = CreaturePhraseUtility.NormalizeToken(token);
        foreach (string form in forms)
        {
            if (CreaturePhraseUtility.NormalizeToken(form) == normalized)
                return true;
        }
        return false;
    }

    static string FormatCurse(GrammarBattleCurse curse)
    {
        return curse switch
        {
            GrammarBattleCurse.I => "I Curse",
            GrammarBattleCurse.You => "You Curse",
            GrammarBattleCurse.HeSheIt => "He/She/It Curse",
            GrammarBattleCurse.They => "They Curse",
            GrammarBattleCurse.PastFog => "Past Fog",
            GrammarBattleCurse.NowMist => "Now Mist",
            _ => "Grammar curse",
        };
    }

    string BuildEnemyRetreatMessage(TacticalBattleUnit enemy)
    {
        if (enemy == null || enemy.currentHp <= 0)
            return "";
        return battler.TryRetreatEnemyFromPlayer(out int cellsMoved)
            ? $" Then it stepped back {cellsMoved} cell(s) to recover."
            : " It could not find a safe recovery cell.";
    }

    void StartPendingEnemyAttack(TacticalBattleUnit enemy)
    {
        float attackSpeed = Mathf.Max(1f, enemy.stats.speed);
        pendingEnemyAttack = new PendingEnemyAttack
        {
            active = true,
            damage = Mathf.Max(1, enemy.stats.attack),
            attackSpeed = attackSpeed,
            hitsAt = Time.time + ResolveAttackWindowSeconds(attackSpeed),
        };
    }

    void ResolvePendingEnemyAttackIfReady()
    {
        if (!pendingEnemyAttack.active || Time.time < pendingEnemyAttack.hitsAt)
            return;

        string message = ResolvePendingEnemyAttack(requireArc: true);
        if (!string.IsNullOrWhiteSpace(message))
            Publish(message);
        RefreshBoardVisuals();
    }

    string ResolvePendingEnemyAttack(bool requireArc)
    {
        if (State?.playerUnit == null || State.enemyUnit == null || State.enemyUnit.currentHp <= 0)
        {
            pendingEnemyAttack = default;
            return "";
        }

        int damage = pendingEnemyAttack.damage;
        pendingEnemyAttack = default;
        ScheduleNextEnemyDecision();

        if (requireArc && !battler.CanEnemyAttackPlayer())
            return "Enemy attack missed because the target left the attack arc.";

        if (TryMitigateIncomingDamage(damage, out int finalDamage, out string mitigation))
        {
            State.playerUnit.currentHp = Mathf.Max(0, State.playerUnit.currentHp - finalDamage);
            return finalDamage <= 0
                ? $"Enemy attack hit the shield and dealt no HP damage. {BuildEnemyRetreatMessage(State.enemyUnit)}"
                : $"Enemy attack dealt {finalDamage} after {mitigation}. {BuildEnemyRetreatMessage(State.enemyUnit)}";
        }

        State.playerUnit.currentHp = Mathf.Max(0, State.playerUnit.currentHp - damage);
        return $"Enemy attack landed for {damage}. {BuildEnemyRetreatMessage(State.enemyUnit)}";
    }

    float ResolveAttackWindowSeconds(float attackSpeed)
    {
        // Speech commands take longer than a button press. Fast enemies still feel
        // urgent, but every learner gets enough time to speak/type a grammatical dodge.
        float advancedWindow = Mathf.Clamp(6.5f - 0.35f * Mathf.Max(1f, attackSpeed), 3.25f, 5.5f);
        float learnerMultiplier = Mathf.Lerp(1.6f, 1f, ResolveEnemyPace01());
        return Mathf.Clamp(advancedWindow * learnerMultiplier, 3.25f, 8.8f);
    }

    TacticalBattleScenePayload BuildScenePayload(WaveDescriptor descriptor)
    {
        var payload = new TacticalBattleScenePayload
        {
            sourceSceneName = gameObject.scene.name,
            battleSceneName = battleSceneName,
            enemyNoun = ResolveEnemyNoun(descriptor),
            enemyDisplayName = descriptor?.enemyDefinition != null ? descriptor.enemyDefinition.displayName : ResolveEnemyNoun(descriptor),
            grammarTopic = descriptor?.grammarTopic ?? "",
            grammarTopicTier = descriptor != null ? descriptor.grammarTopicTier : 1,
            width = State != null ? State.width : Mathf.Max(3, gridWidth),
            height = State != null ? State.height : Mathf.Max(3, gridHeight),
            showDebugGrid = showUnderlyingHexGrid,
            playerStart = ClampPosition(playerSummonPosition),
            enemyStart = ClampPosition(enemyPosition),
        };

        if (descriptor?.practicePatterns != null)
            payload.practicePatterns.AddRange(descriptor.practicePatterns);
        if (descriptor?.masteryTags != null)
            payload.masteryTags.AddRange(descriptor.masteryTags);
        if (descriptor?.enemyDefinition != null)
        {
            payload.enemyMoves.Add("ATTACK");
            payload.enemyMoves.Add("MOVE");
            payload.enemyStatuses.Add("hazard_pressure");
            if (!string.IsNullOrWhiteSpace(descriptor.enemyDefinition.learningFocus))
                payload.enemyStatuses.Add(descriptor.enemyDefinition.learningFocus);
        }

        if (State != null)
        {
            for (int x = 0; x < State.width; x++)
            {
                for (int y = 0; y < State.height; y++)
                {
                    TacticalBattleCellType cell = State.terrain[x, y];
                    if (cell != TacticalBattleCellType.Empty)
                    {
                        payload.terrain.Add(new TacticalBattleTerrainPayload
                        {
                            x = x,
                            y = y,
                            cellType = cell,
                        });
                    }
                }
            }
        }

        return payload;
    }

    void TryLoadBattleScene()
    {
        if (!useDedicatedBattleScene || string.IsNullOrWhiteSpace(battleSceneName) || battleSceneLoading)
            return;
        if (!Application.CanStreamedLevelBeLoaded(battleSceneName))
        {
            Debug.LogWarning($"[TacticalGrammarBattle] Battle scene '{battleSceneName}' is not in build settings yet. Using in-scene cube board fallback.", this);
            return;
        }

        Scene scene = SceneManager.GetSceneByName(battleSceneName);
        if (scene.isLoaded)
        {
            loadedBattleSceneName = battleSceneName;
            SceneManager.SetActiveScene(scene);
            EnsureBattleSceneBootstrap(scene);
            return;
        }

        battleSceneLoading = true;
        AsyncOperation load = SceneManager.LoadSceneAsync(battleSceneName, LoadSceneMode.Additive);
        if (load == null)
        {
            battleSceneLoading = false;
            return;
        }

        load.completed += _ =>
        {
            battleSceneLoading = false;
            loadedBattleSceneName = battleSceneName;
            Scene loaded = SceneManager.GetSceneByName(battleSceneName);
            if (loaded.isLoaded)
            {
                SceneManager.SetActiveScene(loaded);
                EnsureBattleSceneBootstrap(loaded);
            }
            RefreshBoardVisuals();
        };
    }

    static void EnsureBattleSceneBootstrap(Scene scene)
    {
        if (!scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            TacticalBattleSceneBootstrap existing = root.GetComponentInChildren<TacticalBattleSceneBootstrap>(true);
            if (existing != null)
                return;
        }

        var bootstrapObject = new GameObject("TacticalBattleSceneBootstrap");
        SceneManager.MoveGameObjectToScene(bootstrapObject, scene);
        bootstrapObject.AddComponent<TacticalBattleSceneBootstrap>();
    }

    void TryUnloadBattleScene()
    {
        if (string.IsNullOrWhiteSpace(loadedBattleSceneName))
            return;

        Scene scene = SceneManager.GetSceneByName(loadedBattleSceneName);
        loadedBattleSceneName = "";
        if (scene.isLoaded)
            SceneManager.UnloadSceneAsync(scene);
    }
}
