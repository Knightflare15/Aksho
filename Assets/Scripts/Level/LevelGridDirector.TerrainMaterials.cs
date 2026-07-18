using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif


public sealed partial class LevelGridDirector
{
    Material[] GetOptimizedTerrainMaterials()
    {
        return new[]
        {
            optimizedGrassTopMaterial != null
                ? optimizedGrassTopMaterial
                : GetCellMaterial(1, SurfaceBrush.Grass),
            optimizedDirtTopMaterial != null
                ? optimizedDirtTopMaterial
                : GetCellMaterial(DirtHeightThreshold + 1, SurfaceBrush.Dirt),
            optimizedRockTopMaterial != null
                ? optimizedRockTopMaterial
                : GetCellMaterial(RockHeightThreshold + 1, SurfaceBrush.Grass),
            GetCliffSideMaterial(),
            GetDirtCliffBlendSideMaterial(),
            optimizedFallbackSideMaterial != null
                ? optimizedFallbackSideMaterial
                : GetFallbackCellMaterial(ref fallbackTerrainSideMaterial, new Color(0.58f, 0.36f, 0.16f, 1f), "Level Grid Terrain Side"),
            GetGrassDirtBlendTopMaterial(),
        };
    }

    Material GetDirtCliffBlendSideMaterial()
    {
        Material material = optimizedDirtCliffBlendSideMaterial != null
            ? optimizedDirtCliffBlendSideMaterial
            : GetFallbackDirtCliffBlendSideMaterial();

        ConfigureDirtCliffBlendMaterial(material);
        return material;
    }

    Material GetCliffSideMaterial()
    {
        Material material = optimizedCliffSideMaterial != null
            ? optimizedCliffSideMaterial
            : GetFallbackCellMaterial(ref fallbackCliffSideMaterial, new Color(0.58f, 0.36f, 0.16f, 1f), "Level Grid Cliff Side");

        ConfigureSimpleCliffMaterial(material);
        return material;
    }

    Material GetFallbackDirtCliffBlendSideMaterial()
    {
        if (fallbackDirtCliffBlendSideMaterial != null)
            return fallbackDirtCliffBlendSideMaterial;

        Shader shader = Shader.Find("The Script/Terrain/Stylized Dirt Cliff Blend");
        if (shader == null)
            shader = Shader.Find("The Script/Terrain/Stylized Cliff");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return optimizedCliffSideMaterial != null ? optimizedCliffSideMaterial : rockCellMaterial;

        fallbackDirtCliffBlendSideMaterial = new Material(shader)
        {
            name = "Level Grid Dirt Cliff Blend",
            hideFlags = HideFlags.HideAndDontSave
        };
        return fallbackDirtCliffBlendSideMaterial;
    }

