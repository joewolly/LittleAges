# Little Ages M0-M8 Architecture

The sections below retain the implementation record through M8 candidate acceptance.
For the M9 growth, M10 agriculture, and M11 household barter extensions,
see [Growing Settlement](growing-settlement.md). Fresh worlds now select
`m12-rng1-spaced1`, which retains M11 behavior except for spaced construction
sites; all statements below about older defaults describe their
historical milestone. Existing saves retain their rules. The product design and implementation plan remain preserved separately; this is an implementation record, not a release promise.

The opt-in successor is documented in [Living Settlement v0.2](living-settlement-v0.2.md).
Its modules reuse these engine, persistence, and observer boundaries while old
worlds retain the rules described here.

## Project and reference graph

All .NET projects target `net10.0` through `Directory.Build.props`.

```text
LittleAges.Simulation  ──> LittleAges.Domain
LittleAges.Persistence ──> LittleAges.Domain
                       └─> LittleAges.Simulation
LittleAges.Server      ──> LittleAges.Domain
                       ├─> LittleAges.Simulation
                       └─> LittleAges.Persistence
LittleAges.Headless    ──> LittleAges.Domain
                       ├─> LittleAges.Simulation
                       └─> LittleAges.Persistence
```

The arrows indicate project references:

- `LittleAges.Domain` has no project or infrastructure package references. It contains value types, typed IDs, counters, calendar rules, RNG contracts/implementation, and the immutable M1 world-map value model.
- `LittleAges.Simulation` references `LittleAges.Domain` only. It contains the canonical engine, queue, snapshots, synthetic data events, deterministic M1 `WorldGenerator`, M2 founder/movement rules, M3 survival, M4 settlement/construction, M5 social/family/lifecycle systems, and M6 historical read model/emission rules.
- `LittleAges.Persistence` references Domain and Simulation. It owns EF Core SQLite, migrations, database opening, snapshot checkpoint/load, append-only history, statistics, and structured memories.
- `LittleAges.Server` references Domain, Simulation, and Persistence. It owns ASP.NET Core hosting, configuration, the hosted owner, health, immutable observations, and HTTP endpoints.
- `LittleAges.Headless` references Domain, Simulation, and Persistence. It owns only CLI parsing, MAX/benchmark orchestration, invariant/report projection, and acceptance checkpoint/reopen comparison; it does not create a second simulation engine or alter canonical event processing.
- The test projects reference the production project under test; integration tests reference Server and exercise the real host and SQLite files.
- `LittleAges.Web` is a separate React/TypeScript/Vite application and is not a .NET project or simulation dependency.

There is no PixiJS renderer in the server and no gameplay mutation endpoint. M5 social/family/aging, M6 history, M7 persistent hosting, and the M8 headless/MAX acceptance boundary are implemented around the canonical runtime. M8 sampled shortage recovery is rules-versioned; M6 remains an explicit compatibility boundary.

M1 adds deterministic geography and immutable resource definitions to Domain/Simulation. M2 adds the founder roster and movement. M3 adds mutable resource quantities, a singleton stockpile, gathering, needs, health, and mortality. M4 adds bounded settlement storage, structures, material hauling, construction, housing and exposure, and derived occupations. M5 adds relationships, partnerships, households, reproduction, aging, and lifecycle. M6 adds an append-only factual history stream, statistics, structured memories, biographies, and bounded history queries. The generated `WorldMap` remains immutable and is held by `SimulationEngine`; it is included in read and persistence snapshots. Persistence checkpoints and restores the map rows directly, so loading does not regenerate from seed/configuration.

## Canonical ownership and request flow

`SimulationHost` owns one `SimulationEngine` and is the sole hosted mutation path. Its bounded `Channel<SimulationCommand>` has capacity 32, one reader, and multiple writers. A command is written to the channel, consumed by the host, applied to the engine, and—when appropriate—checkpointed by the host. HTTP pause, resume, and speed requests use this same single-reader command path and publish immutable operational status; they do not mutate canonical state or history.

```text
HTTP or host caller
        │
        ▼
bounded command channel (internal checkpoint/test/advance commands)
        │
        ▼
SimulationHost / one logical reader
        │
        ▼
SimulationEngine mutation
        │
        ├── SQLite checkpoint through WorldCheckpointStore
        └── immutable status publication
```

Gameplay has no public mutation endpoint. The current HTTP routes are `GET /api/v1/health`, `GET /api/v1/status`, immutable `GET /api/v1/world`, immutable ID-sorted `GET /api/v1/citizens` and `GET /api/v1/citizens/{id}`, immutable `GET /api/v1/settlement`, immutable ID-sorted `GET /api/v1/structures` and `GET /api/v1/structures/{id}`, and immutable `GET /api/v1/map`. The map projection includes row-major terrain and elevation plus ID-sorted immutable resource definitions for observer rendering; mutable resource quantities remain in the settlement observation. M8 adds bounded operational `POST /api/v1/control/pause`, `POST /api/v1/control/resume`, and `POST /api/v1/control/speed`; the checkpoint request remains an internal host command for infrastructure/tests, not a public API.

Reads do not inspect the mutable engine or database. `SimulationHost.Status` is an atomically published `ServerStatusSnapshot`; the HTTP handler returns that immutable record. The engine's `SimulationStatusSnapshot` deep-copies event data, citizens, settlement state, resource states, structures, and contributions into sorted read-only collections. This keeps observer reads independent of canonical mutation.

The status presentation contract exposes `WorldSeed` as an invariant decimal string, not a JSON number. This preserves every value in the `UInt64` seed range for clients such as JavaScript, including `18446744073709551615`.

## Canonical versus operational data

The canonical M0 snapshot consists of the seed, non-negative world minute, schema/rules/application version strings, world configuration JSON, deterministic counters, and the pending synthetic scheduled-event list. It is the input to restoration and deterministic continuation.

The M1 simulation snapshot additionally carries the generated immutable `WorldMap`. M2 adds citizens and scheduled movement state. M3 adds `survival_version`, mutable resource states, settlement stock, health/need boundaries, carrying state, and survival events. M4 adds `settlement_version`, bounded-storage and demand boundaries, structures, per-citizen contributions, home/target structure IDs, work counters, construction action state, and settlement events. M5 adds social/lifecycle state. M6 adds `history_version`, `HistoryState`, historical events and links, monthly statistics, and structured memories. Historical data is canonical and append-only, but it is not the source of live state reconstruction. Its seed, generation version/attempt, canonical configuration, tiles, resource nodes, starting coordinate, and fingerprint are canonical world state; operational timestamps and database paths remain excluded.

