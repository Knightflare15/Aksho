using System;
using System.IO;
using UnityEngine;

public static class PersistentCoinWallet
{
    [Serializable]
    sealed class WalletFile
    {
        public int coins;
        public int lifetimeCoinsEarned;
        public int lifetimeCoinsSpent;
        public string updatedAtUtc;
    }

    const string FileName = "coin_wallet.json";
    static WalletFile cached;

    public static event Action<int> OnCoinsChanged;

    public static int Coins
    {
        get
        {
            EnsureLoaded();
            return Mathf.Max(0, cached.coins);
        }
    }

    public static int LifetimeCoinsEarned
    {
        get
        {
            EnsureLoaded();
            return Mathf.Max(0, cached.lifetimeCoinsEarned);
        }
    }

    public static int LifetimeCoinsSpent
    {
        get
        {
            EnsureLoaded();
            return Mathf.Max(0, cached.lifetimeCoinsSpent);
        }
    }

    public static void AddCoins(int amount)
    {
        if (amount <= 0)
            return;

        EnsureLoaded();
        cached.coins = Mathf.Max(0, cached.coins + amount);
        cached.lifetimeCoinsEarned = Mathf.Max(0, cached.lifetimeCoinsEarned + amount);
        SaveAndNotify();
    }

    public static bool TrySpendCoins(int amount)
    {
        amount = Mathf.Max(0, amount);
        EnsureLoaded();
        if (cached.coins < amount)
            return false;

        cached.coins -= amount;
        cached.lifetimeCoinsSpent = Mathf.Max(0, cached.lifetimeCoinsSpent + amount);
        SaveAndNotify();
        return true;
    }

    public static void SetBalanceFromServer(int balance)
    {
        EnsureLoaded();
        cached.coins = Mathf.Max(0, balance);
        SaveAndNotify();
    }

    public static void Reload()
    {
        cached = null;
        EnsureLoaded();
        OnCoinsChanged?.Invoke(Coins);
    }

    public static void Reset()
    {
        cached = new WalletFile();
        SaveAndNotify();
    }

    static void EnsureLoaded()
    {
        if (cached != null)
            return;

        string path = PlayerSaveSlots.GetSaveFilePath(FileName);
        if (!File.Exists(path))
        {
            cached = new WalletFile();
            return;
        }

        try
        {
            cached = JsonUtility.FromJson<WalletFile>(File.ReadAllText(path)) ?? new WalletFile();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PersistentCoinWallet] Failed to load wallet: {ex.Message}");
            cached = new WalletFile();
        }
    }

    static void SaveAndNotify()
    {
        cached.updatedAtUtc = DateTime.UtcNow.ToString("o");
        try
        {
            PlayerSaveSlots.EnsureActiveSlotDirectory();
            File.WriteAllText(PlayerSaveSlots.GetSaveFilePath(FileName), JsonUtility.ToJson(cached, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PersistentCoinWallet] Failed to save wallet: {ex.Message}");
        }

        OnCoinsChanged?.Invoke(Coins);
    }
}
