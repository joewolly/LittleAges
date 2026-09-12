namespace LittleAges.Domain;

/// <summary>An immutable unsigned 64-bit seed. Zero is a valid seed.</summary>
public readonly record struct WorldSeed(ulong Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
