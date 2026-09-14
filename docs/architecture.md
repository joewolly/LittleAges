# Little Ages M0-M3 Architecture

This document describes the implementation that exists through M3. The product design and implementation plan remain preserved separately; this is an implementation record, not a promise that later-milestone systems already exist.

## Project and reference graph

All .NET projects target `net10.0` through `Directory.Build.props`.

```text
LittleAges.Simulation  ──> LittleAges.Domain
LittleAges.Persistence ──> LittleAges.Domain
                       └─> LittleAges.Simulation
LittleAges.Server      ──> LittleAges.Domain
                       ├─> LittleAges.Simulation
                       └─> LittleAges.Persistence
```

The arrows indicate project references:

- `LittleAges.Domain` has no project or infrastructure package references. It contains value types, typed IDs, counters, calendar rules, RNG contracts/implementation, and the immutable M1 world-map value model.
- `LittleAges.Simulation` references `LittleAges.Domain` only. It contains the canonical engine, queue, snapshots, synthetic data events, deterministic M1 `WorldGenerator`, M2 founder/movement rules, and M3 survival rules.
- `LittleAges.Persistence` references Domain and Simulation. It owns EF Core SQLite, migrations, database opening, and snapshot checkpoint/load.
- `LittleAges.Server` references Domain, Simulation, and Persistence. It owns ASP.NET Core hosting, configuration, the hosted owner, health, and HTTP endpoints.
- The four test projects reference the production project under test; integration tests reference Server and exercise the real host and SQLite files.
- `LittleAges.Web` is a separate React/TypeScript/Vite application and is not a .NET project or simulation dependency.

There is no SignalR hub, PixiJS renderer, static frontend serving, or mutation endpoint. M3 survival is implemented in the canonical simulation engine; structures/shelters (M4), social/family/aging (M5), and history (M6) are not implemented.

M1 adds deterministic geography and immutable resource definitions to Domain/Simulation. M2 adds the founder roster and movement. M3 adds mutable resource quantities, a singleton stockpile, gathering, needs, health, and mortality. The generated `WorldMap` remains immutable and is held by `SimulationEngine`; it is included in read and persistence snapshots. Persistence checkpoints and restores the map rows directly, so loading does not regenerate from seed/configuration.

## Canonical ownership and request flow

`SimulationHost` owns one `SimulationEngine` and is the sole hosted mutation path. Its bounded `Channel<SimulationCommand>` has capacity 32, one reader, and multiple writers. A command is written to the channel, consumed by the host, applied to the engine, and—when appropriate—checkpointed by the host.

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

M3 exposes no public mutation endpoint. The current HTTP routes are `GET /api/v1/health`, `GET /api/v1/status`, immutable `GET /api/v1/world`, immutable ID-sorted `GET /api/v1/citizens` and `GET /api/v1/citizens/{id}`, and immutable `GET /api/v1/settlement`. The checkpoint request exists as an internal host command for infrastructure/tests, not as a public API.

Reads do not inspect the mutable engine or database. `SimulationHost.Status` is an atomically published `ServerStatusSnapshot`; the HTTP handler returns that immutable record. The engine's `SimulationStatusSnapshot` copies event data into a read-only collection. This keeps observer reads independent of canonical mutation.

The status presentation contract exposes `WorldSeed` as an invariant decimal string, not a JSON number. This preserves every value in the `UInt64` seed range for clients such as JavaScript, including `18446744073709551615`.

## Canonical versus operational data

The canonical M0 snapshot consists of the seed, non-negative world minute, schema/rules/application version strings, world configuration JSON, deterministic counters, and the pending synthetic scheduled-event list. It is the input to restoration and deterministic continuation.

The M1 simulation snapshot additionally carries the generated immutable `WorldMap`. M2 adds citizens and scheduled movement state. M3 adds `survival_version`, mutable resource states, settlement stock, health/need boundaries, carrying state, and survival events. Its seed, generation version/attempt, canonical configuration, tiles, resource nodes, starting coordinate, and fingerprint are canonical world state; operational timestamps and database paths remain excluded.

