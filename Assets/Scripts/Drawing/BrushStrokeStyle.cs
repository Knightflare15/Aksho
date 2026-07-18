using UnityEngine;
using UnityEngine.UI;

public static class BrushStrokeStyle
{
    static Sprite _segmentSprite;
    static Sprite _dotSprite;

    public static void ApplySegment(Image image, Color color)
    {
        if (image == null) return;

        image.sprite = GetSegmentSprite();
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.color = color;
        image.raycastTarget = false;
    }

    public static void ApplyDot(Image image, Color color)
    {
        if (image == null) return;

        image.sprite = GetDotSprite();
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = color;
        image.raycastTarget = false;
    }

    static Sprite GetSegmentSprite()
    {
        if (_segmentSprite != null) return _segmentSprite;

        const int width = 128;
        const int height = 48;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "RuntimeBrushSegment";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        float halfHeight = (height - 1) * 0.5f;
        for (int y = 0; y < height; y++)
        {
            float dy = Mathf.Abs(y - halfHeight) / halfHeight;
            float edgeFade = Mathf.Clamp01(1f - Mathf.Pow(dy, 1.7f));
            edgeFade = Mathf.SmoothStep(0f, 1f, edgeFade);

            for (int x = 0; x < width; x++)
            {
                float nx = x / (width - 1f);
                float grain = 0.92f + Mathf.PerlinNoise(nx * 5.5f, y * 0.12f) * 0.08f;
                float taper = 0.95f + Mathf.Sin(nx * Mathf.PI * 2f) * 0.03f;
                float alpha = Mathf.Clamp01(edgeFade * grain * taper);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        _segmentSprite = Sprite.Create(
            texture,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            100f);
        return _segmentSprite;
    }

    static Sprite GetDotSprite()
    {
        if (_dotSprite != null) return _dotSprite;

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "RuntimeBrushDot";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = center.x;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / radius;
                float radial = Mathf.Clamp01(1f - dist);
                radial = Mathf.SmoothStep(0f, 1f, radial);
                float grain = 0.9f + Mathf.PerlinNoise(x * 0.14f, y * 0.14f) * 0.1f;
                float alpha = Mathf.Clamp01(radial * grain);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        _dotSprite = Sprite.Create(
            texture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f);
        return _dotSprite;
    }
}
