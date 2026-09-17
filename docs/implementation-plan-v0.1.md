# Little Ages v0.1 — Technical Implementation Plan

**Status:** Implementation baseline plus candidate acceptance evidence
**Product source of truth:** [`docs/design-v0.1.md`](./design-v0.1.md)  
**Target release:** Little Ages v0.1 — First Settlement  
**Primary deployment target:** Always-on Windows PC on a trusted LAN

This document translates the v0.1 product design into an implementation plan. When this document and the product design disagree about product behavior or scope, **`design-v0.1.md` wins**. This plan may be revised as implementation evidence appears, but it must not silently expand v0.1 scope.

---

## 1. Engineering Objectives

The implementation must optimize for these properties, in this order:

1. **Deterministic simulation** — identical seed, rules, configuration, and interventions produce identical canonical outcomes.
2. **Inspectability** — important behavior can be explained and historical facts can be queried.
3. **Persistence correctness** — save/reload and service restart do not alter history.
4. **Headless throughput** — the simulation can run far faster than visual time for long-run testing.
5. **Long-lived Windows operation** — the browser is optional; the server owns and runs the civilization.
6. **Clear subsystem boundaries** — UI, persistence, networking, and future AI cannot become simulation authorities.
7. **Reasonable performance before clever optimization** — prove bottlenecks with benchmarks before adding complexity.

The first implementation should prefer boring, testable mechanisms over sophisticated frameworks or premature ECS/distributed architecture.

---

## 2. Locked Technology Baseline

### Backend

- **C# / .NET 10 LTS**
- ASP.NET Core
- `BackgroundService`/hosted services for runtime hosting
- `System.Threading.Channels` for commands into the authoritative simulation thread
- SQLite
- EF Core SQLite for schema migrations and persistence plumbing
- ASP.NET Core health checks
- SignalR for live browser updates
- xUnit for .NET tests
- BenchmarkDotNet when performance benchmarks begin

### Frontend

- React
- TypeScript with strict mode
- Vite
- PixiJS for the world renderer
- `@microsoft/signalr` for live updates
- Vitest for frontend unit tests
- ESLint

Do not introduce Redux, a game engine, an actor framework, a message broker, Redis, PostgreSQL, Docker as a runtime requirement, or an external model provider in v0.1 unless concrete evidence later demonstrates that the baseline cannot meet requirements.

---

## 3. Repository Layout

Initial repository layout:

```text
LittleAges/
├── src/
│   ├── LittleAges.Domain/
│   ├── LittleAges.Simulation/
│   ├── LittleAges.Persistence/
│   ├── LittleAges.Server/
│   ├── LittleAges.Headless/
│   └── LittleAges.Web/
├── tests/
│   ├── LittleAges.Domain.Tests/
│   ├── LittleAges.Simulation.Tests/
│   ├── LittleAges.Persistence.Tests/
│   ├── LittleAges.Integration.Tests/
│   └── LittleAges.Headless.Tests/
├── benchmarks/
│   └── LittleAges.Benchmarks/
├── docs/
│   ├── design-v0.1.md
│   ├── implementation-plan-v0.1.md
│   ├── architecture.md             # generated/refined during implementation
│   ├── simulation-model.md         # generated/refined during implementation
│   └── v0.1-acceptance-report.md   # candidate-only acceptance evidence
├── tools/
├── .github/workflows/
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── LittleAges.sln
├── README.md
└── .gitignore
```

`LittleAges.Web` is a Vite application directory rather than a .NET project.

---

## 4. Project Boundaries and Dependency Rules

### `LittleAges.Domain`

Contains simulation vocabulary and state types that do not perform infrastructure work.

Examples:

- strongly typed entity IDs;
- `WorldMinute`;
- coordinates;
- enums such as `TerrainType`, `ResourceType`, `StructureType`, `HistoricalEventType`;
- citizen state records/entities;
- relationship state;
- household state;
- structure state;
- historical event contracts;
- persisted world configuration contracts.

**May reference:** BCL only wherever practical.  
**Must not reference:** ASP.NET, EF Core, SQLite, SignalR, React concepts, Windows APIs.

### `LittleAges.Simulation`

Owns all canonical world mutation and simulation rules.

Examples:

- `SimulationEngine`;
- scheduled-event queue;
- deterministic random service;
- world generation;
- citizen decision systems;
- movement/pathfinding;
- resource systems;
- construction;
- relationship/family systems;
- mortality;
- historical event emission;
- snapshot/read-model creation.

**References:** `LittleAges.Domain` only.

The simulation assembly must be runnable directly from tests without ASP.NET, SQLite, or the web application.

### `LittleAges.Persistence`

Owns SQLite infrastructure and mapping between persistent data and canonical simulation snapshots.

Examples:

- EF Core DbContext;
- migrations;
- world database creation/opening;
- checkpoint transactions;
- historical query repositories;
- WAL/foreign-key initialization;
- snapshot serialization where appropriate.

**References:** `LittleAges.Domain`, `LittleAges.Simulation` where engine snapshot contracts require it.

Persistence does **not** decide simulation outcomes.

### `LittleAges.Server`

Owns process hosting and network boundaries.

Examples:

- ASP.NET Core host;
- Windows Service integration;
- `SimulationHost` background service;
- REST API;
- SignalR hub;
- command routing;
- read-only API snapshots;
- health/status endpoints;
- graceful shutdown/checkpoint orchestration.

**References:** Domain, Simulation, Persistence.

### `LittleAges.Web`

Owns presentation only.

The browser can request commands such as pause/resume/speed changes but never mutates canonical state directly.

---

## 5. Canonical State vs. Historical Record

Little Ages v0.1 is **not event sourced**.

There are two distinct concepts:

### Canonical live state

The authoritative current state includes:

- world clock;
- map and resource nodes;
- citizens;
- needs/skills/traits;
- relationships;
- households;
- structures;
- settlement resources;
- scheduled future simulation events;
- deterministic counters/configuration.

