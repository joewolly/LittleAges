namespace LittleAges.Domain;

/// <summary>Canonical sparse social edge. The persisted orientation is always A &lt; B.</summary>
public sealed record RelationshipState(CitizenId CitizenAId, CitizenId CitizenBId, int Familiarity, int Affinity, int Trust, int Conflict, long LastInteractionMinute, long InteractionCount)
{
    public RelationshipState Validate()
    {
        if (CitizenAId.Value >= CitizenBId.Value || Familiarity is < 0 or > 10000 || Affinity is < -10000 or > 10000 || Trust is < 0 or > 10000 || Conflict is < 0 or > 10000 || LastInteractionMinute < 0 || InteractionCount < 1)
            throw new ArgumentException("Relationship state is not canonical.");
        return this;
    }

    public static (CitizenId A, CitizenId B) Normalize(CitizenId first, CitizenId second)
    {
        if (first == second) throw new ArgumentException("A relationship cannot reference itself.");
        return first.Value < second.Value ? (first, second) : (second, first);
    }
}

public sealed class Household
{
    public Household(HouseholdId id, long createdMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(createdMinute);
        Id = id; CreatedMinute = createdMinute;
    }
    public HouseholdId Id { get; }
    public HouseholdId HouseholdId => Id;
    public long CreatedMinute { get; }
    public long? DissolvedMinute { get; set; }
    public StructureId? DwellingStructureId { get; set; }
}

public static class RelationshipLabels
{
    public const string Partner = "Partner";
    public const string Family = "Family";
    public const string Rival = "Rival";
    public const string CloseFriend = "Close Friend";
    public const string Friend = "Friend";
    public const string Acquaintance = "Acquaintance";
    public const string Stranger = "Stranger";

    public static string Derive(RelationshipState? relationship, bool isPartner, bool isFamily)
    {
        if (isPartner) return Partner;
        if (isFamily) return Family;
        if (relationship is null) return Stranger;
        if (relationship.Conflict >= 2000 && relationship.Affinity <= 0) return Rival;
        if (relationship.Familiarity >= 6000 && relationship.Affinity >= 5000 && relationship.Trust >= 4500 && relationship.Conflict < 1500) return CloseFriend;
        if (relationship.Familiarity >= 2500 && relationship.Affinity >= 2000 && relationship.Trust >= 1500 && relationship.Conflict < 2000) return Friend;
        return relationship.Familiarity >= 500 ? Acquaintance : Stranger;
    }
}

public static class LifeStages
{
    public static int ProductivityBasisPoints(int age) => age switch { <= 5 => 0, <= 12 => 5000, <= 17 => 7500, <= 59 => 10000, _ => 7500 };
}
