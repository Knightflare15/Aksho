using UnityEngine;

public interface IDrawInputAssist
{
    bool TryGetInputBounds(out Rect bounds);

    bool TryAdjustStrokePoint(
        Vector2 rawPoint,
        Vector2 previousPoint,
        bool isStrokeStart,
        out Vector2 adjustedPoint);
}