Operational data is deliberately separate: database path, listen URL, host state/error, log records, `CreatedUtc`, and `LastCheckpointUtc`. UTC checkpoint timestamps are metadata only and never enter simulation decisions. M0 is snapshot-based, not event-sourced; it has no historical gameplay event store.

## Persistence

`WorldDatabase.OpenAsync` creates the parent directory, opens one SQLite file, enables connection-level foreign keys, sets a 5,000 ms busy timeout, enables WAL, applies EF migrations, and verifies those settings. It uses a non-shared-cache connection. The M0 migration creates `world_meta` and `scheduled_events`; M1 extends `world_meta` and creates immutable `world_tiles`/`resource_nodes`; M2 adds the founder table; M3 adds survival metadata/state and constraints.

### `world_meta`

One row is required, with `id = 1` enforced by a check constraint. It stores:

- `world_seed` as invariant decimal `TEXT`;
- `world_minute`;
- `world_schema_version`, `simulation_rules_version`, and `application_version`;
- `world_configuration_json`;
- `generation_version`, `generation_attempt`, and `starting_x`/`starting_y`;
- `world_fingerprint` as the stored canonical SHA-256 fingerprint;
- `citizen_generation_version` and `survival_version` compatibility sentinels;
- `next_entity_id`, `next_historical_event_id`, and `next_scheduled_event_sequence`;
- operational `created_utc` and `last_checkpoint_utc`.

SQLite `INTEGER` is signed `Int64`, while a `WorldSeed` is the complete `UInt64` range. Therefore the seed is encoded and decoded as invariant, lossless decimal text rather than a SQLite integer. M3 requires `survival_version = 1` with current rules; pre-M3 rows use zero.

### M3 mutable state

`resource_nodes` remains the immutable M1 definition (`initial_quantity`, `maximum_quantity`, regeneration potential). M3 stores current depletion separately in `resource_state`, one row per node, with a foreign key and non-negative quantity bounded by the immutable node. `settlement_state` is a singleton row (`id = 1`) containing `food_stored`, `wood_stored`, and `stone_stored`; M3 starts at `400/0/0`, has no capacity limit, and uses the world starting site as the stockpile location.

The `citizens` row retains the M2 fields and adds explicit survival state: `health_updated_minute`, `action_phase`, `target_resource_node_id`, `carried_resource_type`, and `carried_resource_quantity`. All fields are explicit snake_case columns. SQLite and application validation enforce positive IDs, founder ordinals 0..19, health/need ranges, actions 0..9, phases 0..3, non-negative counters/quantities, carrying/type coherence, valid targets, action timing, and living/dead invariants. Rows and canonical snapshots are ordered by citizen ID; resource states are ordered by resource-node ID.

### `scheduled_events`

Each row stores `id`, `due_world_minute`, `priority`, `entity_sort_key`, `sequence`, `event_name`, and `event_payload_json`. `id` is the primary key, `sequence` has a unique index, and a check constraint enforces `id = sequence`.

A checkpoint runs in one explicit transaction. It validates the complete snapshot and map, removes old resource/tile/event/metadata rows, writes all canonical rows (including M3 mutable state), retains existing `created_utc`, and commits. Any failure rolls back and clears tracked state, leaving the previous committed checkpoint intact. Load requires exactly one metadata row, a complete `width * height` row-major tile set, valid resource references and IDs, a valid immutable `WorldMap`, a matching stored fingerprint, exactly one M3 resource state per immutable node, and exactly one settlement row. It rejects orphaned rows, missing/duplicate/out-of-range or semantically invalid rows, malformed configuration, unsupported generation/survival versions, fingerprint mismatches, partial M3 state, and invalid event data. Loaded rows are used directly; generation is not repeated.

