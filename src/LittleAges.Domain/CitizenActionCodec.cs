namespace LittleAges.Domain;

/// <summary>Preserves the action IDs of independently versioned saved worlds.</summary>
public static class CitizenActionCodec
{
    // Living Settlement and Growing Settlement both originally assigned ID 13.
    // Keep their disk and fingerprint values stable while using distinct runtime actions.
    public static int ToCanonicalValue(CitizenAction action, bool unifiedRules = false) =>
        action == CitizenAction.LivingWork ? (unifiedRules ? 16 : 13) : (int)action;

    public static CitizenAction FromCanonicalValue(int value, bool livingRules, bool unifiedRules = false)
    {
        if (value < 0 || value > (unifiedRules ? 16 : livingRules ? 13 : 15))
            throw new ArgumentOutOfRangeException(nameof(value), "Unsupported persisted citizen action.");
        if (unifiedRules && value == 16) return CitizenAction.LivingWork;
        return livingRules && !unifiedRules && value == 13 ? CitizenAction.LivingWork : (CitizenAction)value;
    }
}