This state is checkpointed to SQLite.

### Historical events

Historical events are immutable factual records emitted when something worth remembering occurs.

They support:

- timelines;
- biographies;
- historical queries;
- structured observer timelines and biographies. M6 does not implement AI narration or generated prose.

They are **not** used as the only mechanism for reconstructing the live world.

This distinction should remain explicit in names, interfaces, and database tables.

---

## 6. Deterministic Identity Strategy

Canonical simulation entities must not use `Guid.NewGuid()` or wall-clock-based IDs.

Use strongly typed monotonic 64-bit IDs allocated from persisted world-local counters, for example:

```csharp
public readonly record struct CitizenId(long Value);
public readonly record struct StructureId(long Value);
public readonly record struct HouseholdId(long Value);
public readonly record struct HistoricalEventId(long Value);
public readonly record struct ScheduledEventId(long Value);
```

Counters are part of canonical state and survive checkpoint/reload.

A server-level world-file identifier may use a non-deterministic identifier if needed for file management, but it must not influence simulation behavior or canonical outcome fingerprints.

---

## 7. Canonical Time

Implement a dedicated value type around signed 64-bit simulated minutes:

```text
WorldMinute(long)
```

Rules:

- world creation starts at minute `0`;
- the simulation clock never moves backward;
- no simulation rule may call `DateTime.Now`, `DateTime.UtcNow`, `Stopwatch`, or another wall-clock source to decide canonical outcomes;
- calendar rendering is derived from `WorldMinute`;
- 360 days/year, 12×30-day months, four 90-day seasons.

Wall time is used only by the server host to decide how quickly to ask the engine to advance.

---

## 8. Deterministic Randomness

Do not use `System.Random` as the canonical simulation RNG because implementation behavior can change across runtime versions and a shared mutable RNG makes subsystem changes cascade unpredictably.

Implement a small **versioned PRNG owned by the repository**, such as PCG32 or xoshiro256**, with published golden-vector tests.

Expose randomness through an interface such as:

```csharp
public interface IDeterministicRandom
{
    ulong NextUInt64(RandomDomain domain, ulong keyA = 0, ulong keyB = 0, ulong keyC = 0);
    double NextUnitDouble(RandomDomain domain, ulong keyA = 0, ulong keyB = 0, ulong keyC = 0);
}
```

Preferred approach: derive independent deterministic values from stable inputs rather than relying on one mutable global stream.

Inputs can include:

- immutable world seed;
- versioned random domain;
- entity ID;
- world minute or lifecycle sequence;
- local decision/event sequence.

Initial domains:

```text
WorldGeneration
CitizenGeneration
DecisionVariation
Relationships
Reproduction
Mortality
ResourceRegeneration
```

Requirements:

- adding a random draw to `Relationships` must not alter world generation;
- save/reload must not require guessing how many random draws already occurred;
- PRNG algorithm/version is part of `SimulationRulesVersion` compatibility;
- golden tests lock expected outputs.

---

## 9. Scheduled Simulation Events

Use a deterministic priority queue/min-heap ordered by the exact tuple:

```text
DueWorldMinute
Priority
EntitySortKey
Sequence
```

`Sequence` is a persisted monotonic counter allocated when scheduling.

Internal scheduled events and historical events must use separate types.

Initial scheduled-event categories eventually include:

- citizen decision due;
- action completion;
- movement completion/waypoint completion where required;
- resource regeneration;
- periodic statistics sample;
- lifecycle checks;
- checkpoint request notification to host where appropriate.

The engine API should support operations conceptually equivalent to:

```text
ProcessNextEvent()
AdvanceUntil(targetMinute)
AdvanceEvents(maxEvents)
CreateReadSnapshot()
```

The same engine path must power normal speed and MAX/headless mode.

MAX is an operational driver mode: it removes wall-clock pacing while retaining
the normal event queue, RNG derivation, decision flow, and single-writer
mutation path. The driver must never put elapsed time, speed, pause state, or
benchmark counters into canonical state.

---

## 10. Runtime Concurrency Model

Canonical state is **single-writer**.

`SimulationHost` owns one `SimulationEngine` instance and mutates it from one dedicated logical execution path.

Commands from ASP.NET endpoints are sent through a bounded or appropriately managed `Channel<SimulationCommand>`.

Examples:

```text
Pause
Resume
SetSpeed
CreateWorld
LoadWorld
RequestCheckpoint
```

Pause, Resume, and SetSpeed are operational commands only. HTTP handlers submit
them to the host's bounded single-reader channel; `Program.cs` never accesses a
mutable `SimulationEngine`. The published immutable status includes paused
state and the current bounded positive operational speed. A zero startup rate is
paused; resume restores a safe positive rate. These controls do not emit
history, affect fingerprints, or change persistence schema.

REST requests and SignalR clients must not lock and directly inspect mutable simulation collections.

Instead, the engine periodically publishes an immutable/read-only `WorldReadSnapshot` containing browser-facing state.

Flow:

```text
HTTP/SignalR command
      ↓
SimulationCommand channel
      ↓
SimulationHost
      ↓
SimulationEngine
      ↓
canonical mutation
      ↓
immutable read snapshot
      ↓
API + SignalR subscribers
```

This keeps network concurrency from affecting simulation ordering.

---

## 11. World Storage Model

Use **one SQLite database file per civilization/world**.

Suggested layout on the Windows host:

```text
<data-root>/
├── worlds/
│   ├── <world-file-id>.db
│   └── ...
└── server.json
```

Only one world must be active in-process at a time for v0.1.

Multiple saved world files are acceptable, but multi-world simultaneous simulation is explicitly out of scope.

The non-canonical file ID must never be fed into simulation rules.

---

## 12. Initial Persistence Schema

The exact EF entities may evolve per milestone, but the database should converge on the following logical schema.

### `world_meta`

Single canonical row per world database.

