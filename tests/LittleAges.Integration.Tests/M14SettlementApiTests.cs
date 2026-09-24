using System.Reflection;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class M14SettlementApiTests
{
    [Fact]
    public async Task MigrationSettlementRoutesUseOwnedSnapshotsAndKeepTheSingularRouteAtSiteOne()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var checkpoint = await WriteMigrationCheckpointWithDaughterAsync(Path.Combine(dataRoot, "integration-world.db"));
            using var factory = new ServerFactory(dataRoot);
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();

            using var listResponse = await client.GetAsync("/api/v1/settlements");
            listResponse.EnsureSuccessStatusCode();
            using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var sites = list.RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, sites.Length);
            Assert.Equal("1", sites[0].GetProperty("settlementId").GetString());
            Assert.Equal("2", sites[1].GetProperty("settlementId").GetString());

            var owners = checkpoint.MigrationState!.CitizenResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
            var expectedLivingBySite = checkpoint.Citizens
                .Where(static citizen => citizen.IsAlive)
                .GroupBy(citizen => owners[citizen.Id.Value])
                .ToDictionary(group => group.Key, group => group.Count());
            Assert.Equal(expectedLivingBySite[1], sites[0].GetProperty("livingPopulation").GetInt32());
            Assert.Equal(expectedLivingBySite[2], sites[1].GetProperty("livingPopulation").GetInt32());
            Assert.Equal(checkpoint.MigrationState.DaughterSettlement!.Site.X, sites[1].GetProperty("site").GetProperty("x").GetInt32());
            Assert.Equal(checkpoint.MigrationState.DaughterSettlement.Site.Y, sites[1].GetProperty("site").GetProperty("y").GetInt32());
            Assert.Equal(37, sites[1].GetProperty("foodStored").GetInt32());
            Assert.Equal(8, sites[1].GetProperty("woodStored").GetInt32());
            Assert.Equal(5, sites[1].GetProperty("stoneStored").GetInt32());

            using var detailResponse = await client.GetAsync("/api/v1/settlements/2");
            detailResponse.EnsureSuccessStatusCode();
            using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
            Assert.Equal("2", detail.RootElement.GetProperty("settlementId").GetString());
            Assert.Equal(sites[1].GetProperty("site").GetRawText(), detail.RootElement.GetProperty("site").GetRawText());
            Assert.Equal(expectedLivingBySite[2], detail.RootElement.GetProperty("livingPopulation").GetInt32());
            var migrationProjection = MigrationValidation.CreateReadSnapshot(checkpoint);
            var expectedResources = ExpectedMigrationResources(checkpoint, migrationProjection);
            Assert.NotEmpty(expectedResources.BySettlement[1]);
            Assert.NotEmpty(expectedResources.BySettlement[2]);
            Assert.NotEmpty(expectedResources.UnreachableNodeIds);
            var projectedSiteTwo = migrationProjection.Settlements.Single(static site => site.Id == 2);
            var expectedSiteTwoStorageUsed = ExpectedMigrationStorageUsed(checkpoint, projectedSiteTwo);
            Assert.True(expectedSiteTwoStorageUsed > projectedSiteTwo.CommunalStock.StorageUsed);
            Assert.Equal(expectedSiteTwoStorageUsed, detail.RootElement.GetProperty("storageUsed").GetInt32());
            var siteTwoStructures = checkpoint.MigrationState.StructureOwners
                .Where(x => x.SettlementId == 2)
                .Select(x => x.EntityId)
                .ToHashSet();
            Assert.Equal(checkpoint.Structures.Count(structure => siteTwoStructures.Contains(structure.Id.Value) && structure.Status == StructureStatus.Complete && structure.Type == StructureType.Shelter),
                detail.RootElement.GetProperty("completedShelters").GetInt32());
            Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("activeConstructionProject").ValueKind);

            var originalConstruction = Assert.Single(checkpoint.Structures, static structure => structure.Status == StructureStatus.UnderConstruction);
            Assert.Equal(1, checkpoint.MigrationState.StructureOwners.Single(owner => owner.EntityId == originalConstruction.Id.Value).SettlementId);
            using var originalDetailResponse = await client.GetAsync("/api/v1/settlements/1");
            originalDetailResponse.EnsureSuccessStatusCode();
            using var originalDetail = JsonDocument.Parse(await originalDetailResponse.Content.ReadAsStringAsync());
            Assert.Equal(originalConstruction.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                originalDetail.RootElement.GetProperty("activeConstructionProject").GetProperty("structureId").GetString());

            var siteOneResources = ReadResourceSummary(originalDetail.RootElement);
            var siteTwoResources = ReadResourceSummary(detail.RootElement);
            var worldResourceNodes = checkpoint.World!.Resources.ToDictionary(static node => node.Id.Value);
            Assert.Equal(expectedResources.BySettlement[1].Select(static state => state.ResourceNodeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), siteOneResources.NodeIds);
            Assert.Equal(expectedResources.BySettlement[2].Select(static state => state.ResourceNodeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), siteTwoResources.NodeIds);
            Assert.Empty(siteOneResources.NodeIds.Intersect(siteTwoResources.NodeIds));
            Assert.Equal(expectedResources.UnreachableNodeIds.Count, checkpoint.ResourceStates.Count(state =>
                !siteOneResources.NodeIds.Contains(state.ResourceNodeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
                !siteTwoResources.NodeIds.Contains(state.ResourceNodeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            Assert.Equal(expectedResources.BySettlement.Values.SelectMany(static states => states).Sum(static state => state.CurrentQuantity),
                siteOneResources.TotalQuantity + siteTwoResources.TotalQuantity);
            Assert.Equal(expectedResources.BySettlement[1].GroupBy(state => worldResourceNodes[state.ResourceNodeId.Value].Type)
                    .ToDictionary(static group => (int)group.Key, static group => group.Sum(state => state.CurrentQuantity))
                    .OrderBy(static item => item.Key),
                siteOneResources.RemainingByType.OrderBy(static item => item.Key));
            Assert.Equal(expectedResources.BySettlement[2].GroupBy(state => worldResourceNodes[state.ResourceNodeId.Value].Type)
                    .ToDictionary(static group => (int)group.Key, static group => group.Sum(state => state.CurrentQuantity))
                    .OrderBy(static item => item.Key),
                siteTwoResources.RemainingByType.OrderBy(static item => item.Key));

            using var singularResponse = await client.GetAsync("/api/v1/settlement");
            singularResponse.EnsureSuccessStatusCode();
            using var singular = JsonDocument.Parse(await singularResponse.Content.ReadAsStringAsync());
            Assert.Equal(expectedLivingBySite[1], singular.RootElement.GetProperty("livingPopulation").GetInt32());
            Assert.Equal(sites[0].GetProperty("foodStored").GetInt32(), singular.RootElement.GetProperty("foodStored").GetInt32());
            Assert.False(singular.RootElement.TryGetProperty("settlementId", out _));
            Assert.False(singular.RootElement.TryGetProperty("site", out _));
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task PreMigrationWorldListsItsOriginalSiteAndReturnsNotFoundForSettlementTwo()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
            var checkpoint = engine.CreatePersistenceSnapshot();
            await WriteCheckpointAsync(Path.Combine(dataRoot, "integration-world.db"), checkpoint);

            using var factory = new ServerFactory(dataRoot);
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();

            using var listResponse = await client.GetAsync("/api/v1/settlements");
            listResponse.EnsureSuccessStatusCode();
            using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var site = Assert.Single(list.RootElement.EnumerateArray());
            Assert.Equal("1", site.GetProperty("settlementId").GetString());
            Assert.Equal(checkpoint.World!.StartingSite.X, site.GetProperty("site").GetProperty("x").GetInt32());
            Assert.Equal(checkpoint.World.StartingSite.Y, site.GetProperty("site").GetProperty("y").GetInt32());

            using var detailResponse = await client.GetAsync("/api/v1/settlements/1");
            detailResponse.EnsureSuccessStatusCode();
            using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
            Assert.Equal("1", detail.RootElement.GetProperty("settlementId").GetString());
            Assert.Equal(site.GetProperty("site").GetRawText(), detail.RootElement.GetProperty("site").GetRawText());

            using var missing = await client.GetAsync("/api/v1/settlements/2");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);

            using var singularResponse = await client.GetAsync("/api/v1/settlement");
            singularResponse.EnsureSuccessStatusCode();
            using var singular = JsonDocument.Parse(await singularResponse.Content.ReadAsStringAsync());
            Assert.Equal(site.GetProperty("livingPopulation").GetInt32(), singular.RootElement.GetProperty("livingPopulation").GetInt32());
            Assert.Equal(site.GetProperty("foodStored").GetInt32(), singular.RootElement.GetProperty("foodStored").GetInt32());
            Assert.False(singular.RootElement.TryGetProperty("settlementId", out _));
            Assert.False(singular.RootElement.TryGetProperty("site", out _));
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task ConfiguredServerCanCreateAnExplicitM14WorldAndDefaultTracksCurrentRules()
    {
        var defaults = ServerOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, defaults.NewWorldRules);
        Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, defaults.NewWorldRules);

        var dataRoot = CreateDataRoot();
        try
        {
            using var factory = new ServerFactory(dataRoot, SimulationEngine.MigrationSimulationRulesVersion);
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();

            var engine = Assert.IsType<SimulationEngine>(typeof(SimulationHost)
                .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(host));
            Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, engine.SimulationRulesVersion);
            Assert.Single(engine.CreateMigrationReadSnapshot().Settlements);

            using var listResponse = await client.GetAsync("/api/v1/settlements");
            listResponse.EnsureSuccessStatusCode();
            using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            Assert.Single(list.RootElement.EnumerateArray());
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task MigrationTravelCostMapsAreReusedAndInvalidatedWhenSitesOrWorldChange()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            using var factory = new ServerFactory(dataRoot, SimulationEngine.MigrationSimulationRulesVersion);
            using var client = factory.CreateClient();
            var host = factory.Services.GetRequiredService<SimulationHost>();
            await host.WaitForRunningForTestingAsync();

            var engine = Assert.IsType<SimulationEngine>(typeof(SimulationHost)
                .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(host));
            var migration = engine.CreateMigrationReadSnapshot();
            var initialComputationCount = host.MigrationTravelCostMapComputationsForTesting;
            Assert.Equal(migration.Settlements.Count, initialComputationCount);

            await host.AdvanceForTestingAsync(0);
            Assert.Equal(initialComputationCount, host.MigrationTravelCostMapComputationsForTesting);

            var site = Assert.Single(migration.Settlements);
            var movedCoordinate = FindAlternativeWalkableSite(engine.World, site.Site);
            var movedSite = new MigrationSettlementReadSnapshot(site.Id, movedCoordinate, site.CommunalStock,
                site.LivingGoods, site.StorageCapacity, site.CitizenIds, site.HouseholdIds, site.StructureIds,
                site.FarmStructureIds, site.FacilityIds, site.WorkOrderIds);
            var movedMigration = new MigrationReadSnapshot(migration.RulesVersion, migration.Seed,
                migration.WorldMinute, migration.WorldFingerprint, [movedSite], migration.InTransitParties);
            host.EnsureMigrationTravelCostMapsForTesting(engine.World, movedMigration);
            Assert.Equal(initialComputationCount + migration.Settlements.Count, host.MigrationTravelCostMapComputationsForTesting);

            var changedWorldEngine = new SimulationEngine(new WorldSeed(43),
                simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
            Assert.NotEqual(engine.World.Fingerprint, changedWorldEngine.World.Fingerprint);
            host.EnsureMigrationTravelCostMapsForTesting(changedWorldEngine.World, changedWorldEngine.CreateMigrationReadSnapshot());
            Assert.Equal(initialComputationCount + migration.Settlements.Count + 1,
                host.MigrationTravelCostMapComputationsForTesting);
        }
        finally
        {
            CleanupDataRoot(dataRoot);
        }
    }

    private static async Task<SimulationPersistenceSnapshot> WriteMigrationCheckpointWithDaughterAsync(string path)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));
        var initial = engine.CreatePersistenceSnapshot();
        var state = initial.MigrationState ?? throw new InvalidDataException("The M14 fixture did not produce migration state.");
        var household = initial.Households.FirstOrDefault(candidate =>
            candidate.DissolvedMinute is null && candidate.DwellingStructureId is null &&
            initial.Citizens.Where(citizen => citizen.HouseholdId == candidate.Id).All(static citizen => citizen.HomeStructureId is null));
        long[] movedCitizenIds = household is null
            ? [initial.Citizens.First(citizen => citizen.HouseholdId is null && citizen.HomeStructureId is null).Id.Value]
            : initial.Citizens.Where(citizen => citizen.HouseholdId == household.Id).Select(static citizen => citizen.Id.Value).ToArray();
        var movedCitizenSet = movedCitizenIds.ToHashSet();
        var movedHouseholdId = household?.Id.Value;
        var site = FindWalkableSite(engine.World);
        var daughter = new MigrationDaughterSettlementState(site,
            new MigrationSettlementStockState(37, 8, 5, 200, 0, 0),
            Enum.GetValues<LivingGood>().Select(good => new LivingStock(good, good == LivingGood.Tool ? 13 : 0)).ToArray());
        var migration = new MigrationWorldState(
            state.Version,
            state.CitizenResidences.Select(residence => movedCitizenSet.Contains(residence.EntityId) ? residence with { SettlementId = MigrationDaughterSettlementState.SettlementId } : residence).ToArray(),
            state.HouseholdResidences.Select(residence => movedHouseholdId == residence.EntityId ? residence with { SettlementId = MigrationDaughterSettlementState.SettlementId } : residence).ToArray(),
            state.StructureOwners,
            state.FacilityOwners,
            state.WorkOrderOwners,
            daughter,
            state.InTransitParties);
        typeof(SimulationEngine).GetField("_migrationState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, migration);
        if (engine.Settlement.FoodStored < daughter.CommunalStock.FoodStored ||
            engine.Settlement.WoodStored < daughter.CommunalStock.WoodStored ||
            engine.Settlement.StoneStored < daughter.CommunalStock.StoneStored)
            throw new InvalidDataException("The M14 API fixture does not have enough original communal stock for its daughter-site transfer.");
        engine.Settlement.FoodStored -= daughter.CommunalStock.FoodStored;
        engine.Settlement.WoodStored -= daughter.CommunalStock.WoodStored;
        engine.Settlement.StoneStored -= daughter.CommunalStock.StoneStored;
        var checkpoint = engine.CreatePersistenceSnapshot();
        Assert.Contains(checkpoint.Structures, static structure => structure.Status == StructureStatus.UnderConstruction);
        await WriteCheckpointAsync(path, checkpoint);
        return checkpoint;
    }

    private static int ExpectedMigrationStorageUsed(SimulationPersistenceSnapshot checkpoint, MigrationSettlementReadSnapshot site)
    {
        var householdIds = site.HouseholdIds.ToHashSet();
        long stored = checked((long)site.CommunalStock.StorageUsed + site.LivingGoods.Sum(static item => (long)item.Quantity));
        if (checkpoint.Economy is { } economy)
        {
            stored = checked(stored + economy.Households.Where(item => householdIds.Contains(item.HouseholdId)).Sum(static item => item.Holdings.Total));
            foreach (var trade in economy.Trades.Where(static item => item.Status == BarterStatus.Reserved))
            {
                if (trade.DeliveredA && householdIds.Contains(trade.HouseholdA)) stored = checked(stored + trade.QuantityA);
                if (trade.DeliveredB && householdIds.Contains(trade.HouseholdB)) stored = checked(stored + trade.QuantityB);
            }
            stored = checked(stored + economy.PublicWork.Where(item => householdIds.Contains(item.HouseholdId)).Sum(static item => (long)item.Food));
        }
        return checked((int)stored);
    }

    private static ExpectedMigrationResourceSummary ExpectedMigrationResources(
        SimulationPersistenceSnapshot checkpoint,
        MigrationReadSnapshot migration)
    {
        var world = checkpoint.World ?? throw new InvalidDataException("The M14 fixture is missing its world.");
        var settlements = migration.Settlements.OrderBy(static site => site.Id).ToArray();
        var travelCostsBySettlement = settlements.ToDictionary(
            static site => site.Id,
            site => DeterministicPathfinder.ComputeTravelCosts(world, site.Site));
        var resourceNodes = world.Resources.ToDictionary(static node => node.Id.Value);
        var bySettlement = settlements.ToDictionary(static site => site.Id, static _ => new List<ResourceState>());
        var unreachable = new List<long>();

        foreach (var resource in checkpoint.ResourceStates.OrderBy(static state => state.ResourceNodeId.Value))
        {
            var node = resourceNodes[resource.ResourceNodeId.Value];
            var nearestSettlement = settlements
                .Select(site => (SettlementId: site.Id, Reachable: travelCostsBySettlement[site.Id].TryGetValue(node.Coordinate, out var cost), TravelCost: cost))
                .Where(static candidate => candidate.Reachable)
                .OrderBy(static candidate => candidate.TravelCost)
                .ThenBy(static candidate => candidate.SettlementId)
                .FirstOrDefault();
            if (!nearestSettlement.Reachable)
            {
                unreachable.Add(resource.ResourceNodeId.Value);
                continue;
            }
            bySettlement[nearestSettlement.SettlementId].Add(resource);
        }

        return new ExpectedMigrationResourceSummary(
            bySettlement.ToDictionary(static entry => entry.Key, static entry => (IReadOnlyList<ResourceState>)entry.Value.AsReadOnly()),
            unreachable.AsReadOnly());
    }

    private static ObservedResourceSummary ReadResourceSummary(JsonElement settlement)
    {
        var resources = settlement.GetProperty("resources").EnumerateArray().ToArray();
        var nodeIds = resources.Select(static resource => resource.GetProperty("resourceNodeId").GetString()!).ToArray();
        var totalQuantity = resources.Sum(static resource => resource.GetProperty("currentQuantity").GetInt32());
        var remainingByType = settlement.GetProperty("remainingResources").EnumerateArray()
            .ToDictionary(static resource => (int)Enum.Parse<ResourceType>(resource.GetProperty("resourceType").GetString()!), static resource => resource.GetProperty("quantity").GetInt32());
        return new ObservedResourceSummary(nodeIds, totalQuantity, remainingByType);
    }

    private static TileCoordinate FindWalkableSite(WorldMap world)
    {
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var coordinate = new TileCoordinate(x, y);
            if (coordinate != world.StartingSite && world.GetTile(coordinate).Walkable) return coordinate;
        }
        throw new InvalidOperationException("The M14 fixture world has no second walkable site.");
    }

    private static TileCoordinate FindAlternativeWalkableSite(WorldMap world, TileCoordinate excluded)
    {
        foreach (var tile in world.EnumerateTilesRowMajor())
            if (tile.Walkable && tile.Coordinate != excluded) return tile.Coordinate;
        throw new InvalidOperationException("The M14 cache fixture world has no alternative walkable site.");
    }

    private static async Task WriteCheckpointAsync(string path, SimulationPersistenceSnapshot checkpoint)
    {
        await using var database = await WorldDatabase.OpenAsync(path);
        await database.CreateCheckpointStore().CheckpointAsync(checkpoint, DateTime.UtcNow);
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "little-ages-m14-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDataRoot(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed record ExpectedMigrationResourceSummary(
        IReadOnlyDictionary<long, IReadOnlyList<ResourceState>> BySettlement,
        IReadOnlyList<long> UnreachableNodeIds);

    private sealed record ObservedResourceSummary(string[] NodeIds, int TotalQuantity, IReadOnlyDictionary<int, int> RemainingByType);

    private sealed class ServerFactory(string dataRoot, string? newWorldRules = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DataRoot", dataRoot);
            builder.UseSetting("ActiveWorld", "integration-world");
            builder.UseSetting("WorldSeed", "42");
            builder.UseSetting("ListenUrls", "http://127.0.0.1:0");
            builder.UseSetting("SimulationMinutesPerSecond", "0");
            if (newWorldRules is not null) builder.UseSetting("NewWorldRules", newWorldRules);
        }
    }
}
