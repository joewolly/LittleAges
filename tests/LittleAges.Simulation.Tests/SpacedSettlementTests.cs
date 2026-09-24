using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class SpacedSettlementTests
{
    [Fact]
    public void FreshConstructionAndFarmsKeepAWalkableGap()
    {
        var newWorld = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.SpacedSimulationRulesVersion);
        var select = typeof(SimulationEngine).GetMethod("SelectConstructionSite", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null)!;

        var newSite = Assert.IsType<TileCoordinate>(select.Invoke(newWorld, null));
        var start = newWorld.World.StartingSite;
        Assert.True(Math.Max(Math.Abs(newSite.X - start.X), Math.Abs(newSite.Y - start.Y)) >= 2);

        newWorld.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));
        var project = Assert.Single(newWorld.Structures, x => x.Status == StructureStatus.UnderConstruction);
        Assert.Equal(newSite, project.Location);
        var next = Assert.IsType<TileCoordinate>(select.Invoke(newWorld, null));
        Assert.True(Math.Max(Math.Abs(next.X - newSite.X), Math.Abs(next.Y - newSite.Y)) >= 2);
        var selectFarm = typeof(SimulationEngine).GetMethod("SelectFarmSite", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null)!;
        var farmSite = Assert.IsType<TileCoordinate>(selectFarm.Invoke(newWorld, null));
        Assert.True(Math.Max(Math.Abs(farmSite.X - start.X), Math.Abs(farmSite.Y - start.Y)) >= 2);
        Assert.True(Math.Max(Math.Abs(farmSite.X - newSite.X), Math.Abs(farmSite.Y - newSite.Y)) >= 2);
        Assert.Equal(SimulationEngine.SpacedSimulationRulesVersion, new SimulationEngine(newWorld.CreatePersistenceSnapshot()).SimulationRulesVersion);
    }
}
