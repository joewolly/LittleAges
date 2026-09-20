using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public static class LivingValidation
{
    public static void Validate(SimulationPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var pulse = snapshot.ScheduledEvents.Where(x => x.Name == SimulationEngine.LivingPulseEvent).ToArray();
        if (snapshot.SimulationRulesVersion != SimulationEngine.LivingSimulationRulesVersion)
        {
            Require(snapshot.LivingStateJson is null && pulse.Length == 0 && snapshot.Citizens.All(x => x.CurrentAction != CitizenAction.LivingWork), "Legacy rules cannot contain living-world state.");
            return;
        }
        Require(snapshot.LivingStateJson is not null, "Living-world state is required.");
        LivingWorldState state;
        try { state = LivingWorldCodec.Deserialize(snapshot.LivingStateJson!); }
        catch (JsonException error) { throw new ArgumentException("Living-world JSON is invalid.", nameof(snapshot), error); }
        Require(LivingWorldCodec.Serialize(state) == snapshot.LivingStateJson, "Living-world JSON must be complete and canonical.");
        var minute = snapshot.WorldMinute.Value;
        Require(state.Version == 1 && state.NextId > 0 && state.NextFactId > 0, "Unsupported living version or counter.");
        Require(state.People is not null && state.Orders is not null && state.Fields is not null && state.Facilities is not null && state.Animals is not null && state.Stock is not null && state.Facts is not null, "Living collections are required.");
        Require(state.People.All(x => x is not null) && state.Orders.All(x => x is not null) && state.Fields.All(x => x is not null) && state.Facilities.All(x => x is not null) && state.Animals.All(x => x is not null) && state.Stock.All(x => x is not null) && state.Facts.All(x => x is not null), "Living entities cannot be null.");
        Require(state.UpdatedMinute == minute / SimulationEngine.LivingPulseMinutes * SimulationEngine.LivingPulseMinutes && state.LastDailyMinute == minute / WorldCalendar.MinutesPerDay * WorldCalendar.MinutesPerDay, "Living update boundaries are invalid.");
        Require(pulse.Length == 1 && pulse[0].Order.Priority == 5 && pulse[0].Order.EntitySortKey == 0 && pulse[0].PayloadJson == "{\"version\":1}" && pulse[0].Order.DueWorldMinute.Value == (minute / SimulationEngine.LivingPulseMinutes + 1) * SimulationEngine.LivingPulseMinutes, "Living pulse is missing or malformed.");
        Require(Enum.IsDefined(state.Weather) && state.Temperature is >= -40 and <= 60 && state.Rainfall is >= 0 and <= 100, "Weather is invalid.");
        Require(state.CompletedOrders >= 0 && state.FoodHarvested >= 0 && state.FoodPrepared >= 0 && state.GoodsSpoiled >= 0 && state.CareGiven >= 0, "Living counters are invalid.");
        var citizens = snapshot.Citizens.ToDictionary(x => x.Id.Value);
        Require(state.People!.Select(x => x.CitizenId).SequenceEqual(citizens.Keys.Order()), "Living people must match the complete citizen roster.");
        foreach (var person in state.People!)
        {
            Require(Enum.IsDefined(person.Goal) && person.DeathObserved == !citizens[person.CitizenId].IsAlive && person.Practice is >= 0 and <= 100000, "Person state is invalid.");
            Require(new[] { person.Mood, person.Stress, person.Injury, person.Illness, person.ToolCondition, person.ClothingCondition }.All(x => x is >= 0 and <= 10000), "Person ranges are invalid.");
            Require(person.Knowledge is not null && person.Experiences is not null && person.Knowledge.SequenceEqual(person.Knowledge.Distinct().Order()) && person.Knowledge.All(Enum.IsDefined), "Knowledge is invalid.");
            Require(person.LastCareMinute >= -1440 && person.LastCareMinute <= minute && person.LastTeachingMinute >= -1440 && person.LastTeachingMinute <= minute && person.LastLeisureMinute >= -1440 && person.LastLeisureMinute <= minute, "Personal timing is invalid.");
            Require(person.Experiences!.Count <= 24 && person.Experiences.All(x => Enum.IsDefined(x.Kind) && x.Minute >= 0 && x.Minute <= minute && (x.OtherCitizenId is null || citizens.ContainsKey(x.OtherCitizenId.Value))), "Active memories are invalid.");
        }
        Require(state.Stock!.Select(x => x.Good).SequenceEqual(Enum.GetValues<LivingGood>()) && state.Stock.All(x => x.Quantity >= 0), "Stock must contain exactly the canonical goods.");
        var ids = state.Orders!.Select(x => x.Id).Concat(state.Fields!.Select(x => x.Id)).Concat(state.Facilities!.Select(x => x.Id)).Concat(state.Animals!.Select(x => x.Id)).ToArray();
        Require(ids.Distinct().Count() == ids.Length && ids.All(x => x > 0 && x < state.NextId), "Living IDs overlap or exceed their counter.");
        Require(IsOrdered(state.Orders.Select(x => x.Id)) && IsOrdered(state.Fields.Select(x => x.Id)) && IsOrdered(state.Facilities.Select(x => x.Id)) && IsOrdered(state.Animals.Select(x => x.Id)), "Living entity order is invalid.");
        var world = snapshot.World!;
        bool ValidLocation(TileCoordinate location) => location.X < world.Width && location.Y < world.Height && world.GetTile(location).Walkable;
        var occupied = state.Fields.Select(x => x.Location).Concat(state.Facilities.Select(x => x.Location)).Concat(snapshot.Structures.Select(x => x.Location)).ToArray();
        Require(occupied.Distinct().Count() == occupied.Length && occupied.All(ValidLocation), "Living sites overlap or are not walkable.");
        foreach (var field in state.Fields)
            Require(field.SownMinute >= -1 && field.SownMinute <= minute && field.LastTendedMinute >= 0 && field.LastTendedMinute <= minute && field.Growth is >= 0 and <= 10000 && field.Moisture is >= 0 and <= 10000 && field.Condition is >= 0 and <= 10000 && field.Harvests >= 0 && field.YieldRemaining is >= 0 and <= 900 && (field.YieldRemaining == 0 || field.Growth == 10000 && field.SownMinute >= 0), "Field state is invalid.");
        Require(state.Facilities.Count <= 18 && state.Facilities.All(x => Enum.IsDefined(x.Kind) && x.CompletedMinute >= 0 && x.CompletedMinute <= minute), "Facilities are invalid.");
        Require(state.Animals.Count <= 40 && state.Animals.All(x => ValidLocation(x.Location) && x.Energy is > 0 and <= 10000 && x.BornMinute >= 0 && x.BornMinute <= minute), "Wildlife is invalid.");
        Require(state.Orders.Where(x => x.CitizenId is not null).Select(x => x.CitizenId).Distinct().Count() == state.Orders.Count(x => x.CitizenId is not null), "Citizens cannot own multiple work claims.");
        foreach (var order in state.Orders)
        {
            Require(Enum.IsDefined(order.Kind) && Enum.IsDefined(order.Phase) && ValidLocation(order.Location) && ValidLocation(order.SupplyLocation), "Work kind, phase, or site is invalid.");
            Require(order.CreatedMinute >= 0 && order.CreatedMinute <= minute && order.ClaimedMinute >= 0 && order.ClaimedMinute <= minute && order.Priority is >= 0 and <= 10000 && order.RequiredWork is > 0 and <= 10000 && order.WorkDone >= 0 && order.WorkDone <= order.RequiredWork, "Work timing or progress is invalid.");
            Require(order.Technique is null || Enum.IsDefined(order.Technique.Value), "Work technique is invalid.");
            Require(order.Ingredients is not null && order.Cargo is not null && order.Ingredients.All(x => x.Quantity > 0 && (x.Resource is "Food" or "Wood" or "Stone" || Enum.TryParse<LivingGood>(x.Resource, out var good) && Enum.IsDefined(good))) && order.Ingredients.Select(x => x.Resource).Distinct().Count() == order.Ingredients.Count, "Work ingredients are invalid.");
            Require(order.RequiredWork == LivingWorkDefinitions.Work(order.Kind) && LivingWorkDefinitions.ValidIngredients(order) && LivingWorkDefinitions.ValidCargo(order), "Work recipe or outputs do not match the rules.");
            Require(order.Reserved || !order.SuppliesDelivered && order.WorkDone == 0 && !order.Produced, "Unreserved work cannot have progress or delivered supplies.");
            Require(!order.Produced || order.Phase == LivingWorkPhase.Deliver, "Produced work must be delivered.");
            Require(!order.CargoInTransit || order.Produced && order.CitizenId is not null && order.Cargo.Count > 0, "Cargo in transit requires a worker and produced goods.");
            Require(order.Technique is not null == (order.Kind is LivingWorkKind.Teach or LivingWorkKind.Experiment), "Technique references belong to learning work.");
            Require(order.Cargo!.All(x => Enum.IsDefined(x.Good) && x.Quantity > 0) && (order.Cargo.Count == 0 || order.Produced) && (!order.Produced || order.Reserved && order.WorkDone == order.RequiredWork), "Work cargo or completion is invalid.");
            if (order.CitizenId is { } id)
            {
                Require(citizens.TryGetValue(id, out var citizen) && citizen.IsAlive && citizen.CurrentAction == CitizenAction.LivingWork && order.Reserved, "Work claim is orphaned.");
                var worker = citizens[id];
                if (order.Produced)
                    Require(worker.ActionPhase == CitizenActionPhase.TravelToTarget && worker.ActionTarget == (order.CargoInTransit ? world.StartingSite : order.SupplyLocation), "Cargo movement does not match pickup or delivery.");
                else if (order.Phase == LivingWorkPhase.Work)
                    Require(order.SuppliesDelivered && worker.ActionPhase == CitizenActionPhase.Perform && worker.Location == order.Location, "Work must occur at its supplied site.");
                else
                    Require(worker.ActionPhase == CitizenActionPhase.TravelToTarget && worker.ActionTarget == (order.Phase == LivingWorkPhase.Collect ? order.SupplyLocation : order.Location), "Supply movement does not match the work phase.");
            }
            if (order.Kind is LivingWorkKind.Care or LivingWorkKind.Teach or LivingWorkKind.Recreate or LivingWorkKind.RepairRelationship or LivingWorkKind.EquipTool or LivingWorkKind.EquipClothing)
                Require(order.SubjectId is { } subject && citizens.ContainsKey(subject), "Work recipient is unknown.");
            if (order.Kind is LivingWorkKind.Sow or LivingWorkKind.Tend or LivingWorkKind.Harvest)
                Require(state.Fields.Any(x => x.Id == order.SubjectId), "Work field is unknown.");
            if (order.Kind is LivingWorkKind.Cook or LivingWorkKind.Preserve)
                Require(state.Facilities.Any(x => x.Id == order.SubjectId && x.Kind == LivingFacilityKind.Hearth && x.Location == order.Location), "Food production requires its hearth.");
        }
        Require(snapshot.Citizens.Where(x => x.CurrentAction == CitizenAction.LivingWork).All(c => state.Orders.Count(x => x.CitizenId == c.Id.Value) == 1), "A living action requires one claim.");
        var capacity = (long)snapshot.Settlement!.BaseStorageCapacity + snapshot.Structures.Count(x => x.Type == StructureType.Stockpile && x.Status == StructureStatus.Complete) * CitizenSimulationRules.StockpileStorageBonus;
        var used = (long)snapshot.Settlement.StorageUsed + state.Stock.Sum(x => (long)x.Quantity) + state.Orders.Sum(x => x.Cargo.Sum(y => (long)y.Quantity) + (x.Reserved && !x.Produced ? x.Ingredients.Sum(y => (long)y.Quantity) : 0));
        Require(used <= capacity, "Living storage including escrow and transit exceeds capacity.");
        Require(state.Facts!.Select(x => x.Id).SequenceEqual(Enumerable.Range(1, state.Facts.Count).Select(x => (long)x)) && state.NextFactId == state.Facts.Count + 1L && state.Facts.Zip(state.Facts.Skip(1)).All(x => x.First.Minute <= x.Second.Minute), "Living history order or counter is invalid.");
        Require(state.Facts.All(x => Enum.IsDefined(x.Kind) && x.Minute >= 0 && x.Minute <= minute && (x.CitizenId is null || citizens.ContainsKey(x.CitizenId.Value)) && (x.Location is null || ValidLocation(x.Location.Value))), "Living historical fact is invalid.");
    }
    public static void ValidateRetainedFacts(string? previousJson, string? nextJson)
    {
        if (previousJson is null) return;
        if (nextJson is null) throw new InvalidDataException("A checkpoint cannot remove living-world state.");
        var previous = LivingWorldCodec.Deserialize(previousJson);
        var next = LivingWorldCodec.Deserialize(nextJson);
        if (next.Facts.Count < previous.Facts.Count || !previous.Facts.SequenceEqual(next.Facts.Take(previous.Facts.Count)))
            throw new InvalidDataException("A checkpoint cannot alter committed living history.");
    }
    private static bool IsOrdered(IEnumerable<long> ids) => ids.SequenceEqual(ids.Order());
    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
