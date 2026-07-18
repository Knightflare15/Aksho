using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PlayerCosmeticApplicator : MonoBehaviour
{
    Transform companionRoot;
    string appliedSkinId;
    string appliedCompanionId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (_, __) => AttachToPlayer();
        AttachToPlayer();
    }

    static void AttachToPlayer()
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player == null || player.GetComponent<PlayerCosmeticApplicator>() != null)
            return;

        player.gameObject.AddComponent<PlayerCosmeticApplicator>();
    }

    void OnEnable()
    {
        CosmeticInventoryStore.OnCosmeticsChanged += ApplyCosmetics;
        ApplyCosmetics();
    }

    void OnDisable()
    {
        CosmeticInventoryStore.OnCosmeticsChanged -= ApplyCosmetics;
    }

    void ApplyCosmetics()
    {
        ApplySkin(CosmeticCatalog.EquippedSkin);
        ApplyCompanion(CosmeticCatalog.EquippedCompanion);
    }

    void ApplySkin(CosmeticShopItem skin)
    {
        if (skin == null || appliedSkinId == skin.id)
            return;

        appliedSkinId = skin.id;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                renderer is TrailRenderer ||
                renderer is LineRenderer ||
                renderer is ParticleSystemRenderer ||
                companionRoot != null && renderer.transform.IsChildOf(companionRoot))
            {
                continue;
            }

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", skin.color);
            properties.SetColor("_Color", skin.color);
            renderer.SetPropertyBlock(properties);
        }
    }

    void ApplyCompanion(CosmeticShopItem companion)
    {
        string companionId = companion != null ? companion.id : CosmeticCatalog.NoCompanionId;
        if (appliedCompanionId == companionId)
            return;

        appliedCompanionId = companionId;
        if (companionRoot != null)
            Destroy(companionRoot.gameObject);
        companionRoot = null;

        if (companion == null || companion.id == CosmeticCatalog.NoCompanionId)
            return;

        GameObject root = new GameObject("EquippedCompanion");
        companionRoot = root.transform;
        CompanionFollower follower = root.AddComponent<CompanionFollower>();
        follower.target = transform;
        follower.followOffset = new Vector3(1.1f, 1.25f, -0.75f);

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = companion.displayName;
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * 0.36f;
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            renderer.material = new Material(shader) { color = companion.color };
        }
    }
}