Fields include:

```text
world_seed
world_minute
world_schema_version
simulation_rules_version
application_version
world_configuration_json
next_entity_id
next_historical_event_id
next_scheduled_event_sequence
social_version
history_version
created_utc            # metadata only; not canonical simulation input
last_checkpoint_utc    # metadata only; not canonical simulation input
```

### `tiles`

Persist once after generation unless a future system makes a property mutable.

```text
x
 y
terrain_type
elevation
fertility
water_access
movement_cost
```

Primary key: `(x, y)`.

### `resource_nodes`

```text
id
x
y
resource_type
current_quantity
max_quantity
regeneration_state
```

### `settlement_state`

One row in v0.1:

```text
food_stored
wood_stored
stone_stored
storage_capacity
shelter_capacity
```

### `citizens`

Use explicit columns for stable v0.1 fields rather than hiding the entire citizen in opaque JSON.

Logical groups:

- identity/name;
- birth/death;
- parent IDs;
- partner/household/home IDs;
- logical position;
- health;
- needs;
- personality traits;
- skill experience;
- current action state;
- lifetime counters needed for biographies/derived occupation.

### `relationships`

Canonicalize each pair so the smaller citizen ID is first.

Composite primary key:

```text
(citizen_a_id, citizen_b_id)
```

Fields:

```text
familiarity
affinity
trust
conflict
last_interaction_minute
interaction_count
```

### `households`

```text
id
created_minute
dissolved_minute
dwelling_structure_id
```

### `household_members`

```text
household_id
citizen_id
joined_minute
left_minute
role
```

### `structures`

```text
id
type
x
y
construction_started_minute
completed_minute
condition
resource_costs
```

Resource costs may initially use explicit columns for food/wood/stone where applicable.

### `structure_contributions`

```text
structure_id
citizen_id
work_units
```

### `memories`

```text
id
citizen_id
historical_event_id
memory_type
importance
emotional_valence
created_minute
```

### `scheduled_events`

Checkpointed internal queue.

```text
sequence
due_minute
priority
entity_sort_key
event_type
payload_json
```

Scheduled-event payload schemas are versioned with simulation rules.

### `historical_events`

Append-only factual event record.

```text
id
world_minute
event_type
importance
origin
location_x
location_y
payload_json
schema_version
```

### `historical_event_citizens`

Queryable many-to-many index:

```text
historical_event_id
citizen_id
role            # subject, parent, partner, founder, participant, member, contributor
```

### `historical_event_structures`

```text
historical_event_id
structure_id
role
```

### `statistics_samples`

```text
world_minute
period_start_minute
population
births_period
deaths_period
food_stored
food_produced_period
food_consumed_period
wood_stored
stone_stored
shelter_capacity
average_health
average_hunger
```

Do not create every table during Milestone 0 if it has no implemented owner yet. Add migrations with the subsystem that owns the data while preserving the target model above.

---

## 13. Checkpoint Semantics

A checkpoint is a transactionally consistent representation of canonical state at one exact `WorldMinute`.

Required behavior:

1. simulation reaches a safe event boundary;
2. engine produces a persistence snapshot;
3. persistence writes mutable canonical state and the scheduled-event queue inside one SQLite transaction;
4. newly emitted historical events are appended consistently;
5. `world_meta.world_minute` advances only as part of the committed transaction;
6. transaction commits;
7. server may publish checkpoint success diagnostics.

SQLite settings:

- WAL mode;
- foreign keys `ON`;
- sensible busy timeout;
- explicit transaction boundaries.

A partially written checkpoint must never be considered valid.

---

## 14. Canonical State Fingerprint

Implement a deterministic canonical-state fingerprint utility before the 100-year acceptance milestone.

The fingerprint excludes non-canonical metadata such as:

- file path;
- database filename;
- wall-clock creation time;
- last checkpoint wall time;
- UI state.

It includes ordered canonical data such as:

- world minute;
- configuration and rules version;
- tiles/resources;
- citizens;
- relationships;
- households;
- structures;
- settlement resources;
- scheduled events;
- historical events where the acceptance comparison requires them.

Use this in determinism and save/reload equivalence tests.

---

## 15. Simulation Configuration

Create a versioned immutable configuration object persisted at world creation.

Suggested sections:

```text
World
Calendar
Needs
Movement
Resources
Seasons
Skills
Relationships
Reproduction
Mortality
Construction
Population
History
```

Do not read mutable application defaults during an existing world's simulation.

A world always runs from the configuration stored with that world unless an explicit migration changes it.

---

## 16. Domain Model Direction

### IDs

Strongly typed 64-bit IDs.

### Coordinates

Integer tile coordinates:

```text
TileCoordinate(int X, int Y)
```

Use integer/fixed logical representation for canonical positions wherever feasible. Rendering interpolation remains client-only.

### Numeric simulation values

Needs/traits/relationships may be represented as bounded `double` values in v0.1, but all calculations must be deterministic on the supported .NET/runtime architecture and tested.

Do not persist `NaN` or infinity. Clamp bounded values at subsystem boundaries.

If cross-architecture floating-point drift becomes observable in determinism tests, move affected canonical values to fixed-point integers rather than tolerating divergence.

### Derived data

Do not persist data that can safely and cheaply be derived unless it is required for historical fidelity or performance.

Examples:

- age derives from birth minute;
- life stage derives from age;
- occupation label derives from lifetime/recent work statistics;
- relationship label derives from relationship values.

---

## 17. World Generation Plan

Milestone 1 should implement a deterministic pipeline:

```text
world seed
  ↓
elevation field
  ↓
water placement
  ↓
terrain classification
  ↓
fertility
  ↓
resource placement
  ↓
starting-site scoring
  ↓
viability validation
```

Use a deterministic noise/generation implementation owned by the repository or a dependency whose behavior is explicitly pinned and tested.

Starting-site validation must require reasonable nearby access to freshwater, food, wood, and stone.