### M0-to-M1 compatibility upgrade

After EF applies the M1 schema, `WorldDatabase.OpenAsync` performs one application-level compatibility check before normal checkpoint use. A legacy checkpoint is recognized only when there is exactly one `world_meta` row with `id = 1`, all M1 sentinel fields still at their migration defaults (`generation_version = 0`, `generation_attempt = 0`, `starting_x = 0`, `starting_y = 0`, empty fingerprint), no tile/resource rows, and a valid M0 snapshot. The M0 JSON is validated as JSON without imposing the versioned M1 configuration shape; seed, time, metadata compatibility, counters, event identities/order, and next sequence are still strict. A missing metadata row with no canonical rows remains an empty database. Any partial sentinel or canonical row is rejected as corrupt, while generation version 1 with missing rows is handled by strict M1 loading and is never treated as legacy.

The upgrade starts one SQLite transaction before reading legacy state. It generates the default M1 world from the persisted M0 seed with `WorldGenerator`, normalizes the stored configuration to canonical default M1 JSON, preserves seed/time/versions/application/counters/events and `created_utc`, updates only operational `last_checkpoint_utc`, and writes metadata, events, tiles, and resources through the same transaction-bound writer used by ordinary checkpoints. Failure before commit rolls back to the untouched M0 sentinel, so a later open safely retries. Persistence never uses the simulation engine's optional null-world compatibility fallback to decide whether a database needs upgrading.

The complete compatibility chain is transactional and idempotent: M0→M1 creates the immutable world; M1→M2 requires the strict M1 sentinel (`citizen_generation_version=0`, no citizens/citizen events, and `m0-rng1`) and allocates the 20 founders; M2→M3 requires the complete M2 roster and no partial M3 rows/events, derives the in-flight action phase, sets each founder's health boundary to the M2 minute, initializes `resource_state` and `settlement_state`, and schedules survival/regeneration events. The M3 rules value is `m3-rng1-survival1`, `SurvivalVersion = 1`, and the predecessor is `m2-rng1-citizen1`; World/Citizen generation versions remain `1`. Reopening an M3 database validates it and does not repair it. Unknown or partial sentinels, corruption, and injected write failures are rejected or rolled back; retry does not regenerate an already committed map or founder roster.

`world_tiles` stores row-major `tile_index`, coordinates, terrain, normalized fields, walkability, and movement cost. `resource_nodes` stores deterministic ID, row-major tile reference and coordinates, resource type, quantities, and regeneration potential, with foreign-key cascade from its tile. SQLite check constraints enforce persisted enum/range/walkability invariants; application validation enforces completeness, coordinate/index agreement, ecology, deterministic resource IDs, map viability, and fingerprint integrity.

## Server lifecycle, health, and failure semantics

Configuration keys are `DataRoot`, `ListenUrls`, `ActiveWorld`, and `WorldSeed`. Defaults are a data directory below the application base directory, `default-world`, seed `0`, and `http://127.0.0.1:5274`. The active world database is `<DataRoot>\<ActiveWorld>.db` (with platform path separators).

On startup the host opens/migrates the database. It loads a valid checkpoint, or creates an engine with the configured seed and writes an initial checkpoint. A browser is not required.

The host states are `Starting`, `Running`, `Stopping`, and `Faulted`. Health maps Running to Healthy, Starting/Stopping to Degraded, and Faulted to Unhealthy. Startup errors, malformed JSON, unsupported schema/rules versions, orphaned rows, command-loop failures, and checkpoint failures are not swallowed: the host records Faulted, stops the host under the configured `StopHost` policy, and preserves the last valid checkpoint where one exists.

