using UnityEngine;

public static class ObjectivePlacer
{
    public static void PlaceData(WorldData data)
    {
        data.objectives.Clear();
        foreach (ZoneData zone in data.zones)
        {
            if (zone.kind != ZoneKind.Objective)
                continue;
            data.objectives.Add(new ObjectivePoint
            {
                gridPosition = zone.gridPosition,
                worldPosition = WorldPlacementUtility.GroundedPosition(data, zone.gridPosition.x, zone.gridPosition.y),
                zone = zone,
            });
        }
    }

    public static void Spawn(WorldData data, WorldGenerationProfile profile, Transform parent)
    {
        // Grammar creature combat no longer uses spell pillars. Objective zone data is
        // still kept for layout/path generation, but no gameplay object is spawned here.
    }
}