Do not silently replace a requested seed with another seed. If the seed cannot satisfy starting constraints under the algorithm, derive candidate sites deterministically within that same generated world and either find one or report generation failure clearly.

---

## 18. Citizen Decision Engine Plan

Each decision cycle:

1. derive current needs analytically from last materialized need values + elapsed simulation time;
2. determine legal candidate actions;
3. calculate utility components for each candidate;
4. apply deterministic variation using the decision domain and stable decision sequence;
5. select the highest utility using a deterministic tie-breaker;
6. materialize action state;
7. schedule action completion/next decision;
8. optionally expose the scored explanation in development diagnostics.

No LLM or remote API is involved.

Candidate actions are introduced only when their owning milestone lands.

---

## 19. Movement and Pathfinding

Use deterministic grid pathfinding.

Initial implementation:

- A*;
- fixed neighbor ordering;
- deterministic tie-breaking;
- terrain movement costs from configuration;
- immutable map geometry cache;
- optional path cache keyed by source/destination/map revision.

Do not let browser animation positions feed back into the server.

At high simulation speeds, movement may be represented as scheduled travel completion rather than rendering every intermediate visual step, provided resource access and travel time remain physically consistent.

---

## 20. Historical Event Architecture

Create historical events only for user-meaningful or analytically important facts.

Each event has:

```text
Id
WorldMinute
Type
Importance
Location?
Citizen links with semantic roles
Structure links with semantic roles
Typed/versioned payload
```

M6's persisted vocabulary is `HistoricalEventType`: `WorldCreated=1`, `SettlementFounded=2`, `CitizenBorn=3`, `CitizenDied=4`, `PartnershipFormed=5`, `FriendshipFormed=6`, `RivalryFormed=7`, `HouseholdCreated=8`, `StructureStarted=9`, `StructureCompleted=10`, `PopulationMilestone=11`, `ResourceShortageStarted=12`, `ResourceShortageEnded=13`, `CitizenSpecializationChanged=14`, and `SeasonStarted=15`. Importance values are `Debug=0`, `Routine=1`, `Personal=2`, `Notable=3`, `Major=4`, `Historic=5`; origin is `Live=1` or `MigrationBackfill=2`. Citizen roles are the fixed vocabulary `subject`, `parent`, `partner`, `founder`, `participant`, `member`, and `contributor`; structure links use `subject`. Entity IDs are invariant decimal strings in payloads and read DTOs.

Prefer typed payload records in C# serialized to JSON at the persistence boundary.

Do not build historical prose inside the simulation. The canonical payload is schema-controlled JSON with stable property order and no wall-clock values; summaries are derived presentation text only.

The UI formats events from structured values. M6 has no AI historian, fictional summary, or mutation route.

History is separate from event sourcing: canonical snapshot state plus the deterministic scheduled-event queue remain authoritative. M6 events are append-only facts. Checkpoint/load validates the persisted prefix, appends only a new tail, and writes links, statistics, and memories transactionally; conflict is corruption, not a repair opportunity. The history scheduler is `history.statistics-sample.v1` at priority 19 on exact 43,200-minute boundaries. M5→M6 backfill is direct and atomic, starts at the existing historical counter, and includes only exact world/settlement/season/descendant birth/death/partnership/household/structure/milestone facts. It intentionally excludes friendship, rivalry, specialization, founder pre-world births, and pre-M6 statistics.

---

## 21. API Shape

Use `/api/v1` from the beginning.

Expected endpoints by v0.1 completion:

```text
GET    /api/v1/health
GET    /api/v1/status

POST   /api/v1/worlds
GET    /api/v1/worlds
POST   /api/v1/worlds/{id}/load
GET    /api/v1/world

POST   /api/v1/control/pause
POST   /api/v1/control/resume
POST   /api/v1/control/speed

GET    /api/v1/settlement
GET    /api/v1/citizens
GET    /api/v1/citizens/{id}
GET    /api/v1/citizens/{id}/biography
GET    /api/v1/structures
GET    /api/v1/history
GET    /api/v1/statistics
```

The exact DTOs should be presentation-safe read models rather than EF entities or mutable simulation objects.

The operational control DTOs are deliberately separate from canonical world
state. They report host state, paused state, and bounded current speed; they do
not report or accept gameplay mutations.

Implemented M6 read routes are `GET /api/v1/history`, `GET /api/v1/history/{eventId}`, `GET /api/v1/citizens/{id}/biography`, `GET /api/v1/citizens/{id}/memories`, and `GET /api/v1/statistics`. History defaults to `minimumImportance=2`, `limit=50`, newest-first `(WorldMinute, EventId)` and bounds `limit` at 100. It accepts minute range, event type, importance, citizen, family root, structure, and `beforeEventId` cursor filters. Statistics returns bounded samples ascending by minute. Malformed canonical IDs return 400 and unknown IDs return 404. All M6 routes are immutable GET reads from the host's atomically published observation.

SignalR hub, conceptually:

```text
/hubs/world
```

Possible messages:

```text
WorldSnapshotUpdated
CitizenChanged
HistoricalEventsAppended
SimulationStatusChanged
```

High-speed modes should favor throttled/coalesced snapshots rather than emitting one network update per simulation event.

---

## 22. Frontend Architecture

Top-level v0.1 application layout:

```text
App shell
├── World viewport (PixiJS)
├── Settlement summary
├── Simulation controls
├── Selection inspector
│   ├── Citizen
│   └── Structure
└── History panel
```

Principles:

- server is authoritative;
- REST handles initial/query data;
- SignalR handles live invalidation/updates;
- PixiJS renders world entities but does not own them;
- React handles panels and controls;
- renderer and React share stable read-model IDs, not mutable simulation objects.

Mobile browsers need not receive a specialized UI in v0.1, but the layout should remain usable for basic inspection on an iPhone/iPad.

