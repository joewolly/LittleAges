using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleAges.Domain;

public enum NewcomerPhase { Approaching = 1, Visiting, Leaving, Resident, Departed, Dead }

/// <summary>External people are independent travelers until admission into the resident roster.</summary>
public sealed class NewcomersWorldState
{
    public int Version { get; set; } = 1;
    public long LastAttemptYear { get; set; } = -1;
    public List<NewcomerState> Visitors { get; set; } = [];
}

public sealed class NewcomerState
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long CitizenId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long HostSettlementId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long ShelterStructureId { get; set; }
    public TileCoordinate EntryTile { get; set; }
    /// <summary>First appearance at the world edge; VisitingStartedMinute records physical arrival at the host.</summary>
    public long ArrivalMinute { get; set; }
    public long? VisitingStartedMinute { get; set; }
    public long? StayDeadlineMinute { get; set; }
    public long? JoinedMinute { get; set; }
    public long? DepartedMinute { get; set; }
    public long? DeathMinute { get; set; }
    public string? ExitReason { get; set; }
    public NewcomerPhase Phase { get; set; } = NewcomerPhase.Approaching;
    [JsonConverter(typeof(NewcomerCitizenJsonConverter))]
    public Citizen? Person { get; set; }
    public List<TileCoordinate> Route { get; set; } = [];
    public int RouteIndex { get; set; }
    public long SegmentStartedMinute { get; set; }
    public long? NextStepMinute { get; set; }
    public int InitialProvisions { get; set; }
    public int ProvisionsRemaining { get; set; }
    public int ProvisionsConsumed { get; set; }
    public int ProvisionsTransferred { get; set; }
    public int ProvisionsExported { get; set; }
    public int ProvisionsLost { get; set; }
    [JsonConverter(typeof(NewcomerContactsJsonConverter))]
    public List<RelationshipState> Contacts { get; set; } = [];

    public void ValidateProvisions()
    {
        if (InitialProvisions <= 0 || new[] { ProvisionsRemaining, ProvisionsConsumed, ProvisionsTransferred, ProvisionsExported, ProvisionsLost }.Any(x => x < 0) ||
            (long)InitialProvisions != (long)ProvisionsRemaining + ProvisionsConsumed + ProvisionsTransferred + ProvisionsExported + ProvisionsLost)
            throw new ArgumentException("Traveler provisions must balance their external source, consumption, transfer, departure, and death losses.");
        if (JoinedMinute is null && ProvisionsTransferred != 0 || JoinedMinute is not null && (ProvisionsRemaining != 0 || ProvisionsExported != 0 || ProvisionsLost != 0) ||
            Phase != NewcomerPhase.Departed && ProvisionsExported != 0 || Phase != NewcomerPhase.Dead && ProvisionsLost != 0)
            throw new ArgumentException("Traveler provisions disagree with their lifecycle transition.");
    }
}

/// <summary>A scoped contract avoids changing serialization of any pre-M17 canonical component.</summary>
public sealed class NewcomerCitizenJsonConverter : JsonConverter<Citizen>
{
    public override Citizen Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var p = JsonSerializer.Deserialize<PersonData>(ref reader, options) ?? throw new JsonException("Missing traveler biography.");
        if (p.Traits is null || p.Skills is null || p.Needs is null) throw new JsonException("Traveler traits, skills, and needs are required.");
        return new Citizen(new CitizenId(p.Id), (int?)null, p.GivenName, p.FamilyName, p.BirthMinute, p.Location, p.Traits, p.Skills, p.Needs)
        {
            Health = p.Health, DeathMinute = p.DeathMinute, DeathCause = p.DeathCause, CurrentAction = p.CurrentAction,
            ActionPhase = p.ActionPhase, ActionSequence = p.ActionSequence, ActionStartedMinute = p.ActionStartedMinute is { } start ? new(start) : null,
            ActionCompletesMinute = p.ActionCompletesMinute is { } end ? new(end) : null, ActionTarget = p.ActionTarget,
            TargetCitizenId = p.TargetCitizenId is { } target ? new(target) : null, NeedsUpdatedMinute = p.NeedsUpdatedMinute,
            HealthUpdatedMinute = p.HealthUpdatedMinute, LifetimeMovementSteps = p.LifetimeMovementSteps, LifetimeMovementCost = p.LifetimeMovementCost
        };
    }

    public override void Write(Utf8JsonWriter writer, Citizen value, JsonSerializerOptions options)
    {
        if (value.FounderOrdinal is not null || value.ParentAId is not null || value.ParentBId is not null || value.PartnerId is not null ||
            value.HouseholdId is not null || value.HomeStructureId is not null || value.TargetStructureId is not null || value.TargetResourceNodeId is not null ||
            value.CarriedResourceType is not null || value.CarriedResourceQuantity != 0 || value.LifetimeForagingMinutes != 0 || value.LifetimeWoodcuttingMinutes != 0 ||
            value.LifetimeStoneworkingMinutes != 0 || value.LifetimeConstructionMinutes != 0 || value.LifetimeHaulingMinutes != 0)
            throw new JsonException("An external biography cannot contain resident ancestry, ownership, production, or cargo.");
        JsonSerializer.Serialize(writer, new PersonData(value.Id.Value, value.GivenName, value.FamilyName, value.BirthMinute, value.Location,
            value.Traits, value.Skills, value.Needs, value.Health, value.DeathMinute, value.DeathCause, value.CurrentAction, value.ActionPhase,
            value.ActionSequence, value.ActionStartedMinute?.Value, value.ActionCompletesMinute?.Value, value.ActionTarget, value.TargetCitizenId?.Value,
            value.NeedsUpdatedMinute, value.HealthUpdatedMinute, value.LifetimeMovementSteps, value.LifetimeMovementCost), options);
    }

    private sealed record PersonData(
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Id,
        string GivenName, string FamilyName, long BirthMinute, TileCoordinate Location, CitizenTraits Traits, CitizenSkills Skills, CitizenNeeds Needs,
        int Health, long? DeathMinute, string? DeathCause, CitizenAction CurrentAction, CitizenActionPhase ActionPhase, long ActionSequence,
        long? ActionStartedMinute, long? ActionCompletesMinute, TileCoordinate? ActionTarget,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? TargetCitizenId,
        long NeedsUpdatedMinute, long HealthUpdatedMinute, long LifetimeMovementSteps, long LifetimeMovementCost);
}

public sealed class NewcomerContactsJsonConverter : JsonConverter<List<RelationshipState>>
{
    public override List<RelationshipState> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        (JsonSerializer.Deserialize<List<ContactData>>(ref reader, options) ?? throw new JsonException("Missing traveler contacts."))
        .Select(p => new RelationshipState(new CitizenId(p.CitizenAId), new CitizenId(p.CitizenBId), p.Familiarity, p.Affinity, p.Trust, p.Conflict, p.LastInteractionMinute, p.InteractionCount)).ToList();

    public override void Write(Utf8JsonWriter writer, List<RelationshipState> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Select(p => new ContactData(p.CitizenAId.Value, p.CitizenBId.Value, p.Familiarity, p.Affinity, p.Trust, p.Conflict, p.LastInteractionMinute, p.InteractionCount)).ToList(), options);

    private sealed record ContactData(
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long CitizenAId,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long CitizenBId,
        int Familiarity, int Affinity, int Trust, int Conflict, long LastInteractionMinute, long InteractionCount);
}
