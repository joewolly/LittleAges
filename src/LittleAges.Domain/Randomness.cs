namespace LittleAges.Domain;

/// <summary>Versioned independent randomness domains. Numeric values are persisted compatibility data.</summary>
public enum RandomDomain : uint
{
    WorldGeneration = 1,
    CitizenGeneration = 2,
    DecisionVariation = 3,
    Relationships = 4,
    Reproduction = 5,
    Mortality = 6,
    ResourceRegeneration = 7
}

public interface IDeterministicRandom
{
    ulong NextUInt64(RandomDomain domain, ulong keyA = 0, ulong keyB = 0, ulong keyC = 0);
    double NextUnitDouble(RandomDomain domain, ulong keyA = 0, ulong keyB = 0, ulong keyC = 0);
}

/// <summary>
/// Stateless SplitMix64 derivation RNG, version 1. Every value is derived from seed, domain and keys.
/// It intentionally has no mutable stream and does not use a runtime-provided PRNG.
/// </summary>
public sealed class DeterministicRandom : IDeterministicRandom
{
    public const int AlgorithmVersion = 1;
    private const ulong DomainSalt = 0xD6E8FEB86659FD93UL;
    private const ulong KeySaltA = 0xA0761D6478BD642FUL;
    private const ulong KeySaltB = 0xE7037ED1A0B428DBUL;
    private const ulong KeySaltC = 0x8EBC6AF09C88C6E3UL;

    private readonly WorldSeed _seed;

    public DeterministicRandom(WorldSeed seed) => _seed = seed;

    public ulong NextUInt64(RandomDomain domain, ulong keyA = 0, ulong keyB = 0, ulong keyC = 0)
    {
        if (!Enum.IsDefined(domain))
        {
            throw new ArgumentOutOfRangeException(nameof(domain), domain, "The random domain is not supported by this algorithm version.");
        }

        var state = _seed.Value;
        state = Mix(state ^ DomainSalt ^ (uint)domain);
        state = Mix(state ^ keyA ^ KeySaltA);
        state = Mix(state ^ keyB ^ KeySaltB);
        state = Mix(state ^ keyC ^ KeySaltC);
        return Mix(state);
    }

    public double NextUnitDouble(RandomDomain domain, ulong keyA = 0, ulong keyB = 0, ulong keyC = 0)
    {
        const double twoToThe53 = 9007199254740992d;
        return (NextUInt64(domain, keyA, keyB, keyC) >> 11) / twoToThe53;
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
