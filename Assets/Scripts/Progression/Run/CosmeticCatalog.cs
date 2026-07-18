using System;
using System.Collections.Generic;
using UnityEngine;

public enum CosmeticItemKind
{
    Skin,
    Companion,
}

[Serializable]
public sealed class CosmeticShopItem
{
    public string id;
    public CosmeticItemKind kind;
    public string displayName;
    public string description;
    public int price;
    public Color color;

    public CosmeticShopItem(
        string id,
        CosmeticItemKind kind,
        string displayName,
        string description,
        int price,
        Color color)
    {
        this.id = id;
        this.kind = kind;
        this.displayName = displayName;
        this.description = description;
        this.price = Mathf.Max(0, price);
        this.color = color;
    }
}

public static class CosmeticCatalog
{
    public const string DefaultSkinId = "skin_default";
    public const string NoCompanionId = "companion_none";

    static readonly CosmeticShopItem[] SkinItems =
    {
        new CosmeticShopItem(DefaultSkinId, CosmeticItemKind.Skin, "Classic", "The starting look.", 0, Color.white),
        new CosmeticShopItem("skin_azure", CosmeticItemKind.Skin, "Azure Robe", "A bright blue adventuring robe.", 25, new Color(0.25f, 0.58f, 1f, 1f)),
        new CosmeticShopItem("skin_rose", CosmeticItemKind.Skin, "Rose Robe", "A warm pink robe for world practice.", 30, new Color(1f, 0.42f, 0.62f, 1f)),
        new CosmeticShopItem("skin_forest", CosmeticItemKind.Skin, "Forest Robe", "A soft green explorer look.", 35, new Color(0.34f, 0.78f, 0.42f, 1f)),
        new CosmeticShopItem("skin_starlight", CosmeticItemKind.Skin, "Starlight Robe", "A pale gold magical robe.", 45, new Color(1f, 0.86f, 0.35f, 1f)),
    };

    static readonly CosmeticShopItem[] CompanionItems =
    {
        new CosmeticShopItem(NoCompanionId, CosmeticItemKind.Companion, "No Companion", "Travel solo.", 0, Color.clear),
        new CosmeticShopItem("companion_spark", CosmeticItemKind.Companion, "Spark Wisp", "A tiny glowing study buddy.", 40, new Color(1f, 0.78f, 0.24f, 1f)),
        new CosmeticShopItem("companion_moon", CosmeticItemKind.Companion, "Moon Mote", "A calm silver light that follows along.", 55, new Color(0.66f, 0.78f, 1f, 1f)),
        new CosmeticShopItem("companion_pebble", CosmeticItemKind.Companion, "Pebble Pal", "A sturdy little floating charm.", 65, new Color(0.58f, 0.72f, 0.62f, 1f)),
    };

    public static IReadOnlyList<CosmeticShopItem> Skins => SkinItems;
    public static IReadOnlyList<CosmeticShopItem> Companions => CompanionItems;

    public static List<CosmeticShopItem> GetShopItems()
    {
        var items = new List<CosmeticShopItem>();
        items.AddRange(SkinItems);
        items.AddRange(CompanionItems);
        return items;
    }

    public static CosmeticShopItem Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        foreach (CosmeticShopItem item in SkinItems)
            if (item.id == id)
                return item;

        foreach (CosmeticShopItem item in CompanionItems)
            if (item.id == id)
                return item;

        return null;
    }

    public static CosmeticShopItem EquippedSkin => Find(CosmeticInventoryStore.EquippedSkinId) ?? SkinItems[0];
    public static CosmeticShopItem EquippedCompanion => Find(CosmeticInventoryStore.EquippedCompanionId) ?? CompanionItems[0];
}