The implemented observer uses strict TypeScript DTO parsers, retains decimal IDs as strings, and makes bounded requests. It renders a factual newest-first history feed with citizen/family/type/importance/structure/time filters and cursor pagination, a selected-citizen biography timeline and structured memories, and a bounded monthly statistics table with a lightweight population trend. Requests are stale-response/unmount protected and GET-only; no router or chart dependency is required.

---

## 23. Windows Service and LAN Deployment

`LittleAges.Server` must support both:

1. normal console execution for development;
2. Windows Service execution for the always-on host.

Use supported .NET Windows Service hosting integration rather than custom service control code.

Configuration should include:

```text
DataRoot
ListenUrls
ActiveWorld
Checkpoint policy
Browser update rate
Logging level
```

Default development binding should be loopback-safe.

LAN binding must be explicit/documented, for example:

```text
http://0.0.0.0:5274
```

The documentation must warn that v0.1 assumes a trusted LAN and is **not designed for public Internet exposure**.

Windows Firewall setup should be documented when LAN access is implemented; do not silently modify firewall policy from application code.

---

## 24. Logging and Diagnostics

Use structured `ILogger` logging.

Log operational facts, not entire simulation state.

Useful diagnostics:

- service start/stop;
- world opened/closed;
- checkpoint duration/result;
- simulation speed;
- world minute;
- population;
- scheduled queue depth;
- events processed/second;
- years simulated/real minute;
- SignalR client count if useful;
- unhandled simulation exceptions.

Development-only diagnostic API/UI may expose utility-score breakdowns for a selected citizen.

Never make logs the canonical historical record.

---

## 25. Error Handling and Failure Policy

The simulation host must fail loudly rather than silently corrupt state.

Examples:

- invariant violation → pause simulation, log fatal simulation error, preserve last valid checkpoint;
- checkpoint transaction failure → keep current in-memory state, report degraded persistence status, retry according to bounded policy;
- malformed persisted world → refuse to run it without an explicit migration/recovery path;
- unsupported `SimulationRulesVersion` → refuse automatic continuation;
- browser disconnect → no simulation impact.

Do not catch broad exceptions and continue mutating canonical state when consistency is uncertain.

---

## 26. Testing Pyramid

### Domain unit tests

- IDs/value types;
- calendar conversion;
- clamping/normalization;
- derived life stages;
- relationship label derivation.

### Simulation unit tests

- deterministic RNG golden vectors;
- queue ordering;
- need decay;
- utility scoring;
- tie-breaking;
- resource calculations;
- mortality calculations;
- inheritance;
- relationship transitions.

### Simulation scenario tests

Small deterministic worlds/scenarios:

- hungry citizen chooses food when legal;
- no food creates health consequences;
- construction demand emerges from shelter shortage;
- successful repeated interactions can form friendship;
- household/child constraints are respected.

### Persistence tests

Use temporary real SQLite files rather than mocking SQLite.

Test:

- migrations;
- WAL/foreign keys;
- checkpoint transaction atomicity;
- open/close/reopen;
- all ID counters survive reload;
- scheduled queue survives reload;
- historical link integrity.

### Integration tests

Start the actual ASP.NET host in-process and verify:

- health/status;
- world lifecycle;
- simulation commands;
- read models;
- SignalR reconnect behavior where practical.

### Determinism tests

Run the same seed/config twice and compare canonical fingerprints.

Run at multiple host speeds and compare fingerprints.

### Save/reload equivalence

Continuous run vs. interrupted checkpoint/restart run must match.

### Long-run tests

Named test categories:

```text
LongRun1Year
LongRun10Years
LongRun100Years
LongRun500Years
```

Do not run every 500-year scenario on every small PR if runtime becomes large. Keep a fast deterministic PR suite plus scheduled/manual long-run validation.

---

## 27. Invariant Framework

Add explicit invariant validation callable in tests and optionally at configurable development intervals.

Examples:

- world minute monotonic;
- entity IDs unique;
- dead citizens cannot own active actions;
- resource quantities finite and non-negative;
- needs/traits/relationship values within valid ranges;
- partner links symmetrical;
- no self-parent/self-partner relationships;
- ancestry acyclic;
- household memberships internally consistent;
- structure occupancy within capacity;
- scheduled events reference valid entities or documented tombstone-safe behavior;
- historical links reference valid persistent entities;
- no `NaN`/infinity in canonical numeric state.

A failed invariant is a correctness failure, not an ignorable warning.

---

## 28. CI Strategy

Create GitHub Actions CI in Milestone 0.

Required pull-request checks:

### Backend — Linux

- restore;
- build Release;
- test fast suite.

### Backend — Windows

- restore;
- build Release;
- test fast suite.

Windows validation is mandatory because Windows is the production host.

### Frontend

- `npm ci`;
- lint;
- typecheck;
- unit tests;
- production build.

### Repository hygiene

- formatting check where practical;
- no generated build output committed;
- dependency lockfiles committed;
- warnings treated as errors for first-party C# code unless a documented exception is required.

Later CI additions:

- determinism scenario artifact/report;
- benchmark trend output;
- scheduled 100-year run;
- publishable Windows service artifact.

---

## 29. Coding Constraints That Protect Determinism

Inside `LittleAges.Domain` and `LittleAges.Simulation`, prohibit or review carefully:

```text
Guid.NewGuid()
Random / Random.Shared
DateTime.Now / DateTime.UtcNow as simulation input
Environment.TickCount
Stopwatch as simulation input
unordered iteration whose order affects decisions
Parallel.ForEach over mutating simulation state
Task.Run for canonical mutation
culture-dependent parsing/formatting used as canonical values
```

Collections whose iteration can affect outcomes must use explicit sorting/stable ordering.

Wall-clock calls remain valid in server logging, metrics, checkpoint metadata, and host pacing as long as they never affect canonical decisions.

---

## 30. Milestone/Branch Strategy

The repository begins with the design and implementation-plan documents on `main`.

