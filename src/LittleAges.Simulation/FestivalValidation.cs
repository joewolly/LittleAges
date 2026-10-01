using System.Diagnostics.CodeAnalysis;
using LittleAges.Domain;

namespace LittleAges.Simulation;

public static class FestivalValidation
{
    public static void Validate(SimulationPersistenceSnapshot snapshot, LivingWorldState living)
    {
        if (!SimulationEngine.FestivalSystemsEnabled(snapshot.SimulationRulesVersion))
        {
            Require(living.Festivals is null && living.FestivalVisit is null && living.Orders.All(x => x.Kind != LivingWorkKind.AttendFestival) && living.People.All(x => x.Experiences.All(y => y.Kind != LivingExperienceKind.Festival)), "Festival state requires M16 rules.");
            return;
        }
        Require(living.Festivals is not null, "M16 festival state is required.");
        var festivals = living.Festivals!;
        var citizens = snapshot.Citizens.ToDictionary(x => x.Id.Value);
        var minute = snapshot.WorldMinute.Value;
        Require(festivals.All(x => x is not null), "Festival records cannot be null.");
        Require(festivals.Select(x => x.SettlementId).SequenceEqual(festivals.Select(x => x.SettlementId).Distinct().Order()), "Festival sites must be unique and ordered.");
        foreach (var festival in festivals)
        {
            Require(festival.Attendance is not null && festival.StrengthenedPairs is not null && festival.Attendance.All(x => x is not null) && festival.StrengthenedPairs.All(x => x is not null), "Festival attendance and relationship collections are required.");
            Require(festival.SettlementId == 1 || festival.SettlementId == 2 && snapshot.MigrationState?.DaughterSettlement is not null, "Unknown festival site.");
            var site = festival.SettlementId == 1 ? snapshot.World!.StartingSite : snapshot.MigrationState!.DaughterSettlement!.Site;
            Require(festival.Location == site && festival.Year >= 0 && festival.StartMinute == FestivalRules.Start(festival.SettlementId, festival.Year) && festival.EndMinute == festival.StartMinute + FestivalRules.DurationMinutes, "Festival schedule or location is invalid.");
            Require(Enum.IsDefined(festival.Mode) && (!festival.Started || minute >= festival.StartMinute) && (!festival.Finished || minute >= festival.EndMinute) && (festival.Finished || minute < festival.EndMinute), "Festival phase is invalid.");
            Require(festival.InitialFood >= 0 && festival.ReservedFood >= 0 && festival.ConsumedFood >= 0 && festival.InitialFood % 10 == 0 && festival.ReservedFood % 10 == 0 && festival.ConsumedFood % 10 == 0 && (festival.Finished ? festival.ReservedFood == 0 && festival.InitialFood >= festival.ConsumedFood : festival.InitialFood == festival.ReservedFood + festival.ConsumedFood), "Festival food is not conserved.");
            Require(festival.Mode != FestivalMode.Gathering || festival.InitialFood == 0, "A gathering cannot spend feast food.");
            Require(festival.Started || festival.InitialFood == 0 && festival.Attendance.Count == 0 && festival.StrengthenedPairs.Count == 0, "An unopened festival cannot have attendance.");
            Require(festival.Attendance.Select(x => x.CitizenId).SequenceEqual(festival.Attendance.Select(x => x.CitizenId).Distinct().Order()), "Festival attendance must be unique and ordered.");
            foreach (var attendance in festival.Attendance)
                Require(citizens.TryGetValue(attendance.CitizenId, out var attendee) && attendee.BirthMinute <= festival.EndMinute - 6L * WorldCalendar.MinutesPerYear && (attendee.DeathMinute is null || attendee.DeathMinute >= festival.StartMinute) && attendance.Minutes is > 0 and <= FestivalRules.DurationMinutes && attendance.BenefitsGranted == (attendance.Minutes >= FestivalRules.AttendanceMinutes) && (!attendance.PortionConsumed || attendance.BenefitsGranted) && (!attendance.RelationshipGranted || attendance.BenefitsGranted), "Festival attendee or reward is invalid.");
            Require(festival.ConsumedFood == festival.Attendance.Count(x => x.PortionConsumed) * CitizenSimulationRules.MealFoodUnits, "Festival meal accounting disagrees with attendance.");
            var qualified = festival.Attendance.Where(x => x.BenefitsGranted).Select(x => x.CitizenId).ToHashSet();
            Require(festival.StrengthenedPairs.SequenceEqual(festival.StrengthenedPairs.Distinct().OrderBy(x => x.FirstId).ThenBy(x => x.SecondId)) && festival.StrengthenedPairs.All(x => x.FirstId < x.SecondId && qualified.Contains(x.FirstId) && qualified.Contains(x.SecondId)), "Festival relationship pairs are invalid.");
        }
        foreach (var order in living.Orders.Where(x => x.Kind == LivingWorkKind.AttendFestival))
            Require(order.SubjectId is { } id && citizens.ContainsKey(id) && !order.Produced && order.Ingredients.Count == 0 && order.Cargo.Count == 0 && festivals.Any(x => x.Started && !x.Finished && x.StartMinute <= minute && minute < x.EndMinute && x.Location == order.Location), "Festival attendance order is invalid.");
        if (living.FestivalVisit is { } visit)
            Require(visit.Year >= 0 && citizens.ContainsKey(visit.CitizenId) && citizens.ContainsKey(visit.RelativeId) && visit.CitizenId != visit.RelativeId && visit.SettlementId is 1 or 2 && visit.StartMinute == FestivalRules.Start(visit.SettlementId, visit.Year) && visit.DepartMinute >= 0 && visit.DepartMinute < visit.StartMinute && visit.LastAttendanceMinute >= 0 && visit.LastAttendanceMinute <= minute && (visit.PartyId is null || visit.PartyId > 0 && visit.PartyId < snapshot.Counters.NextEntityId), "Festival visit plan is invalid.");
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