Operational data is deliberately separate: database path, listen URL, host state/error, log records, `CreatedUtc`, and `LastCheckpointUtc`. UTC checkpoint timestamps are metadata only and never enter simulation decisions. Little Ages remains snapshot-based, not event-sourced: the authoritative world is canonical snapshot state plus the deterministic scheduled-event queue; history describes important transitions alongside it.

## Persistence

`WorldDatabase.OpenAsync` creates the parent directory, opens one SQLite file, enables connection-level foreign keys, sets a 5,000 ms busy timeout, enables WAL, applies EF migrations, and verifies those settings. It uses a non-shared-cache connection. The M0 migration creates `world_meta` and `scheduled_events`; M1 extends `world_meta` and creates immutable `world_tiles`/`resource_nodes`; M2 adds the founder table; M3 adds survival metadata/state and constraints; M4 adds settlement metadata, construction-state columns, `structures`, and `structure_contributions`; M5 adds social/lifecycle state; M6 adds history events/links, `history_state`, `statistics_samples`, and `memories`.

### `world_meta`

One row is required, with `id = 1` enforced by a check constraint. It stores:

- `world_seed` as invariant decimal `TEXT`;
- `world_minute`;
- `world_schema_version`, `simulation_rules_version`, and `application_version`;
- `world_configuration_json`;
- `generation_version`, `generation_attempt`, and `starting_x`/`starting_y`;
- `world_fingerprint` as the stored canonical SHA-256 fingerprint;
- `citizen_generation_version`, `survival_version`, and `settlement_version` compatibility sentinels;
- `social_version` and `history_version` compatibility sentinels;
- `next_entity_id`, `next_historical_event_id`, and `next_scheduled_event_sequence`;
- operational `created_utc` and `last_checkpoint_utc`.

SQLite `INTEGER` is signed `Int64`, while a `WorldSeed` is the complete `UInt64` range. Therefore the seed is encoded and decoded as invariant, lossless decimal text rather than a SQLite integer. M6 requires `survival_version = 1`, `settlement_version = 1`, `social_version = 1`, `history_version = 1`, and rules `m6-rng1-history1`; fresh M8 rows use `m8-rng1-balance1` with the same unchanged sentinels. Pre-M6 rows use `history_version = 0` and are accepted only by the direct M5→M6 upgrade sentinel. There is no silent M6→M8 migration.

### M3 and M4 mutable state

`resource_nodes` remains the immutable M1 definition (`initial_quantity`, `maximum_quantity`, regeneration potential). M3 stores current depletion separately in `resource_state`, one row per node, with a foreign key and non-negative quantity bounded by the immutable node. `settlement_state` remains a singleton row (`id = 1`) holding food/wood/stone, and M4 adds `base_storage_capacity`, `demand_updated_minute`, and `exposure_consequences_start_minute`. A fresh M4 world starts at `400/0/0`, base capacity `800`, and seven-day exposure grace. Total stored resources may never exceed base capacity plus `800` per completed stockpile.

The `citizens` row retains the M2 fields and adds explicit survival and M4 state: `health_updated_minute`, `action_phase`, `target_resource_node_id`, `target_structure_id`, `home_structure_id`, carried resource/type/quantity, and five work counters. All fields are explicit snake_case columns. SQLite and application validation enforce positive IDs, founder ordinals 0..19, health/need ranges, actions 0..11, phases 0..6, non-negative counters/quantities, carrying/type coherence, valid targets, action timing, home capacity, and living/dead invariants. `structures` are ID-keyed with canonical type/status/cost/progress/condition state; `structure_contributions` is keyed by `(structure_id, citizen_id)` and stores cumulative work/wood/stone. Rows and canonical snapshots are ordered by IDs.

### `scheduled_events`

Each row stores `id`, `due_world_minute`, `priority`, `entity_sort_key`, `sequence`, `event_name`, and `event_payload_json`. `id` is the primary key, `sequence` has a unique index, and a check constraint enforces `id = sequence`. Scheduled events are instructions for future simulation work (including `history.statistics-sample.v1` at priority 19); historical events are immutable facts already observed and never replace the queue.

A checkpoint runs in one explicit transaction. It validates the complete snapshot and map, removes old resource/tile/event/metadata rows, writes all canonical rows (including M4 settlement, structures, contributions, and construction state), retains existing `created_utc`, and commits. Any failure rolls back and clears tracked state, leaving the previous committed checkpoint intact. Load requires exactly one metadata row, a complete `width * height` row-major tile set, valid resource references and IDs, a valid immutable `WorldMap`, a matching stored fingerprint, complete resource state, exactly one settlement row, coherent M4 structure/contribution/home/action state, and the exact reserved events. It rejects orphaned rows, missing/duplicate/out-of-range or semantically invalid rows, malformed configuration, unsupported compatibility versions, fingerprint mismatches, partial M4 state, and invalid event data. Loaded rows are used directly; generation is not repeated.

### M0-to-M1 compatibility upgrade

After EF applies the M1 schema, `WorldDatabase.OpenAsync` performs one application-level compatibility check before normal checkpoint use. A legacy checkpoint is recognized only when there is exactly one `world_meta` row with `id = 1`, all M1 sentinel fields still at their migration defaults (`generation_version = 0`, `generation_attempt = 0`, `starting_x = 0`, `starting_y = 0`, empty fingerprint), no tile/resource rows, and a valid M0 snapshot. The M0 JSON is validated as JSON without imposing the versioned M1 configuration shape; seed, time, metadata compatibility, counters, event identities/order, and next sequence are still strict. A missing metadata row with no canonical rows remains an empty database. Any partial sentinel or canonical row is rejected as corrupt, while generation version 1 with missing rows is handled by strict M1 loading and is never treated as legacy.