Recommended development sequence:

| Milestone | Branch | Primary outcome |
|---|---|---|
| M0 Foundations | `codex/v0.1-foundations` | Buildable/tested skeleton + deterministic primitives + persistence/server/web foundations |
| M1 World | `codex/v0.1-world` | Deterministic terrain/resources/starting site |
| M2 Citizens | `codex/v0.1-citizens` | Generated citizens, scheduler, movement |
| M3 Survival | `codex/v0.1-survival` | Needs/resources/gathering/health |
| M4 Settlement | `codex/v0.1-settlement` | Structures/demand/specialization |
| M5 Social Life | `codex/v0.1-social` | Relationships/households/birth/aging/death |
| M6 History | `codex/v0.1-history` | Historical events/biographies/statistics |
| M7 Persistent Server | `codex/v0.1-persistence-host` | Robust checkpointing, Windows Service, LAN, reconnect |
| M8 100 Years | `codex/v0.1-100-years` | MAX/headless, profiling, tuning, acceptance evidence |

Each milestone should be independently reviewable and should not pull later milestone behavior forward merely because it is convenient.

---

## 31. Milestone 0 — Foundations Detailed Scope

Milestone 0 is the first implementation task.

### Required repository scaffolding

- `LittleAges.sln`;
- .NET 10 SDK pinning via `global.json`;
- central package/version management;
- nullable enabled;
- first-party warnings as errors;
- Domain, Simulation, Persistence, Server projects;
- four backend test projects;
- Vite React TypeScript web app;
- frontend lint/typecheck/test/build scripts;
- GitHub Actions CI for Windows + Linux backend and frontend.

### Required deterministic primitives

- strongly typed core ID pattern;
- `WorldMinute` and calendar conversion;
- `WorldSeed`;
- `RandomDomain`;
- repository-owned deterministic PRNG/hash derivation;
- golden RNG tests;
- deterministic scheduled-event ordering primitive and tests;
- monotonic deterministic counters.

### Required simulation shell

Implement a minimal engine capable of:

- holding `WorldMinute`;
- maintaining an empty scheduled event queue;
- stable event scheduling/ordering;
- advancing through synthetic test events;
- producing a minimal immutable status snapshot.

No citizens, terrain, resources, relationships, or gameplay yet.

### Required persistence shell

- EF Core SQLite setup;
- initial migration;
- `world_meta` minimal schema;
- database open/create service;
- WAL and foreign-key setup;
- minimal checkpoint/load for clock, seed, config/version metadata, deterministic counters, and synthetic scheduled events;
- round-trip tests using real temporary SQLite files.

### Required server shell

- ASP.NET Core host;
- console mode;
- Windows Service-compatible host configuration;
- `SimulationHost` skeleton using a single writer;
- command channel;
- graceful shutdown hook;
- `/api/v1/health`;
- `/api/v1/status`;
- development CORS only if needed for separate Vite dev server;
- configuration for data root and listening URLs.

### Required frontend shell

A deliberately small page proving the boundary works:

- Little Ages title;
- server health/status;
- current world minute when a test world is loaded;
- simulation running/paused state if implemented in the shell;
- no fake world map and no invented gameplay UI.

### Required documentation

- README with developer prerequisites and commands;
- `docs/architecture.md` describing actual M0 boundaries;
- `docs/simulation-model.md` documenting deterministic time/RNG/event ordering as implemented;
- Windows development/run instructions.

### M0 acceptance criteria

All must pass:

```text
dotnet restore
dotnet build -c Release
dotnet test -c Release

npm ci
npm run lint
npm run typecheck
npm test
npm run build
```

Additionally:

- CI passes on Windows and Linux;
- same seed/domain/keys produce locked RNG golden values;
- simultaneous scheduled events execute in documented stable order;
- a minimal persisted engine snapshot reloads identically;
- save/reload does not alter the next deterministic random result or scheduled-event order;
- server can run with no browser connected;
- health/status API works;
- frontend can display server status;
- Windows Service hosting support compiles on the production target;
- no v0.1 gameplay is prematurely implemented.

---

## 32. Milestones 1–8 Implementation Notes

### M1 — World

Add:

- map state/tables;
- deterministic generation pipeline;
- terrain/resources;
- starting-site viability;
- server world query;
- first PixiJS terrain renderer;
- generation fingerprints/golden seeds.

Gate: same seed/config produces byte-for-byte equivalent canonical world-generation snapshot/fingerprint.

### M2 — Citizens

Add:

- citizen schema;
- names, birth ages, traits, skills, needs baselines;
- 20 founding citizens;
- movement and A*;
- decision scheduling shell;
- client citizen rendering/selection.

Gate: 20 citizens exist and move deterministically in a world without survival behavior yet.

### M3 — Survival

Add:

- food/wood/stone gathering;
- hunger/rest/shelter evaluation;
- shared stockpile;
- action utility scoring;
- health consequences;
- seasonal resource modifiers;
- starvation/exposure death where defined.

Gate: a settlement can succeed or fail from resource conditions rather than scripts.

### M4 — Settlement

Add:

- shelters;
- stockpiles;
- workshop;
- construction demand;
- resource costs;
- contribution tracking;
- derived occupation labels.

Gate: map visibly evolves from autonomous settlement demand.

### M5 — Social Life

Add:

- relationship state;
- social actions;
- friends/rivals;
- partnerships;
- households;
- reproduction constraints;
- trait inheritance;
- aging and mortality;
- family graph invariants.

Gate: multiple generations can emerge without direct player control.

### M6 — History

Add:

- complete v0.1 historical event schema;
- event/citizen/structure link tables;
- biography queries;
- timeline filters;
- statistics sampling;
- UI history/biography surfaces.