    void ConfigureDirtCliffBlendMaterial(Material material)
    {
        if (material == null)
            return;

        Material dirt = optimizedDirtTopMaterial != null ? optimizedDirtTopMaterial : dirtCellMaterial;
        CopyTextureProperty(material, "_DirtBaseMap", dirt, "_BaseMap");
        CopyTextureProperty(material, "_DirtBumpMap", dirt, "_BumpMap");
        CopyTextureProperty(material, "_DirtSmoothnessMap", dirt, "_SmoothnessMap");
        CopyTextureScaleOffset(material, "_DirtBaseMap", dirt, "_BaseMap");
        CopyTextureScaleOffset(material, "_DirtBumpMap", dirt, "_BumpMap");
        CopyTextureScaleOffset(material, "_DirtSmoothnessMap", dirt, "_SmoothnessMap");

        SetColorIfPresent(material, "_DirtBaseColor", GetMaterialColor(dirt, "_BaseColor", dirtCellColor));
        SetColorIfPresent(material, "_DirtShadowColor", GetMaterialColor(dirt, "_ShadowColor", new Color(0.72f, 0.48f, 0.22f, 1f)));
        SetColorIfPresent(material, "_DirtHighlightColor", GetMaterialColor(dirt, "_HighlightColor", new Color(1.28f, 1.08f, 0.62f, 1f)));
        SetColorIfPresent(material, "_DirtNoiseTint", GetMaterialColor(dirt, "_NoiseTint", new Color(0.32f, 0.14f, 0.04f, 1f)));
        SetColorIfPresent(material, "_BaseColor", new Color(0.62f, 0.34f, 0.14f, 1f));
        SetColorIfPresent(material, "_ShadowColor", new Color(0.34f, 0.19f, 0.08f, 1f));
        SetColorIfPresent(material, "_HighlightColor", new Color(0.86f, 0.54f, 0.25f, 1f));
        SetColorIfPresent(material, "_BandColor", new Color(0.72f, 0.42f, 0.18f, 1f));
        SetColorIfPresent(material, "_CreviceColor", new Color(0.28f, 0.15f, 0.06f, 1f));

        SetFloatIfPresent(material, "_DirtBumpScale", GetMaterialFloat(dirt, "_BumpScale", 0.55f));
        SetFloatIfPresent(material, "_DirtSmoothness", GetMaterialFloat(dirt, "_Smoothness", 0.1f));
        SetFloatIfPresent(material, "_RockNormalMapStrength", 0f);
        SetFloatIfPresent(material, "_VoronoiScale", 0.25f);
        SetFloatIfPresent(material, "_CreviceWidth", 0.04f);
        SetFloatIfPresent(material, "_CreviceStrength", 0f);
        SetFloatIfPresent(material, "_RockNormalStrength", 0f);
        SetFloatIfPresent(material, "_StrataStrength", 0.08f);
        SetFloatIfPresent(material, "_PlateVariation", 0f);
        SetFloatIfPresent(material, "_BandScale", 1.35f);
        SetFloatIfPresent(material, "_BandStrength", 0.08f);
        SetFloatIfPresent(material, "_PatchScale", 0.18f);
        SetFloatIfPresent(material, "_PatchStrength", 0.08f);
        SetFloatIfPresent(material, "_VerticalWearStrength", 0f);
        SetFloatIfPresent(material, "_DirtBlendSharpness", 1.35f);
        SetFloatIfPresent(material, "_DirtBlendStrength", 0.32f);
        SetFloatIfPresent(material, "_SpecularStrength", 0.02f);
        SetFloatIfPresent(material, "_ReceiveShadowStrength", 0f);
        SetFloatIfPresent(material, "_RampContrast", 1.15f);
        SetFloatIfPresent(material, "_RampSteps", 2f);
        SetFloatIfPresent(material, "_FacetStrength", 0.12f);
    }

    void ConfigureSimpleCliffMaterial(Material material)
    {
        if (material == null)
            return;

        SetColorIfPresent(material, "_BaseColor", new Color(0.62f, 0.34f, 0.14f, 1f));
        SetColorIfPresent(material, "_ShadowColor", new Color(0.34f, 0.19f, 0.08f, 1f));
        SetColorIfPresent(material, "_HighlightColor", new Color(0.86f, 0.54f, 0.25f, 1f));
        SetColorIfPresent(material, "_BandColor", new Color(0.72f, 0.42f, 0.18f, 1f));
        SetColorIfPresent(material, "_CreviceColor", new Color(0.28f, 0.15f, 0.06f, 1f));
        SetFloatIfPresent(material, "_RockNormalMapStrength", 0f);
        SetFloatIfPresent(material, "_CreviceStrength", 0f);
        SetFloatIfPresent(material, "_RockNormalStrength", 0f);
        SetFloatIfPresent(material, "_StrataStrength", 0.08f);
        SetFloatIfPresent(material, "_PlateVariation", 0f);
        SetFloatIfPresent(material, "_BandScale", 1.35f);
        SetFloatIfPresent(material, "_BandStrength", 0.08f);
        SetFloatIfPresent(material, "_PatchScale", 0.18f);
        SetFloatIfPresent(material, "_PatchStrength", 0.08f);
        SetFloatIfPresent(material, "_VerticalWearStrength", 0f);
        SetFloatIfPresent(material, "_SpecularStrength", 0.02f);
        SetFloatIfPresent(material, "_ReceiveShadowStrength", 0f);
        SetFloatIfPresent(material, "_RampContrast", 1.15f);
        SetFloatIfPresent(material, "_RampSteps", 2f);
        SetFloatIfPresent(material, "_FacetStrength", 0.12f);
    }

