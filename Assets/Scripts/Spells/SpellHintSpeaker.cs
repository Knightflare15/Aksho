using System;
using System.Reflection;
using UnityEngine;

public class SpellHintSpeaker : MonoBehaviour
{
    public bool Speak(string word)
    {
        string text = SpellRegistry.NormalizeWord(word);
        if (string.IsNullOrEmpty(text))
            return false;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            Type voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (voiceType == null)
                return false;

            object voice = Activator.CreateInstance(voiceType);
            voiceType.InvokeMember(
                "Speak",
                BindingFlags.InvokeMethod,
                null,
                voice,
                new object[] { text, 1 });
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log($"[SpellHintSpeaker] Spoken hint unavailable: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }
}