Implemented contract: `HistoryVersion=1`, `m6-rng1-history1`, and `HistoricalEventSchemaVersion=1`. Historical events use the stable 15-value event enum, five-level importance, live/backfill origin, canonical structured payloads, separate historical IDs, immutable citizen/structure links, and append-only prefix-validated checkpointing. M6 emits important transitions only (not every movement, meal, gather, decision, or construction shift), generates bounded structured memories, and samples monthly statistics at exact 43,200-minute boundaries through priority-19 `history.statistics-sample.v1`. The direct M5→M6 migration backfills only exact facts and does not invent friendship, rivalry, specialization, founder pre-world births, or pre-M6 aggregates. Read APIs are bounded and immutable; the React observer parses DTOs strictly, keeps IDs as strings, filters/paginates history, and exposes factual biography/memory and statistics surfaces. These M6 contracts and goldens remain preserved for explicit M6 worlds; fresh M8 worlds use `m8-rng1-balance1` and only version the sampled shortage-recovery boundary.

Gate: important outcomes can be explained from structured facts without reading debug logs.

### M7 — Persistent Server

Harden:

- checkpoint policy;
- crash/restart behavior;
- service installation documentation;
- LAN binding;
- reconnect semantics;
- coalesced SignalR updates;
- backup guidance;
- sleep/resume testing on target Windows hardware.

Gate: close every browser, reboot/restart the service, and the world remains valid and resumes correctly.

### M8 — 100 Years (candidate evidence)

Implemented candidate boundary:

- `LittleAges.Headless` runs the normal `SimulationEngine` in MAX mode, with
  stable invariant JSON/Markdown projections for 1/10/100/500-year horizons;
- `acceptance` compares uninterrupted Run A with a real-SQLite checkpoint,
  dispose, reopen, load, and continued Run B, including full snapshot/history
  equivalence and mandatory invariants;
- `m8-rng1-balance1` is the fresh-world rules value. M6 remains explicitly
  selectable and its old goldens remain unchanged;
- M8 shortage recovery starts below 10 food per living citizen and confirms a
  non-zero-population end at the monthly statistics sample at 20 food per living
  citizen; zero population ends immediately;
- bounded 250/500 synthetic-adult one-day fixtures measure advance, read
  snapshot, and server projection costs without persistence;
- the manual `.github/workflows/v01-acceptance.yml` workflow uses
  `workflow_dispatch`, `windows-latest`, locked restore, Release build, seed 42,
  100 years, year-37 checkpoint, and artifact upload;
- candidate-only evidence is recorded in
  [`docs/v0.1-acceptance-report.md`](./v0.1-acceptance-report.md).

The acceptance artifact recorded exact Run A/Run B canonical equivalence and
the Section 62 matrix, but this document does not claim that service install,
reboot, sleep/resume, or hands-on browser checks have been completed. Gate:
evaluate those manual notes before calling the candidate released.

---

## 33. First Codex Implementation Task

Use the following prompt as the first implementation handoff.