    Material GetGrassDirtBlendTopMaterial()
    {
        Material material = optimizedGrassDirtBlendTopMaterial != null
            ? optimizedGrassDirtBlendTopMaterial
            : GetFallbackGrassDirtBlendTopMaterial();

        ConfigureGrassDirtBlendMaterial(material);
        return material;
    }

    Material GetFallbackGrassDirtBlendTopMaterial()
    {
        if (fallbackGrassDirtBlendTopMaterial != null)
            return fallbackGrassDirtBlendTopMaterial;

        Shader shader = Shader.Find("The Script/Terrain/Stylized Grass Dirt Blend");
        if (shader == null)
            shader = Shader.Find("The Script/Terrain/Stylized Surface");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return optimizedGrassTopMaterial != null ? optimizedGrassTopMaterial : grassCellMaterial;

        fallbackGrassDirtBlendTopMaterial = new Material(shader)
        {
            name = "Level Grid Grass Dirt Blend",
            hideFlags = HideFlags.HideAndDontSave
        };
        return fallbackGrassDirtBlendTopMaterial;
    }

    void ConfigureGrassDirtBlendMaterial(Material material)
    {
        if (material == null)
            return;

        Material grass = optimizedGrassTopMaterial != null ? optimizedGrassTopMaterial : grassCellMaterial;
        Material dirt = optimizedDirtTopMaterial != null ? optimizedDirtTopMaterial : dirtCellMaterial;

        CopyTextureProperty(material, "_GrassBaseMap", grass, "_BaseMap");
        CopyTextureProperty(material, "_GrassBumpMap", grass, "_BumpMap");
        CopyTextureProperty(material, "_GrassSmoothnessMap", grass, "_SmoothnessMap");
        CopyTextureProperty(material, "_DirtBaseMap", dirt, "_BaseMap");
        CopyTextureProperty(material, "_DirtBumpMap", dirt, "_BumpMap");
        CopyTextureProperty(material, "_DirtSmoothnessMap", dirt, "_SmoothnessMap");

        CopyTextureScaleOffset(material, "_GrassBaseMap", grass, "_BaseMap");
        CopyTextureScaleOffset(material, "_GrassBumpMap", grass, "_BumpMap");
        CopyTextureScaleOffset(material, "_GrassSmoothnessMap", grass, "_SmoothnessMap");
        CopyTextureScaleOffset(material, "_DirtBaseMap", dirt, "_BaseMap");
        CopyTextureScaleOffset(material, "_DirtBumpMap", dirt, "_BumpMap");
        CopyTextureScaleOffset(material, "_DirtSmoothnessMap", dirt, "_SmoothnessMap");

        SetColorIfPresent(material, "_GrassBaseColor", GetMaterialColor(grass, "_BaseColor", grassCellColor));
        SetColorIfPresent(material, "_DirtBaseColor", GetMaterialColor(dirt, "_BaseColor", dirtCellColor));
        SetColorIfPresent(material, "_GrassShadowColor", GetMaterialColor(grass, "_ShadowColor", new Color(0.24f, 0.58f, 0.44f, 1f)));
        SetColorIfPresent(material, "_DirtShadowColor", GetMaterialColor(dirt, "_ShadowColor", new Color(0.72f, 0.48f, 0.22f, 1f)));
        SetColorIfPresent(material, "_GrassHighlightColor", GetMaterialColor(grass, "_HighlightColor", new Color(1.24f, 1.18f, 0.72f, 1f)));
        SetColorIfPresent(material, "_DirtHighlightColor", GetMaterialColor(dirt, "_HighlightColor", new Color(1.28f, 1.08f, 0.62f, 1f)));
        SetColorIfPresent(material, "_GrassNoiseTint", GetMaterialColor(grass, "_NoiseTint", new Color(0.06f, 0.3f, 0.08f, 1f)));
        SetColorIfPresent(material, "_DirtNoiseTint", GetMaterialColor(dirt, "_NoiseTint", new Color(0.32f, 0.14f, 0.04f, 1f)));
        SetColorIfPresent(material, "_WindDirection", GetMaterialColor(grass, "_WindDirection", new Color(1f, 0.35f, 0f, 0f)));

        SetFloatIfPresent(material, "_GrassBumpScale", GetMaterialFloat(grass, "_BumpScale", 0.72f));
        SetFloatIfPresent(material, "_DirtBumpScale", GetMaterialFloat(dirt, "_BumpScale", 0.55f));
        SetFloatIfPresent(material, "_GrassSmoothness", GetMaterialFloat(grass, "_Smoothness", 0.18f));
        SetFloatIfPresent(material, "_DirtSmoothness", GetMaterialFloat(dirt, "_Smoothness", 0.1f));
        SetFloatIfPresent(material, "_SpecularStrength", Mathf.Lerp(
            GetMaterialFloat(grass, "_SpecularStrength", 0.1f),
            GetMaterialFloat(dirt, "_SpecularStrength", 0.05f),
            0.5f));
        SetFloatIfPresent(material, "_ReceiveShadowStrength", GetMaterialFloat(grass, "_ReceiveShadowStrength", 0f));
        SetFloatIfPresent(material, "_RampContrast", GetMaterialFloat(grass, "_RampContrast", 1.55f));
        SetFloatIfPresent(material, "_RampSteps", GetMaterialFloat(grass, "_RampSteps", 3f));
        SetFloatIfPresent(material, "_WorldNoiseScale", GetMaterialFloat(grass, "_WorldNoiseScale", 0.18f));
        SetFloatIfPresent(material, "_WorldNoiseStrength", Mathf.Max(
            GetMaterialFloat(grass, "_WorldNoiseStrength", 0f),
            GetMaterialFloat(dirt, "_WorldNoiseStrength", 0f)));
        SetFloatIfPresent(material, "_WindStrength", GetMaterialFloat(grass, "_WindStrength", 0.18f));
        SetFloatIfPresent(material, "_WindScale", GetMaterialFloat(grass, "_WindScale", 0.52f));
        SetFloatIfPresent(material, "_WindSpeed", GetMaterialFloat(grass, "_WindSpeed", 1.15f));
        SetFloatIfPresent(material, "_FacetStrength", Mathf.Lerp(
            GetMaterialFloat(grass, "_FacetStrength", 0.08f),
            GetMaterialFloat(dirt, "_FacetStrength", 0.04f),
            0.5f));
    }

