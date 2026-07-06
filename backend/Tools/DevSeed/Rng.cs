namespace DevSeed;

/// <summary>
/// mulberry32 PRNG — same algorithm as the web mock's generator
/// (<c>web/server/utils/dev-api-mock.ts</c>), so a given seed produces the same
/// sequence shape. Deterministic: reseeding and rerunning the tool always
/// produces the same dataset.
/// </summary>
public sealed class Rng(uint seed)
{
    private uint _state = seed;

    public double NextDouble()
    {
        _state += 0x6D2B79F5;
        var t = _state;
        t = (t ^ (t >> 15)) * (t | 1);
        t ^= t + (t ^ (t >> 7)) * (t | 61);
        return (double)(t ^ (t >> 14)) / 4294967296.0;
    }

    /// <summary>Uniform double in [min, max).</summary>
    public double NextDouble(double min, double max) => min + NextDouble() * (max - min);

    public int NextInt(int minInclusive, int maxExclusive) =>
        minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
}
