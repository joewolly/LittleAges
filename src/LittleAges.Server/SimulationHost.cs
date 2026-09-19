using System.Threading.Channels;
using System.Globalization;
using System.Diagnostics;
using System.Collections.ObjectModel;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LittleAges.Server;

public enum SimulationHostState
{
    Starting,
    Running,
    Stopping,
    Faulted
}

public enum PersistenceState
{
    Healthy,
    Degraded,
    Faulted
}

public sealed record ServerStatusSnapshot(
    SimulationHostState State,
    long WorldMinute,
    int PendingEventCount,
    string WorldSeed,
    string? Error,
    WorldSummarySnapshot? World = null,
    int Population = 0,
    int TotalPopulation = 0,
    int LivingPopulation = 0,
    int DeadPopulation = 0,
    PersistenceState PersistenceState = PersistenceState.Healthy,
    long? LastSuccessfulCheckpointWorldMinute = null,
    DateTime? LastSuccessfulCheckpointUtc = null,
    int ConsecutiveCheckpointFailures = 0,
    bool Paused = false,
    double OperationalSpeed = 0);

public sealed record ServerNeedsSnapshot(int Hunger, int Rest, int Shelter, int Social);

public sealed record ServerTraitsSnapshot(
    int Industriousness,
    int Sociability,
    int Curiosity,
    int Cooperativeness,
    int RiskTolerance,
    int Resilience);

public sealed record ServerSkillsSnapshot(
    int Foraging,
    int Woodcutting,
    int Stoneworking,
    int Construction,
    int Hauling,
    int Domestic);

public sealed record ServerLifetimeWorkActivitySnapshot(
    long ForagingMinutes,
    long WoodcuttingMinutes,
    long StoneworkingMinutes,
    long ConstructionMinutes,
    long HaulingMinutes);

public sealed record ServerCitizenMovementWaypointSnapshot(int X, int Y, long ArriveMinute);
public sealed record ServerCitizenMovementPlanSnapshot(long ActionSequence, long ObservedMinute, IReadOnlyList<ServerCitizenMovementWaypointSnapshot> Waypoints, long? SegmentStartedMinute = null);

/// <summary>Immutable server-owned citizen read model. It never exposes domain mutable records.</summary>
public sealed record ServerCitizenSnapshot
{
    public ServerCitizenSnapshot(CitizenReadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CitizenId = snapshot.CitizenId;
        FounderOrdinal = snapshot.FounderOrdinal;
        GivenName = snapshot.GivenName;
        FamilyName = snapshot.FamilyName;
        Name = snapshot.Name;
        Age = snapshot.Age;
        LifeStage = snapshot.LifeStage;
        Location = snapshot.Location;
        Health = snapshot.Health;
        ProjectedNeeds = new ServerNeedsSnapshot(snapshot.ProjectedNeeds.Hunger, snapshot.ProjectedNeeds.Rest, snapshot.ProjectedNeeds.Shelter, snapshot.ProjectedNeeds.Social);
        Traits = new ServerTraitsSnapshot(snapshot.Traits.Industriousness, snapshot.Traits.Sociability, snapshot.Traits.Curiosity, snapshot.Traits.Cooperativeness, snapshot.Traits.RiskTolerance, snapshot.Traits.Resilience);
        Skills = new ServerSkillsSnapshot(snapshot.Skills.Foraging, snapshot.Skills.Woodcutting, snapshot.Skills.Stoneworking, snapshot.Skills.Construction, snapshot.Skills.Hauling, snapshot.Skills.Domestic);
        CurrentAction = snapshot.CurrentAction;
        ActionStartedMinute = snapshot.ActionStartedMinute;
        ActionCompletesMinute = snapshot.ActionCompletesMinute;
        Target = snapshot.Target;
        ActionSequence = snapshot.ActionSequence;
        IsAlive = snapshot.IsAlive;
        DeathCause = snapshot.DeathCause;
        CarriedResource = snapshot.CarriedResourceType;
        CarriedQuantity = snapshot.CarriedResourceQuantity == 0 ? null : snapshot.CarriedResourceQuantity;
        TargetResourceNodeId = snapshot.TargetResourceNodeId?.Value.ToString(CultureInfo.InvariantCulture);
        ActionPhase = snapshot.ActionPhase;
        DeathMinute = snapshot.DeathMinute?.Value;
        HomeStructureId = snapshot.HomeStructureId?.Value.ToString(CultureInfo.InvariantCulture);
        TargetStructureId = snapshot.TargetStructureId?.Value.ToString(CultureInfo.InvariantCulture);
        Occupation = snapshot.Occupation;
        LifetimeWorkActivity = new ServerLifetimeWorkActivitySnapshot(
            snapshot.LifetimeForagingMinutes,
            snapshot.LifetimeWoodcuttingMinutes,
            snapshot.LifetimeStoneworkingMinutes,
            snapshot.LifetimeConstructionMinutes,
            snapshot.LifetimeHaulingMinutes);
        ParentAId = snapshot.ParentAId?.Value.ToString(CultureInfo.InvariantCulture);
        ParentBId = snapshot.ParentBId?.Value.ToString(CultureInfo.InvariantCulture);
        PartnerId = snapshot.PartnerId?.Value.ToString(CultureInfo.InvariantCulture);
        HouseholdId = snapshot.HouseholdId?.Value.ToString(CultureInfo.InvariantCulture);
        ChildrenIds = Array.AsReadOnly((snapshot.ChildrenIds ?? Array.Empty<string>()).OrderBy(static value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray());
        TargetCitizenId = snapshot.TargetCitizenId?.Value.ToString(CultureInfo.InvariantCulture);
        BirthMinute = snapshot.BirthMinute;
        MovementPlan = snapshot.MovementPlan is null ? null : new ServerCitizenMovementPlanSnapshot(
            snapshot.MovementPlan.ActionSequence,
            snapshot.MovementPlan.ObservedMinute.Value,
            Array.AsReadOnly(snapshot.MovementPlan.Waypoints.Select(static waypoint => new ServerCitizenMovementWaypointSnapshot(waypoint.Location.X, waypoint.Location.Y, waypoint.ArriveMinute.Value)).ToArray()),
            snapshot.MovementPlan.SegmentStartedMinute.Value);
    }

    public string CitizenId { get; }
    public int? FounderOrdinal { get; }
    public string GivenName { get; }
    public string FamilyName { get; }
    public string Name { get; }
    public int Age { get; }
    public string LifeStage { get; }
    public TileCoordinate Location { get; }
    public int Health { get; }
    public ServerNeedsSnapshot ProjectedNeeds { get; }
    public int Hunger => ProjectedNeeds.Hunger;
    public int Rest => ProjectedNeeds.Rest;
    public int Shelter => ProjectedNeeds.Shelter;
    public int Social => ProjectedNeeds.Social;
    public ServerTraitsSnapshot Traits { get; }
    public ServerSkillsSnapshot Skills { get; }
    public CitizenAction CurrentAction { get; }
    public WorldMinute? ActionStartedMinute { get; }
    public WorldMinute? ActionCompletesMinute { get; }
    public TileCoordinate? Target { get; }
    public long ActionSequence { get; }
    public bool IsAlive { get; }
    public string? DeathCause { get; }
    public ResourceType? CarriedResource { get; }
    public int? CarriedQuantity { get; }
    public ResourceType? CarriedResourceType => CarriedResource;
    public int CarriedResourceQuantity => CarriedQuantity ?? 0;
    public string? TargetResourceNodeId { get; }
    public CitizenActionPhase ActionPhase { get; }
    public long? DeathMinute { get; }
    public string? HomeStructureId { get; }
    public string? TargetStructureId { get; }
    public string Occupation { get; }
    public ServerLifetimeWorkActivitySnapshot LifetimeWorkActivity { get; }
    public string? ParentAId { get; }
    public string? ParentBId { get; }
    public string? PartnerId { get; }
    public string? HouseholdId { get; }
    public IReadOnlyList<string> ChildrenIds { get; }
    public string? TargetCitizenId { get; }
    public long BirthMinute { get; }
    public ServerCitizenMovementPlanSnapshot? MovementPlan { get; }
}

public sealed record ServerRelationshipSnapshot(string OtherCitizenId, string OtherCitizenName, int Familiarity, int Affinity, int Trust, int Conflict, long LastInteractionMinute, long InteractionCount, string Label);
public sealed record ServerHouseholdSnapshot
{
    public ServerHouseholdSnapshot(string householdId, long createdMinute, long? dissolvedMinute, string? dwellingStructureId, IEnumerable<string> memberIds, IEnumerable<string> livingMemberIds, IEnumerable<string>? partnerPair, IEnumerable<string> childrenIds)
    {
        HouseholdId = householdId;
        CreatedMinute = createdMinute;
        DissolvedMinute = dissolvedMinute;
        DwellingStructureId = dwellingStructureId;
        MemberIds = CopyIds(memberIds);
        LivingMemberIds = CopyIds(livingMemberIds);
        PartnerPair = partnerPair is null ? null : CopyIds(partnerPair);
        ChildrenIds = CopyIds(childrenIds);
    }