    static void CopyTextureProperty(Material target, string targetProperty, Material source, string sourceProperty)
    {
        if (target == null || source == null || !target.HasProperty(targetProperty) || !source.HasProperty(sourceProperty))
            return;

        target.SetTexture(targetProperty, source.GetTexture(sourceProperty));
    }

    static void CopyTextureScaleOffset(Material target, string targetProperty, Material source, string sourceProperty)
    {
        if (target == null || source == null || !target.HasProperty(targetProperty) || !source.HasProperty(sourceProperty))
            return;

        target.SetTextureScale(targetProperty, source.GetTextureScale(sourceProperty));
        target.SetTextureOffset(targetProperty, source.GetTextureOffset(sourceProperty));
    }

    static Color GetMaterialColor(Material material, string property, Color fallback)
    {
        return material != null && material.HasProperty(property) ? material.GetColor(property) : fallback;
    }

    static float GetMaterialFloat(Material material, string property, float fallback)
    {
        return material != null && material.HasProperty(property) ? material.GetFloat(property) : fallback;
    }

    static void SetColorIfPresent(Material material, string property, Color value)
    {
        if (material != null && material.HasProperty(property))
            material.SetColor(property, value);
    }

    static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material != null && material.HasProperty(property))
            material.SetFloat(property, value);
    }

    Material GetOptimizedTerrainWireMaterial()
    {
        Material material = optimizedTerrainWireMaterial != null
            ? optimizedTerrainWireMaterial
            : GetFallbackWireMaterial();
        SetMaterialColor(material, optimizedTerrainWireColor);
        return material;
    }

    Material GetFallbackWireMaterial()
    {
        if (fallbackOptimizedTerrainWireMaterial != null)
            return fallbackOptimizedTerrainWireMaterial;

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return null;

        fallbackOptimizedTerrainWireMaterial = new Material(shader)
        {
            name = "Level Grid Terrain Wire Overlay",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
        };
        fallbackOptimizedTerrainWireMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        fallbackOptimizedTerrainWireMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        fallbackOptimizedTerrainWireMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        fallbackOptimizedTerrainWireMaterial.SetInt("_ZWrite", 0);

        return fallbackOptimizedTerrainWireMaterial;
    }
}
