# Little Ages M0/M1 Architecture

This document describes the implementation that exists in M0 and M1. The product design and implementation plan remain preserved separately; this is an implementation record, not a promise that later-milestone systems already exist.

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
- `LittleAges.Simulation` references `LittleAges.Domain` only. It contains the canonical M0 engine shell, queue, snapshots, synthetic data events, and the deterministic M1 `WorldGenerator`.
- `LittleAges.Persistence` references Domain and Simulation. It owns EF Core SQLite, migrations, database opening, and snapshot checkpoint/load.
- `LittleAges.Server` references Domain, Simulation, and Persistence. It owns ASP.NET Core hosting, configuration, the hosted owner, health, and HTTP endpoints.
- The four test projects reference the production project under test; integration tests reference Server and exercise the real host and SQLite files.
- `LittleAges.Web` is a separate React/TypeScript/Vite application and is not a .NET project or simulation dependency.

There is no SignalR hub, PixiJS renderer, static frontend serving, or gameplay implementation in M0/M1.

M1 adds deterministic geography and resources to Domain/Simulation. The generated `WorldMap` is immutable and is held by `SimulationEngine`; it is included in read and persistence snapshots. Persistence checkpoints and restores the map rows directly, so loading does not regenerate from seed/configuration.

## Canonical ownership and request flow

`SimulationHost` owns one `SimulationEngine` and is the sole hosted mutation path. Its bounded `Channel<SimulationCommand>` has capacity 32, one reader, and multiple writers. A command is written to the channel, consumed by the host, applied to the engine, and—when appropriate—checkpointed by the host.

```text
HTTP or host caller
        │
        ▼
bounded command channel (M0 checkpoint/test commands)
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

M0/M1 exposes no public mutation endpoint. The current HTTP routes are `GET /api/v1/health`, `GET /api/v1/status`, and immutable `GET /api/v1/world`. The checkpoint request exists as an internal host command for infrastructure/tests, not as a public API.

Reads do not inspect the mutable engine or database. `SimulationHost.Status` is an atomically published `ServerStatusSnapshot`; the HTTP handler returns that immutable record. The engine's `SimulationStatusSnapshot` copies event data into a read-only collection. This keeps observer reads independent of canonical mutation.

The status presentation contract exposes `WorldSeed` as an invariant decimal string, not a JSON number. This preserves every value in the `UInt64` seed range for clients such as JavaScript, including `18446744073709551615`.

## Canonical versus operational data

The canonical M0 snapshot consists of the seed, non-negative world minute, schema/rules/application version strings, world configuration JSON, deterministic counters, and the pending synthetic scheduled-event list. It is the input to restoration and deterministic continuation.

The M1 simulation snapshot additionally carries the generated immutable `WorldMap`. Its seed, generation version/attempt, canonical configuration, tiles, resource nodes, starting coordinate, and fingerprint are canonical world state; operational timestamps and database paths remain excluded.

Operational data is deliberately separate: database path, listen URL, host state/error, log records, `CreatedUtc`, and `LastCheckpointUtc`. UTC checkpoint timestamps are metadata only and never enter simulation decisions. M0 is snapshot-based, not event-sourced; it has no historical gameplay event store.

## Persistence

`WorldDatabase.OpenAsync` creates the parent directory, opens one SQLite file, enables connection-level foreign keys, sets a 5,000 ms busy timeout, enables WAL, applies EF migrations, and verifies those settings. It uses a non-shared-cache connection. The M0 migration creates `world_meta` and `scheduled_events`; the M1 migration extends `world_meta` and creates immutable `world_tiles` and `resource_nodes` tables.

### `world_meta`

One row is required, with `id = 1` enforced by a check constraint. It stores:

- `world_seed` as invariant decimal `TEXT`;
- `world_minute`;
- `world_schema_version`, `simulation_rules_version`, and `application_version`;
- `world_configuration_json`;
- `generation_version`, `generation_attempt`, and `starting_x`/`starting_y`;
- `world_fingerprint` as the stored canonical SHA-256 fingerprint;
- `next_entity_id`, `next_historical_event_id`, and `next_scheduled_event_sequence`;
- operational `created_utc` and `last_checkpoint_utc`.

SQLite `INTEGER` is signed `Int64`, while a `WorldSeed` is the complete `UInt64` range. Therefore the seed is encoded and decoded as invariant, lossless decimal text rather than a SQLite integer.

### `scheduled_events`

Each row stores `id`, `due_world_minute`, `priority`, `entity_sort_key`, `sequence`, `event_name`, and `event_payload_json`. `id` is the primary key, `sequence` has a unique index, and a check constraint enforces `id = sequence`.

A checkpoint runs in one explicit transaction. It validates the complete snapshot and map, removes old resource/tile/event/metadata rows, writes all canonical rows, retains existing `created_utc`, and commits. Any failure rolls back and clears tracked state, leaving the previous committed checkpoint intact. Load requires exactly one metadata row, a complete `width * height` row-major tile set, valid resource references and IDs, a valid immutable `WorldMap`, and a matching stored fingerprint. It rejects orphaned rows, missing/duplicate/out-of-range or semantically invalid rows, malformed configuration, unsupported generation versions, fingerprint mismatches, and invalid event data. Loaded rows are used directly; generation is not repeated.

### M0-to-M1 compatibility upgrade

After EF applies the M1 schema, `WorldDatabase.OpenAsync` performs one application-level compatibility check before normal checkpoint use. A legacy checkpoint is recognized only when there is exactly one `world_meta` row with `id = 1`, all M1 sentinel fields still at their migration defaults (`generation_version = 0`, `generation_attempt = 0`, `starting_x = 0`, `starting_y = 0`, empty fingerprint), no tile/resource rows, and a valid M0 snapshot. The M0 JSON is validated as JSON without imposing the versioned M1 configuration shape; seed, time, metadata compatibility, counters, event identities/order, and next sequence are still strict. A missing metadata row with no canonical rows remains an empty database. Any partial sentinel or canonical row is rejected as corrupt, while generation version 1 with missing rows is handled by strict M1 loading and is never treated as legacy.

The upgrade starts one SQLite transaction before reading legacy state. It generates the default M1 world from the persisted M0 seed with `WorldGenerator`, normalizes the stored configuration to canonical default M1 JSON, preserves seed/time/versions/application/counters/events and `created_utc`, updates only operational `last_checkpoint_utc`, and writes metadata, events, tiles, and resources through the same transaction-bound writer used by ordinary checkpoints. Failure before commit rolls back to the untouched M0 sentinel, so a later open safely retries. Persistence never uses the simulation engine's optional null-world compatibility fallback to decide whether a database needs upgrading.

`world_tiles` stores row-major `tile_index`, coordinates, terrain, normalized fields, walkability, and movement cost. `resource_nodes` stores deterministic ID, row-major tile reference and coordinates, resource type, quantities, and regeneration potential, with foreign-key cascade from its tile. SQLite check constraints enforce persisted enum/range/walkability invariants; application validation enforces completeness, coordinate/index agreement, ecology, deterministic resource IDs, map viability, and fingerprint integrity.

## Server lifecycle, health, and failure semantics

Configuration keys are `DataRoot`, `ListenUrls`, `ActiveWorld`, and `WorldSeed`. Defaults are a data directory below the application base directory, `default-world`, seed `0`, and `http://127.0.0.1:5274`. The active world database is `<DataRoot>\<ActiveWorld>.db` (with platform path separators).