    public string HouseholdId { get; }
    public long CreatedMinute { get; }
    public long? DissolvedMinute { get; }
    public string? DwellingStructureId { get; }
    public IReadOnlyList<string> MemberIds { get; }
    public IReadOnlyList<string> LivingMemberIds { get; }
    public IReadOnlyList<string>? PartnerPair { get; }
    public IReadOnlyList<string> ChildrenIds { get; }

    private static ReadOnlyCollection<string> CopyIds(IEnumerable<string> ids) => Array.AsReadOnly((ids ?? throw new ArgumentNullException(nameof(ids))).ToArray());
}

public sealed record ServerResourceQuantitySnapshot(ResourceType ResourceType, int Quantity);

public sealed record ServerResourceNodeSnapshot(string ResourceNodeId, ResourceType ResourceType, int CurrentQuantity);

/// <summary>Immutable visual definition for a canonical resource node.</summary>
public sealed record ServerMapResourceSnapshot(
    string ResourceNodeId,
    ResourceType ResourceType,
    WorldStartingSiteSnapshot Location,
    int MaximumQuantity,
    int RegenerationPotential);

public sealed record ServerStructureContributionSnapshot(
    string CitizenId,
    int ConstructionWork,
    int WoodDelivered,
    int StoneDelivered);

/// <summary>Immutable, compact server-owned structure read model.</summary>
public sealed record ServerStructureSnapshot
{
    public ServerStructureSnapshot(
        Structure structure,
        IEnumerable<string>? currentOccupantIds = null,
        IEnumerable<ServerStructureContributionSnapshot>? contributions = null)
    {
        ArgumentNullException.ThrowIfNull(structure);
        StructureId = structure.Id.Value.ToString(CultureInfo.InvariantCulture);
        Type = structure.Type;
        Status = structure.Status;
        Location = structure.Location;
        StartedMinute = structure.ConstructionStartedMinute;
        CompletedMinute = structure.CompletedMinute;
        RequiredWood = structure.RequiredWood;
        DeliveredWood = structure.DeliveredWood;
        RequiredStone = structure.RequiredStone;
        DeliveredStone = structure.DeliveredStone;
        RequiredWork = structure.RequiredWork;
        CompletedWork = structure.CompletedWork;
        Condition = structure.Condition;
        Capacity = structure.Type == StructureType.Shelter ? CitizenSimulationRules.ShelterCapacityPerBuilding : null;
        StorageBonus = structure.Type == StructureType.Stockpile ? CitizenSimulationRules.StockpileStorageBonus : null;
        ConstructionMultiplierBasisPoints = structure.Type == StructureType.Workshop ? CitizenSimulationRules.WorkshopConstructionMultiplierBasisPoints : null;
        CurrentOccupantIds = Array.AsReadOnly((currentOccupantIds ?? Array.Empty<string>())
            .OrderBy(static id => long.Parse(id, CultureInfo.InvariantCulture))
            .ToArray());
        Contributions = Array.AsReadOnly((contributions ?? Array.Empty<ServerStructureContributionSnapshot>())
            .OrderBy(static contribution => long.Parse(contribution.CitizenId, CultureInfo.InvariantCulture))
            .ToArray());
    }

