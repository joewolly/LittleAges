using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LittleAges.Domain;

/// <summary>A zero-based immutable map coordinate. Ordering is row-major (Y, then X).</summary>
public readonly record struct TileCoordinate : IComparable<TileCoordinate>
{
    public TileCoordinate(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }

    public int CompareTo(TileCoordinate other)
    {
        var result = Y.CompareTo(other.Y);
        return result != 0 ? result : X.CompareTo(other.X);
    }

    public long ToIndex(int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        if (X >= width) throw new ArgumentOutOfRangeException(nameof(width), width, "X must be less than the map width.");
        return checked(((long)Y * width) + X);
    }

    public static TileCoordinate FromIndex(long index, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new TileCoordinate(checked((int)(index % width)), checked((int)(index / width)));
    }

    public static TileCoordinate FromIndex(int index, int width) => FromIndex((long)index, width);
    public long ToLinearIndex(int width) => ToIndex(width);
    public static TileCoordinate FromLinearIndex(long index, int width) => FromIndex(index, width);
    public static bool operator <(TileCoordinate left, TileCoordinate right) => left.CompareTo(right) < 0;
    public static bool operator >(TileCoordinate left, TileCoordinate right) => left.CompareTo(right) > 0;
    public static bool operator <=(TileCoordinate left, TileCoordinate right) => left.CompareTo(right) <= 0;
    public static bool operator >=(TileCoordinate left, TileCoordinate right) => left.CompareTo(right) >= 0;
}

public enum TerrainType : int
{
    Freshwater = 1,
    Grassland = 2,
    Forest = 3,
    RockyGround = 4,
    DenseWilderness = 5
}

public enum ResourceType : int
{
    Food = 1,
    Wood = 2,
    Stone = 3
}

public readonly record struct ResourceNodeId : IWorldEntityId
{
    public ResourceNodeId(long value) => Value = WorldIdValidation.RequirePositive(value);
    public long Value { get; }
}

/// <summary>One immutable normalized tile. MovementCost is zero only for non-walkable tiles.</summary>
public sealed record WorldTile
{
    public WorldTile(TileCoordinate coordinate, TerrainType terrain, int elevation, int fertility,
        int waterAccess, bool walkable, int movementCost)
    {
        if (!Enum.IsDefined(terrain)) throw new ArgumentOutOfRangeException(nameof(terrain));
        ValidateNormalized(elevation, nameof(elevation));
        ValidateNormalized(fertility, nameof(fertility));
        ValidateNormalized(waterAccess, nameof(waterAccess));
        if (walkable && movementCost <= 0) throw new ArgumentOutOfRangeException(nameof(movementCost));
        if (!walkable && movementCost != 0) throw new ArgumentException("Non-walkable tiles must use movement cost sentinel 0.", nameof(movementCost));
        Coordinate = coordinate;
        Terrain = terrain;
        Elevation = elevation;
        Fertility = fertility;
        WaterAccess = waterAccess;
        Walkable = walkable;
        MovementCost = movementCost;
    }

    public TileCoordinate Coordinate { get; }
    public TerrainType Terrain { get; }
    public TerrainType TerrainType => Terrain;
    public int Elevation { get; }
    public int Fertility { get; }
    public int WaterAccess { get; }
    public bool Walkable { get; }
    public int MovementCost { get; }
    public bool Buildable => Walkable && Terrain != TerrainType.Freshwater;

    private static void ValidateNormalized(int value, string name)
    {
        if (value is < 0 or > 10000) throw new ArgumentOutOfRangeException(name, value, "Normalized values must be in [0, 10000].");
    }
}

/// <summary>Immutable deterministic resource deposit. RegenerationPotential is descriptive metadata.</summary>
public sealed record ResourceNode
{
    public ResourceNode(ResourceNodeId id, TileCoordinate coordinate, ResourceType type, int initialQuantity,
        int maximumQuantity, int regenerationPotential = 0)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialQuantity);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumQuantity, initialQuantity);
        if (regenerationPotential is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(regenerationPotential));
        Id = id;
        Coordinate = coordinate;
        Type = type;
        InitialQuantity = initialQuantity;
        MaximumQuantity = maximumQuantity;
        RegenerationPotential = regenerationPotential;
    }

    public ResourceNodeId Id { get; }
    public TileCoordinate Coordinate { get; }
    public ResourceType Type { get; }
    public int InitialQuantity { get; }
    public int MaximumQuantity { get; }
    public int MaxQuantity => MaximumQuantity;
    public int RegenerationPotential { get; }
    public int Quantity => InitialQuantity;
}

