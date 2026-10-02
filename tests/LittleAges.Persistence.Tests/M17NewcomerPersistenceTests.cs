using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M17NewcomerPersistenceTests
{
    private static readonly Lazy<SimulationPersistenceSnapshot> Village = new(() =>
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        engine.AdvanceUntil(new(30L * WorldCalendar.MinutesPerDay));
        var location = (TileCoordinate)Invoke(engine, "SelectConstructionSite", 1L, StructureType.Shelter)!;
        Invoke(engine, "CreateStructure", StructureType.Shelter, location);
        engine.AdvanceUntil(new(60L * WorldCalendar.MinutesPerDay));
        return engine.CreatePersistenceSnapshot();
    });

    [Fact]
    public async Task GuestPhasesCheckpointReopenAndContinueWithIdenticalAccountsAndEvents()
    {
        var engine = new SimulationEngine(Village.Value);
        var create = typeof(SimulationEngine).GetMethod("TryCreateNewcomer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)create.Invoke(engine, [1L])!, $"Residents {engine.LivingPopulation}; shelters {engine.Structures.Count(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete)}; edge tiles {engine.World.Tiles.Count(x => x.Walkable && (x.Coordinate.X == 0 || x.Coordinate.Y == 0 || x.Coordinate.X == engine.World.Width - 1 || x.Coordinate.Y == engine.World.Height - 1))}; occupancy {string.Join(',', engine.Citizens.Where(x => x.IsAlive && x.HomeStructureId is not null).GroupBy(x => x.HomeStructureId).Select(x => x.Count()))}.");
        var start = engine.CurrentMinute.Value;
        var checkpoints = new List<SimulationPersistenceSnapshot> { engine.CreatePersistenceSnapshot() };
        var phases = new HashSet<NewcomerPhase> { NewcomerPhase.Approaching };
        for (var elapsed = 60; elapsed <= 10 * WorldCalendar.MinutesPerDay; elapsed += 60)
        {
            engine.AdvanceUntil(new(start + elapsed));
            var phase = LivingWorldCodec.Deserialize(engine.LivingStateJson!).Newcomers!.Visitors.Single().Phase;
            if (phases.Add(phase)) checkpoints.Add(engine.CreatePersistenceSnapshot());
        }
        Assert.Contains(NewcomerPhase.Visiting, phases);
        Assert.Contains(phases, x => x is NewcomerPhase.Resident or NewcomerPhase.Departed or NewcomerPhase.Dead);
        var end = new WorldMinute(start + 12L * WorldCalendar.MinutesPerDay);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-checkpoints-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < checkpoints.Count; i++)
            {
                var expected = new SimulationEngine(checkpoints[i]);
                expected.AdvanceUntil(end);
                var path = Path.Combine(root, i + ".db");
                await using (var db = await WorldDatabase.OpenAsync(path))
                    await db.CreateCheckpointStore().CheckpointAsync(checkpoints[i]);
                await using (var db = await WorldDatabase.OpenAsync(path))
                {
                    var loaded = await db.CreateCheckpointStore().LoadAsync();
                    Assert.Equal(checkpoints[i].LivingStateJson, loaded.LivingStateJson);
                    var actual = new SimulationEngine(loaded);
                    actual.AdvanceUntil(end);
                    Assert.Equal(expected.LivingStateJson, actual.LivingStateJson);
                    Assert.Equal(expected.HistoryFingerprint, actual.HistoryFingerprint);
                    Assert.Equal(expected.ComputeSocialFingerprint(), actual.ComputeSocialFingerprint());
                    Assert.Equal(expected.CreatePersistenceSnapshot().Economy!.ToUnifiedCanonicalJson(), actual.CreatePersistenceSnapshot().Economy!.ToUnifiedCanonicalJson());
                    await db.CreateCheckpointStore().CheckpointAsync(actual.CreatePersistenceSnapshot());
                }
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task NewHistoryMigrationPreservesLegacySaveAndRejectsUnsupportedTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            var legacy = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.FestivalsSimulationRulesVersion).CreatePersistenceSnapshot();
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                await db.Context.Database.GetService<IMigrator>().MigrateAsync("20260930000000_M16Storehouses");
                await db.CreateCheckpointStore().CheckpointAsync(legacy);
            }
            await using var migrated = await WorldDatabase.OpenAsync(path);
            var loaded = await migrated.CreateCheckpointStore().LoadAsync();
            Assert.Equal(legacy.LivingStateJson, loaded.LivingStateJson);
            Assert.Equal(legacy.HistoricalEvents, loaded.HistoricalEvents);
            Assert.Equal(SimulationEngine.FestivalsSimulationRulesVersion, loaded.SimulationRulesVersion);
            var expected = new SimulationEngine(legacy); var actual = new SimulationEngine(loaded);
            AssertCanonicalState(expected, actual);
            expected.AdvanceUntil(new(2L * WorldCalendar.MinutesPerDay));
            actual.AdvanceUntil(new(WorldCalendar.MinutesPerDay)); actual.AdvanceUntil(new(2L * WorldCalendar.MinutesPerDay));
            AssertCanonicalState(expected, actual);
            var connection = migrated.Context.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='historical_events'";
            Assert.Contains("BETWEEN 1 AND 37", (string)(await command.ExecuteScalarAsync())!);
            command.CommandText = "UPDATE historical_events SET event_type=38 WHERE id=1";
            await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuestDepartureAndDeathArchivesRemainImmutableAcrossSqliteReopen(bool dies)
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        var phaseCheckpoints = new List<SimulationPersistenceSnapshot> { engine.CreatePersistenceSnapshot() };
        if (dies)
        {
            guest.Person!.Health = 1; guest.Person.Needs = new(10000, 10000, 10000, 10000);
            guest.Person.HealthUpdatedMinute = engine.CurrentMinute.Value - CitizenSimulationRules.SurvivalCheckIntervalMinutes;
            engine.AdvanceUntil(engine.CurrentMinute.Add(360));
        }
        else
        {
            engine.AdvanceUntil(engine.CurrentMinute.Add(180));
            Invoke(engine, "BeginNewcomerExit", guest, "choice");
            Assert.Equal(NewcomerPhase.Leaving, guest.Phase);
            phaseCheckpoints.Add(engine.CreatePersistenceSnapshot());
            engine.AdvanceUntil(engine.CurrentMinute.Add(3L * WorldCalendar.MinutesPerDay));
        }
        Assert.Equal(dies ? NewcomerPhase.Dead : NewcomerPhase.Departed, guest.Phase);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var snapshot = engine.CreatePersistenceSnapshot();
            var path = Path.Combine(root, "world.db");
            phaseCheckpoints.Add(snapshot);
            foreach (var phaseSnapshot in phaseCheckpoints)
            {
                await using (var db = await WorldDatabase.OpenAsync(path)) await db.CreateCheckpointStore().CheckpointAsync(phaseSnapshot);
                await using (var db = await WorldDatabase.OpenAsync(path))
                {
                    var reopenedPhase = new SimulationEngine(await db.CreateCheckpointStore().LoadAsync());
                    var uninterrupted = new SimulationEngine(phaseSnapshot);
                    var comparisonEnd = phaseSnapshot.WorldMinute.Add(360);
                    reopenedPhase.AdvanceUntil(comparisonEnd); uninterrupted.AdvanceUntil(comparisonEnd);
                    Assert.Equal(uninterrupted.LivingStateJson, reopenedPhase.LivingStateJson);
                    Assert.Equal(uninterrupted.HistoryFingerprint, reopenedPhase.HistoryFingerprint);
                }
            }
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                var reopened = new SimulationEngine(await db.CreateCheckpointStore().LoadAsync());
                var archiveJson = System.Text.Json.JsonSerializer.Serialize(Guest(reopened));
                reopened.AdvanceUntil(reopened.CurrentMinute.Add(2L * WorldCalendar.MinutesPerDay));
                Assert.Equal(archiveJson, System.Text.Json.JsonSerializer.Serialize(Guest(reopened)));
                await db.CreateCheckpointStore().CheckpointAsync(reopened.CreatePersistenceSnapshot());
                Guest(reopened).Person!.Needs = Guest(reopened).Person!.Needs with { Social = Math.Max(0, Guest(reopened).Person!.Needs.Social - 1) };
                await Assert.ThrowsAnyAsync<ArgumentException>(() => db.CreateCheckpointStore().CheckpointAsync(reopened.CreatePersistenceSnapshot()));
                Assert.Equal(archiveJson, System.Text.Json.JsonSerializer.Serialize(Guest(new SimulationEngine(await db.CreateCheckpointStore().LoadAsync()))));
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OffBoundaryAdmissionPreservesRootIdentityImportedFoodAndSurvivalTiming()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        Assert.Equal(NewcomerPhase.Visiting, guest.Phase);
        var donor = Field<Dictionary<long, Household>>(engine, "_households").Values.First(x => x.DissolvedMinute is null).Id.Value;
        var common = engine.Settlement.FoodStored; engine.Settlement.FoodStored = 0;
        Invoke(engine, "AddPrivate", donor, ResourceType.Food, (long)common);
        engine.AdvanceUntil(engine.CurrentMinute.Add(WorldCalendar.MinutesPerDay + 17));
        Assert.Equal(NewcomerPhase.Visiting, guest.Phase);
        var old = guest.Person!;
        var cooperative = new Citizen(old.Id, (int?)null, old.GivenName, old.FamilyName, old.BirthMinute, old.Location,
            new(5000, 10000, 5000, 10000, 5000, 5000), old.Skills, old.GetProjectedNeeds(engine.CurrentMinute))
        { NeedsUpdatedMinute = engine.CurrentMinute.Value, HealthUpdatedMinute = old.HealthUpdatedMinute, ActionSequence = old.ActionSequence };
        guest.Person = cooperative;
        // Supply controlled factual contact, then restore the resident's canonical in-flight route.
        var target = Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.First(x => x.IsAlive);
        var targetLocation = target.Location;
        target.Location = cooperative.Location;
        cooperative.TargetCitizenId = target.Id;
        for (var index = 0; index < 20; index++) Invoke(engine, "CompleteGuestContact", guest);
        target.Location = targetLocation;
        var stocks = Field<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        var required = 20 * (engine.Population + 1) - engine.Settlement.FoodStored;
        foreach (var stock in stocks.Values.ToArray())
        {
            var quantity = Math.Min(required, checked((int)stock.Holdings.Food));
            if (quantity <= 0) continue;
            Invoke(engine, "AddPrivate", stock.HouseholdId, ResourceType.Food, -(long)quantity);
            Invoke(engine, "AddCommons", 1L, ResourceType.Food, quantity);
            required -= quantity;
            if (required <= 0) break;
        }
        Assert.True(required <= 0);
        var before = engine.CaptureEconomy()!.Produced;
        var imported = guest.ProvisionsRemaining;
        Invoke(engine, "SetGuestAction", cooperative, CitizenAction.Idle, 60);
        var beforeAdmission = engine.CreatePersistenceSnapshot();
        Assert.True((bool)Invoke(engine, "TryJoinNewcomer", guest)!);
        RemoveGuestEvents(engine);
        var snapshot = engine.CreatePersistenceSnapshot();
        var contacts = guest.Contacts; guest.Contacts = [];
        var noEvidence = Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        Assert.Contains("full day", noEvidence.Message);
        guest.Contacts = contacts;
        var joinedMinute = guest.JoinedMinute; guest.JoinedMinute = guest.VisitingStartedMinute + WorldCalendar.MinutesPerDay - 1;
        var tooEarly = Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        Assert.Contains("full day", tooEarly.Message);
        guest.JoinedMinute = joinedMinute;
        Assert.Equal(before, snapshot.Economy!.Produced);
        Assert.Equal(imported, guest.ProvisionsTransferred);
        var resident = snapshot.Citizens.Single(x => x.Id == cooperative.Id);
        Assert.Null(resident.ParentAId); Assert.Null(resident.ParentBId); Assert.Null(resident.FounderOrdinal);
        Assert.Equal(old.BirthMinute, resident.BirthMinute);
        Assert.Equal(cooperative.HealthUpdatedMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes,
            Assert.Single(snapshot.ScheduledEvents, x => x.Name == CitizenEventNames.SurvivalCheck && x.Order.EntitySortKey == guest.CitizenId).Order.DueWorldMinute.Value);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-admit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                await db.CreateCheckpointStore().CheckpointAsync(beforeAdmission);
                var changedGuest = new SimulationEngine(beforeAdmission);
                RewriteGivenName(Guest(changedGuest).Person!, "Rewritten");
                await Assert.ThrowsAnyAsync<ArgumentException>(() => db.CreateCheckpointStore().CheckpointAsync(changedGuest.CreatePersistenceSnapshot()));
                Assert.Equal(beforeAdmission.LivingStateJson, (await db.CreateCheckpointStore().LoadAsync()).LivingStateJson);
                var admittedPerson = Field<Dictionary<long, Citizen>>(engine, "_citizens")[guest.CitizenId];
                var originalName = admittedPerson.GivenName; RewriteGivenName(admittedPerson, "Rewritten");
                await Assert.ThrowsAnyAsync<ArgumentException>(() => db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot()));
                Assert.Equal(beforeAdmission.LivingStateJson, (await db.CreateCheckpointStore().LoadAsync()).LivingStateJson);
                RewriteGivenName(admittedPerson, originalName);
                await db.CreateCheckpointStore().CheckpointAsync(snapshot);
            }
            await using var reopened = await WorldDatabase.OpenAsync(path);
            var actual = new SimulationEngine(await reopened.CreateCheckpointStore().LoadAsync());
            var expected = new SimulationEngine(snapshot);
            var end = engine.CurrentMinute.Add(2L * WorldCalendar.MinutesPerDay);
            actual.AdvanceUntil(end); expected.AdvanceUntil(end);
            Assert.Equal(expected.ComputeSocialFingerprint(), actual.ComputeSocialFingerprint());
            Assert.Equal(expected.HistoryFingerprint, actual.HistoryFingerprint);
            Assert.Equal(expected.LivingStateJson, actual.LivingStateJson);
            await reopened.CreateCheckpointStore().CheckpointAsync(actual.CreatePersistenceSnapshot());

            var people = Field<Dictionary<long, Citizen>>(actual, "_citizens");
            var outsider = people[guest.CitizenId];
            var partner = people.Values.Where(x => x.IsAlive && x.Id != outsider.Id && x.AgeYears(actual.CurrentMinute) is >= 18 and <= 45)
                .OrderBy(x => Math.Abs(x.AgeYears(actual.CurrentMinute) - outsider.AgeYears(actual.CurrentMinute))).ThenBy(x => x.Id.Value).First();
            if (partner.PartnerId is { } previousPartner) Invoke(actual, "KillNatural", people[previousPartner.Value]);
            Invoke(actual, "SetRelationship", outsider.Id, partner.Id, 9000, 9000, 9000, 0);
            var pair = RelationshipState.Normalize(outsider.Id, partner.Id);
            var relationship = actual.Relationships.Single(x => x.CitizenAId == pair.A && x.CitizenBId == pair.B);
            Invoke(actual, "TryFormPartnership", outsider, partner, relationship);
            Assert.Equal(partner.Id, outsider.PartnerId);
            Invoke(actual, "RecordPartnershipHistory", outsider, partner);
            var household = Field<Dictionary<long, Household>>(actual, "_households")[outsider.HouseholdId!.Value.Value];
            Invoke(actual, "CreateChild", outsider, partner, household);
            Invoke(actual, "ReconcileEconomicHouseholds"); Invoke(actual, "SynchronizeLivingPeople");
            var child = people.Values.Single(x => x.ParentAId == pair.A && x.ParentBId == pair.B);
            var descendantSnapshot = actual.CreatePersistenceSnapshot();
            await reopened.CreateCheckpointStore().CheckpointAsync(descendantSnapshot);
            var lineage = new SimulationEngine(await reopened.CreateCheckpointStore().LoadAsync());
            Assert.Equal(child.ParentAId, lineage.GetCitizen(child.Id)!.ParentAId);
            Assert.Equal(child.ParentBId, lineage.GetCitizen(child.Id)!.ParentBId);
            Assert.Null(lineage.GetCitizen(outsider.Id)!.ParentAId);
            Assert.Null(lineage.GetCitizen(outsider.Id)!.FounderOrdinal);
            var lineageEnd = actual.CurrentMinute.Add(WorldCalendar.MinutesPerDay);
            actual.AdvanceUntil(lineageEnd); lineage.AdvanceUntil(lineageEnd);
            Assert.Equal(actual.ComputeSocialFingerprint(), lineage.ComputeSocialFingerprint());
            Assert.Equal(actual.HistoryFingerprint, lineage.HistoryFingerprint);
            Assert.Equal(actual.LivingStateJson, lineage.LivingStateJson);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private static NewcomerState Guest(SimulationEngine engine) => Assert.Single(Field<LivingWorldState>(engine, "_living").Newcomers!.Visitors);
    [Fact]
    public void VisitingCheckpointRejectsFalseArrivalRoutesDeadlinesAndUnbalancedSources()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        var location = guest.Person!.Location; var index = guest.RouteIndex;
        guest.Person.Location = guest.EntryTile; guest.RouteIndex = 0;
        Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        guest.Person.Location = location; guest.RouteIndex = index;
        guest.StayDeadlineMinute++;
        Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        guest.StayDeadlineMinute--;
        guest.ProvisionsRemaining++;
        Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        guest.ProvisionsRemaining--;
        var last = guest.Route[^1]; guest.Route[^1] = guest.EntryTile;
        Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
        guest.Route[^1] = last;
        _ = engine.CreatePersistenceSnapshot();
    }

    [Fact]
    public async Task ResidentDeathDuringGuestContactImmediatelyCheckpointsAndReopens()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        var target = Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.First(x => x.IsAlive);
        Invoke(engine, "SetGuestAction", guest.Person!, CitizenAction.Socialize, CitizenSimulationRules.SocializeDurationMinutes);
        guest.Person!.TargetCitizenId = target.Id;
        var contacts = guest.Contacts.ToArray();
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-contact-death-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            await using (var db = await WorldDatabase.OpenAsync(path)) await db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            Invoke(engine, "KillNatural", target); Invoke(engine, "SynchronizeLivingPeople");
            Assert.Null(guest.Person.TargetCitizenId); Assert.Equal(CitizenAction.Idle, guest.Person.CurrentAction);
            Assert.Equal(contacts, guest.Contacts);
            var snapshot = engine.CreatePersistenceSnapshot();
            Assert.Single(snapshot.ScheduledEvents, x => x.Name == SimulationEngine.NewcomerStepEvent);
            await using (var db = await WorldDatabase.OpenAsync(path)) await db.CreateCheckpointStore().CheckpointAsync(snapshot);
            await using var reopened = await WorldDatabase.OpenAsync(path);
            var actual = new SimulationEngine(await reopened.CreateCheckpointStore().LoadAsync());
            var expected = new SimulationEngine(snapshot);
            var end = engine.CurrentMinute.Add(WorldCalendar.MinutesPerDay);
            actual.AdvanceUntil(end); expected.AdvanceUntil(end);
            AssertCanonicalState(expected, actual);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task NativePartnershipReleasesGuestAndKeepsResidentsHousedAcrossSqliteReplay()
    {
        var (engine, guest, parent, partner, children, unrelated, shelterA, shelterB) = NativeHousingFixture();
        var before = engine.CreatePersistenceSnapshot();
        Assert.Equal(NewcomerPhase.Visiting, guest.Phase);
        var guestLocation = guest.Person!.Location;
        var root = Path.Combine(Path.GetTempPath(), "littleages-m17-native-housing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            await using (var db = await WorldDatabase.OpenAsync(path))
                await db.CreateCheckpointStore().CheckpointAsync(before);
            engine.AdvanceUntil(new WorldMinute(parent.ActionCompletesMinute!.Value.Value - 1));
            var provisions = guest.ProvisionsRemaining;
            var healthBoundary = guest.Person.HealthUpdatedMinute;
            engine.AdvanceUntil(parent.ActionCompletesMinute!.Value);
            Assert.Equal(partner.Id, parent.PartnerId);
            Assert.Equal(NewcomerPhase.Leaving, guest.Phase);
            Assert.Equal("lost_support", guest.ExitReason);
            Assert.Equal(guestLocation, guest.Person.Location);
            Assert.Equal(guestLocation, guest.Route[0]);
            Assert.Equal(guest.EntryTile, guest.Route[^1]);
            Assert.Null(guest.Person.HomeStructureId);
            Assert.Equal(0, (int)Invoke(engine, "GuestShelterReservations", shelterA.Id.Value)!);
            Assert.All(children.Append(parent).Append(partner), citizen => Assert.Equal(shelterA.Id, citizen.HomeStructureId));
            Assert.All(unrelated, citizen => Assert.Equal(shelterB.Id, citizen.HomeStructureId));
            Assert.Equal(provisions, guest.ProvisionsRemaining);
            Assert.Equal(healthBoundary + (engine.CurrentMinute.Value - healthBoundary) / CitizenSimulationRules.SurvivalCheckIntervalMinutes * CitizenSimulationRules.SurvivalCheckIntervalMinutes, guest.Person.HealthUpdatedMinute);
            var immediate = engine.CreatePersistenceSnapshot();
            Assert.Single(immediate.ScheduledEvents, item => item.Name == SimulationEngine.NewcomerStepEvent);
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                var replay = new SimulationEngine(await db.CreateCheckpointStore().LoadAsync());
                replay.AdvanceUntil(engine.CurrentMinute);
                AssertCanonicalState(engine, replay);
                await db.CreateCheckpointStore().CheckpointAsync(immediate);
            }
            SimulationPersistenceSnapshot departed;
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                var actual = new SimulationEngine(await db.CreateCheckpointStore().LoadAsync());
                AssertCanonicalState(engine, actual);
                var expected = new SimulationEngine(immediate);
                var until = engine.CurrentMinute.Add(3L * WorldCalendar.MinutesPerDay);
                expected.AdvanceUntil(until); actual.AdvanceUntil(until);
                AssertCanonicalState(expected, actual);
                var archived = Guest(actual);
                Assert.Equal(NewcomerPhase.Departed, archived.Phase);
                Assert.Equal(archived.EntryTile, archived.Person!.Location);
                Assert.Null(archived.Person.HomeStructureId);
                departed = actual.CreatePersistenceSnapshot();
                Assert.DoesNotContain(departed.ScheduledEvents, item => item.Name == SimulationEngine.NewcomerStepEvent);
                await db.CreateCheckpointStore().CheckpointAsync(departed);
            }
            await using (var db = await WorldDatabase.OpenAsync(path))
                AssertCanonicalState(new SimulationEngine(departed), new SimulationEngine(await db.CreateCheckpointStore().LoadAsync()));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private static (SimulationEngine Engine, NewcomerState Guest, Citizen Parent, Citizen Partner, Citizen[] Children, Citizen[] Unrelated, Structure ShelterA, Structure ShelterB) NativeHousingFixture()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        var people = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var households = Field<Dictionary<long, Household>>(engine, "_households");
        var adults = people.Values.Where(x => x.AgeYears(engine.CurrentMinute) is >= 18 and <= 45).OrderBy(x => x.Id.Value).Take(5).ToArray();
        Assert.Equal(5, adults.Length);
        var parent = adults[0]; var deceased = adults[1]; var partner = adults[2];
        var unrelatedParents = (First: adults[3], Second: adults[4]);
        Assert.True(Math.Abs(parent.AgeYears(engine.CurrentMinute) - partner.AgeYears(engine.CurrentMinute)) <= 20);
        foreach (var citizen in people.Values.Where(x => !adults.Contains(x)).ToArray()) Invoke(engine, "KillNatural", citizen);
        foreach (var pair in new[] { (First: parent, Second: deceased), unrelatedParents })
        {
            Invoke(engine, "SetRelationship", pair.First.Id, pair.Second.Id, 10000, 10000, 10000, 0);
            Invoke(engine, "TryFormPartnership", pair.First, pair.Second, engine.Relationships.Single(x => x.CitizenAId == pair.First.Id && x.CitizenBId == pair.Second.Id));
            Invoke(engine, "RecordPartnershipHistory", pair.First, pair.Second);
        }
        var family = households[parent.HouseholdId!.Value.Value];
        var otherFamily = households[unrelatedParents.First.HouseholdId!.Value.Value];
        Invoke(engine, "CreateChild", parent, deceased, family); Invoke(engine, "CreateChild", parent, deceased, family);
        Invoke(engine, "CreateChild", unrelatedParents.First, unrelatedParents.Second, otherFamily);
        Invoke(engine, "SynchronizeLivingPeople");
        Invoke(engine, "KillNatural", deceased); Invoke(engine, "SynchronizeLivingPeople");
        Invoke(engine, "ReconcileHouseholdsAndHousing");
        Assert.Equal(7, engine.Population);
        var structures = Field<Dictionary<long, Structure>>(engine, "_structures");
        var deadline = engine.CurrentMinute.Add(120L * WorldCalendar.MinutesPerDay);
        while (structures.Values.Count(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete) < 2 && engine.CurrentMinute < deadline)
            engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        var shelters = structures.Values.Where(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete).OrderBy(x => x.Id.Value).ToArray();
        Assert.Equal(2, shelters.Length);
        var shelterA = shelters[0]; var shelterB = shelters[1];
        var children = people.Values.Where(x => x.IsAlive && (x.ParentAId == parent.Id || x.ParentBId == parent.Id)).ToArray();
        var unrelated = people.Values.Where(x => x.IsAlive && x.HouseholdId == otherFamily.Id).ToArray();
        Assert.Equal(2, children.Length); Assert.Equal(3, unrelated.Length);
        var selected = children.Append(parent).Append(partner).Concat(unrelated).ToArray();
        foreach (var citizen in selected)
        {
            Invoke(engine, "RecoverInterruptedCargo", citizen); Invoke(engine, "ReleaseLivingClaim", citizen);
            Invoke(engine, "FinishFoundingTravel", citizen); Invoke(engine, "WaitForFoundingParty", citizen);
            citizen.HomeStructureId = children.Contains(citizen) || citizen.Id == parent.Id ? shelterA.Id : shelterB.Id;
            citizen.Location = citizen.HomeStructureId == shelterA.Id ? shelterA.Location : shelterB.Location;
        }
        family.DwellingStructureId = shelterA.Id; otherFamily.DwellingStructureId = shelterB.Id;
        households[partner.HouseholdId!.Value.Value].DwellingStructureId = shelterB.Id;
        Invoke(engine, "ReconcileHouseholdsAndHousing");
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine); Assert.Equal(shelterA.Id.Value, guest.ShelterStructureId);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        foreach (var citizen in new[] { parent, partner })
        {
            Invoke(engine, "RecoverInterruptedCargo", citizen); Invoke(engine, "ReleaseLivingClaim", citizen);
            Invoke(engine, "FinishFoundingTravel", citizen); Invoke(engine, "WaitForFoundingParty", citizen);
            citizen.Location = shelterA.Location;
        }
        Invoke(engine, "SetRelationship", parent.Id, partner.Id, 10000, 10000, 10000, 0);
        // Replace the controlled actor's wait with a canonical 60-minute native social completion.
        var queue = typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        foreach (var pending in ((System.Collections.IEnumerable)queue).Cast<object>().Where(x =>
            (string)x.GetType().GetProperty("Name")!.GetValue(x)! == CitizenEventNames.ActionComplete &&
            ((ScheduledEventOrder)x.GetType().GetProperty("Order")!.GetValue(x)!).EntitySortKey == parent.Id.Value).ToArray())
            queue.GetType().GetMethod("Remove")!.Invoke(queue, [pending]);
        parent.CurrentAction = CitizenAction.Socialize; parent.ActionPhase = CitizenActionPhase.Perform;
        parent.TargetCitizenId = partner.Id; parent.ActionStartedMinute = engine.CurrentMinute; parent.ActionCompletesMinute = engine.CurrentMinute.Add(60);
        Invoke(engine, "ScheduleCitizen", parent, CitizenEventNames.ActionComplete, parent.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
        return (engine, guest, parent, partner, children, unrelated, shelterA, shelterB);
    }

    private static T Field<T>(SimulationEngine engine, string name) => (T)typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
    private static void RewriteGivenName(Citizen citizen, string name) => typeof(Citizen).GetField("<GivenName>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(citizen, name);
    private static void AssertCanonicalState(SimulationEngine expected, SimulationEngine actual)
    {
        Assert.Equal(expected.World.Fingerprint, actual.World.Fingerprint);
        Assert.Equal(expected.SurvivalFingerprint, actual.SurvivalFingerprint);
        Assert.Equal(expected.SettlementFingerprint, actual.SettlementFingerprint);
        Assert.Equal(expected.SocialFingerprint, actual.SocialFingerprint);
        Assert.Equal(expected.HistoryFingerprint, actual.HistoryFingerprint);
        Assert.Equal(expected.ComputeAgricultureFingerprint(), actual.ComputeAgricultureFingerprint());
        Assert.Equal(expected.ComputeEconomyFingerprint(), actual.ComputeEconomyFingerprint());
        Assert.Equal(expected.LivingStateJson, actual.LivingStateJson);
        Assert.Equal(expected.CounterSnapshot, actual.CounterSnapshot);
        Assert.Equal(expected.CreatePersistenceSnapshot().MigrationStateJson, actual.CreatePersistenceSnapshot().MigrationStateJson);
        Assert.Equal(expected.CreatePersistenceSnapshot().ScheduledEvents, actual.CreatePersistenceSnapshot().ScheduledEvents);
    }
    private static void RemoveGuestEvents(SimulationEngine engine)
    {
        var queue = typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        var remove = queue.GetType().GetMethod("Remove")!;
        foreach (var item in ((System.Collections.IEnumerable)queue).Cast<object>().Where(x => (string)x.GetType().GetProperty("Name")!.GetValue(x)! == SimulationEngine.NewcomerStepEvent).ToArray()) remove.Invoke(queue, [item]);
    }
    private static object? Invoke(SimulationEngine engine, string name, params object[] args) => typeof(SimulationEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, args.Select(x => x.GetType()).ToArray(), null)!.Invoke(engine, args);
}
