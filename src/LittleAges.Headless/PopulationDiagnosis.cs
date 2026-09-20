using LittleAges.Domain;
using LittleAges.Simulation;

namespace LittleAges.Headless;

public sealed record PopulationCensus(long Minute, int Living, int Births, int PartneredAdults,
    int UnpartneredAdults, int Housed, int Food, int ShelterCapacity,
    IReadOnlyDictionary<string, int> Ages, IReadOnlyDictionary<string, int> DeathCauses,
    IReadOnlyDictionary<string, int> ReproductionBlockers, int ReadyHouseholds);

public sealed record PopulationDiagnosisReport(ulong Seed, string Rules, int Years,
    string Sampling, IReadOnlyList<PopulationCensus> Samples, string SocialFingerprint, string HistoryFingerprint);

/// <summary>Monthly, observational census. Sampling never participates in canonical decisions.</summary>
public static class PopulationDiagnosis
{
    public static PopulationDiagnosisReport Run(HeadlessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var engine = new SimulationEngine(new WorldSeed(options.Seed), simulationRulesVersion: options.Rules);
        var samples = new List<PopulationCensus> { Capture(engine) };
        var target = checked((long)options.Years * WorldCalendar.MinutesPerYear);
        const long month = 30L * WorldCalendar.MinutesPerDay;
        while (engine.CurrentMinute.Value < target)
        {
            engine.AdvanceUntil(new WorldMinute(Math.Min(target, engine.CurrentMinute.Value + month)));
            samples.Add(Capture(engine));
            Console.Error.WriteLine($"seed={options.Seed} rules={options.Rules} month={engine.CurrentMinute.Value / month} living={engine.LivingPopulation}");
        }
        // Validate the complete canonical state, including history, before reporting success.
        _ = engine.CreatePersistenceSnapshot();
        return new(options.Seed, options.Rules, options.Years, "monthly boundary observations; blocker counts overlap", samples,
            engine.ComputeSocialFingerprint(), engine.ComputeHistoryFingerprint());
    }

    public static PopulationCensus Capture(SimulationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var citizens = engine.Citizens;
        var living = citizens.Where(c => c.IsAlive).ToArray();
        var opportunities = engine.CaptureFamilyCheckCounterfactualDiagnostic().Opportunities;
        return new(engine.CurrentMinute.Value, living.Length, citizens.Count(c => c.ParentAId is not null),
            living.Count(c => c.AgeYears(engine.CurrentMinute) >= 18 && c.PartnerId is not null),
            living.Count(c => c.AgeYears(engine.CurrentMinute) >= 18 && c.PartnerId is null),
            living.Count(c => c.HomeStructureId is not null), engine.Settlement.FoodStored, engine.ShelterCapacity,
            living.GroupBy(c => c.LifeStage(engine.CurrentMinute)).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            citizens.Where(c => !c.IsAlive).GroupBy(c => c.DeathCause ?? "unknown").OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            Enum.GetValues<FamilyCheckBlocker>().Where(b => b != FamilyCheckBlocker.None).ToDictionary(b => b.ToString(), b => opportunities.Count(o => (o.Blockers & b) != 0), StringComparer.Ordinal),
            opportunities.Count(o => o.Blockers == FamilyCheckBlocker.None));
    }
}
