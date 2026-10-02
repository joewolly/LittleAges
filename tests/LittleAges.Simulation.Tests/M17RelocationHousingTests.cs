using System.Reflection;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;
namespace LittleAges.Simulation.Tests;
public sealed class M17RelocationHousingTests
{
    private sealed record Fixture(SimulationEngine Engine, Household Household, Citizen[] Members, Citizen[] DestinationResidents, Structure[] Shelters, Structure Origin);
    private static object? Call(object o,string n,params object?[] a) => o.GetType().GetMethods(BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length&&m.GetParameters().Select((p,i)=>a[i]==null||p.ParameterType.IsInstanceOfType(a[i])).All(x=>x)).Invoke(o,a);
    private static T Get<T>(object o,string n)=>(T)o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(o)!;
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(o,v);
    private static Fixture Create(string rules, bool fragmented)
    {
var helper=new M14MigrationFoundationTests();
var fixture=Call(helper,"CreateRelocationEngine",new WorldSeed(920),1L,2L,rules)!;
var engine=(SimulationEngine)fixture.GetType().GetProperty("Engine")!.GetValue(fixture)!;
var parents=(Citizen[])fixture.GetType().GetProperty("Members")!.GetValue(fixture)!;
Call(engine,"SetRelationship",parents[0].Id,parents[1].Id,10000,10000,10000,0);
var relation=engine.Relationships.Single(r=>r.CitizenAId==parents[0].Id&&r.CitizenBId==parents[1].Id);
Call(engine,"TryFormPartnership",parents[0],parents[1],relation);
if(parents[0].PartnerId!=parents[1].Id)throw new InvalidOperationException("Native partnership rejected");
Call(engine,"RecordPartnershipHistory",parents[0],parents[1]);
var households=Get<Dictionary<long,Household>>(engine,"_households");
var household=households[parents[0].HouseholdId!.Value.Value];
Call(engine,"CreateChild",parents[0],parents[1],household);
Call(engine,"SynchronizeLivingPeople");
var members=Get<Dictionary<long,Citizen>>(engine,"_citizens").Values.Where(c=>c.IsAlive&&c.HouseholdId==household.Id).OrderBy(c=>c.Id.Value).ToArray();
var state=Get<MigrationWorldState>(engine,"_migrationState");
var destination=state.DaughterSettlement!.Site;
var extras=Get<Dictionary<long,Citizen>>(engine,"_citizens").Values.Where(c=>c.Id.Value==5||c.Id.Value==6).ToArray();
foreach(var c in extras){Call(engine,"RecordMigrationOwner",Enum.Parse(typeof(SimulationEngine).GetNestedType("MigrationEntityKind",BindingFlags.NonPublic)!,"Citizen"),c.Id.Value,2L);Call(engine,"RecordMigrationOwner",Enum.Parse(typeof(SimulationEngine).GetNestedType("MigrationEntityKind",BindingFlags.NonPublic)!,"Household"),c.HouseholdId!.Value.Value,2L);c.Location=destination;}
var structures=Get<Dictionary<long,Structure>>(engine,"_structures");
var shelters=structures.Values.Where(s=>s.Type==StructureType.Shelter).OrderBy(s=>s.Id.Value).ToArray();
if(shelters.Length!=2)throw new InvalidOperationException("Expected exactly two destination shelters");
var destCitizens=Get<Dictionary<long,Citizen>>(engine,"_citizens").Values.Where(c=>(long)Call(engine,"SiteIdForCitizen",c)! ==2).OrderBy(c=>c.Id.Value).ToArray();
foreach(var (c,index) in destCitizens.Select((c,i)=>(c,i))){var shelter=shelters[fragmented ? index%2 : 0];households[c.HouseholdId!.Value.Value].DwellingStructureId=shelter.Id;Call(engine,"SetHomeStructure",c,(StructureId?)shelter.Id);}
// Accounted completed origin shelter follows the existing M14 fixture's contribution/production recipe.
var counters=Get<DeterministicCounters>(engine,"_counters");
var shelterId=counters.AllocateStructureId();
var location=(TileCoordinate)Call(helper,"FindFreeBuildableTile",engine,1L,engine.World!.StartingSite,structures.Values.ToArray(),Array.Empty<TileCoordinate>())!;
var origin=new Structure(shelterId,StructureType.Shelter,location,0,StructureDefinitions.ShelterRequiredWood,StructureDefinitions.ShelterRequiredStone,StructureDefinitions.ShelterRequiredWork){Status=StructureStatus.Complete,CompletedMinute=0,DeliveredWood=StructureDefinitions.ShelterRequiredWood,DeliveredStone=StructureDefinitions.ShelterRequiredStone,CompletedWork=StructureDefinitions.ShelterRequiredWork};
structures.Add(shelterId.Value,origin);Call(engine,"RecordMigrationOwner",Enum.Parse(typeof(SimulationEngine).GetNestedType("MigrationEntityKind",BindingFlags.NonPublic)!,"Structure"),shelterId.Value,1L);
Get<Dictionary<(long StructureId,long CitizenId),StructureContribution>>(engine,"_structureContributions").Add((shelterId.Value,parents[0].Id.Value),new StructureContribution(shelterId,parents[0].Id,origin.CompletedWork,origin.DeliveredWood,origin.DeliveredStone));
Set(engine,"_producedGoods",Get<Goods>(engine,"_producedGoods").Plus(new Goods(Wood:origin.DeliveredWood,Stone:origin.DeliveredStone)));
household.DwellingStructureId=shelterId;foreach(var c in members)Call(engine,"SetHomeStructure",c,(StructureId?)shelterId);
state=Get<MigrationWorldState>(engine,"_migrationState");
Set(engine,"_migrationState",new MigrationWorldState(state.Version,state.CitizenResidences,state.HouseholdResidences,state.StructureOwners,state.FacilityOwners,state.WorkOrderOwners,state.DaughterSettlement,state.InTransitParties,state.FoundingPressure,households.Values.Where(h=>h.Id!=household.Id&&h.DissolvedMinute==null).Select(h=>new MigrationHouseholdRelocationState(h.Id.Value,0)).ToArray(),state.LastVisitAttemptYear,state.Roads));
foreach(var c in Get<Dictionary<long,Citizen>>(engine,"_citizens").Values.Where(c=>c.IsAlive)){Call(engine,"RecoverInterruptedCargo",c);Call(engine,"ReleaseLivingClaim",c);Call(engine,"FinishFoundingTravel",c);Call(engine,"WaitForFoundingParty",c);}
Call(engine,"ReconcileHouseholdsAndHousing");

        _ = engine.CreatePersistenceSnapshot();
        return new Fixture(engine, household, members, destCitizens, shelters, origin);
    }
    private static string State(SimulationEngine engine) => JsonSerializer.Serialize(engine.CreatePersistenceSnapshot());
    private static void DriveParty(SimulationEngine engine, HashSet<long> members)
    {
        var deadline=engine.CurrentMinute.Value+20000;
        while(Get<MigrationWorldState>(engine,"_migrationState").InTransitParties.Any(p=>p.JourneyKind==MigrationJourneyKind.Relocation))
        {
            Assert.True(engine.CurrentMinute.Value<deadline);
            foreach(var c in Get<Dictionary<long,Citizen>>(engine,"_citizens").Values.Where(c=>c.IsAlive&&!members.Contains(c.Id.Value)))
            {
                Call(engine,"RecoverInterruptedCargo",c);Call(engine,"ReleaseLivingClaim",c);
                Call(engine,"FinishFoundingTravel",c);Call(engine,"WaitForFoundingParty",c);
            }
            engine.AdvanceUntil(engine.CurrentMinute.Add(30));
        }
    }
    [Theory]
    [InlineData(SimulationEngine.NewcomersRulesVersion,false)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion,true)]
    public void FragmentedCompletedBedsRejectM17DepartureAndRetainLegacyAggregateBehavior(string rules,bool expected)
    {
        var f=Create(rules,true);var before=State(f.Engine);
        Assert.Equal(expected,(bool)Call(f.Engine,"HasRelocationSupport",2L,3)!);
        Assert.Equal(before,State(f.Engine)); // pure support cannot change homes, cargo, guests or queue
        Call(f.Engine,"EvaluateMigrationRelocation");
        if(!expected)
        {
            Assert.Empty(Get<MigrationWorldState>(f.Engine,"_migrationState").InTransitParties);
            Assert.Equal(before,State(f.Engine));
            Assert.All(f.Members,c=>Assert.Equal(f.Origin.Id,c.HomeStructureId));
        }
        else
        {
            DriveParty(f.Engine,f.Members.Select(c=>c.Id.Value).ToHashSet());
            Assert.All(f.Members,c=>Assert.Null(c.HomeStructureId));
            Assert.Contains(f.Engine.CreatePersistenceSnapshot().HistoricalEvents,e=>e.EventType==HistoricalEventType.HouseholdRelocated);
        }
    }
    [Fact]
    public void WholeCompletedDwellingAllowsPhysicalRelocationWithCheckpointReplay()
    {
        var f=Create(SimulationEngine.NewcomersRulesVersion,false);
        Assert.True((bool)Call(f.Engine,"HasRelocationSupport",2L,3)!);
        Call(f.Engine,"EvaluateMigrationRelocation");
        var checkpoint=f.Engine.CreatePersistenceSnapshot();
        var replay=SimulationEngine.FromPersistenceSnapshot(checkpoint);
        var ids=f.Members.Select(c=>c.Id.Value).ToHashSet();
        DriveParty(f.Engine,ids);DriveParty(replay,ids);
        Assert.Equal(State(f.Engine),State(replay));
        Assert.All(f.Members,c=>Assert.Equal(f.Shelters[1].Id,c.HomeStructureId));
        Assert.All(f.Members,c=>Assert.Equal(Get<MigrationWorldState>(f.Engine,"_migrationState").DaughterSettlement!.Site,c.Location));
        Assert.Equal(State(f.Engine),State(SimulationEngine.FromPersistenceSnapshot(f.Engine.CreatePersistenceSnapshot())));
    }
    [Fact]
    public void LosingWholeDwellingDuringPhysicalTravelReturnsFamilyAndOwnedCargoToOldHomes()
    {
        var f=Create(SimulationEngine.NewcomersRulesVersion,false);
        Call(f.Engine,"EvaluateMigrationRelocation");
        foreach(var (citizen,index) in f.DestinationResidents.Select((c,i)=>(c,i)))
        {
            Get<Dictionary<long,Household>>(f.Engine,"_households")[citizen.HouseholdId!.Value.Value].DwellingStructureId=f.Shelters[index%2].Id;
            Call(f.Engine,"SetHomeStructure",citizen,(StructureId?)f.Shelters[index%2].Id);
        }
        var checkpoint=f.Engine.CreatePersistenceSnapshot();var replay=SimulationEngine.FromPersistenceSnapshot(checkpoint);
        var ids=f.Members.Select(c=>c.Id.Value).ToHashSet();
        DriveParty(f.Engine,ids);DriveParty(replay,ids);
        Assert.Equal(State(f.Engine),State(replay));
        Assert.All(f.Members,c=>Assert.Equal(f.Origin.Id,c.HomeStructureId));
        Assert.All(f.Members,c=>Assert.Equal(f.Engine.World.StartingSite,c.Location));
        Assert.Equal(1,Get<MigrationWorldState>(f.Engine,"_migrationState").HouseholdResidences.Single(r=>r.EntityId==f.Household.Id.Value).SettlementId);
        Assert.DoesNotContain(f.Engine.CreatePersistenceSnapshot().HistoricalEvents,e=>e.EventType==HistoricalEventType.HouseholdRelocated);
        Assert.Equal(5L,(long)Call(f.Engine,"AvailablePrivate",f.Household.Id.Value,ResourceType.Wood)!);
        Assert.Equal(3L,(long)Call(f.Engine,"AvailablePrivate",f.Household.Id.Value,ResourceType.Stone)!);
        Assert.Equal(State(f.Engine),State(SimulationEngine.FromPersistenceSnapshot(f.Engine.CreatePersistenceSnapshot())));
    }
    [Fact]
    public void ExistingUnhousedDestinationFamilyParticipatesBeforeIncomingFamily()
    {
        var f=Create(SimulationEngine.NewcomersRulesVersion,false);
        var parents=f.DestinationResidents.Where(c=>c.Id.Value is 5 or 6).ToArray();
        Call(f.Engine,"SetRelationship",parents[0].Id,parents[1].Id,10000,10000,10000,0);
        var relationship=f.Engine.Relationships.Single(r=>r.CitizenAId==parents[0].Id&&r.CitizenBId==parents[1].Id);
        Call(f.Engine,"TryFormPartnership",parents[0],parents[1],relationship);
        Call(f.Engine,"RecordPartnershipHistory",parents[0],parents[1]);
        var household=Get<Dictionary<long,Household>>(f.Engine,"_households")[parents[0].HouseholdId!.Value.Value];
        Call(f.Engine,"CreateChild",parents[0],parents[1],household);Call(f.Engine,"CreateChild",parents[0],parents[1],household);
        Call(f.Engine,"SynchronizeLivingPeople");Call(f.Engine,"ReconcileEconomicHouseholds");
        household.DwellingStructureId=null;
        foreach(var c in Get<Dictionary<long,Citizen>>(f.Engine,"_citizens").Values.Where(c=>c.HouseholdId==household.Id))
        {
            Call(f.Engine,"SetHomeStructure",c,(StructureId?)null);
            Call(f.Engine,"FinishFoundingTravel",c);Call(f.Engine,"WaitForFoundingParty",c);
        }
        var before=State(f.Engine);
        Assert.False((bool)Call(f.Engine,"HasRelocationSupport",2L,3)!);
        Assert.Equal(before,State(f.Engine));
        var plan=M17HousingPlacementPlanner.Plan(f.Shelters,Get<Dictionary<long,Citizen>>(f.Engine,"_citizens").Values.Where(c=>(long)Call(f.Engine,"SiteIdForCitizen",c)! ==2),Get<Dictionary<long,Household>>(f.Engine,"_households").Values.Where(h=>(long)Call(f.Engine,"SiteIdForHousehold",h.Id.Value)! ==2),f.Household.Id.Value,3);
        Assert.Equal(f.Shelters[1].Id.Value,plan.HouseholdDwellings[household.Id.Value]);
        Assert.Null(plan.HouseholdDwellings[f.Household.Id.Value]);
    }
}
