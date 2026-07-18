using UnityEngine;

[System.Serializable]
public class RunUpgradeOffer
{
    public RunUpgradeType type;
    public string displayName;
    public string description;
    public int price;

    public RunUpgradeOffer(RunUpgradeType type, string displayName, string description, int price)
    {
        this.type = type;
        this.displayName = displayName;
        this.description = description;
        this.price = Mathf.Max(0, price);
    }
}
