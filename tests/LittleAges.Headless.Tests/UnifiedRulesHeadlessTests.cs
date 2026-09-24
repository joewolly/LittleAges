using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Headless.Tests;

public sealed class UnifiedRulesHeadlessTests
{
    [Fact]
    public void M13RemainsSelectableByHeadless()
    {
        var acceptance = HeadlessCommandLine.Parse(["acceptance", "--rules", SimulationEngine.UnifiedSimulationRulesVersion,
            "--years", "10", "--checkpoint-year", "5"]);
        var diagnostic = HeadlessCommandLine.Parse(["run", "--rules", SimulationEngine.UnifiedSimulationRulesVersion,
            "--living-diagnostic", "monthly", "--diagnostic-end-year", "1"]);

        Assert.True(acceptance.Succeeded, acceptance.Error);
        Assert.Equal(SimulationEngine.UnifiedSimulationRulesVersion, acceptance.Options!.Rules);
        Assert.True(diagnostic.Succeeded, diagnostic.Error);
        Assert.Equal(SimulationEngine.UnifiedSimulationRulesVersion, diagnostic.Options!.Rules);
    }

    [Fact]
    public void M13HeadlessRunPassesMandatoryInvariants()
    {
        var options = HeadlessOptions.Default(HeadlessCommand.Run) with { Rules = SimulationEngine.UnifiedSimulationRulesVersion };
        var report = HeadlessRunner.Run(options);

        Assert.Equal(SimulationEngine.UnifiedSimulationRulesVersion, report.Rules);
        Assert.True(report.MandatoryInvariantsPassed,
            string.Join("; ", report.Invariants.Where(x => !x.Passed).Select(x => $"{x.Name}: {x.Details}")));
    }
}