The server exposes immutable `GET /api/v1/world` alongside `/api/v1/health`, `/api/v1/status`, `/api/v1/citizens`, `/api/v1/citizens/{id}`, and `/api/v1/settlement`. The world response contains the seed as a decimal string, dimensions and tile count, generation version/attempt, starting coordinate, terrain counts, resource counts, and the canonical fingerprint. Citizen responses are copied, ID-sorted published observations with action/phase, health, hunger/rest, carrying, target, and death fields; settlement responses contain stockpile, living/dead/total population, and immutable resource quantities. Malformed citizen IDs are `400`, missing IDs are `404`, and unavailable read models return `503` where applicable.

Normal shutdown closes the command writer, drains pending commands, performs a final checkpoint, and disposes the database. A final-checkpoint or cleanup failure is terminal and is rethrown after cleanup. A faulted host remains Faulted rather than being overwritten by Stopping. Browser disconnects have no effect on simulation state.

The `Microsoft.Extensions.Hosting.WindowsServices` package is referenced and `AddWindowsService` is registered, so the server is Windows-Service compatible at the hosting integration level. Installation, service hardening, firewall rules, production publishing, restart/reconnect hardening, and LAN deployment work are deferred to M7.

## Frontend observer boundary

The web application is a small read-only React page. It fetches relative health/status/citizen/settlement read models, parses safe string IDs and enum values, and displays connection/host state, world minute, shared stores, population, citizen action/phase, needs, health, carrying, and death information. It owns no canonical state and has no map, controls, mutation endpoint, or gameplay write path. M3 observer fields are published copies; browser refreshes cannot mutate simulation state.

Vite proxies `/api` to `http://127.0.0.1:5274` during development. The browser is therefore an optional observer and never a prerequisite for the server's world ownership.

## CI and dependency locks

The pull-request workflow has:

- a backend job matrix on `ubuntu-latest` and `windows-latest`, each running `dotnet restore --locked-mode`, Release build with `--no-restore`, and tests with `--no-build`;
- a frontend job on Ubuntu using Node 22, `npm ci`, lint, strict typecheck, tests, and production build.

NuGet lock-file generation is enabled centrally and a `packages.lock.json` is committed for each of the eight .NET projects. The frontend uses its committed npm lock file with `npm ci`.

The workflow remains configured for Ubuntu/Windows backend and Ubuntu frontend jobs. The M3 acceptance suite is shared and cross-platform configured; the recorded evidence for this branch is local Windows evidence, and no hosted Linux execution is claimed here. No platform-specific fingerprint is accepted.

## Implemented and deferred milestones

M0 foundations, M1 generation/persistence, M2 citizens/movement, and M3 survival are implemented. The current rules version is `m3-rng1-survival1`.

M2 adds a canonical `Citizen` collection owned by `SimulationEngine`. Twenty founders are
generated from `WorldSeed`, `CitizenGenerationVersion = 1`, and founder ordinal, then placed
on nearest walkable tiles around the starting site. HTTP and the observer UI consume immutable
ID-sorted snapshots. Citizen events use `citizen.decision.v1`, `citizen.move-step.v1`, and
`citizen.action-complete.v1`; payload IDs are invariant decimal strings. The M2 compatibility
rules value is `m2-rng1-citizen1`; `m0-rng1` snapshots are upgraded once without regenerating
their map. M3 extends this preserved M2 state with the survival events and mutable state described above.
- **M4:** structures, construction, shelter, stockpiles/workshop, settlement demand, and derived occupations.
- **M5:** relationships, households, reproduction, aging, and family systems.
- **M6:** historical gameplay events, biographies, statistics, and historical queries.
- **M7:** persistent-server hardening, service installation, LAN/firewall/deployment guidance, reconnect behavior, and production publishing.
- **M8:** headless/MAX operation, canonical fingerprints, long-run determinism evidence, profiling, tuning, and the 100-year acceptance run.

## M3 survival loop

M3 is the implemented survival milestone. `SurvivalVersion = 1` and `CurrentSimulationRulesVersion = m3-rng1-survival1`; the predecessor is `m2-rng1-citizen1`. World generation and founder generation remain version `1`.

