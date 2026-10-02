using System.Reflection;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M17LocalHousingShuffleTests
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new() { WriteIndented = true };
    [Fact]
    public async Task ForeignOnlyShelterKeepsDonorHomeAndBlocksBirth()
    {
        var fixture = CreateFixture(false);
        var engine = fixture.Engine;
        var before = engine.CreatePersistenceSnapshot();
        RunFamilyCheck(engine);
        var after = engine.CreatePersistenceSnapshot();
        WriteEvidence(before, after, fixture);
        Assert.Equal(4, engine.LivingPopulation);
        Assert.All(engine.Citizens.Where(x => x.IsAlive), x => Assert.Equal(fixture.SharedHome, x.HomeStructureId));
        Assert.Equal(fixture.SharedHome, engine.Households.Single(x => x.Id == fixture.Donor).DwellingStructureId);
        Assert.All(after.MigrationState!.CitizenResidences.Where(x => after.Citizens.Any(c => c.IsAlive && c.Id.Value == x.EntityId)), x => Assert.Equal(2, x.SettlementId));
        await AssertCheckpointContinuation(engine, before, after);
    }

    [Fact]
    public async Task SameSiteShelterAllowsBirthAndCheckpointReopenContinuation()
    {
        var fixture = CreateFixture(true);
        var engine = fixture.Engine;
        var before = engine.CreatePersistenceSnapshot();
        RunFamilyCheck(engine);
        var after = engine.CreatePersistenceSnapshot();
        Assert.Equal(5, engine.LivingPopulation);
        Assert.All(after.Citizens.Where(x => x.IsAlive && x.HouseholdId == fixture.Donor), x => Assert.Equal(fixture.EmptyHome, x.HomeStructureId));
        Assert.All(after.Citizens.Where(x => x.IsAlive && x.HouseholdId == fixture.Parent), x => Assert.Equal(fixture.SharedHome, x.HomeStructureId));
        await AssertCheckpointContinuation(engine, before, after);
    }

    private static async Task AssertCheckpointContinuation(SimulationEngine engine, SimulationPersistenceSnapshot before, SimulationPersistenceSnapshot after)
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-local-shuffle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            await using (var db = await WorldDatabase.OpenAsync(path)) await db.CreateCheckpointStore().CheckpointAsync(before);
            await using (var db = await WorldDatabase.OpenAsync(path)) await db.CreateCheckpointStore().CheckpointAsync(after);
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                var actual = new SimulationEngine(await db.CreateCheckpointStore().LoadAsync());
                var end = engine.CurrentMinute.Add(1440);
                engine.AdvanceUntil(end);
                for (var minute = actual.CurrentMinute.Value + 37; minute < end.Value; minute += 37) actual.AdvanceUntil(new(minute));
                actual.AdvanceUntil(end);
                var a = engine.CreatePersistenceSnapshot();
                var b = actual.CreatePersistenceSnapshot();
                Assert.Equal(a.Citizens, b.Citizens);
                Assert.Equal(a.Households.Select(x => (x.Id, x.CreatedMinute, x.DissolvedMinute, x.DwellingStructureId)),
                    b.Households.Select(x => (x.Id, x.CreatedMinute, x.DissolvedMinute, x.DwellingStructureId)));
                Assert.Equal(a.ScheduledEvents, b.ScheduledEvents);
                Assert.Equal(a.MigrationStateJson, b.MigrationStateJson);
                Assert.Equal(a.LivingStateJson, b.LivingStateJson);
                Assert.Equal(a.Economy!.ToUnifiedCanonicalJson(), b.Economy!.ToUnifiedCanonicalJson());
                Assert.Equal(engine.HistoryFingerprint, actual.HistoryFingerprint);
                Assert.Equal(engine.SurvivalFingerprint, actual.SurvivalFingerprint);
                await db.CreateCheckpointStore().CheckpointAsync(b);
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    public sealed record ShuffleFixture(SimulationEngine Engine, HouseholdId Parent, HouseholdId Donor, StructureId SharedHome, StructureId EmptyHome);

    public static ShuffleFixture CreateFixture(bool sameSite)
    {
        var baseline = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        var householdId = baseline.CounterSnapshot.NextEntityId;
        var seed = Enumerable.Range(1, 100000).First(x => new DeterministicRandom(new WorldSeed((ulong)x)).NextUInt64(RandomDomain.Reproduction,
            (ulong)householdId, 0, 0x4249525448UL) % 10000 < 20);
        var engine = new SimulationEngine(new WorldSeed((ulong)seed), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        var people = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var adults = people.Values.Where(x => x.AgeYears(engine.CurrentMinute) is >= 18 and <= 38).OrderBy(x => x.Id.Value).Take(4).ToArray();
        Assert.Equal(4, adults.Length);
        foreach (var citizen in people.Values.Where(x => !adults.Contains(x)).ToArray()) Invoke(engine, "KillNatural", citizen);
        foreach (var (first, second) in new[] { (adults[0], adults[1]), (adults[2], adults[3]) })
        {
            Invoke(engine, "SetRelationship", first.Id, second.Id, 10000, 10000, 10000, 0);
            Invoke(engine, "TryFormPartnership", first, second, engine.Relationships.Single(x => x.CitizenAId == first.Id && x.CitizenBId == second.Id));
            Assert.Equal(second.Id, first.PartnerId);
            Invoke(engine, "RecordPartnershipHistory", first, second);
        }
        var households = Field<Dictionary<long, Household>>(engine, "_households");
        var parent = households[adults[0].HouseholdId!.Value.Value];
        var donor = households[adults[2].HouseholdId!.Value.Value];
        Assert.True((int)Invoke(engine, "CalculateBirthDraw", parent)! < 20);
        var daughterSite = engine.World.Tiles.First(x => x.Walkable && x.Buildable && x.Coordinate != engine.World.StartingSite && engine.World.GetResources(x.Coordinate).Count == 0).Coordinate;
        var old = Field<MigrationWorldState>(engine, "_migrationState");
        var state = new MigrationWorldState(old.Version,
            old.CitizenResidences.Select(x => adults.Any(c => c.Id.Value == x.EntityId) ? x with { SettlementId = 2 } : x).ToArray(),
            old.HouseholdResidences.Select(x => x.EntityId == parent.Id.Value || x.EntityId == donor.Id.Value ? x with { SettlementId = 2 } : x).ToArray(),
            old.StructureOwners, old.FacilityOwners, old.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(200, 0, 0, 4000, 0, 0), Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()),
            old.InTransitParties, old.FoundingPressure, old.LastRelocations, old.LastVisitAttemptYear, old.Roads);
        engine.Settlement.FoodStored -= 200;
        SetField(engine, "_migrationState", state);
        Invoke(engine, "InitializeMigrationRuntime");
        foreach (var citizen in adults) { citizen.Location = daughterSite; citizen.Needs = new(); }
        adults[2].Health = adults[3].Health = 6000;
        var structures = Field<Dictionary<long, Structure>>(engine, "_structures");
        var occupied = structures.Values.Select(x => x.Location).Concat(engine.World.Resources.Select(x => x.Coordinate)).ToHashSet();
        var sites = engine.World.Tiles.Where(x => x.Buildable && x.Walkable && x.Coordinate != engine.World.StartingSite && !occupied.Contains(x.Coordinate)).Take(2).Select(x => x.Coordinate).ToArray();
        var counters = Field<DeterministicCounters>(engine, "_counters");
        Structure AddShelter(TileCoordinate location)
        {
            var structure = new Structure(counters.AllocateStructureId(), StructureType.Shelter, location, 0,
                StructureDefinitions.ShelterRequiredWood, StructureDefinitions.ShelterRequiredStone, StructureDefinitions.ShelterRequiredWork)
            {
                Status = StructureStatus.Complete, CompletedMinute = 0,
                DeliveredWood = StructureDefinitions.ShelterRequiredWood, DeliveredStone = StructureDefinitions.ShelterRequiredStone,
                CompletedWork = StructureDefinitions.ShelterRequiredWork
            };
            structures.Add(structure.Id.Value, structure);
            Field<Dictionary<(long, long), StructureContribution>>(engine, "_structureContributions").Add((structure.Id.Value, adults[0].Id.Value),
                new(structure.Id, adults[0].Id, structure.CompletedWork, structure.DeliveredWood, structure.DeliveredStone));
            var produced = Field<Goods>(engine, "_producedGoods");
            SetField(engine, "_producedGoods", produced.Plus(new Goods(Wood: structure.DeliveredWood, Stone: structure.DeliveredStone)));
            return structure;
        }
        var a = AddShelter(sites[0]); var b = AddShelter(sites[1]);
        state = Field<MigrationWorldState>(engine, "_migrationState");
        SetField(engine, "_migrationState", new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners.Append(new(a.Id.Value, 2)).Append(new(b.Id.Value, sameSite ? 2 : 1)).ToArray(),
            state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement, state.InTransitParties, state.FoundingPressure,
            state.LastRelocations, state.LastVisitAttemptYear, state.Roads));
        foreach (var citizen in adults) citizen.HomeStructureId = a.Id;
        parent.DwellingStructureId = donor.DwellingStructureId = a.Id;
        Invoke(engine, "SynchronizeLivingPeople");
        Invoke(engine, "ReconcileEconomicHouseholds");
        _ = engine.CreatePersistenceSnapshot();
        return new(engine, parent.Id, donor.Id, a.Id, b.Id);
    }

    private static void RunFamilyCheck(SimulationEngine engine)
    {
        Invoke(engine, "RunFamilyCheck");
        Invoke(engine, "SynchronizeLivingPeople");
    }

    private static void WriteEvidence(SimulationPersistenceSnapshot before, SimulationPersistenceSnapshot after, ShuffleFixture fixture)
    {
        var directory = Environment.GetEnvironmentVariable("LITTLEAGES_SHUFFLE_EVIDENCE");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new
        {
            seed = before.Seed.Value, before.WorldMinute, fixture.Parent, fixture.Donor, fixture.SharedHome, fixture.EmptyHome,
            beforePopulation = before.Citizens.Count(x => x.IsAlive), afterPopulation = after.Citizens.Count(x => x.IsAlive),
            beforeHomes = before.Citizens.Where(x => x.IsAlive).Select(x => new { x.Id, x.HouseholdId, x.HomeStructureId }),
            afterHomes = after.Citizens.Where(x => x.IsAlive).Select(x => new { x.Id, x.HouseholdId, x.HomeStructureId }),
            ownership = after.MigrationState
        }, EvidenceJsonOptions));
    }
    private static T Field<T>(SimulationEngine engine, string name) => (T)typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
    private static void SetField(SimulationEngine engine, string name, object value) => typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, value);
    private static object? Invoke(SimulationEngine engine, string name, params object[] args) => typeof(SimulationEngine).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic, null, args.Select(x => x.GetType()).ToArray(), null)!.Invoke(engine, args);
}