```text
We are starting implementation of Little Ages in repository `joewolly/LittleAges`.

Little Ages is a persistent autonomous civilization simulation. The product definition is already decided. Do not redesign or expand the product during this task.

Before making changes, read these files in full:

- `docs/design-v0.1.md` — authoritative v0.1 product specification
- `docs/implementation-plan-v0.1.md` — technical implementation plan

If the two documents conflict on product behavior or scope, `docs/design-v0.1.md` wins.

TASK
Implement **Milestone 0 — Foundations only** from `docs/implementation-plan-v0.1.md`.

Do not implement terrain generation, citizens, resources, survival gameplay, construction, relationships, families, historical gameplay events, AI features, or other later milestones.

GIT
1. Fetch the latest remote state.
2. Start from the current `main`.
3. Create and work on branch:
   `codex/v0.1-foundations`
4. Preserve the existing design documents.
5. Do not merge to `main`, tag a release, or publish a release as part of this task.

REQUIRED IMPLEMENTATION

1. Repository/toolchain foundations
   - Create `LittleAges.sln` targeting .NET 10.
   - Add `global.json` and central package/version configuration.
   - Enable nullable reference types and first-party warnings-as-errors.
   - Create:
     - `src/LittleAges.Domain`
     - `src/LittleAges.Simulation`
     - `src/LittleAges.Persistence`
     - `src/LittleAges.Server`
     - `tests/LittleAges.Domain.Tests`
     - `tests/LittleAges.Simulation.Tests`
     - `tests/LittleAges.Persistence.Tests`
     - `tests/LittleAges.Integration.Tests`
   - Create `src/LittleAges.Web` as a strict TypeScript React/Vite app.

2. Enforce dependency direction
   - Domain must not reference Simulation, Persistence, Server, EF Core, ASP.NET, SQLite, SignalR, or frontend concerns.
   - Simulation references Domain only.
   - Persistence may reference Domain/Simulation as required for snapshot contracts.
   - Server may reference Domain/Simulation/Persistence.
   - Simulation must be directly runnable/testable without ASP.NET or SQLite.

3. Deterministic primitives
   - Add strongly typed deterministic 64-bit ID primitives/patterns.
   - Add `WorldMinute` and 360-day calendar conversion primitives.
   - Add `WorldSeed`.
   - Add versioned `RandomDomain` values.
   - Implement a repository-owned deterministic PRNG/derivation algorithm; do NOT use `System.Random` as canonical simulation randomness.
   - Add golden-vector tests so RNG behavior is locked.
   - Add persisted monotonic deterministic counters.
   - Add a deterministic scheduled-event ordering primitive ordered by:
       DueWorldMinute,
       Priority,
       EntitySortKey,
       Sequence.
   - Add tests proving stable tie ordering.

4. Minimal simulation shell
   - Implement a minimal `SimulationEngine` that owns canonical `WorldMinute` and an internal scheduled-event queue.
   - It must be able to schedule/process synthetic test events deterministically and emit a minimal immutable read/status snapshot.
   - No gameplay entities or systems yet.
   - Canonical mutation must be single-writer by design.

5. Persistence shell
   - Use SQLite with EF Core migrations.
   - Enable WAL mode and foreign keys.
   - Create an initial minimal `world_meta` persistence model containing at least seed, world minute, schema/rules/application version metadata, persisted deterministic counters, and persisted world configuration metadata.
   - Persist/restore the synthetic scheduled-event queue required for M0 tests.
   - Use real temporary SQLite files in persistence tests.
   - A checkpoint must be transactional.
   - Add a round-trip test proving a minimal engine state reloads with the same clock, counters, and future scheduled-event order.

6. Server shell
   - Create ASP.NET Core `LittleAges.Server`.
   - Support normal console hosting and Windows Service-compatible hosting.
   - Implement a `SimulationHost`/BackgroundService skeleton with one authoritative simulation writer.
   - Route host commands through `System.Threading.Channels` or the equivalent architecture specified in the implementation plan.
   - Add graceful shutdown/checkpoint plumbing.
   - Add:
       GET `/api/v1/health`
       GET `/api/v1/status`
   - Add configurable data root and listen URLs.
   - Browser presence must not be required for the simulation host to run.

7. Frontend shell
   - React + strict TypeScript + Vite.
   - Add lint, typecheck, test, and production-build scripts.
   - Create a deliberately small Little Ages status page that can show server health/status and current world minute/state when available.
   - Do not create fake gameplay/map UI yet.

8. CI
   - Add GitHub Actions pull-request CI.
   - Backend restore/build/test must run on Linux and Windows.
   - Frontend must run npm clean install, lint, typecheck, tests, and production build.
   - Commit dependency lockfiles.

9. Documentation
   - Add/update `README.md` with development prerequisites and exact commands.
   - Add `docs/architecture.md` describing the actual M0 architecture and dependency boundaries.
   - Add `docs/simulation-model.md` documenting the implemented deterministic time, RNG, ID/counter, and scheduled-event ordering rules.
   - Include Windows console-development instructions and note that service installation/hardening comes later.

DETERMINISM RULES
Inside Domain/Simulation, do not use canonical behavior based on:

- `Guid.NewGuid()`
- `System.Random` / `Random.Shared`
- `DateTime.Now` / `DateTime.UtcNow`
- `Environment.TickCount`
- wall-clock timing
- unordered collection iteration where order could change outcomes
- parallel mutation of simulation state

Wall-clock time is allowed for logs/operational metadata only and must not influence simulation outcomes.

VALIDATION
At minimum, run and report:

- `dotnet restore`
- `dotnet build -c Release`
- `dotnet test -c Release`
- frontend `npm ci`
- frontend lint
- frontend typecheck
- frontend tests
- frontend production build

Also validate:

- deterministic RNG golden vectors pass;
- same-time scheduled events execute in the required stable ordering;
- SQLite checkpoint/reload preserves world minute, seed, deterministic counters, and future event order;
- server runs without a browser;
- `/api/v1/health` and `/api/v1/status` respond successfully;
- Windows-targeted build/service-host integration compiles;
- no later-milestone gameplay was added.

DELIVERABLE
Return a concise implementation report containing:

1. starting branch/SHA;
2. final branch/SHA;
3. project/repository structure created;
4. deterministic RNG algorithm and why it is stable;
5. scheduled-event ordering implementation;
6. persistence/checkpoint approach;
7. server/Windows-service host structure;
8. frontend foundation;
9. CI jobs;
10. exact validation commands and results;
11. any deviations from the implementation plan and why;
12. known risks or items intentionally deferred to Milestone 1+.

Keep the branch clean and ready for review. Do not merge, tag, or release.
```

---

## 34. Review Checklist for the First PR

Before merging Milestone 0, independently verify:

- [ ] Domain has no infrastructure dependencies.
- [ ] Simulation has no ASP.NET/EF/SQLite dependencies.
- [ ] no canonical use of `Random`/GUID/wall-clock values exists.
- [ ] custom RNG has golden vectors.
- [ ] scheduled-event order is explicitly stable.
- [ ] queue/counters survive persistence round-trip.
- [ ] database checkpoint is transactional.
- [ ] Windows and Linux CI both pass.
- [ ] frontend lint/typecheck/test/build pass.
- [ ] server works with zero connected browsers.
- [ ] status/health endpoints are read-only.
- [ ] Windows Service integration does not make local console development awkward.
- [ ] documentation matches the actual implementation.
- [ ] no gameplay has leaked into M0.

---

## 35. Decision Log — Initial Locked Decisions

These decisions should not be casually reopened during M0:

| Decision | v0.1 choice |
|---|---|
| Server language/runtime | C# / .NET 10 LTS |
| Runtime host | ASP.NET Core |
| Persistent database | SQLite |
| Database model | Explicit canonical snapshots + append-only historical facts; not event sourcing |
| Active worlds | One running world at a time |
| Simulation mutation | Single writer |
| Simulation time | Signed 64-bit simulated minutes |
| Calendar | 360-day year |
| Canonical IDs | Deterministic world-local 64-bit counters |
| Randomness | Repository-owned, versioned deterministic algorithm/domains |
| UI | React + TypeScript |
| Map renderer | PixiJS |
| Live transport | SignalR |
| Production host | Windows Service-capable server |
| Network scope | Trusted LAN |
| AI in v0.1 | None |
| First implementation branch | `codex/v0.1-foundations` |

Changes to these decisions should require an explicit rationale and update to this document rather than incidental implementation drift.

---

## 36. Definition of Ready for Milestone 1

Milestone 1 may start only when:

- Milestone 0 branch has passed review;
- CI is green on Windows/Linux/frontend;
- deterministic primitives are locked by tests;
- persistence round-trip is proven;
- project dependency boundaries are correct;
- server/frontend development workflow is documented;
- M0 is merged to `main`;
- there are no unresolved correctness issues in the foundation.

At that point the next planning/implementation target is deterministic world generation — not citizens, survival, or UI polish.