    public string StructureId { get; }
    public StructureType Type { get; }
    public StructureStatus Status { get; }
    public TileCoordinate Location { get; }
    public long StartedMinute { get; }
    public long? CompletedMinute { get; }
    public int RequiredWood { get; }
    public int DeliveredWood { get; }
    public int RequiredStone { get; }
    public int DeliveredStone { get; }
    public int RequiredWork { get; }
    public int CompletedWork { get; }
    public int Condition { get; }
    public int? Capacity { get; }
    public int? StorageBonus { get; }
    public int? ConstructionMultiplierBasisPoints { get; }
    public IReadOnlyList<string> CurrentOccupantIds { get; }
    public IReadOnlyList<ServerStructureContributionSnapshot> Contributions { get; }
}

public sealed record ServerMapSnapshot
{
    public ServerMapSnapshot(WorldMap world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Width = world.Width;
        Height = world.Height;
        Terrain = Array.AsReadOnly(world.Tiles.Select(static tile => (int)tile.Terrain).ToArray());
        Elevation = Array.AsReadOnly(world.Tiles.Select(static tile => tile.Elevation).ToArray());
        Resources = Array.AsReadOnly(world.Resources
            .OrderBy(static node => node.Id.Value)
            .Select(static node => new ServerMapResourceSnapshot(
                node.Id.Value.ToString(CultureInfo.InvariantCulture),
                node.Type,
                new WorldStartingSiteSnapshot(node.Coordinate.X, node.Coordinate.Y),
                node.MaximumQuantity,
                node.RegenerationPotential))
            .ToArray());
        StartingSite = new WorldStartingSiteSnapshot(world.StartingSite.X, world.StartingSite.Y);
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<int> Terrain { get; }
    public IReadOnlyList<int> Elevation { get; }
    public IReadOnlyList<ServerMapResourceSnapshot> Resources { get; }
    public WorldStartingSiteSnapshot StartingSite { get; }
}

/// <summary>Immutable settlement and resource read model published with the citizen roster.</summary>
public sealed record ServerSettlementSnapshot
{
    public ServerSettlementSnapshot(
        int foodStored,
        int woodStored,
        int stoneStored,
        int livingPopulation,
        int deadPopulation,
        IReadOnlyList<ServerResourceQuantitySnapshot>? remainingResources = null,
        IReadOnlyList<ServerResourceNodeSnapshot>? resources = null,
        int storageCapacity = 0,
        int storageUsed = 0,
        int shelterCapacity = 0,
        int shelteredPopulation = 0,
        int unhousedPopulation = 0,
        int completedShelters = 0,
        int completedStockpiles = 0,
        int completedWorkshops = 0,
        long exposureGraceUntilMinute = 0,
        ServerStructureSnapshot? activeConstructionProject = null,
        int householdCount = 0, int activeHouseholdCount = 0, int partnershipCount = 0, int relationshipCount = 0, int friendCount = 0, int rivalCount = 0, int youngChildCount = 0, int childCount = 0, int adolescentCount = 0, int adultCount = 0, int elderCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(foodStored);
        ArgumentOutOfRangeException.ThrowIfNegative(woodStored);
        ArgumentOutOfRangeException.ThrowIfNegative(stoneStored);
        ArgumentOutOfRangeException.ThrowIfNegative(livingPopulation);
        ArgumentOutOfRangeException.ThrowIfNegative(deadPopulation);
        FoodStored = foodStored;
        WoodStored = woodStored;
        StoneStored = stoneStored;
        LivingPopulation = livingPopulation;
        DeadPopulation = deadPopulation;
        RemainingResources = Array.AsReadOnly((remainingResources ?? Array.Empty<ServerResourceQuantitySnapshot>()).ToArray());
        Resources = Array.AsReadOnly((resources ?? Array.Empty<ServerResourceNodeSnapshot>()).ToArray());
        StorageCapacity = storageCapacity;
        StorageUsed = storageUsed;
        ShelterCapacity = shelterCapacity;
        ShelteredPopulation = shelteredPopulation;
        UnhousedPopulation = unhousedPopulation;
        CompletedShelters = completedShelters;
        CompletedStockpiles = completedStockpiles;
        CompletedWorkshops = completedWorkshops;
        ExposureGraceUntilMinute = exposureGraceUntilMinute;
        ActiveConstructionProject = activeConstructionProject;
        HouseholdCount = householdCount; ActiveHouseholdCount = activeHouseholdCount; PartnershipCount = partnershipCount; RelationshipCount = relationshipCount; FriendCount = friendCount; RivalCount = rivalCount; YoungChildCount = youngChildCount; ChildCount = childCount; AdolescentCount = adolescentCount; AdultCount = adultCount; ElderCount = elderCount;
    }

    public int FoodStored { get; }
    public int WoodStored { get; }
    public int StoneStored { get; }
    public int LivingPopulation { get; }
    public int DeadPopulation { get; }
    public IReadOnlyList<ServerResourceQuantitySnapshot> RemainingResources { get; }
    public IReadOnlyList<ServerResourceNodeSnapshot> Resources { get; }
    public int TotalPopulation => LivingPopulation + DeadPopulation;
    public int StorageCapacity { get; }
    public int StorageUsed { get; }
    public int ShelterCapacity { get; }
    public int ShelteredPopulation { get; }
    public int UnhousedPopulation { get; }
    public int CompletedShelters { get; }
    public int CompletedStockpiles { get; }
    public int CompletedWorkshops { get; }
    public long ExposureGraceUntilMinute { get; }
    public ServerStructureSnapshot? ActiveConstructionProject { get; }
    public int HouseholdCount { get; } public int ActiveHouseholdCount { get; } public int PartnershipCount { get; } public int RelationshipCount { get; } public int FriendCount { get; } public int RivalCount { get; } public int YoungChildCount { get; } public int ChildCount { get; } public int AdolescentCount { get; } public int AdultCount { get; } public int ElderCount { get; }
}

public sealed record ServerObservationSnapshot
{
    public ServerObservationSnapshot(ServerStatusSnapshot status, IReadOnlyList<CitizenReadSnapshot>? citizens = null)
        : this(status, citizens?.Select(static citizen => new ServerCitizenSnapshot(citizen)), null, null, null)
    {
    }

    public ServerObservationSnapshot(ServerStatusSnapshot status, IReadOnlyList<CitizenReadSnapshot>? citizens, ServerSettlementSnapshot? settlement)
        : this(status, citizens?.Select(static citizen => new ServerCitizenSnapshot(citizen)), settlement, null, null)
    {
    }

