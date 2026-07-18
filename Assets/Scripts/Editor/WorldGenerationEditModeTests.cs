using NUnit.Framework;
using UnityEngine;

public sealed class WorldGenerationEditModeTests
{
    [Test]
    public void SameSeedProducesSameGameplayData()
    {
        WorldGenerationProfile profile = CreateProfile();
        WorldData a = WorldGenerator.BuildWorldData(profile, 4242);
        WorldData b = WorldGenerator.BuildWorldData(profile, 4242);

        Assert.AreEqual(a.resolution, b.resolution);
        for (int i = 0; i < a.height.Length; i += 137)
            Assert.That(b.height[i], Is.EqualTo(a.height[i]).Within(0.0001f));

        Assert.AreEqual(a.objectives.Count, b.objectives.Count);
        for (int i = 0; i < a.objectives.Count; i++)
            Assert.AreEqual(a.objectives[i].gridPosition, b.objectives[i].gridPosition);

        Assert.AreEqual(a.chests.Count, b.chests.Count);
        for (int i = 0; i < a.chests.Count; i++)
            Assert.AreEqual(a.chests[i].gridPosition, b.chests[i].gridPosition);

        Assert.AreEqual(a.enemyTriggers.Count, b.enemyTriggers.Count);
        for (int i = 0; i < a.enemyTriggers.Count; i++)
            Assert.AreEqual(a.enemyTriggers[i].gridPosition, b.enemyTriggers[i].gridPosition);
    }

    [Test]
    public void PropProfileDoesNotChangeTerrainLayoutOrGameplayPlacement()
    {
        WorldGenerationProfile noProps = CreateProfile();
        WorldGenerationProfile withProps = CreateProfile();
        withProps.propProfile = ScriptableObject.CreateInstance<PropSpawnProfile>();
        withProps.propProfile.rules = new[] { new PropRule { minCount = 1, maxCount = 3 } };

        WorldData a = WorldGenerator.BuildWorldData(noProps, 9001);
        WorldData b = WorldGenerator.BuildWorldData(withProps, 9001);

        for (int i = 0; i < a.height.Length; i += 211)
            Assert.That(b.height[i], Is.EqualTo(a.height[i]).Within(0.0001f));
        Assert.AreEqual(a.objectives.Count, b.objectives.Count);
        Assert.AreEqual(a.chests.Count, b.chests.Count);
        Assert.AreEqual(a.enemyTriggers.Count, b.enemyTriggers.Count);
        for (int i = 0; i < a.objectives.Count; i++)
            Assert.AreEqual(a.objectives[i].gridPosition, b.objectives[i].gridPosition);
    }

    [Test]
    public void GameplayPlacementIsReachable()
    {
        WorldGenerationProfile profile = CreateProfile();
        WorldData data = WorldGenerator.BuildWorldData(profile, 1717);
        Assert.IsTrue(GameplayPlacementValidator.Validate(data));
        Assert.That(data.objectives.Count, Is.EqualTo(profile.GameplayProfile.objectivePillarCount));
        Assert.That(data.chests.Count, Is.GreaterThanOrEqualTo(profile.GameplayProfile.minChests));
        Assert.That(data.enemyTriggers.Count, Is.GreaterThanOrEqualTo(profile.GameplayProfile.minEnemyTriggers));
    }

    [Test]
    public void IslandMaskCreatesCentralPlayableLand()
    {
        WorldGenerationProfile profile = CreateProfile();
        WorldData data = WorldGenerator.BuildWorldData(profile, 31337);
        int center = data.resolution / 2;
        Assert.That(data.landMask[data.Index(center, center)], Is.GreaterThan(0));
        Assert.That(data.waterMask[data.Index(center, center)], Is.EqualTo(0));

        int landCells = 0;
        int dryLandCells = 0;
        for (int i = 0; i < data.landMask.Length; i++)
        {
            if (data.landMask[i] > 0)
            {
                landCells++;
                if (data.waterMask[i] == 0)
                    dryLandCells++;
            }
        }

        float landRatio = landCells / (float)data.landMask.Length;
        float dryLandRatio = dryLandCells / (float)data.landMask.Length;
        Assert.That(landRatio, Is.GreaterThan(0.52f));
        Assert.That(landRatio, Is.LessThan(0.9f));
        Assert.That(dryLandRatio, Is.GreaterThan(0.52f));
    }

    [Test]
    public void CoastlineHeightTransitionIsNotACliffWall()
    {
        WorldGenerationProfile profile = CreateProfile();
        WorldData data = WorldGenerator.BuildWorldData(profile, 6161);
        float worstAdjacentCoastDrop = 0f;

        for (int z = 1; z < data.resolution - 1; z++)
        {
            for (int x = 1; x < data.resolution - 1; x++)
            {
                int index = data.Index(x, z);
                if (data.landMask[index] == 0)
                    continue;

                CheckNeighbor(data, x, z, x + 1, z, ref worstAdjacentCoastDrop);
                CheckNeighbor(data, x, z, x - 1, z, ref worstAdjacentCoastDrop);
                CheckNeighbor(data, x, z, x, z + 1, ref worstAdjacentCoastDrop);
                CheckNeighbor(data, x, z, x, z - 1, ref worstAdjacentCoastDrop);
            }
        }

        Assert.That(worstAdjacentCoastDrop, Is.LessThan(1.9f));
    }

    [Test]
    public void ChunkBuilderMatchesDefaultWorldDimensions()
    {
        WorldGenerationProfile profile = CreateProfile();
        WorldData data = WorldGenerator.BuildWorldData(profile, 777);
        GameObject root = new GameObject("WorldGenerationChunkTestRoot");
        try
        {
            var chunks = TerrainChunkBuilder.Build(data, profile, root.transform, null);
            Assert.AreEqual(151, data.resolution);
            Assert.AreEqual(25, chunks.Count);
            foreach (TerrainChunk chunk in chunks)
            {
                Assert.IsNotNull(chunk.meshFilter.sharedMesh);
                Assert.That(chunk.meshFilter.sharedMesh.vertexCount, Is.LessThanOrEqualTo(61 * 61));
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static WorldGenerationProfile CreateProfile()
    {
        WorldGenerationProfile profile = ScriptableObject.CreateInstance<WorldGenerationProfile>();
        profile.worldSizeMeters = 150;
        profile.terrainGridSpacing = 1f;
        profile.chunkSizeMeters = 30;
        profile.coarseGridSpacing = 2;
        profile.waterLevel = 0f;
        profile.gameplayPlacementProfile = ScriptableObject.CreateInstance<GameplayPlacementProfile>();
        profile.gameplayPlacementProfile.objectivePillarCount = 3;
        profile.gameplayPlacementProfile.minChests = 4;
        profile.gameplayPlacementProfile.maxChests = 5;
        profile.gameplayPlacementProfile.minEnemyTriggers = 8;
        profile.gameplayPlacementProfile.maxEnemyTriggers = 9;
        return profile;
    }

    static void CheckNeighbor(WorldData data, int landX, int landZ, int otherX, int otherZ, ref float worstDrop)
    {
        int otherIndex = data.Index(otherX, otherZ);
        if (data.landMask[otherIndex] > 0)
            return;

        float drop = data.height[data.Index(landX, landZ)] - data.height[otherIndex];
        if (drop > worstDrop)
            worstDrop = drop;
    }
}