/// <summary>Versioned canonical input to world generation. All values are persisted, never process defaults.</summary>
public sealed record WorldGenerationConfiguration
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public int Width { get; init; } = 160;
    public int Height { get; init; } = 160;
    public int TerrainWaterThreshold { get; init; } = 2500;
    public int TerrainRockThreshold { get; init; } = 8200;
    public int TerrainForestFertilityThreshold { get; init; } = 5600;
    public int TerrainDenseFertilityThreshold { get; init; } = 7600;
    public int FoodPlacementThreshold { get; init; } = 4200;
    public int WoodPlacementThreshold { get; init; } = 4800;
    public int StonePlacementThreshold { get; init; } = 7000;
    public int StartSiteRadius { get; init; } = 8;
    public int MinimumNearbyFood { get; init; } = 1;
    public int MinimumNearbyWood { get; init; } = 1;
    public int MinimumNearbyStone { get; init; } = 1;
    public int MinimumNearbyFreshwater { get; init; } = 1;
    public int MinimumWalkableCount { get; init; } = 24;
    public int MaximumAttempts { get; init; } = 8;

    public static WorldGenerationConfiguration Default { get; } = new();
    public static WorldGenerationConfiguration CreateDefault() => Default;
    public int WorldGenerationVersion => Version;
    public int MapWidth => Width;
    public int MapHeight => Height;
    public int MaxAttempts => MaximumAttempts;
    public string CanonicalJson => ToCanonicalJson();

    public WorldGenerationConfiguration Validate()
    {
        if (Version != CurrentVersion) throw new NotSupportedException($"World generation configuration version {Version} is not supported.");
        if (Width is < 8 or > 1024) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height is < 8 or > 1024) throw new ArgumentOutOfRangeException(nameof(Height));
        foreach (var value in new[] { TerrainWaterThreshold, TerrainRockThreshold, TerrainForestFertilityThreshold, TerrainDenseFertilityThreshold,
            FoodPlacementThreshold, WoodPlacementThreshold, StonePlacementThreshold })
            if (value is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(value));
        if (TerrainWaterThreshold >= TerrainRockThreshold) throw new ArgumentException("Water threshold must be below rock threshold.");
        if (TerrainForestFertilityThreshold >= TerrainDenseFertilityThreshold) throw new ArgumentException("Forest threshold must be below dense threshold.");
        if (StartSiteRadius is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(StartSiteRadius));
        if (MinimumNearbyFood < 0 || MinimumNearbyWood < 0 || MinimumNearbyStone < 0 || MinimumNearbyFreshwater < 0 || MinimumWalkableCount < 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumWalkableCount));
        if (MaximumAttempts is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(MaximumAttempts));
        return this;
    }

    public string ToCanonicalJson()
    {
        Validate();
        var builder = new StringBuilder(512);
        builder.Append('{');
        Append(builder, "version", Version); Append(builder, "width", Width); Append(builder, "height", Height);
        Append(builder, "terrainWaterThreshold", TerrainWaterThreshold); Append(builder, "terrainRockThreshold", TerrainRockThreshold);
        Append(builder, "terrainForestFertilityThreshold", TerrainForestFertilityThreshold); Append(builder, "terrainDenseFertilityThreshold", TerrainDenseFertilityThreshold);
        Append(builder, "foodPlacementThreshold", FoodPlacementThreshold); Append(builder, "woodPlacementThreshold", WoodPlacementThreshold); Append(builder, "stonePlacementThreshold", StonePlacementThreshold);
        Append(builder, "startSiteRadius", StartSiteRadius); Append(builder, "minimumNearbyFood", MinimumNearbyFood); Append(builder, "minimumNearbyWood", MinimumNearbyWood);
        Append(builder, "minimumNearbyStone", MinimumNearbyStone); Append(builder, "minimumNearbyFreshwater", MinimumNearbyFreshwater); Append(builder, "minimumWalkableCount", MinimumWalkableCount); Append(builder, "maximumAttempts", MaximumAttempts, last: true);
        builder.Append('}');
        return builder.ToString();
    }

    public string ToJson() => ToCanonicalJson();

    /// <summary>Parses strict versioned properties in any JSON property order and normalizes to canonical output.</summary>
    public static WorldGenerationConfiguration FromCanonicalJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Configuration JSON is required.", nameof(json));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("World configuration must be a JSON object.");
        var expected = new[] { "version", "width", "height", "terrainWaterThreshold", "terrainRockThreshold", "terrainForestFertilityThreshold", "terrainDenseFertilityThreshold", "foodPlacementThreshold", "woodPlacementThreshold", "stonePlacementThreshold", "startSiteRadius", "minimumNearbyFood", "minimumNearbyWood", "minimumNearbyStone", "minimumNearbyFreshwater", "minimumWalkableCount", "maximumAttempts" };
        var properties = document.RootElement.EnumerateObject().ToDictionary(static p => p.Name, StringComparer.Ordinal);
        if (properties.Count != expected.Length || expected.Any(name => !properties.ContainsKey(name))) throw new FormatException("World configuration has missing or unknown properties.");
        int Read(string name) => properties[name].Value.ValueKind == JsonValueKind.Number && properties[name].Value.TryGetInt32(out var value) ? value : throw new FormatException($"Configuration property '{name}' must be an invariant integer.");
        return new WorldGenerationConfiguration { Version = Read("version"), Width = Read("width"), Height = Read("height"), TerrainWaterThreshold = Read("terrainWaterThreshold"), TerrainRockThreshold = Read("terrainRockThreshold"), TerrainForestFertilityThreshold = Read("terrainForestFertilityThreshold"), TerrainDenseFertilityThreshold = Read("terrainDenseFertilityThreshold"), FoodPlacementThreshold = Read("foodPlacementThreshold"), WoodPlacementThreshold = Read("woodPlacementThreshold"), StonePlacementThreshold = Read("stonePlacementThreshold"), StartSiteRadius = Read("startSiteRadius"), MinimumNearbyFood = Read("minimumNearbyFood"), MinimumNearbyWood = Read("minimumNearbyWood"), MinimumNearbyStone = Read("minimumNearbyStone"), MinimumNearbyFreshwater = Read("minimumNearbyFreshwater"), MinimumWalkableCount = Read("minimumWalkableCount"), MaximumAttempts = Read("maximumAttempts") }.Validate();
    }

    public static WorldGenerationConfiguration Parse(string json) => FromCanonicalJson(json);

    private static void Append(StringBuilder builder, string name, int value, bool last = false)
    {
        if (builder[^1] != '{') builder.Append(',');
        builder.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        if (last) return;
    }
}

