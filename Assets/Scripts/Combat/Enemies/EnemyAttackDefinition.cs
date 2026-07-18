using System;
using UnityEngine;

[Serializable]
public class EnemyAttackDefinition
{
    public string attackId = "Punch";
    public string animationTrigger = "Attack";
    public string hitboxId = "Melee";
    [Min(0.1f)] public float range = 1.75f;
    [Min(0.05f)] public float cooldown = 1.35f;
    [Min(0f)] public float windupSeconds = 0.28f;
    [Min(0.02f)] public float activeSeconds = 0.22f;
    [Min(0f)] public float recoverySeconds = 0.45f;
    [Min(0.01f)] public float weight = 1f;
    [Min(0)] public int damage = 1;
    public BattleActionRole battleRole = BattleActionRole.Offense;
    public string grammarNounFamily = "";
    public string grammarVerb = "";
    public string grammarCommand = "";
    public GrammarPhrasePattern grammarPattern = GrammarPhrasePattern.VerbOnly;
    public GrammarBattleCurse inflictedGrammarCurse = GrammarBattleCurse.None;
    [Min(0f)] public float dodgeSeconds = 0f;
}
