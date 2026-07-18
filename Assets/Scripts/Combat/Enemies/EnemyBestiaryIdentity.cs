using UnityEngine;

public sealed class EnemyBestiaryIdentity : MonoBehaviour
{
    public EnemyDefinition definition;

    public string EnemyId => definition != null ? definition.enemyId : gameObject.name;
    public string DisplayName => definition != null ? definition.displayName : gameObject.name;
    public string WeaknessWord => definition != null ? SpellRegistry.NormalizeWord(definition.weaknessSpell) : "";
    public string StartingLetter => string.IsNullOrEmpty(WeaknessWord) ? "" : WeaknessWord[0].ToString();
    public Sprite Photo => definition != null ? definition.bestiaryPhoto : null;
    public AudioClip WeaknessPronunciationClip => definition != null ? definition.weaknessPronunciationClip : null;
}