On startup the host opens/migrates the database. It loads a valid checkpoint, or creates an engine with the configured seed and writes an initial checkpoint. A browser is not required.

The host states are `Starting`, `Running`, `Stopping`, and `Faulted`. Health maps Running to Healthy, Starting/Stopping to Degraded, and Faulted to Unhealthy. Startup errors, malformed JSON, unsupported schema/rules versions, orphaned rows, command-loop failures, and checkpoint failures are not swallowed: the host records Faulted, stops the host under the configured `StopHost` policy, and preserves the last valid checkpoint where one exists.

The server exposes immutable `GET /api/v1/world` alongside `/api/v1/health` and `/api/v1/status`. The world response contains the seed as a decimal string, dimensions and tile count, generation version/attempt, starting coordinate, terrain counts, resource counts, and the canonical fingerprint. It returns `503` while no world is available.

Normal shutdown closes the command writer, drains pending commands, performs a final checkpoint, and disposes the database. A final-checkpoint or cleanup failure is terminal and is rethrown after cleanup. A faulted host remains Faulted rather than being overwritten by Stopping. Browser disconnects have no effect on simulation state.

The `Microsoft.Extensions.Hosting.WindowsServices` package is referenced and `AddWindowsService` is registered, so the server is Windows-Service compatible at the hosting integration level. Installation, service hardening, firewall rules, production publishing, restart/reconnect hardening, and LAN deployment work are deferred to M7.

## Frontend observer boundary

The web application is a small read-only React page. It fetches relative `/api/v1/health` and `/api/v1/status` URLs, parses safe read models, and displays connection/host state and world minute. It owns no canonical state and has no map, controls, or gameplay view. M1 does not add a rendered map, PixiJS surface, or frontend gameplay; the world summary is a server/read-model boundary only.

Vite proxies `/api` to `http://127.0.0.1:5274` during development. The browser is therefore an optional observer and never a prerequisite for the server's world ownership.

## CI and dependency locks

The pull-request workflow has:

- a backend job matrix on `ubuntu-latest` and `windows-latest`, each running `dotnet restore --locked-mode`, Release build with `--no-restore`, and tests with `--no-build`;
- a frontend job on Ubuntu using Node 22, `npm ci`, lint, strict typecheck, tests, and production build.

NuGet lock-file generation is enabled centrally and a `packages.lock.json` is committed for each of the eight .NET projects. The frontend uses its committed npm lock file with `npm ci`.

The M1 determinism gate uses that existing Ubuntu/Windows backend matrix to run the same world-generation golden fingerprints (including seed `0` with the default configuration). No platform-specific fingerprint is accepted.

