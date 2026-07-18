using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SpellDefinition
{
    public string word = "CAT";
    [Tooltip("Exact alternate transcriptions that should activate this spell.")]
    public List<string> pronunciationAliases = new List<string>();
    [Min(1)] public int unlockLevel = 1;
    [TextArea] public string instructionalFocus = "Short vowel CVC";
    [Header("Grimoire")]
    public Sprite grimoirePhoto;
    public AudioClip pronunciationClip;
    public GameObject projectilePrefab;
    public Color projectileColour = new Color(1f, 0.92f, 0.35f, 1f);

    [Header("Spell FX")]
    public GameObject castEffectPrefab;
    public GameObject impactEffectPrefab;
    public GameObject areaImpactEffectPrefab;
    [Min(0.1f)] public float castEffectLifetime = 1.1f;
    [Min(0.1f)] public float impactEffectLifetime = 2f;

    public float projectileSpeed = 16f;
    [Range(3, 8)] public int fallbackShots = 3;
    [Min(0.5f)] public float specialAreaRadius = 5f;
}
