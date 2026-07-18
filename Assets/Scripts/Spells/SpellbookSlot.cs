using UnityEngine;

public enum SpellbookPageType
{
    Empty,
    Letter,
    SpecialWord,
}

[System.Serializable]
public class SpellbookSlot
{
    public SpellbookPageType pageType;
    public string pageLetter = "";
    public string spellWord = "";
    public int currentAmmo;
    public int maxAmmo;
    public SpellDefinition spellDefinition;

    public bool IsEmpty =>
        pageType == SpellbookPageType.Empty ||
        currentAmmo <= 0 ||
        maxAmmo <= 0;

    public void FillLetter(char letter, int ammo, int ammoCap = 10)
    {
        pageType = SpellbookPageType.Letter;
        pageLetter = char.ToUpperInvariant(letter).ToString();
        spellWord = "";
        spellDefinition = null;
        currentAmmo = Mathf.Clamp(ammo, 0, Mathf.Max(1, ammoCap));
        maxAmmo = currentAmmo;
    }

    public void FillSpecial(SpellDefinition definition, int ammo, int ammoCap = 5)
    {
        pageType = SpellbookPageType.SpecialWord;
        pageLetter = "";
        spellDefinition = definition;
        spellWord = definition != null ? SpellRegistry.NormalizeWord(definition.word) : "";
        currentAmmo = Mathf.Clamp(ammo, 0, Mathf.Max(1, ammoCap));
        maxAmmo = currentAmmo;
    }

    public void Clear()
    {
        pageType = SpellbookPageType.Empty;
        pageLetter = "";
        spellWord = "";
        currentAmmo = 0;
        maxAmmo = 0;
        spellDefinition = null;
    }
}