    public ServerObservationSnapshot(ServerStatusSnapshot status, IEnumerable<ServerCitizenSnapshot>? citizens, ServerSettlementSnapshot? settlement, IEnumerable<ServerStructureSnapshot>? structures = null, ServerMapSnapshot? map = null, IEnumerable<RelationshipState>? relationships = null, IEnumerable<Household>? households = null, HistoryReadSnapshot? history = null, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Revision = revision;
        Status = status;
        Citizens = Array.AsReadOnly((citizens ?? Array.Empty<ServerCitizenSnapshot>()).OrderBy(static citizen => long.Parse(citizen.CitizenId, CultureInfo.InvariantCulture)).ToArray());
        Settlement = settlement;
        Structures = Array.AsReadOnly((structures ?? Array.Empty<ServerStructureSnapshot>()).OrderBy(static structure => long.Parse(structure.StructureId, CultureInfo.InvariantCulture)).ToArray());
        Map = map;
        Relationships = Array.AsReadOnly((relationships ?? Array.Empty<RelationshipState>()).OrderBy(x => x.CitizenAId.Value).ThenBy(x => x.CitizenBId.Value).ToArray());
        Households = Array.AsReadOnly((households ?? Array.Empty<Household>()).OrderBy(x => x.Id.Value).Select(x => new Household(x.Id, x.CreatedMinute) { DissolvedMinute = x.DissolvedMinute, DwellingStructureId = x.DwellingStructureId }).ToArray());
        History = history is null ? null : new ServerHistorySnapshot(history, Citizens, Structures);
    }

    public ServerStatusSnapshot Status { get; }
    public long Revision { get; }
    public IReadOnlyList<ServerCitizenSnapshot> Citizens { get; }
    public ServerSettlementSnapshot? Settlement { get; }
    public IReadOnlyList<ServerStructureSnapshot> Structures { get; }
    public ServerMapSnapshot? Map { get; }
    public IReadOnlyList<RelationshipState> Relationships { get; }
    public IReadOnlyList<Household> Households { get; }
    public ServerHistorySnapshot? History { get; }
}

public sealed record WorldStartingSiteSnapshot(int X, int Y);

public sealed record WorldSummarySnapshot(
    string WorldSeed,
    int Width,
    int Height,
    int TileCount,
    int GenerationVersion,
    int GenerationAttempt,
    WorldStartingSiteSnapshot StartingSite,
    IReadOnlyDictionary<string, int> TerrainCounts,
    IReadOnlyDictionary<string, int> ResourceCounts,
    string Fingerprint);

public sealed record CheckpointCommandResult(bool Succeeded, long WorldMinute);

internal abstract record SimulationCommand
{
    internal abstract void SetException(Exception exception);
}

internal sealed record CheckpointSimulationCommand(TaskCompletionSource<CheckpointCommandResult> Completion) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion.TrySetException(exception);
}

internal sealed record FailSimulationCommand(TaskCompletionSource<bool> Completion, Exception Failure) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion.TrySetException(exception);
}
internal sealed record AdvanceSimulationCommand(long Minutes, TaskCompletionSource<bool>? Completion = null, bool RespectOperationalState = false) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion?.TrySetException(exception);
}

internal sealed record PauseSimulationCommand(TaskCompletionSource<ServerStatusSnapshot> Completion) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion.TrySetException(exception);
}

internal sealed record ResumeSimulationCommand(TaskCompletionSource<ServerStatusSnapshot> Completion) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion.TrySetException(exception);
}

internal sealed record SetOperationalSpeedCommand(double Speed, TaskCompletionSource<ServerStatusSnapshot> Completion) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion.TrySetException(exception);
}