The upgrade starts one SQLite transaction before reading legacy state. It generates the default M1 world from the persisted M0 seed with `WorldGenerator`, normalizes the stored configuration to canonical default M1 JSON, preserves seed/time/versions/application/counters/events and `created_utc`, updates only operational `last_checkpoint_utc`, and writes metadata, events, tiles, and resources through the same transaction-bound writer used by ordinary checkpoints. Failure before commit rolls back to the untouched M0 sentinel, so a later open safely retries. Persistence never uses the simulation engine's optional null-world compatibility fallback to decide whether a database needs upgrading.

The complete compatibility chain is transactional and idempotent: M0→M1 creates the immutable world; M1→M2 requires the strict M1 sentinel (`citizen_generation_version=0`, no citizens/citizen events, and `m0-rng1`) and allocates the 20 founders; M2→M3 requires the complete M2 roster and no partial M3 rows/events, derives the in-flight action phase, initializes resource/settlement state, and schedules survival/regeneration events; M3→M4 requires exact M3 rules `m3-rng1-survival1`, `settlement_version=0`, no structures/contributions/demand event/M4 citizen state, preserves stores, raises base capacity to `max(800, pre-upgrade stored total)`, establishes a fresh seven-day exposure grace, and schedules demand after 360 minutes. The M4 rules value is `m4-rng1-settlement1`, `SettlementVersion = 1`, while `SurvivalVersion = 1` and World/Citizen generation versions remain `1`. Direct M0 opening chains through all upgrades. Reopening M4 validates it and does not repair it. Unknown or partial sentinels, corruption, and injected write failures are rejected or rolled back; retry does not regenerate an already committed map or founder roster.

`world_tiles` stores row-major `tile_index`, coordinates, terrain, normalized fields, walkability, and movement cost. `resource_nodes` stores deterministic ID, row-major tile reference and coordinates, resource type, quantities, and regeneration potential, with foreign-key cascade from its tile. SQLite check constraints enforce persisted enum/range/walkability invariants; application validation enforces completeness, coordinate/index agreement, ecology, deterministic resource IDs, map viability, and fingerprint integrity.

### M6 append-only history

`historical_events` stores `id`, `world_minute`, `event_type`, `importance`, `origin`, optional location, canonical payload JSON, and schema version. `historical_event_citizens` and `historical_event_structures` are indexed link tables with explicit role vocabularies. `statistics_samples` is keyed by sample `world_minute`; `history_state` is a singleton containing the history start, period counters, food-shortage state, and population milestone watermark. `memories` is keyed by `(citizen_id, historical_event_id, memory_type)` and contains only structured references, importance, valence, and created minute.

History uses the separate monotonic `NextHistoricalEventId` stream and never consumes citizen, structure, household, or ordinary scheduled-event IDs. A checkpoint validates the persisted event prefix against the in-memory canonical prefix, then appends only a new tail and its links in the same transaction. It never deletes and recreates committed history. Conflicting IDs, future minutes, invalid payloads, links, roles, ordering, or counters reject load/checkpoint as corruption. Repeated checkpoints are idempotent.

M5→M6 is a direct, transactional upgrade. It preserves the pre-M6 historical counter as `HistoryStartEventId` and backfills only exact facts: world creation, settlement founding, mathematically exact seasons, descendant births, deaths, partnerships/households, structures, and reconstructible population milestones. It does not fabricate friendship, rivalry, or specialization transitions whose threshold minute M5 did not retain, and it creates no retroactive statistics. A preexisting food shortage is marked at the upgrade boundary rather than assigned an invented onset. Upgrade failure leaves the complete M5 checkpoint unchanged.

## Server lifecycle, health, and failure semantics

Configuration keys are `DataRoot`, `ListenUrls`, `ActiveWorld`, `WorldSeed`, `SimulationMinutesPerSecond`, `CheckpointSimulationMinutes`, `CheckpointMinimumRealSeconds`, `CheckpointRetryCount`, `CheckpointRetryDelaySeconds`, `BrowserUpdateIntervalMilliseconds`, and `ObserverStreamIntervalMilliseconds`. Defaults are a data directory below the application base directory, `http://127.0.0.1:5274`, `default-world`, seed `0`, simulation rate `1.44` minutes per real second (one 24-hour world day every 16 minutes 40 seconds), checkpoint every `360` simulation minutes with a `30` real-second minimum, `3` retries after the initial checkpoint attempt with a `2` second delay, browser invalidation every `500` milliseconds, and connected observer frames every `100` milliseconds. The active world database is `<DataRoot>\<ActiveWorld>.db` (with platform path separators).

On startup the host opens/migrates the database. It loads a valid checkpoint, or creates an engine with the configured seed and writes an initial checkpoint. A browser is not required.

The host states are `Starting`, `Running`, `Stopping`, and `Faulted`. Health maps Running to Healthy, Starting/Stopping to Degraded, and Faulted to Unhealthy. Startup errors, malformed JSON, unsupported schema/rules versions, orphaned rows, command-loop failures, and checkpoint failures are not swallowed: the host records Faulted, stops the host under the configured `StopHost` policy, and preserves the last valid checkpoint where one exists. Periodic checkpointing is serialized with simulation writes; a failed attempt marks persistence Degraded, retries according to the bounded policy, and marks the host Faulted when the attempts are exhausted.

The server exposes immutable `GET /api/v1/world` alongside `/api/v1/health`, `/api/v1/status`, `/api/v1/citizens`, `/api/v1/citizens/{id}`, `/api/v1/settlement`, `/api/v1/structures`, `/api/v1/structures/{id}`, and `/api/v1/map`. M6 adds `GET /api/v1/history`, `GET /api/v1/history/{eventId}`, `GET /api/v1/citizens/{id}/biography`, `GET /api/v1/citizens/{id}/memories`, and `GET /api/v1/statistics`; M7 adds the observer-only SignalR hub at `/hubs/world`. History accepts bounded `fromMinute`, `toMinute`, `eventType`, `minimumImportance`, `citizenId`, `familyCitizenId`, `structureId`, `beforeEventId`, and `limit` filters; it defaults to importance ≥2 and 50 rows, with a maximum of 100, newest first. Statistics accepts bounded minute ranges and limit and returns samples ascending by minute. The world response contains the seed as a decimal string, dimensions and tile count, generation version/attempt, starting coordinate, terrain counts, resource counts, and the canonical fingerprint. Citizen responses are copied, ID-sorted published observations with action/phase, health, needs, carrying, target/home structure IDs, occupation, work counters, and death fields. Settlement responses add capacity/used storage, shelter capacity and housing counts, completed structure counts, exposure grace, and the active project. Structure responses are ID sorted and include canonical progress, derived condition/type capability, sorted occupants, and contributions; map responses are immutable row-major terrain with the starting site. Canonical decimal IDs are required (malformed `400`, missing `404`); unavailable read models return `503` where applicable.