## Deferred milestones

M0 intentionally stops at infrastructure foundations. M1 generation, persistence, and world-summary endpoint are implemented. The following remain later work:

- **M2:** citizens, movement, and gameplay scheduling.

M2 adds a canonical `Citizen` collection owned by `SimulationEngine`. Twenty founders are
generated from `WorldSeed`, `CitizenGenerationVersion = 1`, and founder ordinal, then placed
on nearest walkable tiles around the starting site. HTTP and the observer UI consume immutable
ID-sorted snapshots. Citizen events use `citizen.decision.v1`, `citizen.move-step.v1`, and
`citizen.action-complete.v1`; payload IDs are invariant decimal strings. The M2 compatibility
rules value is `m2-rng1-citizen1`; `m0-rng1` snapshots are upgraded once without regenerating
their map.
- **M3:** needs, gathering, survival, health, and mortality consequences.
- **M4:** structures, construction, settlement demand, and derived occupations.
- **M5:** relationships, households, reproduction, aging, and family systems.
- **M6:** historical gameplay events, biographies, statistics, and historical queries.
- **M7:** persistent-server hardening, service installation, LAN/firewall/deployment guidance, reconnect behavior, and production publishing.
- **M8:** headless/MAX operation, canonical fingerprints, long-run determinism evidence, profiling, tuning, and the 100-year acceptance run.

## M2 compatibility boundary

M2 adds a canonical `Citizen` aggregate with exactly 20 founder rows. Each row persists a positive shared-counter ID, ordinal 0..19, catalog-derived given/family names, signed birth minute, four fixed-point needs, six fixed-point traits, six immutable non-negative skills, health 10000, action/sequence/timing/target state, nullable future lifecycle IDs/causes, and lifetime movement counters. Future death, relationships, households, structures, survival, resource consumption, skill advancement, and health effects remain explicitly out of scope.

Founder generation is version `1` and is keyed by seed, ordinal, field, and the fixed repository name catalogs. Collision handling is deterministic. Founders are assigned to the nearest walkable tiles by squared distance then row-major order. Ages are 18..45 using signed checked `birth_minute = current_minute - (age * 518400 + deterministic_year_offset)`. Traits are 0..10000; skills are six fixed values that never change in M2. M2 rules are `m2-rng1-citizen1`; `m0-rng1` is accepted only as a one-time upgrade input.

Needs projection is pure, integer, and saturating: Hunger +2, Rest +3, Shelter +1, Social +1 per minute, with all values clamped to 0..10000. Rest is the only active need behavior. Utilities are Rest weight 2, Explore 1000, Wander 700, Idle 500; action tie order is Rest, Explore, Wander, Idle. Idle lasts 30..90 minutes, Rest 120, Wander targets radius 4, Explore radius 12. Citizen-local action sequence and explicit purpose keys feed stateless decision/target variation.

Movement uses non-persisted deterministic weighted A*: N, NE, E, SE, S, SW, W, NW; no diagonal corner cutting; orthogonal cost 10 and diagonal cost 14 multiplied by destination movement cost. Queue ties are F, H, row-major tile index, and local insertion sequence. The locked path golden is seed 42 `(131,130)` to `(127,126)`: `(131,130),(130,130),(129,130),(128,130),(127,129),(127,128),(127,127),(127,126)`.

Citizen event names are `citizen.decision.v1`, `citizen.move-step.v1`, and `citizen.action-complete.v1`, with priorities 20, 10, and 15 respectively. Payloads are exactly `{"citizenId":"<positive invariant decimal>","actionSequence":<non-negative integer>}`; legacy synthetic events retain `{}`. Dispatch validates payload structure, identity, entity key, action sequence, state, timing, reachability, and one-next-event-per-citizen invariants. Processed events retain a monotonic count and bounded 32-entry diagnostics.

The M2 EF migration adds `citizen_generation_version` to `world_meta` and a constrained `citizens` table with explicit snake_case columns and a unique ordinal index. Rows are ID ordered on load. Metadata, world, resources, events, and citizens are checkpointed transactionally. M1→M2 requires the complete M1 world, cver `0`, zero citizen rows/events, and exact `m0-rng1`; it preserves seed/minute/map fingerprint/counters/events/CreatedUtc, allocates 20 IDs, and queues current-minute decisions. M0→M2 chains M0→M1 then M1→M2. Partial/corrupt/unknown state rejects; failed upgrades roll back and retry once without regeneration.

The host remains the only writer. `SimulationMinutesPerSecond` is finite and non-negative, defaults to 10, and accepts 0 to disable automatic progression. Fractional advancement accumulates and floors whole minutes; driver commands pass through the host loop, and shutdown checkpoints. Status exposes population only. Immutable ID-sorted citizens are read through `GET /api/v1/citizens` and `/api/v1/citizens/{id}` (canonical decimal IDs, 400 malformed, 404 missing). The frontend polls these read models and provides accessible loading/error/list observation only; it has no map, PixiJS, SignalR, mutation controls, or canonical simulation state.
