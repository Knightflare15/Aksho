using UnityEngine;

public static class CliffDetector
{
    public static void Detect(WorldData data, WorldGenerationProfile profile)
    {
        for (int i = 0; i < data.cliffMask.Length; i++)
            data.cliffMask[i] = data.slope[i] >= profile.cliffs.slopeThreshold && data.waterMask[i] == 0 ? (byte)255 : (byte)0;
    }
}
