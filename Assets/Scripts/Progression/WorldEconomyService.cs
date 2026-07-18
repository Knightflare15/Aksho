using System;
using UnityEngine;

/// <summary>Persistent Grammar RPG economy, independent of transient runs.</summary>
[DisallowMultipleComponent]
public sealed class WorldEconomyService : MonoBehaviour
{
    public static WorldEconomyService Instance { get; private set; }
    public int Coins => PersistentCoinWallet.Coins;
    public event Action<int> OnCoinsChanged;

    public static WorldEconomyService EnsureExists()
    {
        if (Instance != null)
            return Instance;
        Instance = FindAnyObjectByType<WorldEconomyService>();
        if (Instance != null)
            return Instance;
        GameObject root = new GameObject("WorldEconomyService");
        Instance = root.AddComponent<WorldEconomyService>();
        if (Application.isPlaying)
            DontDestroyOnLoad(root);
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);
    }

    void OnEnable() => PersistentCoinWallet.OnCoinsChanged += ForwardBalance;
    void OnDisable() => PersistentCoinWallet.OnCoinsChanged -= ForwardBalance;

    public void AddCoins(int amount) => PersistentCoinWallet.AddCoins(amount);
    public bool TrySpendCoins(int amount) => PersistentCoinWallet.TrySpendCoins(amount);
    public void MirrorServerBalance(int balance) => PersistentCoinWallet.SetBalanceFromServer(balance);
    void ForwardBalance(int balance) => OnCoinsChanged?.Invoke(balance);
}