/// <summary>
/// The sole hosted simulation writer. HTTP code reads only the immutable Status value and
/// submits commands through the bounded channel; it never touches the engine or database.
/// </summary>
public sealed partial class SimulationHost : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly ILogger<SimulationHost> _logger;
    private readonly WorldChangeBroadcaster? _broadcaster;
    private readonly Channel<SimulationCommand> _commands = Channel.CreateBounded<SimulationCommand>(
        new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private ServerObservationSnapshot _observation;
    private SimulationEngine? _engine;
    private WorldDatabase? _database;
    private WorldCheckpointStore? _checkpointStore;
    private int _failNextFinalCheckpointForTesting;
    private int _failNextCheckpointAttemptsForTesting;
    private Exception? _terminalFailure;
    private PersistenceState _persistenceState = PersistenceState.Healthy;
    private long? _lastSuccessfulCheckpointWorldMinute;
    private DateTime? _lastSuccessfulCheckpointUtc;
    private int _consecutiveCheckpointFailures;
    private DateTimeOffset _lastCheckpointAttemptAt;
    private long _observationRevision;
    private int _acceptingCommands = 1;
    private int _shutdownRequested;
    private int _paused;
    private double _operationalSpeed;
    private readonly TaskCompletionSource<bool> _runningForTesting = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SimulationHost(ServerOptions options, ILogger<SimulationHost> logger, WorldChangeBroadcaster? broadcaster = null)
    {
        if (!double.IsFinite(options.SimulationMinutesPerSecond) || options.SimulationMinutesPerSecond < 0 || options.SimulationMinutesPerSecond > ServerOptions.MaximumSimulationMinutesPerSecond) throw new ArgumentOutOfRangeException(nameof(options), "Simulation advancement must be finite, non-negative, and no greater than the configured maximum.");
        options.Validate();
        _options = options;
        _logger = logger;
        _broadcaster = broadcaster;
        _operationalSpeed = options.SimulationMinutesPerSecond;
        _paused = options.SimulationMinutesPerSecond <= 0 ? 1 : 0;
        var status = new ServerStatusSnapshot(SimulationHostState.Starting, 0, 0, FormatWorldSeed(options.WorldSeed.Value), null, Paused: _paused != 0, OperationalSpeed: _operationalSpeed);
        _observation = new ServerObservationSnapshot(status);
    }

    public ServerObservationSnapshot Observation => Volatile.Read(ref _observation);
    public ServerStatusSnapshot Status => Observation.Status;
    public int CommandCapacity => 32;

    public async Task<ServerStatusSnapshot> RequestPauseAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<ServerStatusSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new PauseSimulationCommand(completion);
        await EnqueueCommandAsync(command, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<ServerStatusSnapshot> RequestResumeAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<ServerStatusSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new ResumeSimulationCommand(completion);
        await EnqueueCommandAsync(command, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<ServerStatusSnapshot> RequestOperationalSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        ValidateOperationalSpeed(speed);
        var completion = new TaskCompletionSource<ServerStatusSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new SetOperationalSpeedCommand(speed, completion);
        await EnqueueCommandAsync(command, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<CheckpointCommandResult> RequestCheckpointAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<CheckpointCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new CheckpointSimulationCommand(completion);
        try
        {
            await EnqueueCommandAsync(command, cancellationToken);
            return await completion.Task.WaitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            throw;
        }
    }

    internal async Task TriggerCommandLoopFailureForTestingAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new FailSimulationCommand(
            completion,
            new InvalidOperationException("Controlled simulation command failure.")));
        await completion.Task;
    }

    internal async Task AdvanceForTestingAsync(long minutes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new AdvanceSimulationCommand(minutes, completion), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    internal async Task AdvanceOperationalForTestingAsync(long minutes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueCommandAsync(new AdvanceSimulationCommand(minutes, completion, RespectOperationalState: true), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    internal Task WaitForRunningForTestingAsync(CancellationToken cancellationToken = default) => _runningForTesting.Task.WaitAsync(cancellationToken);

    internal void FailNextFinalCheckpointForTesting() => Interlocked.Exchange(ref _failNextFinalCheckpointForTesting, 1);

    /// <summary>
    /// Schedules deterministic failures immediately before a checkpoint store call. This is
    /// an internal integration-test seam; it never changes the simulation snapshot and is
    /// consumed by the single-reader retry loop.
    /// </summary>
    internal void FailNextCheckpointAttemptsForTesting(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);
        Interlocked.Exchange(ref _failNextCheckpointAttemptsForTesting, attempts);
    }

    /// <summary>Optional internal test hook receiving checkpoint kind and one-based attempt.</summary>
    internal Func<string, int, Exception?>? CheckpointAttemptFailureHookForTesting { get; set; }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopAcceptingCommands();
        await base.StopAsync(cancellationToken);
        var terminalFailure = Volatile.Read(ref _terminalFailure);
        if (terminalFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(terminalFailure).Throw();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Exception? terminalFailure = null;
        try
        {
            await OpenOrCreateWorldAsync(stoppingToken);
            Publish(IsShutdownRequested ? SimulationHostState.Stopping : SimulationHostState.Running);
            if (!IsShutdownRequested) LogHostRunning(_options.ActiveWorld, _engine!.CurrentMinute.Value);
            await ConsumeCommandsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown proceeds through the final checkpoint below.
        }
        catch (Exception exception)
        {
            terminalFailure = exception;
            Publish(SimulationHostState.Faulted, exception.Message);
            LogHostFaulted(exception);
        }
        finally
        {
            var shutdownFailure = await ShutdownAsync(terminalFailure is not null);
            terminalFailure ??= shutdownFailure;
        }

        if (terminalFailure is not null)
        {
            Interlocked.CompareExchange(ref _terminalFailure, terminalFailure, null);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(terminalFailure).Throw();
        }
    }

    private async Task OpenOrCreateWorldAsync(CancellationToken cancellationToken)
    {
        _database = await WorldDatabase.OpenAsync(_options.DatabasePath, cancellationToken);
        _checkpointStore = _database.CreateCheckpointStore();
        LogDatabaseOpened(_options.DatabasePath);
        var hasCheckpoint = await _database.HasCheckpointAsync(cancellationToken);
        if (hasCheckpoint)
        {
            var snapshot = await _checkpointStore.LoadAsync(cancellationToken);
            _engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
            _lastSuccessfulCheckpointWorldMinute = _engine.CurrentMinute.Value;
            _lastSuccessfulCheckpointUtc = await ReadLastCheckpointUtcAsync(cancellationToken);
            _lastCheckpointAttemptAt = DateTimeOffset.UtcNow;
            LogWorldResumed(_options.ActiveWorld, _engine.CurrentMinute.Value);
            return;
        }

        _engine = new SimulationEngine(_options.WorldSeed, simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion, worldConfiguration: WorldGenerationConfiguration.Default.CanonicalJson);
        await WriteCheckpointWithRetriesAsync("initial", cancellationToken);
        _lastCheckpointAttemptAt = DateTimeOffset.UtcNow;
        LogWorldCreated(_options.ActiveWorld, _options.DatabasePath);
    }

    private async Task<DateTime?> ReadLastCheckpointUtcAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_checkpoint_utc FROM world_meta WHERE id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull) return null;
        return DateTime.SpecifyKind(DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTimeKind.Utc);
    }

    private async Task ConsumeCommandsAsync(CancellationToken stoppingToken)
    {
        using var driverCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var driver = RunOperationalDriverAsync(driverCancellation.Token);
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(CancellationToken.None))
            {
                switch (command)
                {
                    case CheckpointSimulationCommand checkpoint: await ProcessCheckpointAsync(checkpoint, CancellationToken.None); break;
                    case FailSimulationCommand failure: failure.Completion.TrySetException(failure.Failure); throw failure.Failure;
                    case PauseSimulationCommand pause:
                        Volatile.Write(ref _paused, 1);
                        Publish(IsShutdownRequested ? SimulationHostState.Stopping : SimulationHostState.Running);
                        pause.Completion.TrySetResult(Status);
                        break;
                    case ResumeSimulationCommand resume:
                        if (Volatile.Read(ref _operationalSpeed) <= 0) Volatile.Write(ref _operationalSpeed, ServerOptions.DefaultSimulationMinutesPerSecond);
                        Volatile.Write(ref _paused, 0);
                        Publish(IsShutdownRequested ? SimulationHostState.Stopping : SimulationHostState.Running);
                        resume.Completion.TrySetResult(Status);
                        break;
                    case SetOperationalSpeedCommand speed:
                        Volatile.Write(ref _operationalSpeed, speed.Speed);
                        Publish(IsShutdownRequested ? SimulationHostState.Stopping : SimulationHostState.Running);
                        speed.Completion.TrySetResult(Status);
                        break;
                    case AdvanceSimulationCommand advance:
                        try
                        {
                            var operationallyEnabled = Volatile.Read(ref _paused) == 0 && Volatile.Read(ref _operationalSpeed) > 0;
                            if (advance.Minutes > 0 && (!advance.RespectOperationalState || operationallyEnabled)) _engine!.AdvanceUntil(_engine.CurrentMinute.Add(advance.Minutes));
                            Publish(IsShutdownRequested ? SimulationHostState.Stopping : SimulationHostState.Running);
                            if (advance.Minutes > 0) await CheckpointIfDueAsync(CancellationToken.None);
                            advance.Completion?.TrySetResult(true);
                        }
                        catch (Exception exception)
                        {
                            advance.Completion?.TrySetException(exception);
                            throw;
                        }
                        break;
                    default: command.SetException(new InvalidOperationException("Unsupported simulation command.")); break;
                }
            }
        }
        finally
        {
            driverCancellation.Cancel();
            try
            {
                await driver;
            }
            catch (OperationCanceledException) when (driverCancellation.IsCancellationRequested)
            {
            }
            catch (ChannelClosedException) when (IsShutdownRequested)
            {
            }
        }
    }

    private async Task RunOperationalDriverAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var accumulatedMinutes = 0d;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var speed = Volatile.Read(ref _operationalSpeed);
            if (Volatile.Read(ref _paused) != 0 || speed <= 0) { accumulatedMinutes = 0; continue; }
            accumulatedMinutes += speed / 10;
            var minutes = (long)Math.Floor(accumulatedMinutes + 1e-9);
            accumulatedMinutes = Math.Max(0, accumulatedMinutes - minutes);
            if (minutes > 0)
            {
                try
                {
                    // One operational command at a time: a slow checkpoint cannot build a
                    // queue of stale advances that run after the user pauses the world.
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await _commands.Writer.WriteAsync(new AdvanceSimulationCommand(minutes, completion, RespectOperationalState: true), stoppingToken);
                    await completion.Task.WaitAsync(stoppingToken);
                }
                catch (ChannelClosedException) when (IsShutdownRequested || stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task ProcessCheckpointAsync(CheckpointSimulationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await WriteCheckpointWithRetriesAsync("explicit", cancellationToken);
            command.Completion.TrySetResult(new CheckpointCommandResult(true, _engine!.CurrentMinute.Value));
        }
        catch (Exception exception)
        {
            command.SetException(exception);
            throw;
        }
    }

    private async Task CheckpointIfDueAsync(CancellationToken cancellationToken)
    {
        if (_options.CheckpointSimulationMinutes == 0 || _engine is null) return;
        var dueByMinute = !_lastSuccessfulCheckpointWorldMinute.HasValue || _engine.CurrentMinute.Value - _lastSuccessfulCheckpointWorldMinute.Value >= _options.CheckpointSimulationMinutes;
        var dueByWallClock = _lastCheckpointAttemptAt == default || DateTimeOffset.UtcNow - _lastCheckpointAttemptAt >= TimeSpan.FromSeconds(_options.CheckpointMinimumRealSeconds);
        if (dueByMinute && dueByWallClock)
        {
            await WriteCheckpointWithRetriesAsync("periodic", cancellationToken);
        }
    }

    private async Task WriteCheckpointWithRetriesAsync(string kind, CancellationToken cancellationToken)
    {
        var engine = _engine ?? throw new InvalidOperationException("The simulation engine is not initialized.");
        var snapshot = engine.CreatePersistenceSnapshot();
        var attemptCount = checked(_options.CheckpointRetryCount + 1);
        var worldMinute = engine.CurrentMinute.Value;
        var started = Stopwatch.GetTimestamp();
        Exception? lastException = null;
        if (kind == "periodic") LogPeriodicCheckpointStarted(worldMinute, _options.CheckpointRetryCount);
        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            _lastCheckpointAttemptAt = DateTimeOffset.UtcNow;
            try
            {
                var checkpointUtc = DateTime.UtcNow;
                if (kind == "final" && Interlocked.Exchange(ref _failNextFinalCheckpointForTesting, 0) != 0)
                {
                    throw new InvalidOperationException("Controlled final checkpoint failure.");
                }
                var injectedFailure = CheckpointAttemptFailureHookForTesting?.Invoke(kind, attempt + 1);
                if (injectedFailure is not null)
                {
                    throw injectedFailure;
                }
                if (Volatile.Read(ref _failNextCheckpointAttemptsForTesting) > 0 && Interlocked.Decrement(ref _failNextCheckpointAttemptsForTesting) >= 0)
                {
                    throw new InvalidOperationException($"Controlled {kind} checkpoint attempt failure.");
                }
                await _checkpointStore!.CheckpointAsync(snapshot, checkpointUtc, cancellationToken: cancellationToken);
                Interlocked.Exchange(ref _failNextFinalCheckpointForTesting, 0);
                var wasDegraded = _persistenceState != PersistenceState.Healthy;
                MarkCheckpointSucceeded(worldMinute, checkpointUtc);
                Publish(GetCheckpointLifecycleState(kind));
                if (wasDegraded) LogPersistenceRecovered(worldMinute);
                var duration = Stopwatch.GetElapsedTime(started);
                if (kind == "periodic") LogPeriodicCheckpointCompleted(worldMinute, duration.TotalMilliseconds, attempt);
                else LogFinalOrExplicitCheckpointCompleted(kind, worldMinute, duration.TotalMilliseconds, attempt);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
                _consecutiveCheckpointFailures++;
                _persistenceState = PersistenceState.Degraded;
                Publish(GetCheckpointLifecycleState(kind), exception.Message);
                LogCheckpointFailed(kind, worldMinute, attempt + 1, attemptCount, exception);
                if (attempt + 1 < attemptCount && _options.CheckpointRetryDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.CheckpointRetryDelaySeconds), cancellationToken);
                }
            }
        }

        var exhausted = lastException ?? new InvalidOperationException("Checkpoint failed without an exception.");
        _persistenceState = PersistenceState.Faulted;
        Publish(SimulationHostState.Faulted, exhausted.Message);
        LogCheckpointExhausted(kind, worldMinute, attemptCount, exhausted);
        throw exhausted;
    }

    private void MarkCheckpointSucceeded(long worldMinute, DateTime checkpointUtc)
    {
        _persistenceState = PersistenceState.Healthy;
        _lastSuccessfulCheckpointWorldMinute = worldMinute;
        _lastSuccessfulCheckpointUtc = checkpointUtc;
        _consecutiveCheckpointFailures = 0;
    }

    private SimulationHostState GetCheckpointLifecycleState(string kind) =>
        kind == "initial" ? (IsShutdownRequested ? SimulationHostState.Stopping : SimulationHostState.Starting) :
        kind == "final" || IsShutdownRequested ? SimulationHostState.Stopping : SimulationHostState.Running;

    private async Task<Exception?> ShutdownAsync(bool preserveFaulted)
    {
        StopAcceptingCommands();
        if (!preserveFaulted)
        {
            Publish(SimulationHostState.Stopping);
        }

        _commands.Writer.TryComplete();
        while (_commands.Reader.TryRead(out var command))
        {
            command.SetException(new OperationCanceledException("The simulation host is stopping."));
        }

        Exception? shutdownFailure = null;
        if (!preserveFaulted && _engine is not null && _checkpointStore is not null)
        {
            try
            {
                await WriteCheckpointWithRetriesAsync("final", CancellationToken.None);
            }
            catch (Exception exception)
            {
                shutdownFailure = exception;
                _persistenceState = PersistenceState.Faulted;
                Publish(SimulationHostState.Faulted, exception.Message);
                LogFinalCheckpointFailed(exception);
            }
        }

        if (_database is not null)
        {
            try
            {
                await _database.DisposeAsync();
            }
            catch (Exception exception)
            {
                var hadEarlierFailure = shutdownFailure is not null;
                shutdownFailure ??= exception;
                if (!preserveFaulted && !hadEarlierFailure)
                {
                    Publish(SimulationHostState.Faulted, exception.Message);
                }
                LogDatabaseCleanupFailed(exception);
            }
        }

        return shutdownFailure;
    }

    private bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

    private static void ValidateOperationalSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0 || speed > ServerOptions.MaximumSimulationMinutesPerSecond)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), $"Operational speed must be finite, positive, and no greater than {ServerOptions.MaximumSimulationMinutesPerSecond.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private void StopAcceptingCommands()
    {
        if (Interlocked.Exchange(ref _acceptingCommands, 0) == 1)
        {
            Volatile.Write(ref _shutdownRequested, 1);
            _commands.Writer.TryComplete();
        }
    }

    private async ValueTask EnqueueCommandAsync(SimulationCommand command, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _acceptingCommands) == 0)
        {
            var exception = new InvalidOperationException("The simulation host is stopping and no longer accepts commands.");
            command.SetException(exception);
            throw exception;
        }

        try
        {
            await _commands.Writer.WriteAsync(command, cancellationToken);
        }
        catch (ChannelClosedException) when (Volatile.Read(ref _acceptingCommands) == 0)
        {
            var exception = new InvalidOperationException("The simulation host is stopping and no longer accepts commands.");
            command.SetException(exception);
            throw exception;
        }
    }

    private void Publish(SimulationHostState state, string? error = null)
    {
        var engine = _engine;
        var readSnapshot = engine?.CreateReadSnapshot();
        var citizenSnapshots = readSnapshot?.Citizens.Select(static citizen => new ServerCitizenSnapshot(citizen)).ToArray() ?? Array.Empty<ServerCitizenSnapshot>();
        var structureSnapshots = readSnapshot is null ? Array.Empty<ServerStructureSnapshot>() : CreateStructureSnapshots(readSnapshot, citizenSnapshots);
        var map = readSnapshot?.World is null ? null : Observation.Map ?? new ServerMapSnapshot(readSnapshot.World);
        var livingPopulation = citizenSnapshots.Count(static citizen => citizen.IsAlive);
        var deadPopulation = citizenSnapshots.Length - livingPopulation;
        var settlement = readSnapshot is null ? null : CreateSettlementSummary(readSnapshot, citizenSnapshots, structureSnapshots);
        var status = new ServerStatusSnapshot(
            state,
            readSnapshot?.WorldMinute.Value ?? 0,
            readSnapshot?.PendingEventCount ?? 0,
            FormatWorldSeed(readSnapshot?.Seed.Value ?? _options.WorldSeed.Value),
            error,
            readSnapshot?.World is null ? null : Observation.Status.World ?? CreateWorldSummary(readSnapshot.World),
            livingPopulation,
            citizenSnapshots.Length,
            livingPopulation,
            deadPopulation,
            _persistenceState,
            _lastSuccessfulCheckpointWorldMinute,
            _lastSuccessfulCheckpointUtc,
            _consecutiveCheckpointFailures,
            Paused: Volatile.Read(ref _paused) != 0,
            OperationalSpeed: Volatile.Read(ref _operationalSpeed));
        var revision = Interlocked.Increment(ref _observationRevision);
        var observation = new ServerObservationSnapshot(status, citizenSnapshots, settlement, structureSnapshots, map, readSnapshot?.Relationships, readSnapshot?.Households, readSnapshot?.History, revision);
        Interlocked.Exchange(ref _observation, observation);
        _broadcaster?.Publish(new WorldChangedPayload(revision, status.WorldMinute, state, _persistenceState));
        if (state == SimulationHostState.Running)
        {
            _runningForTesting.TrySetResult(true);
        }
        else if (state == SimulationHostState.Faulted && error is not null)
        {
            _runningForTesting.TrySetException(new InvalidOperationException(error));
        }
    }

    private static ServerStructureSnapshot[] CreateStructureSnapshots(SimulationStatusSnapshot readSnapshot, ServerCitizenSnapshot[] citizens)
    {
        var contributions = readSnapshot.StructureContributions
            .GroupBy(static contribution => contribution.StructureId.Value)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(contribution => new ServerStructureContributionSnapshot(
                    contribution.CitizenId.Value.ToString(CultureInfo.InvariantCulture),
                    contribution.ConstructionWork,
                    contribution.WoodDelivered,
                    contribution.StoneDelivered)).ToArray());
        var occupants = citizens
            .Where(static citizen => citizen.IsAlive && citizen.HomeStructureId is not null)
            .GroupBy(static citizen => long.Parse(citizen.HomeStructureId!, CultureInfo.InvariantCulture))
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static citizen => citizen.CitizenId).ToArray());
        return readSnapshot.Structures
            .OrderBy(static structure => structure.Id.Value)
            .Select(structure => new ServerStructureSnapshot(
                structure,
                occupants.TryGetValue(structure.Id.Value, out var currentOccupants) ? currentOccupants : Array.Empty<string>(),
                contributions.TryGetValue(structure.Id.Value, out var structureContributions) ? structureContributions : Array.Empty<ServerStructureContributionSnapshot>()))
            .ToArray();
    }

    private static ServerSettlementSnapshot CreateSettlementSummary(SimulationStatusSnapshot readSnapshot, ServerCitizenSnapshot[] citizens, ServerStructureSnapshot[] structures)
    {
        var settlement = readSnapshot.Settlement ?? new SettlementState(0, 0, 0);
        var resourceTypes = readSnapshot.World?.Resources.ToDictionary(static node => node.Id.Value, static node => node.Type)
            ?? new Dictionary<long, ResourceType>();
        var resources = readSnapshot.ResourceStates
            .OrderBy(static resource => resource.ResourceNodeId.Value)
            .Select(resource => new ServerResourceNodeSnapshot(
                resource.ResourceNodeId.Value.ToString(CultureInfo.InvariantCulture),
                resourceTypes.TryGetValue(resource.ResourceNodeId.Value, out var type) ? type : throw new InvalidDataException("A resource observation references an unknown node."),
                resource.CurrentQuantity))
            .ToArray();
        var remaining = resources
            .GroupBy(static resource => resource.ResourceType)
            .OrderBy(static group => group.Key)
            .Select(static group => new ServerResourceQuantitySnapshot(group.Key, group.Sum(resource => resource.CurrentQuantity)))
            .ToArray();
        var living = citizens.Count(static citizen => citizen.IsAlive);
        var completedShelters = structures.Count(static structure => structure.Status == StructureStatus.Complete && structure.Type == StructureType.Shelter);
        var completedStockpiles = structures.Count(static structure => structure.Status == StructureStatus.Complete && structure.Type == StructureType.Stockpile);
        var completedWorkshops = structures.Count(static structure => structure.Status == StructureStatus.Complete && structure.Type == StructureType.Workshop);
        var shelteredPopulation = citizens.Count(static citizen => citizen.IsAlive && citizen.HomeStructureId is not null);
        var storageCapacity = checked(settlement.BaseStorageCapacity + completedStockpiles * CitizenSimulationRules.StockpileStorageBonus);
        return new ServerSettlementSnapshot(
            settlement.FoodStored,
            settlement.WoodStored,
            settlement.StoneStored,
            living,
            citizens.Length - living,
            remaining,
            resources,
            storageCapacity,
            settlement.StorageUsed,
            checked(completedShelters * CitizenSimulationRules.ShelterCapacityPerBuilding),
            shelteredPopulation,
            living - shelteredPopulation,
            completedShelters,
            completedStockpiles,
            completedWorkshops,
            settlement.ExposureConsequencesStartMinute,
            structures.SingleOrDefault(static structure => structure.Status == StructureStatus.UnderConstruction),
            readSnapshot.Households.Count,
            readSnapshot.Households.Count(x => x.DissolvedMinute is null),
            citizens.Count(x => x.PartnerId is not null) / 2,
            readSnapshot.Relationships.Count,
            readSnapshot.Relationships.Count(x => RelationshipLabels.Derive(x, false, false) is RelationshipLabels.Friend or RelationshipLabels.CloseFriend),
            readSnapshot.Relationships.Count(x => RelationshipLabels.Derive(x, false, false) == RelationshipLabels.Rival),
            citizens.Count(x => x.LifeStage == "Young Child"), citizens.Count(x => x.LifeStage == "Child"), citizens.Count(x => x.LifeStage == "Adolescent"), citizens.Count(x => x.LifeStage == "Adult"), citizens.Count(x => x.LifeStage == "Elder"));
    }

    private static WorldSummarySnapshot CreateWorldSummary(WorldMap world)
    {
        var terrainCounts = Enum.GetValues<TerrainType>().ToDictionary(type => type.ToString(), type => world.Tiles.Count(tile => tile.Terrain == type), StringComparer.Ordinal);
        var resourceCounts = Enum.GetValues<ResourceType>().ToDictionary(type => type.ToString(), type => world.Resources.Count(resource => resource.Type == type), StringComparer.Ordinal);
        return new WorldSummarySnapshot(
            FormatWorldSeed(world.OriginalSeed.Value),
            world.Width,
            world.Height,
            world.Tiles.Count,
            world.GenerationVersion,
            world.GenerationAttempt,
            new WorldStartingSiteSnapshot(world.StartingSite.X, world.StartingSite.Y),
            new ReadOnlyDictionary<string, int>(terrainCounts),
            new ReadOnlyDictionary<string, int>(resourceCounts),
            world.Fingerprint);
    }

    private static string FormatWorldSeed(ulong seed) => seed.ToString(CultureInfo.InvariantCulture);

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Simulation host running for world {ActiveWorld} at minute {WorldMinute}")]
    private partial void LogHostRunning(string activeWorld, long worldMinute);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Critical, Message = "Simulation host faulted")]
    private partial void LogHostFaulted(Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Created foundation world {ActiveWorld} at {DatabasePath}")]
    private partial void LogWorldCreated(string activeWorld, string databasePath);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Final simulation checkpoint completed at minute {WorldMinute}")]
    private partial void LogFinalCheckpointCompleted(long worldMinute);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Final simulation checkpoint failed")]
    private partial void LogFinalCheckpointFailed(Exception exception);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Simulation database cleanup failed")]
    private partial void LogDatabaseCleanupFailed(Exception exception);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Simulation database opened at {DatabasePath}")]
    private partial void LogDatabaseOpened(string databasePath);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Resumed world {ActiveWorld} at minute {WorldMinute}")]
    private partial void LogWorldResumed(string activeWorld, long worldMinute);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Periodic simulation checkpoint completed at minute {WorldMinute} in {DurationMilliseconds}ms after {RetryCount} additional retries")]
    private partial void LogPeriodicCheckpointCompleted(long worldMinute, double durationMilliseconds, int retryCount);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Periodic simulation checkpoint started at minute {WorldMinute} with {RetryCount} additional retries")]
    private partial void LogPeriodicCheckpointStarted(long worldMinute, int retryCount);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "{CheckpointKind} simulation checkpoint completed at minute {WorldMinute} in {DurationMilliseconds}ms after {RetryCount} additional retries")]
    private partial void LogFinalOrExplicitCheckpointCompleted(string checkpointKind, long worldMinute, double durationMilliseconds, int retryCount);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "{CheckpointKind} simulation checkpoint attempt {Attempt} of {AttemptCount} failed at minute {WorldMinute}; persistence is degraded")]
    private partial void LogCheckpointFailed(string checkpointKind, long worldMinute, int attempt, int attemptCount, Exception exception);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Critical, Message = "{CheckpointKind} simulation checkpoint exhausted {AttemptCount} attempts at minute {WorldMinute}; persistence is faulted")]
    private partial void LogCheckpointExhausted(string checkpointKind, long worldMinute, int attemptCount, Exception exception);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "Simulation persistence recovered after a successful checkpoint at minute {WorldMinute}")]
    private partial void LogPersistenceRecovered(long worldMinute);
}
