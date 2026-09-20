namespace LittleAges.Domain;

/// <summary>Preserves the action IDs of independently versioned saved worlds.</summary>
public static class CitizenActionCodec
{
    // Living Settlement and Growing Settlement both originally assigned ID 13.
    // Keep their disk and fingerprint values stable while using distinct runtime actions.
    public static int ToCanonicalValue(CitizenAction action) =>
        action == CitizenAction.LivingWork ? 13 : (int)action;

    public static CitizenAction FromCanonicalValue(int value, bool livingRules)
    {
        if (value < 0 || value > (livingRules ? 13 : 15))
            throw new ArgumentOutOfRangeException(nameof(value), "Unsupported persisted citizen action.");
        return livingRules && value == 13 ? CitizenAction.LivingWork : (CitizenAction)value;
    }
}
