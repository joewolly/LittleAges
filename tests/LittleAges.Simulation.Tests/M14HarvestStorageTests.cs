using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M14HarvestStorageTests
{
    [Fact]
    public void HarvestLoadsLeaveRoomForCargoAlreadyInTransit()
    {
        // Seed 42 reached day 190 with stone gatherers carrying 44 units toward a nearly full
        // stockpile; a 40-unit harvest then claimed the same free space and every checkpoint
        // failed validation until the loads were delivered.
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        for (var minute = 189L * WorldCalendar.MinutesPerDay; minute <= 192L * WorldCalendar.MinutesPerDay; minute += 30)
        {
            engine.AdvanceUntil(new WorldMinute(minute));
            MigrationValidation.Validate(engine.CreatePersistenceSnapshot());
            if (minute < 274117 && minute + 30 > 274117)
            {
                engine.AdvanceUntil(new WorldMinute(274117));
                MigrationValidation.Validate(engine.CreatePersistenceSnapshot());
            }
        }
    }
}