/// <summary>Read-only validated world map with deterministic row-major and ID-ordered enumeration.</summary>
public sealed class WorldMap
{
    private readonly ReadOnlyCollection<WorldTile> _tiles;
    private readonly ReadOnlyCollection<ResourceNode> _resources;
    private readonly Dictionary<long, WorldTile> _tilesByIndex;
    private readonly Dictionary<TileCoordinate, ResourceNode[]> _resourcesByCoordinate;

    public WorldMap(WorldSeed originalSeed, int generationVersion, int generationAttempt, WorldGenerationConfiguration configuration,
        IEnumerable<WorldTile> tiles, IEnumerable<ResourceNode> resources, TileCoordinate startingSite)
    {
        ArgumentNullException.ThrowIfNull(tiles); ArgumentNullException.ThrowIfNull(resources); ArgumentNullException.ThrowIfNull(configuration);
        Configuration = configuration.Validate();
        if (generationVersion != WorldGenerationConfiguration.CurrentVersion) throw new NotSupportedException("Unknown world generation version.");
        if (generationAttempt < 0 || generationAttempt >= Configuration.MaximumAttempts) throw new ArgumentOutOfRangeException(nameof(generationAttempt));
        OriginalSeed = originalSeed; GenerationVersion = generationVersion; GenerationAttempt = generationAttempt; StartingSite = startingSite;
        var tileArray = tiles.ToArray();
        if (tileArray.Length != Configuration.Width * Configuration.Height) throw new ArgumentException("World tile count does not match configuration.", nameof(tiles));
        _tilesByIndex = new Dictionary<long, WorldTile>(tileArray.Length);
        foreach (var tile in tileArray)
        {
            var index = tile.Coordinate.ToIndex(Configuration.Width);
            ValidateTileSemantics(tile);
            if (tile.Coordinate.Y >= Configuration.Height || !_tilesByIndex.TryAdd(index, tile)) throw new ArgumentException("World tiles must have unique in-range coordinates.", nameof(tiles));
        }
        if (_tilesByIndex.Count != tileArray.Length || _tilesByIndex.Keys.Any((index) => index < 0 || index >= tileArray.Length)) throw new ArgumentException("World tile indexes are incomplete.", nameof(tiles));
        _tiles = Array.AsReadOnly(Enumerable.Range(0, tileArray.Length).Select(index => _tilesByIndex[index]).ToArray());
        var resourceArray = resources.ToArray();
        if (resourceArray.Select(static item => item.Id.Value).Distinct().Count() != resourceArray.Length) throw new ArgumentException("Resource node IDs must be unique.", nameof(resources));
        foreach (var resource in resourceArray)
        {
            var index = resource.Coordinate.ToIndex(Configuration.Width);
            if (!_tilesByIndex.TryGetValue(index, out var tile)) throw new ArgumentException("Resource coordinate is outside the world.", nameof(resources));
            ValidateResourceCompatibility(resource, tile, index);
        }
        _resources = Array.AsReadOnly(resourceArray.OrderBy(static item => item.Id.Value).ToArray());
        _resourcesByCoordinate = _resources.GroupBy(static item => item.Coordinate).ToDictionary(static group => group.Key, static group => group.ToArray());
        Validate();
        Fingerprint = ComputeFingerprint();
    }

