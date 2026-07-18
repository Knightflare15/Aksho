using System;
using System.IO;
using UnityEngine;

public static class PlayerSaveSlots
{
    public const int SlotCount = 1;

    const string ActiveSlotKey = "save_slots.active_slot";
    const string ActiveProfileKey = "save_slots.active_profile";
    const string MigrationCompleteKey = "save_slots.legacy_migration_complete";

    static readonly string[] SaveFileNames =
    {
        "player_learning_profile.json",
        "spell_lesson_progress.json",
        "templates.json",
        "coin_wallet.json",
        "cosmetic_inventory.json",
        "grammar_world_progress.json",
    };

    public static int ActiveSlot
    {
        get
        {
            EnsureInitialized();
            return Mathf.Clamp(PlayerPrefs.GetInt(ActiveSlotKey, 1), 1, SlotCount);
        }
    }

    public static string ActivePlayerName => GetPlayerName(ActiveSlot);
    public static string ActiveProfileId
    {
        get
        {
            EnsureInitialized();
            return SanitizeProfileId(PlayerPrefs.GetString(ActiveProfileKey, "demo-student"));
        }
    }

    public static void SelectSlot(int slot)
    {
        EnsureInitialized();
        PlayerPrefs.SetInt(ActiveSlotKey, Mathf.Clamp(slot, 1, SlotCount));
        PlayerPrefs.Save();
    }

    public static void SelectProfile(string profileId)
    {
        EnsureInitialized();
        string normalized = SanitizeProfileId(profileId);
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "demo-student";
        PlayerPrefs.SetString(ActiveProfileKey, normalized);
        PlayerPrefs.SetInt(ActiveSlotKey, 1);
        PlayerPrefs.Save();
        Directory.CreateDirectory(GetSlotDirectory(1));
    }

    public static string GetPlayerName(int slot)
    {
        string profile = ActiveProfileId;
        return string.IsNullOrWhiteSpace(profile) ? "Student" : profile;
    }

    public static string GetSaveFilePath(string fileName)
    {
        return GetSaveFilePath(fileName, ActiveSlot);
    }

    public static string GetSaveFilePath(string fileName, int slot)
    {
        EnsureInitialized();
        return Path.Combine(GetSlotDirectory(slot), fileName);
    }

    public static void EnsureActiveSlotDirectory()
    {
        Directory.CreateDirectory(GetSlotDirectory(ActiveSlot));
    }

    public static void DeleteActiveSlot()
    {
        string directory = GetSlotDirectory(ActiveSlot);
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
            PersistentCoinWallet.Reset();
            CosmeticInventoryStore.Reset();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerSaveSlots] Could not delete {ActivePlayerName}: {ex.Message}");
        }
    }

    static void EnsureInitialized()
    {
        if (PlayerPrefs.GetInt(MigrationCompleteKey, 0) != 0)
            return;

        string firstSlotDirectory = GetSlotDirectory(1);
        Directory.CreateDirectory(firstSlotDirectory);

        foreach (string fileName in SaveFileNames)
        {
            string legacyPath = Path.Combine(Application.persistentDataPath, fileName);
            string slotPath = Path.Combine(firstSlotDirectory, fileName);
            if (File.Exists(legacyPath) && !File.Exists(slotPath))
                File.Copy(legacyPath, slotPath);
        }

        PlayerPrefs.SetInt(MigrationCompleteKey, 1);
        PlayerPrefs.Save();
    }

    static string GetSlotDirectory(int slot)
    {
        return Path.Combine(
            Application.persistentDataPath,
            "player_saves",
            GetActiveProfileIdWithoutInitializing(),
            $"slot_{Mathf.Clamp(slot, 1, SlotCount)}");
    }

    static string GetActiveProfileIdWithoutInitializing()
    {
        return SanitizeProfileId(PlayerPrefs.GetString(ActiveProfileKey, "demo-student"));
    }

    static string SanitizeProfileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "demo-student";

        char[] chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
                chars[i] = '_';
        }

        return new string(chars);
    }
}
