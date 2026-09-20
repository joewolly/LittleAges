[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $SourceDatabase,
    [UInt64] $Seed = 42,
    [ValidateRange(0, 500)][int] $PrepareYears = 0,
    [string] $Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory so previous evidence cannot be overwritten.' }
New-Item -ItemType Directory -Path $output | Out-Null
$project = [Security.SecurityElement]::Escape((Join-Path $repo 'src/LittleAges.Persistence/LittleAges.Persistence.csproj'))
$simulation = [Security.SecurityElement]::Escape((Join-Path $repo 'src/LittleAges.Simulation/LittleAges.Simulation.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$project"/><ProjectReference Include="$simulation"/></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $output 'Probe.csproj') -Encoding utf8
@'
using System.Diagnostics;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using LittleAges.Persistence;
using Microsoft.Data.Sqlite;

var root = args[0];
var beforePath = Path.Combine(root, "world.db");
SimulationEngine engine;
if (args[1] != "-")
{
    // SQLite backup reads a consistent snapshot, including any committed WAL.
    await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = args[1], Mode = SqliteOpenMode.ReadOnly }.ToString());
    await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = beforePath }.ToString());
    await source.OpenAsync(); await destination.OpenAsync(); source.BackupDatabase(destination);
    await destination.CloseAsync(); await source.CloseAsync();
    await using var db = await WorldDatabase.OpenAsync(beforePath);
    engine = SimulationEngine.FromPersistenceSnapshot(await db.CreateCheckpointStore().LoadAsync());
}
else engine = new SimulationEngine(new WorldSeed(ulong.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
var years = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
var preparation = Stopwatch.StartNew();
long? originDatabaseBytes = null;
if (years > 0)
{
    var originPath = Path.Combine(root, "origin.db");
    await using (var db = await WorldDatabase.OpenAsync(originPath)) await db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
    originDatabaseBytes = new FileInfo(originPath).Length;
    while (engine.CurrentMinute.Value < years * (long)WorldCalendar.MinutesPerYear)
    {
        engine.AdvanceUntil(new WorldMinute(Math.Min(engine.CurrentMinute.Value + WorldCalendar.MinutesPerYear, years * (long)WorldCalendar.MinutesPerYear)));
        Console.Error.WriteLine($"Preparing year {engine.CurrentMinute.ToCalendar().Year}: {engine.LivingPopulation} living");
    }
}
preparation.Stop();
var start = engine.CurrentMinute.Value;
var initialPopulation = engine.LivingPopulation;
var save = Stopwatch.StartNew();
await using (var db = await WorldDatabase.OpenAsync(beforePath)) await db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
save.Stop();
var beforeBytes = new FileInfo(beforePath).Length;
var eventsBefore = engine.ProcessedEventCount;
var clock = Stopwatch.StartNew();
engine.AdvanceUntil(engine.CurrentMinute.Add(10L * WorldCalendar.MinutesPerDay));
clock.Stop();
var eventCount = engine.ProcessedEventCount - eventsBefore;
var snapshotClock = Stopwatch.StartNew();
var snapshot = engine.CreatePersistenceSnapshot();
snapshotClock.Stop();
var afterPath = Path.Combine(root, "after.db");
var checkpointClock = Stopwatch.StartNew();
await using (var db = await WorldDatabase.OpenAsync(afterPath)) await db.CreateCheckpointStore().CheckpointAsync(snapshot);
checkpointClock.Stop();
SimulationEngine restored;
await using (var db = await WorldDatabase.OpenAsync(afterPath)) restored = SimulationEngine.FromPersistenceSnapshot(await db.CreateCheckpointStore().LoadAsync());
if (restored.ComputeEconomyFingerprint() != engine.ComputeEconomyFingerprint() || restored.ComputeHistoryFingerprint() != engine.ComputeHistoryFingerprint() || restored.ComputeSocialFingerprint() != engine.ComputeSocialFingerprint() || restored.ComputeAgricultureFingerprint() != engine.ComputeAgricultureFingerprint()) throw new InvalidOperationException("Benchmark checkpoint mismatch.");
using var process = Process.GetCurrentProcess();
var metrics = new { engine.SimulationRulesVersion, Seed = engine.Seed.Value, StartMinute = start, InitialPopulation = initialPopulation, FinalPopulation = engine.LivingPopulation,
    Days = 10, Events = eventCount, AdvanceMilliseconds = clock.Elapsed.TotalMilliseconds, EventsPerSecond = eventCount / clock.Elapsed.TotalSeconds,
    SnapshotMilliseconds = snapshotClock.Elapsed.TotalMilliseconds, InitialCheckpointMilliseconds = save.Elapsed.TotalMilliseconds, FinalCheckpointMilliseconds = checkpointClock.Elapsed.TotalMilliseconds,
    DatabaseBytesBefore = beforeBytes, DatabaseBytesAfter = new FileInfo(afterPath).Length, DatabaseGrowthBytes = new FileInfo(afterPath).Length - beforeBytes,
    WorkingSetBytes = process.WorkingSet64, PeakWorkingSetBytes = process.PeakWorkingSet64, ManagedHeapBytes = GC.GetTotalMemory(false), ProcessorCount = Environment.ProcessorCount,
    PreparationMilliseconds = preparation.Elapsed.TotalMilliseconds, OriginDatabaseBytes = originDatabaseBytes,
    OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(), ReopenEquivalent = true };
var json = JsonSerializer.Serialize(metrics);
await File.WriteAllTextAsync(Path.Combine(root, "metrics.json"), json);
Console.WriteLine(json);
'@ | Set-Content -LiteralPath (Join-Path $output 'Program.cs') -Encoding utf8
$source = if ($SourceDatabase) { [IO.Path]::GetFullPath($SourceDatabase) } else { '-' }
$arguments = @('run', '--project', (Join-Path $output 'Probe.csproj'), '-c', 'Release', '--', $output, $source, $Seed.ToString([Globalization.CultureInfo]::InvariantCulture), $PrepareYears.ToString([Globalization.CultureInfo]::InvariantCulture))
& $Dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Benchmark probe failed with exit code $LASTEXITCODE." }
