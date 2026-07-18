using System;
using System.Text;
using UnityEngine;

public static class NpcDialogueAudioCatalog
{
    const string ResourceRoot = "Audio/NpcDialogue";

    public static AudioClip LoadFor(LocalizedDialogueLine line)
    {
        if (line == null)
            return null;

        AudioClip clip = Load(line.dialogueTaskId);
        return clip != null ? clip : Load(line.lineId);
    }

    public static AudioClip Load(string clipId)
    {
        if (string.IsNullOrWhiteSpace(clipId))
            return null;

        return Resources.Load<AudioClip>($"{ResourceRoot}/{SanitizeResourceName(clipId)}");
    }

    public static string SanitizeResourceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (char character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
                builder.Append(character);
            else
                builder.Append('_');
        }
        return builder.ToString();
    }
}