    public WorldSeed OriginalSeed { get; }
    public WorldSeed Seed => OriginalSeed;
    public int GenerationVersion { get; }
    public int GenerationAttempt { get; }
    public int Attempt => GenerationAttempt;
    public int Width => Configuration.Width;
    public int Height => Configuration.Height;
    public WorldGenerationConfiguration Configuration { get; }
    public TileCoordinate StartingSite { get; }
    public TileCoordinate StartSite => StartingSite;
    public TileCoordinate StartingCoordinate => StartingSite;
    public IReadOnlyList<WorldTile> Tiles => _tiles;
    public IReadOnlyList<ResourceNode> Resources => _resources;
    public string Fingerprint { get; }
    public string CanonicalFingerprint => Fingerprint;

    public WorldTile GetTile(TileCoordinate coordinate) => GetTileAtIndex(coordinate.ToIndex(Width));
    public WorldTile GetTileAtIndex(long index) => index is >= 0 && index < int.MaxValue && _tilesByIndex.TryGetValue(index, out var tile) ? tile : throw new ArgumentOutOfRangeException(nameof(index));
    public WorldTile GetTile(int x, int y) => GetTile(new TileCoordinate(x, y));
    public IEnumerable<WorldTile> EnumerateTilesRowMajor() => _tiles;
    public IEnumerable<ResourceNode> EnumerateResourcesById() => _resources;
    public IReadOnlyList<ResourceNode> GetResources(TileCoordinate coordinate) => _resourcesByCoordinate.TryGetValue(coordinate, out var result) ? Array.AsReadOnly(result) : Array.Empty<ResourceNode>();
    public void Validate()
    {
        var start = GetTile(StartingSite);
        if (!start.Walkable || !start.Buildable || start.Terrain == TerrainType.Freshwater) throw new ArgumentException("Starting site is not viable.", nameof(StartingSite));
        var radius = Configuration.StartSiteRadius;
        var nearbyTiles = _tiles.Where(tile => Math.Abs(tile.Coordinate.X - StartingSite.X) <= radius && Math.Abs(tile.Coordinate.Y - StartingSite.Y) <= radius).ToArray();
        var nearbyResources = _resources.Where(node => Math.Abs(node.Coordinate.X - StartingSite.X) <= radius && Math.Abs(node.Coordinate.Y - StartingSite.Y) <= radius).ToArray();
        if (nearbyResources.Where(node => node.Type == ResourceType.Food).Sum(static node => node.MaximumQuantity) < Configuration.MinimumNearbyFood || nearbyResources.Where(node => node.Type == ResourceType.Wood).Sum(static node => node.MaximumQuantity) < Configuration.MinimumNearbyWood || nearbyResources.Where(node => node.Type == ResourceType.Stone).Sum(static node => node.MaximumQuantity) < Configuration.MinimumNearbyStone || nearbyTiles.Count(tile => tile.Terrain == TerrainType.Freshwater) < Configuration.MinimumNearbyFreshwater || nearbyTiles.Count(static tile => tile.Walkable) < Configuration.MinimumWalkableCount)
            throw new ArgumentException("Starting site does not satisfy configured viability requirements.", nameof(StartingSite));
    }

