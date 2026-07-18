using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class CosmeticInventoryStore
{
    [Serializable]
    sealed class CosmeticInventoryFile
    {
        public List<string> unlockedSkinIds = new List<string>();
        public List<string> unlockedCompanionIds = new List<string>();
        public string equippedSkinId = CosmeticCatalog.DefaultSkinId;
        public string equippedCompanionId = CosmeticCatalog.NoCompanionId;
        public string updatedAtUtc;
    }

    const string FileName = "cosmetic_inventory.json";
    static CosmeticInventoryFile cached;

    public static event Action OnCosmeticsChanged;

    public static string EquippedSkinId
    {
        get
        {
            EnsureLoaded();
            return string.IsNullOrWhiteSpace(cached.equippedSkinId) ? CosmeticCatalog.DefaultSkinId : cached.equippedSkinId;
        }
    }

    public static string EquippedCompanionId
    {
        get
        {
            EnsureLoaded();
            return string.IsNullOrWhiteSpace(cached.equippedCompanionId) ? CosmeticCatalog.NoCompanionId : cached.equippedCompanionId;
        }
    }

    public static bool IsUnlocked(CosmeticShopItem item)
    {
        if (item == null)
            return false;

        EnsureLoaded();
        return item.kind == CosmeticItemKind.Skin
            ? cached.unlockedSkinIds.Contains(item.id)
            : cached.unlockedCompanionIds.Contains(item.id);
    }

    public static bool IsEquipped(CosmeticShopItem item)
    {
        if (item == null)
            return false;

        return item.kind == CosmeticItemKind.Skin
            ? EquippedSkinId == item.id
            : EquippedCompanionId == item.id;
    }

    public static bool TryBuyOrEquip(CosmeticShopItem item, out string message)
    {
        message = "";
        if (item == null)
        {
            message = "Item unavailable.";
            return false;
        }

        EnsureLoaded();
        if (!IsUnlocked(item))
        {
            if (!PersistentCoinWallet.TrySpendCoins(item.price))
            {
                message = "Not enough coins.";
                return false;
            }

            Unlock(item);
            message = $"Bought {item.displayName}.";
        }
        else
        {
            message = $"Equipped {item.displayName}.";
        }

        Equip(item);
        SaveAndNotify();
        return true;
    }

    public static void ApplyServerPurchase(CosmeticShopItem item)
    {
        if (item == null)
            return;
        EnsureLoaded();
        Unlock(item);
        Equip(item);
        SaveAndNotify();
    }

    public static void Reload()
    {
        cached = null;
        EnsureLoaded();
        OnCosmeticsChanged?.Invoke();
    }

    public static void Reset()
    {
        cached = CreateDefaultFile();
        SaveAndNotify();
    }

    static void Unlock(CosmeticShopItem item)
    {
        List<string> list = item.kind == CosmeticItemKind.Skin
            ? cached.unlockedSkinIds
            : cached.unlockedCompanionIds;

        if (!list.Contains(item.id))
            list.Add(item.id);
    }

    static void Equip(CosmeticShopItem item)
    {
        if (item.kind == CosmeticItemKind.Skin)
            cached.equippedSkinId = item.id;
        else
            cached.equippedCompanionId = item.id;
    }

    static void EnsureLoaded()
    {
        if (cached != null)
            return;

        string path = PlayerSaveSlots.GetSaveFilePath(FileName);
        if (!File.Exists(path))
        {
            cached = CreateDefaultFile();
            return;
        }

        try
        {
            cached = JsonUtility.FromJson<CosmeticInventoryFile>(File.ReadAllText(path)) ?? CreateDefaultFile();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CosmeticInventoryStore] Failed to load cosmetics: {ex.Message}");
            cached = CreateDefaultFile();
        }

        EnsureDefaults();
    }

    static CosmeticInventoryFile CreateDefaultFile()
    {
        var file = new CosmeticInventoryFile();
        file.unlockedSkinIds.Add(CosmeticCatalog.DefaultSkinId);
        file.unlockedCompanionIds.Add(CosmeticCatalog.NoCompanionId);
        return file;
    }

    static void EnsureDefaults()
    {
        if (cached.unlockedSkinIds == null)
            cached.unlockedSkinIds = new List<string>();
        if (cached.unlockedCompanionIds == null)
            cached.unlockedCompanionIds = new List<string>();
        if (!cached.unlockedSkinIds.Contains(CosmeticCatalog.DefaultSkinId))
            cached.unlockedSkinIds.Add(CosmeticCatalog.DefaultSkinId);
        if (!cached.unlockedCompanionIds.Contains(CosmeticCatalog.NoCompanionId))
            cached.unlockedCompanionIds.Add(CosmeticCatalog.NoCompanionId);
        if (string.IsNullOrWhiteSpace(cached.equippedSkinId) || !cached.unlockedSkinIds.Contains(cached.equippedSkinId))
            cached.equippedSkinId = CosmeticCatalog.DefaultSkinId;
        if (string.IsNullOrWhiteSpace(cached.equippedCompanionId) || !cached.unlockedCompanionIds.Contains(cached.equippedCompanionId))
            cached.equippedCompanionId = CosmeticCatalog.NoCompanionId;
    }

    static void SaveAndNotify()
    {
        EnsureDefaults();
        cached.updatedAtUtc = DateTime.UtcNow.ToString("o");
        try
        {
            PlayerSaveSlots.EnsureActiveSlotDirectory();
            File.WriteAllText(PlayerSaveSlots.GetSaveFilePath(FileName), JsonUtility.ToJson(cached, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CosmeticInventoryStore] Failed to save cosmetics: {ex.Message}");
        }

        OnCosmeticsChanged?.Invoke();
    }
}