Structures/shelters, social/family/aging, and historical gameplay are deferred to M4, M5, and M6 respectively.

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

M3 intentionally simplifies the model: one shared unlimited stockpile at the starting site, no structures or shelters, no carrying capacity, no trade, no social/family/aging, no historical event graph, and no player controls or mutation APIs. These are explicit M4/M5/M6 non-goals, not missing pieces of the M3 contract.

## M2 compatibility boundary

M2's canonical `Citizen` aggregate remains the compatibility foundation: exactly 20 founder rows, positive shared-counter IDs, ordinals 0..19, catalog-derived names, signed birth minute, four fixed-point needs, six fixed-point traits, six non-negative skills, action/sequence/timing/target state, nullable future lifecycle IDs/causes, and lifetime movement counters. Under M3, health, survival state, resource consumption, gathering skill progression, and mortality are now active; relationship, household, structure, and family fields remain reserved for M4/M5.

Founder generation is version `1` and is keyed by seed, ordinal, field, and the fixed repository name catalogs. Collision handling is deterministic. Founders are assigned to the nearest walkable tiles by squared distance then row-major order. Ages are 18..45 using signed checked `birth_minute = current_minute - (age * 518400 + deterministic_year_offset)`. Traits are 0..10000; skills are six fixed values that never change in M2. M2 rules are `m2-rng1-citizen1`; `m0-rng1` is accepted only as a one-time upgrade input.

Needs projection is pure, integer, and saturating: Hunger +2 except Winter +3, Rest +3, Shelter +1, Social +1 per minute, with all values clamped to 0..10000. M3 adds Eat, gathering, health, and mortality while retaining M2 movement. Citizen-local action sequence and explicit purpose keys feed stateless decision/target variation.

Movement uses non-persisted deterministic weighted A*: N, NE, E, SE, S, SW, W, NW; no diagonal corner cutting; orthogonal cost 10 and diagonal cost 14 multiplied by destination movement cost. Queue ties are F, H, row-major tile index, and local insertion sequence. The locked path golden is seed 42 `(131,130)` to `(127,126)`: `(131,130),(130,130),(129,130),(128,130),(127,129),(127,128),(127,127),(127,126)`.

Citizen event names are `citizen.decision.v1`, `citizen.move-step.v1`, and `citizen.action-complete.v1`, with priorities 20, 10, and 15 respectively. Payloads are exactly `{"citizenId":"<positive invariant decimal>","actionSequence":<non-negative integer>}`; legacy synthetic events retain `{}`. Dispatch validates payload structure, identity, entity key, action sequence, state, timing, reachability, and one-next-event-per-citizen invariants. Processed events retain a monotonic count and bounded 32-entry diagnostics.

The M2 EF migration adds `citizen_generation_version` to `world_meta` and a constrained `citizens` table with explicit snake_case columns and a unique ordinal index. Rows are ID ordered on load. Metadata, world, resources, events, and citizens are checkpointed transactionally. M1→M2 requires the complete M1 world, cver `0`, zero citizen rows/events, and exact `m0-rng1`; it preserves seed/minute/map fingerprint/counters/events/CreatedUtc, allocates 20 IDs, and queues current-minute decisions. M0→M2 chains M0→M1 then M1→M2. Partial/corrupt/unknown state rejects; failed upgrades roll back and retry once without regeneration.

The host remains the only writer. `SimulationMinutesPerSecond` is finite and non-negative, defaults to 10, and accepts 0 to disable automatic progression. Fractional advancement accumulates and floors whole minutes; driver commands pass through the host loop, and shutdown checkpoints. Status exposes population only. Immutable ID-sorted citizens are read through `GET /api/v1/citizens` and `/api/v1/citizens/{id}` (canonical decimal IDs, 400 malformed, 404 missing). The frontend polls these read models and provides accessible loading/error/list observation only; it has no map, PixiJS, SignalR, mutation controls, or canonical simulation state.