    public string ComputeFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) { var bytes = Encoding.UTF8.GetBytes(value); hash.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":")); hash.AppendData(bytes); }
        Add(OriginalSeed.Value.ToString(CultureInfo.InvariantCulture)); Add(GenerationVersion.ToString(CultureInfo.InvariantCulture)); Add(GenerationAttempt.ToString(CultureInfo.InvariantCulture)); Add(Configuration.ToCanonicalJson());
        foreach (var tile in _tiles) Add($"{tile.Coordinate.X},{tile.Coordinate.Y},{(int)tile.Terrain},{tile.Elevation},{tile.Fertility},{tile.WaterAccess},{(tile.Walkable ? 1 : 0)},{tile.MovementCost}");
        foreach (var node in _resources) Add($"{node.Id.Value},{node.Coordinate.X},{node.Coordinate.Y},{(int)node.Type},{node.InitialQuantity},{node.MaximumQuantity},{node.RegenerationPotential}");
        Add($"{StartingSite.X},{StartingSite.Y}");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public override string ToString() => Fingerprint;

    private static void ValidateTileSemantics(WorldTile tile)
    {
        var expectedWalkable = tile.Terrain is not (TerrainType.Freshwater or TerrainType.RockyGround);
        var expectedMovementCost = tile.Terrain switch
        {
            TerrainType.Grassland => 1,
            TerrainType.Forest => 2,
            TerrainType.DenseWilderness => 3,
            _ => 0
        };
        if (tile.Walkable != expectedWalkable || tile.MovementCost != expectedMovementCost)
            throw new ArgumentException("Tile walkability and movement cost do not match its terrain.", nameof(tile));
    }

    private void ValidateResourceCompatibility(ResourceNode resource, WorldTile tile, long index)
    {
        if (tile.Terrain == TerrainType.Freshwater)
            throw new ArgumentException("Resources cannot be placed on freshwater tiles.", nameof(resource));
        var expectedId = checked((index * 4) + (int)resource.Type);
        if (resource.Id.Value != expectedId)
            throw new ArgumentException("Resource node ID must be derived from tile index and resource type.", nameof(resource));
        var suitable = resource.Type switch
        {
            ResourceType.Food => tile.Fertility >= Configuration.FoodPlacementThreshold && tile.WaterAccess >= 2600 && HasFreshwaterNeighbor(tile.Coordinate),
            ResourceType.Wood => (tile.Terrain is TerrainType.Forest or TerrainType.DenseWilderness) && tile.Fertility >= Configuration.WoodPlacementThreshold,
            ResourceType.Stone => tile.Terrain == TerrainType.RockyGround || tile.Elevation >= Configuration.StonePlacementThreshold,
            _ => false
        };
        if (!suitable) throw new ArgumentException("Resource node ecology is incompatible with its tile.", nameof(resource));
    }

    private bool HasFreshwaterNeighbor(TileCoordinate coordinate)
    {
        for (var y = Math.Max(0, coordinate.Y - 1); y <= Math.Min(Height - 1, coordinate.Y + 1); y++)
        for (var x = Math.Max(0, coordinate.X - 1); x <= Math.Min(Width - 1, coordinate.X + 1); x++)
            if (_tilesByIndex[(y * Width) + x].Terrain == TerrainType.Freshwater) return true;
        return false;
    }
}

public sealed class WorldGenerationException : InvalidOperationException
{
    public WorldGenerationException(string message) : base(message) { }
    public WorldGenerationException(string message, Exception? innerException) : base(message, innerException) { }
}