Normal shutdown closes the command writer, drains pending commands, performs a final checkpoint, and disposes the database. A final-checkpoint or cleanup failure is terminal and is rethrown after cleanup. A faulted host remains Faulted rather than being overwritten by Stopping. Browser disconnects have no effect on simulation state. A process crash or reboot can leave SQLite WAL sidecars, but startup validates and resumes the last committed checkpoint; suspended wall time is never converted into simulated catch-up.

The `Microsoft.Extensions.Hosting.WindowsServices` package is referenced and `AddWindowsService` is registered, so the server is Windows-Service compatible at the hosting integration level. Installation, service hardening, firewall rules, production publishing, restart/reconnect hardening, and LAN deployment procedures are documented in [`docs/windows-service.md`](windows-service.md); application code never changes Windows Firewall policy. Service install, reboot, sleep/resume, and hands-on browser checks remain manual target-Windows acceptance notes.

## Frontend observer boundary

The web application is a small observer React page with a compact operational control. It fetches relative health/status/citizen/settlement/structure read models, fetches the immutable map once, and uses strict typed parsers for history, biography, memory, statistics, and live-frame DTOs. Decimal IDs remain strings (including values beyond JavaScript's safe integer range). The page displays connection/host state, world minute, settlement capacity/construction, structure progress, citizen action/phase/occupation/work, needs, health, carrying, and death information, plus a bounded newest-first history feed, citizen/family/type/importance/structure/time filters, older-page loading, a factual biography timeline, structured memories, and a bounded monthly statistics table with a population trend. Summaries are derived factual server read-model text; no AI or prose is persisted. Requests are bounded and protected against stale responses/unmount updates. Pause, resume, and a few safe positive speed choices submit only host operational commands; they cannot change canonical state or history. Published observer fields are copies; browser refreshes cannot mutate simulation state. Connected clients consume a continuous SignalR stream of compact immutable status, citizen, and structure frames every `100` milliseconds. REST supplies initial state, slower ledger details, reconnect recovery, and disconnected fallback.

Vite proxies `/api` to `http://127.0.0.1:5274` during development. A published server uses ASP.NET Core static-file/default-file middleware to serve the Vite build from `wwwroot`, with an SPA fallback for non-API/non-hub paths. The browser is therefore an optional observer and never a prerequisite for the server's world ownership.

## CI and dependency locks

The pull-request workflow has:

- a fast backend job matrix on `ubuntu-latest` and `windows-latest`, each running `dotnet restore --locked-mode`, Release build with `--no-restore`, and `dotnet test --configuration Release --no-build --filter \"Category!=Long\"`;
- a frontend job on Ubuntu using Node 22, `npm ci`, lint, strict typecheck, tests, and production build.

The separate `Long tests` workflow has only manual dispatch and a weekly schedule. It runs the locked restore, Release no-restore build, and `dotnet test --configuration Release --no-build --filter \"Category=Long\"` on both Ubuntu and Windows, with a 180-minute job timeout. Long tests are not triggered by pull requests.

The manual `.github/workflows/v01-acceptance.yml` workflow is separate from
those suites. It is `workflow_dispatch`-only on `windows-latest`, restores in
locked mode, builds Release, runs seed 42 with `m8-rng1-balance1` for 100 years
with a year-37 SQLite checkpoint, and uploads JSON/Markdown acceptance
artifacts, with a 180-minute job timeout. The candidate evidence is recorded in
[`v0.1-acceptance-report.md`](v0.1-acceptance-report.md).

NuGet lock-file generation is enabled centrally and a `packages.lock.json` is committed for each of the eight .NET projects. The frontend uses its committed npm lock file with `npm ci`.

The workflow remains configured for Ubuntu/Windows backend and Ubuntu frontend jobs. The M4 acceptance suite is shared and cross-platform configured; the recorded evidence for this branch is local Windows evidence, and no hosted Linux execution is claimed here. No platform-specific fingerprint is accepted.

## Implemented and deferred milestones

M0 foundations, M1 generation/persistence, M2 citizens/movement, M3 survival, M4 settlement/construction, M5 social/lifecycle, M6 history, M7 persistent-host hardening, and M8 headless/MAX acceptance are implemented. M6 remains the locked compatibility rules value; fresh worlds use `m8-rng1-balance1`.

M2 adds a canonical `Citizen` collection owned by `SimulationEngine`. Twenty founders are
generated from `WorldSeed`, `CitizenGenerationVersion = 1`, and founder ordinal, then placed
on nearest walkable tiles around the starting site. HTTP and the observer UI consume immutable
ID-sorted snapshots. Citizen events use `citizen.decision.v1`, `citizen.move-step.v1`, and
`citizen.action-complete.v1`; payload IDs are invariant decimal strings. The M2 compatibility
rules value is `m2-rng1-citizen1`; `m0-rng1` snapshots are upgraded once without regenerating
their map. M3 extends this preserved M2 state with the survival events and mutable state described above.
- **M5:** relationships, households, reproduction, aging, and family systems (implemented).
- **M6:** historical gameplay events, biographies, statistics, structured memories, append-only persistence, and historical queries (implemented).
- **M7:** persistent-server hardening, service installation, LAN/firewall/deployment guidance, reconnect behavior, coalesced observer invalidation, backup guidance, sleep/resume verification, and production publishing.
- **M8:** headless/MAX operation, canonical fingerprints, long-run determinism evidence, profiling, tuning, and the 100-year acceptance run.

## M3 survival loop retained by M4

M3 is the retained survival foundation. `SurvivalVersion = 1` and its preserved rules value is `m3-rng1-survival1`; M4's current rules version is `m4-rng1-settlement1` with `SettlementVersion = 1`. World generation and founder generation remain version `1`.

M4 implements structures/shelters; M5 social/family/aging and M6 historical gameplay are retained in later sections as implemented compatibility layers.

The four persisted action phases are `None=0`, `TravelToTarget=1`, `Perform=2`, and `ReturnToStockpile=3`. Actions retain M2 values and add `Eat=5`, `GatherFood=6`, `GatherWood=7`, `GatherStone=8`, and `Dead=9` (all values 0..9 are reserved compatibility data). Decisions evaluate Eat, Rest, food/wood/stone gathering, Explore, Wander, and Idle. Eat uses the starting-site stockpile; gathering travels to a selected reachable node, performs, carries the actual yield home, deposits it, and then returns to a decision boundary. Carried goods are deliberately lost when a citizen dies.

Needs are fixed-point `[0,10000]`. Hunger rises by 2 per minute in Spring/Summer/Autumn and 3 in Winter; Rest rises by 3, Shelter by 1, and Social by 1. All projection is integer, saturating, and observer reads never mutate state. An Eat action lasts 30 minutes and consumes `min(10, FoodStored)`, reducing hunger by `5000 * consumed / 10`. Rest lasts 120 minutes and reduces Rest by 4000, clamped at zero.

Gathering uses base duration 180 minutes, with `max(60, 180 - relevantSkill / 100)`, and yield `baseYield + relevantSkill / 1000`: Food 12, Wood 10, Stone 8. Actual yield is the lesser of calculated yield and current node quantity; zero actual yield gives no carried goods and no experience. A successful gather depletes `resource_state`, adds 25 XP only to the relevant Foraging/Woodcutting/Stoneworking skill, and carries the result to the starting-site stockpile. Resource targets rank reachable nodes by path cost ascending, current quantity descending, then node ID ascending. A* path costs use orthogonal 10 or diagonal 14 multiplied by destination movement cost, with fixed neighbor ordering and no corner cutting.

Action score is integer and includes deterministic variation in `[-50,50]`. Eat: `3000 + hunger*4 - travelCost`. Rest: `rest*2`. Gather food: `hunger*3`; gather wood/stone: `4`/`3`; each also adds `industriousness/20 + relevantSkill/1000 + stockpileContribution - travelCost/10`, where stockpile contribution is 1800 below target or 500 at/above it (targets Food 400, Wood 120, Stone 100). Explore is `1000 + curiosity/4 + riskTolerance/8`; Wander is `700 + curiosity/10`; Idle is `500 + (10000-industriousness)/20`. Each score receives its action-specific variation. Ties resolve Eat, Rest, GatherFood, GatherWood, GatherStone, Explore, Wander, Idle.

Events are `citizen.decision.v1`, `citizen.move-step.v1`, `citizen.action-complete.v1`, `citizen.survival-check.v1`, and `resource.regenerate.v1`, with priorities decision 20, movement 10, completion 15, survival 18, and regeneration 5. Action completion is phase-based: travel is represented by successive move events; a Perform action completes at its persisted completion minute; a returning gather deposits at the stockpile before returning to the decision boundary. Citizen payloads contain exactly `citizenId` as a positive invariant decimal string and `actionSequence`; survival payloads contain exactly `citizenId`; regeneration payload is exactly `{"version":1}`. Survival runs every 360 minutes from `health_updated_minute`; regeneration runs at the next strict day boundary (1,440-minute interval), using the calendar season.

Resource regeneration is integer and capped at the immutable node maximum. Food adds `(regenerationPotential * basisPoints) / 10000` with Spring 12500, Summer 15000, Autumn 10000, Winter 2500. Wood adds `regenerationPotential / 8`; Stone adds exactly zero. Regeneration and target/path caches are deterministic and resource enumeration is ID ordered.

At each survival check, projected needs are materialized and damage is 300 for Hunger >=9000 plus 120 for Rest >=9500, multiplied by `(10000 - resilience/4) / 10000` using integer arithmetic. If there is no damage and Hunger <6000 and Rest <7000, health recovers `120 + resilience/1000`, capped at 10000. Health reaches zero only through these consequences; death cause is `deprivation` when both thresholds apply, otherwise `starvation` or `exhaustion`. Death records the current minute, freezes derived age at `DeathMinute`, sets action `Dead`, clears phase/timing/targets/carrying, removes all reserved citizen events, and leaves the row in the canonical roster. `Population` is living population under M3; total and dead counts remain observable.

## M3 fingerprints and acceptance evidence

`ComputeSurvivalFingerprint()` is SHA-256 over length-prefixed UTF-8 fields in exact order: seed, current minute, world schema, immutable world fingerprint, canonical world configuration, rules version, citizen generation version, survival version, settlement food/wood/stone, next entity/historical/scheduled counters; every resource state as node ID and quantity; every citizen in ID order with ID, ordinal, names, birth, lifecycle IDs, location, health, death minute/cause, needs, needs-updated and health-updated minutes, traits, skills, action, phase, sequence, action timing/target, resource target, carrying type/quantity, and movement counters; then every queued event in order with ID, due minute, priority, entity key, sequence, name, and payload. The result is lowercase 64-character SHA-256.

Locked survival goldens are:

```text
seed 42, day 7 (minute 10080) -> 3671fac075c73d433f2b42344db4f2502e6558382152a36330a1b227854dd33a
seed 0, day 1 (minute 1440) -> 028677a72fa65710a064dd5573c6bc5dafbf0377faed0e10597db652c3238950
seed UInt64.MaxValue, day 1 (minute 1440) -> 3ff6cfe4eea7240784a1b46365a637a74fb0785607edd5e2276ffb3b529a501f
```

M1 world fingerprints, M2 founder roster fingerprints, and the deterministic A* path golden remain unchanged. The shared acceptance suite covers strict migrations and corruption rejection, rollback/idempotence, concurrent/read contention, seven checkpoint points (mid-eat travel/perform, mid-gather outbound/perform/return, before survival, before regeneration), save/reload and chunk equivalence, scarcity death at the computed minute, and server smoke. The recorded 30-day seed-42 evidence is 20→20 citizens, 0 deaths, 17,318 food consumed, 17,598 food gathered, final food 0, wood 126, stone 107, 70,588 depletion observations, 274 regeneration observations, health range 10,000..10,000, and skill progression for all 20 citizens. The suite is configured for cross-platform execution; this branch records local Windows evidence only and makes no hosted-Linux execution claim.

Those M3 simplifications are historical compatibility context. M4 supersedes the unlimited stockpile, no-structure, and no-carrying-capacity portions while preserving the remaining M3 survival data and rules as its upgrade input.

## M4 settlement and construction contract

M4 is the implemented settlement milestone. Its compatibility value is `m4-rng1-settlement1` and `SettlementVersion = 1`; M3 `m3-rng1-survival1` is the only direct predecessor. It adds `StructureType` values `Shelter=1`, `Stockpile=2`, `Workshop=3` and `StructureStatus` values `UnderConstruction=1`, `Complete=2`. The canonical immutable requirements are Shelter `40 wood / 10 stone / 600 work`, Stockpile `60 / 30 / 900`, and Workshop `80 / 50 / 1200`. An under-construction structure has condition 0 and no completion minute; a complete one has condition 10,000, full delivered material/work, and a completion minute.

The singleton settlement starts with food/wood/stone `400/0/0`, base storage capacity `800`, and a seven-day (`10,080` minute) exposure grace. Each completed stockpile adds `800` capacity; deposits accept only available capacity. A gatherer whose carried goods do not fit retains them in `WaitingForStorage` and retries completion after `60` minutes, so nothing is silently lost. Construction material is physical: a hauler travels to the starting-site stockpile, carries either wood or stone to the one active project, and delivers only its unreserved remaining requirement. Reservations include already in-transit material, preventing over-delivery. Carry capacity is `20 + min(20, HaulingSkill / 1000)`. Material deliveries grant 15 hauling XP and record cumulative per-citizen contribution/work time.

Every `360` minutes the reserved `settlement.evaluate-demand.v1` event (priority `7`, canonical `{"version":1}` payload) refreshes `DemandUpdatedMinute`, reconciles housing, and schedules the next evaluation. If no project is active, demand priority is: shelter while shelter capacity is below living population; stockpile once stored resources reach 80% of capacity; otherwise one workshop after at least three completed structures, if none exists. Site selection is reachable buildable non-freshwater, excludes the starting site, resources, and occupied structure tiles, and is ordered by path cost, Manhattan distance, row, then column. Only one `UnderConstruction` project is permitted.

Construction decisions introduce `HaulConstruction=10` and `Build=11`, plus phases `TravelToStockpile=4`, `TransportToConstruction=5`, and `WaitingForStorage=6`. M4 decision ties are Eat, Rest, GatherFood, HaulConstruction, Build, GatherWood, GatherStone, Explore, Wander, Idle. A build shift lasts `180` minutes, applies base `100 + ConstructionSkill / 1000` work (capped to the remaining requirement), and gains a 12,500-basis-point multiplier if a workshop is complete; it grants 25 construction XP. Completion records cumulative `StructureContribution` values, marks the structure complete, and immediately reconciles shelters.

Completed shelters house four living citizens each. Assignment preserves valid existing homes where capacity permits, then assigns remaining living citizens by citizen ID to completed shelters by structure ID. A citizen rests at an assigned shelter when away from it; a shelter rest reduces Shelter need by 7,000 in addition to the normal rest reduction. After the exposure grace, Shelter need at least 9,000 causes survival damage of 150 per check (300 in Winter), with the normal resilience multiplier. Recovery also requires Shelter below 8,000. Death can therefore have the canonical `exposure` cause, and death clears home/action/carrying state and triggers reassignment.

M4 persists target/home structure IDs, the five lifetime work counters (foraging, woodcutting, stoneworking, construction, hauling), structures, and contributions. Occupation is derived rather than stored: under 360 total work minutes, or with no category at least 40% of the total, is `Generalist`; otherwise the largest counter wins, breaking ties Forager, Lumberjack, Stoneworker, Builder, Hauler. The M4 fingerprint extends the M3 length-prefixed SHA-256 state with settlement version/boundaries, structure state, contributions, citizen M4 fields/counters, and the demand event. M1 world and M2 roster goldens remain unchanged; M3 goldens remain evidence for the predecessor contract rather than M4 state.

M3→M4 is a single transactional upgrade. It accepts only the exact untouched M3 sentinel (`settlement_version=0`, M3 rules, no structure rows/contributions/demand event/M4 citizen fields), creates no structure, raises base capacity to at least `800` without invalidating existing stores, initializes the M4 boundaries, changes rules and sentinel, and schedules the first demand evaluation. Any partial sentinel or failed write rejects or rolls back atomically; a retry is idempotent. This is a schema/state upgrade, not map or founder regeneration.

M4 read APIs remain immutable observations: map data is copied row-major, citizens/structures/contributions are sorted and copied, and the host publishes one coherent atomically replaced observation. There is still no public gameplay mutation route. At the M4 boundary, relationships, households, reproduction, aging, and families were still future work; M5 and M6 now provide those layers without changing the M4 contracts.

## M5 social-life contract

M5 uses the explicit `m5-rng1-social1` rules value and `SocialVersion = 1`; M2, M3, and M4 retain their own named compatibility values. Relationships are sparse, ordered citizen pairs with deterministic interaction state; their labels are derived, never persisted. Partnerships are symmetric, close-kin-safe, and create a shared household whose ID comes from the same global entity stream as citizens and structures. A citizen's nullable `HouseholdId` is the sole current/last membership link; household member lists are derived.

M5 introduces local sixty-minute Socialize actions, daily family and lifecycle events, children with deterministic reproduction-domain names and traits, and daily natural mortality. Family/lifecycle events use strict next-day boundaries and priorities 8/17. Social snapshots and persistence retain parent IDs, partners, households, relationship values, births, deaths, and child action state, but M5 deliberately allocates no historical-event IDs and creates no history table.

Social target desirability retains the M5 base, sociability, relationship, partner, family, rival, and deterministic-variation components. Every non-null relationship additionally subtracts `max(0, 4000 - min(4000, (CurrentMinute - LastInteractionMinute) / 10))`; a null edge has no penalty. The term applies uniformly to ordinary, family, partner, and rival edges, retains score-descending/distance-ascending/citizen-ID-ascending ranking, and is M5 compatibility data rather than partnership matchmaking.

The observer remains immutable. Citizens expose family/household fields, relationships are available from `/api/v1/citizens/{id}/relationships`, and household collection/detail endpoints return derived member/partner/child IDs. Settlement summaries expose current social counts only. M6 adds history, biography, memory, and statistics reads on the same immutable host publication boundary; there are still no mutation endpoints.

Citizen observations may additionally expose a renderer-only `movementPlan`.
It contains the current location at the observed minute followed by the
remaining canonical path, with arrival minutes accumulated from the existing
terrain step costs. It is rebuilt from immutable state, never persisted, never
fingerprinted, and never accepted back from a client. A connected client
receives compact immutable scene frames through `StreamWorld`; REST remains the
bootstrap and recovery authority for the complete observation. A disconnected
visible browser refreshes at two seconds, a hidden browser at ten seconds, and
reconnect restores the live stream plus a serialized ledger refresh.

## M6 history, biographies, and statistics

At the M6 compatibility boundary, `CurrentSimulationRulesVersion` was `m6-rng1-history1`, with `HistoryVersion = 1` and `HistoricalEventSchemaVersion = 1`. Its persisted event vocabulary was `WorldCreated=1`, `SettlementFounded=2`, `CitizenBorn=3`, `CitizenDied=4`, `PartnershipFormed=5`, `FriendshipFormed=6`, `RivalryFormed=7`, `HouseholdCreated=8`, `StructureStarted=9`, `StructureCompleted=10`, `PopulationMilestone=11`, `ResourceShortageStarted=12`, `ResourceShortageEnded=13`, `CitizenSpecializationChanged=14`, and `SeasonStarted=15`. Importance values are `Debug=0`, `Routine=1`, `Personal=2`, `Notable=3`, `Major=4`, and `Historic=5`; origin values are `Live=1` and `MigrationBackfill=2`.

Each immutable historical event stores a positive historical ID, non-negative world minute, type, importance, origin, optional tile location, schema-versioned canonical JSON payload, and links to persistent citizens/structures. Citizen link roles are the fixed vocabulary `subject`, `parent`, `partner`, `founder`, `participant`, `member`, and `contributor`; structure links use `subject`. Entity IDs in payloads and wire DTOs are invariant decimal strings. Payloads contain facts only; display summaries are derived from event type, payload, and links. Routine movement, meals, gathering, decisions, and construction shifts do not emit history.

Live emission is placed at canonical transition points: the initial world/settlement/season at minute 0; births before population milestones; deaths on every supported death path; friendship/rivalry only on the first threshold crossing; partnership before household creation; structure start/completion (completion includes ascending contributor links); specialization only when the derived occupation label changes; and season boundaries alongside monthly sampling. M6 shortage transitions use immediate Food `< living population × 10` start and Food `≥ living population × 20` end. Fresh M8 shortage starts at the same threshold, remains active across ordinary recovery, and confirms a non-zero-population end only at the priority-19 monthly statistics sample; zero population ends immediately. The statistics scheduler is `history.statistics-sample.v1`, priority 19, at the next strict 43,200-minute boundary. Samples reset period counters after observing living population, resource stores, shelter capacity, average health, and projected average hunger without mutating needs.

Structured memories reference `(citizen, historical event, memory type)` and contain importance, clamped emotional valence, and created minute. `ChildBorn`, `PartnerDied`, and `PartnershipFormed` memories are permanent; friendship, rivalry, and structure-completion memories are capped at 64 per citizen with deterministic importance/time/event-ID pruning. Memories do not influence simulation decisions. A biography is a factual read model assembled from a citizen snapshot, event links, parent/partner/children IDs, and memories, and remains queryable after death.

M5→M6 upgrade is a direct atomic migration guarded by `SimulationRulesVersion=m5-rng1-social1`, `SocialVersion=1`, `HistoryVersion=0`, and an otherwise complete M5 snapshot with no partial history rows/state/scheduler. It begins at the pre-M6 historical counter and deterministically orders backfill candidates by minute, event rank, and entity IDs. It backfills world, settlement, exact seasons, descendant births, deaths, partnerships/households, structures, and reconstructible population milestones. It does not invent friendship, rivalry, specialization, founder births before minute 0, or pre-M6 aggregate statistics; a preexisting shortage is recorded at the upgrade boundary. Reopening is idempotent and failed upgrade/checkpoint transactions roll back all rows and counters.

`HistoryFingerprint` extends the M5 canonical fingerprint with history state, event payloads/IDs, links, samples, and memories in explicit sorted order. It excludes summaries, filters, pagination, and wall-clock metadata and uses invariant numeric formatting. The host atomically publishes an immutable history observation alongside status/citizens/structures; HTTP queries never access mutable engine state or SQLite directly. `/api/v1/history` defaults to importance ≥2, limit 50, descending minute/ID order and supports bounded citizen, family, type, importance, structure, minute, and cursor filters; detail and biography IDs reject malformed values with 400 and unknown values with 404. `/api/v1/statistics` returns bounded samples ascending by minute. All M6 routes are GET-only.

## M2 compatibility boundary

M2's canonical `Citizen` aggregate remains the compatibility foundation: exactly 20 founder rows, positive shared-counter IDs, ordinals 0..19, catalog-derived names, signed birth minute, four fixed-point needs, six fixed-point traits, six non-negative skills, action/sequence/timing/target state, nullable future lifecycle IDs/causes, and lifetime movement counters. Under M3, health, survival state, resource consumption, gathering skill progression, and mortality are now active; relationship, household, structure, and family fields remain reserved for M4/M5.

Founder generation is version `1` and is keyed by seed, ordinal, field, and the fixed repository name catalogs. Collision handling is deterministic. Founders are assigned to the nearest walkable tiles by squared distance then row-major order. Ages are 18..45 using signed checked `birth_minute = current_minute - (age * 518400 + deterministic_year_offset)`. Traits are 0..10000; skills are six fixed values that never change in M2. M2 rules are `m2-rng1-citizen1`; `m0-rng1` is accepted only as a one-time upgrade input.

Needs projection is pure, integer, and saturating: Hunger +2 except Winter +3, Rest +3, Shelter +1, Social +1 per minute, with all values clamped to 0..10000. M3 adds Eat, gathering, health, and mortality while retaining M2 movement. Citizen-local action sequence and explicit purpose keys feed stateless decision/target variation.

Movement uses non-persisted deterministic weighted A*: N, NE, E, SE, S, SW, W, NW; no diagonal corner cutting; orthogonal cost 10 and diagonal cost 14 multiplied by destination movement cost. Queue ties are F, H, row-major tile index, and local insertion sequence. The locked path golden is seed 42 `(131,130)` to `(127,126)`: `(131,130),(130,130),(129,130),(128,130),(127,129),(127,128),(127,127),(127,126)`.

Citizen event names are `citizen.decision.v1`, `citizen.move-step.v1`, and `citizen.action-complete.v1`, with priorities 20, 10, and 15 respectively. Payloads are exactly `{"citizenId":"<positive invariant decimal>","actionSequence":<non-negative integer>}`; legacy synthetic events retain `{}`. Dispatch validates payload structure, identity, entity key, action sequence, state, timing, reachability, and one-next-event-per-citizen invariants. Processed events retain a monotonic count and bounded 32-entry diagnostics.

The M2 EF migration adds `citizen_generation_version` to `world_meta` and a constrained `citizens` table with explicit snake_case columns and a unique ordinal index. Rows are ID ordered on load. Metadata, world, resources, events, and citizens are checkpointed transactionally. M1→M2 requires the complete M1 world, cver `0`, zero citizen rows/events, and exact `m0-rng1`; it preserves seed/minute/map fingerprint/counters/events/CreatedUtc, allocates 20 IDs, and queues current-minute decisions. M0→M2 chains M0→M1 then M1→M2. Partial/corrupt/unknown state rejects; failed upgrades roll back and retry once without regeneration.

The M2 host/read behavior remains the compatibility foundation: the host is the only writer, status exposes population only, and citizens use canonical decimal IDs. M4 extends this read boundary with the immutable map and structure observations described above; it still has no PixiJS or gameplay mutation controls, and the browser never owns canonical state. M8 adds only bounded operational pause/resume/speed commands through the host channel.

## M7 persistent-host and delivery contract

M7 hardens the long-lived host without changing canonical state. M6 snapshots retain `m6-rng1-history1`; fresh M8 snapshots use `m8-rng1-balance1`. The host advances at `SimulationMinutesPerSecond` (default `1.44`, one world day per 1,000 real seconds; zero starts paused) and checks periodic persistence after serialized advancement. Bounded pause, resume, and positive-speed commands are operational only and use the host's single-reader channel. A periodic checkpoint is due at least every `360` simulation minutes and no more often than the `30` second real-time minimum. Each failure marks persistence `Degraded`, retries up to the configured `CheckpointRetryCount` (`3` retries after the initial attempt), waits `CheckpointRetryDelaySeconds` (`2` seconds) between attempts, and transitions the host to `Faulted` when the bounded attempts are exhausted. The browser update interval defaults to `500` milliseconds.

Normal stop drains the command channel, writes a final checkpoint, and then closes SQLite. The final checkpoint is the recovery boundary: operators should copy a database only after its successful completion. A process crash, forced termination, or reboot does not provide a final checkpoint; on the next start the host opens SQLite, validates the last committed checkpoint (including WAL recovery performed by SQLite), and resumes it. No wall-clock interval spent suspended or stopped is converted into simulation-minute catch-up. Backup and restore procedures, including the WAL sidecar caveat, are in [`docs/backup-and-recovery.md`](backup-and-recovery.md).

The published server uses ASP.NET Core default/static-file middleware to serve the Vite output from `wwwroot`; non-API/non-hub paths fall back to the published `index.html`. This is a static observer surface, not a second state store. `GET /api/v1/*` observations bootstrap the browser and remain the reconnect/failure fallback. The observer-only SignalR hub `/hubs/world` exposes `StreamWorld`, a per-client stream of compact immutable live frames on the `100` millisecond cadence, and retains coalesced `worldChanged` invalidations for slower ledger refresh. Neither path accepts gameplay mutation.

The default listen URL is loopback-only, `http://127.0.0.1:5274`. LAN exposure requires an explicit trusted-LAN binding such as `http://0.0.0.0:5274`; the installer-managed Windows Firewall rule allows any remote address on the `Private` profile so routed trusted VLANs can reach the host without configured source CIDRs. Network routing and ACLs must restrict access to trusted networks. Reachability includes observer pause, resume, and speed controls. Little Ages provides no authentication or TLS and is not designed for public Internet exposure; do not configure Internet port forwarding. The application code does not change firewall policy. The publish script and Windows Service lifecycle are documented in [`docs/windows-service.md`](windows-service.md).

Pull requests use the fast CI workflow: Ubuntu and Windows backend restore/build/test jobs exclude `Category=Long`, while the existing Ubuntu frontend job is unchanged. The separate `Long tests` workflow is manual or weekly only, runs the locked Release backend build and `Category=Long` tests on Ubuntu and Windows, and has a 180-minute timeout. These workflow boundaries do not alter `m6-rng1-history1` or any M0-M6 compatibility golden.

## M8 headless and acceptance boundary

`LittleAges.Headless` calls the normal engine and queue. MAX removes only
wall-clock pacing; it cannot reorder events, change RNG/history, parallelize
mutation, or add timing to canonical state. The CLI emits invariant, stable
JSON/Markdown for supported horizons 1, 10, 100, and 500 years. Its acceptance
mode runs an uninterrupted path and a real-SQLite checkpoint/reopen path, then
compares full canonical snapshot/history projections, sentinels, links,
statistics, memories, and fingerprints. The candidate result and Section 62
matrix are recorded in [`v0.1-acceptance-report.md`](v0.1-acceptance-report.md).

Fresh worlds use `m8-rng1-balance1`; M6 is still selected explicitly for old
snapshots and locked goldens. M8 shortage recovery confirms non-zero-population
ends at a monthly statistics sample while preserving the M6 immediate 10/20
behavior. The manual `workflow_dispatch` acceptance workflow runs on Windows
with locked restore, a Release build, a 180-minute timeout, seed 42, a
year-37 checkpoint, and JSON/Markdown artifact upload. Target-Windows service
install, reboot, sleep/resume, and hands-on browser checks remain manual notes.
