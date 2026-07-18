using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional live-stroke hook for draw modes that want realtime guidance
/// while the player is still drawing the current letter.
/// </summary>
public interface ILiveLetterFeedback
{
    void OnLiveStrokeUpdated(List<List<Vector2>> strokes, List<GameObject> strokeVisuals);
}
