using System.Collections.Generic;
using UnityEngine;

public interface ILetterAcceptedObserver
{
    void OnLetterAccepted(
        char letter,
        List<List<Vector2>> strokes,
        PDollarRecognizer.RecognitionResult result);
}
