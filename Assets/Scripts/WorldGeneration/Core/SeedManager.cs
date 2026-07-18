using System;

public sealed class SeedManager
{
    public readonly int seed;
    public readonly Random terrainRng;
    public readonly Random layoutRng;
    public readonly Random pathRng;
    public readonly Random gameplayRng;
    public readonly Random propRng;

    public SeedManager(int seed)
    {
        this.seed = seed;
        terrainRng = new Random(Mix(seed, 0x13579BDF));
        layoutRng = new Random(Mix(seed, 0x2468ACE));
        pathRng = new Random(Mix(seed, 0x1020304));
        gameplayRng = new Random(Mix(seed, 0x5566778));
        propRng = new Random(Mix(seed, unchecked((int)0xCAFEBABE)));
    }

    public static int Mix(int seed, int salt)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)salt + 0x9E3779B9u + (value << 6) + (value >> 2);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)(value & 0x7FFFFFFF);
        }
    }
}
