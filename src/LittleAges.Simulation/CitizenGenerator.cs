using LittleAges.Domain;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LittleAges.Simulation;

public static class CitizenGenerator
{
    public const int FounderCount = CitizenSimulationRules.FounderCount;
    public const long MinutesPerYear = WorldCalendar.MinutesPerYear;
    private static readonly string[] GivenNames = ["Elara", "Tomas", "Mira", "Jon", "Anwen", "Bram", "Celia", "Darin", "Esme", "Fenn", "Iria", "Kellan", "Liora", "Marek", "Nessa", "Orin", "Petra", "Rhea", "Soren", "Talia", "Una", "Vera", "Wren", "Yara", "Zev"];
    private static readonly string[] FamilyNames = ["Venn", "Rell", "Vale", "Alder", "Briar", "Cairn", "Dale", "Ember", "Fallow", "Glen", "Hearth", "Ives", "Jory", "Kestrel", "Lark", "Moss", "Nettle", "Orchard", "Pike", "Quill", "Reed", "Stone", "Thorne", "Umber", "Wick", "Yew"];

    public static IReadOnlyList<Citizen> Generate(WorldSeed seed, WorldMap world, DeterministicCounters counters, WorldMinute minute)
    {
        var candidates = world.Tiles.Where(x => x.Walkable).OrderBy(x => DistanceSquared(x.Coordinate, world.StartingSite)).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X).Take(FounderCount).ToArray();
        if (candidates.Length != FounderCount) throw new InvalidOperationException("The world does not have enough walkable founder tiles.");
        var random = new DeterministicRandom(seed); var result = new List<Citizen>(FounderCount); var names = new HashSet<string>(StringComparer.Ordinal); var families = new HashSet<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < FounderCount; ordinal++)
        {
            var given = GivenNames[(int)(random.NextUInt64(RandomDomain.CitizenGeneration, 1, (ulong)ordinal, 0) % (ulong)GivenNames.Length)];
            var familyIndex = (int)(random.NextUInt64(RandomDomain.CitizenGeneration, 2, (ulong)ordinal, 0) % (ulong)FamilyNames.Length); var family = FamilyNames[familyIndex];
            var suffix = 0; while (!families.Add(family) || !names.Add($"{given} {family}")) { familyIndex = (familyIndex + 1 + suffix++) % FamilyNames.Length; family = FamilyNames[familyIndex]; }
            var age = 18 + (int)(random.NextUInt64(RandomDomain.CitizenGeneration, 3, (ulong)ordinal, 0) % 28);
            var traits = new CitizenTraits(Val(random, ordinal, 10), Val(random, ordinal, 11), Val(random, ordinal, 12), Val(random, ordinal, 13), Val(random, ordinal, 14), Val(random, ordinal, 15));
            var skills = new CitizenSkills(Val(random, ordinal, 20), Val(random, ordinal, 21), Val(random, ordinal, 22), Val(random, ordinal, 23), Val(random, ordinal, 24), Val(random, ordinal, 25));
            var offset = random.NextUInt64(RandomDomain.CitizenGeneration, 4, (ulong)ordinal, 0) % (ulong)MinutesPerYear;
            var birthMinute = checked(minute.Value - checked((age * MinutesPerYear) + (long)offset));
            result.Add(new Citizen(counters.AllocateCitizenId(), ordinal, given, family, birthMinute, candidates[ordinal].Coordinate, traits, skills));
        }
        return result;
    }
    public static TileCoordinate? SelectTarget(WorldSeed seed, WorldMap world, Citizen citizen, int radius)
    {
        var candidates = world.Tiles.Where(tile => tile.Walkable && tile.Coordinate != citizen.Location && Math.Abs(tile.Coordinate.X - citizen.Location.X) <= radius && Math.Abs(tile.Coordinate.Y - citizen.Location.Y) <= radius).OrderBy(tile => Rank(seed, citizen, tile.Coordinate, (ulong)radius)).ThenBy(tile => tile.Coordinate.Y).ThenBy(tile => tile.Coordinate.X);
        foreach (var candidate in candidates) if (DeterministicPathfinder.Find(world, citizen.Location, candidate.Coordinate) is { Count: > 1 }) return candidate.Coordinate;
        return null;
    }
    private static int Val(DeterministicRandom random, int ordinal, ulong key) => (int)(random.NextUInt64(RandomDomain.CitizenGeneration, key, (ulong)ordinal, 1) % 10001);
    private static ulong Rank(WorldSeed seed, Citizen citizen, TileCoordinate coordinate, ulong purpose) => new DeterministicRandom(seed).NextUInt64(RandomDomain.DecisionVariation, (ulong)citizen.FounderOrdinal, (ulong)citizen.ActionSequence, purpose ^ ((ulong)(uint)coordinate.X << 32) ^ (uint)coordinate.Y);
    private static long DistanceSquared(TileCoordinate a, TileCoordinate b) { var x = (long)a.X - b.X; var y = (long)a.Y - b.Y; return checked(x * x + y * y); }
    public static string Fingerprint(WorldSeed seed, IReadOnlyList<Citizen> citizens)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static string I<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
        static void Append(IncrementalHash hash, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"));
            hash.AppendData(bytes);
        }
        foreach (var c in citizens.OrderBy(x => x.FounderOrdinal))
        {
            Append(hash, "citizen-generation-version=1");
            Append(hash, $"seed={I(seed.Value)}"); Append(hash, $"ordinal={I(c.FounderOrdinal)}"); Append(hash, $"id={I(c.Id.Value)}");
            Append(hash, $"given={c.GivenName}"); Append(hash, $"family={c.FamilyName}"); Append(hash, $"birth={I(c.BirthMinute)}");
            Append(hash, $"location-x={I(c.Location.X)}"); Append(hash, $"location-y={I(c.Location.Y)}"); Append(hash, $"health={I(c.Health)}");
            Append(hash, $"need-hunger={I(c.Needs.Hunger)}"); Append(hash, $"need-rest={I(c.Needs.Rest)}"); Append(hash, $"need-shelter={I(c.Needs.Shelter)}"); Append(hash, $"need-social={I(c.Needs.Social)}"); Append(hash, $"needs-updated={I(c.NeedsUpdatedMinute)}");
            Append(hash, $"trait-industriousness={I(c.Traits.Industriousness)}"); Append(hash, $"trait-sociability={I(c.Traits.Sociability)}"); Append(hash, $"trait-curiosity={I(c.Traits.Curiosity)}"); Append(hash, $"trait-cooperativeness={I(c.Traits.Cooperativeness)}"); Append(hash, $"trait-risk-tolerance={I(c.Traits.RiskTolerance)}"); Append(hash, $"trait-resilience={I(c.Traits.Resilience)}");
            Append(hash, $"skill-foraging={I(c.Skills.Foraging)}"); Append(hash, $"skill-woodcutting={I(c.Skills.Woodcutting)}"); Append(hash, $"skill-stoneworking={I(c.Skills.Stoneworking)}"); Append(hash, $"skill-construction={I(c.Skills.Construction)}"); Append(hash, $"skill-hauling={I(c.Skills.Hauling)}"); Append(hash, $"skill-domestic={I(c.Skills.Domestic)}");
            Append(hash, $"action={I((int)c.CurrentAction)}"); Append(hash, $"action-sequence={I(c.ActionSequence)}"); Append(hash, $"action-started={(c.ActionStartedMinute?.Value.ToString(CultureInfo.InvariantCulture) ?? "null")}"); Append(hash, $"action-completes={(c.ActionCompletesMinute?.Value.ToString(CultureInfo.InvariantCulture) ?? "null")}"); Append(hash, $"target-x={(c.ActionTarget?.X.ToString(CultureInfo.InvariantCulture) ?? "null")}"); Append(hash, $"target-y={(c.ActionTarget?.Y.ToString(CultureInfo.InvariantCulture) ?? "null")}");
            Append(hash, $"lifetime-steps={I(c.LifetimeMovementSteps)}"); Append(hash, $"lifetime-cost={I(c.LifetimeMovementCost)}");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
