# Little Ages M0 Architecture

This document describes the implementation that exists in M0. The product design and implementation plan remain preserved separately; this is an implementation record, not a promise that later-milestone systems already exist.

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

- `LittleAges.Domain` has no project or infrastructure package references. It contains value types, typed IDs, counters, calendar rules, and RNG contracts/implementation.
- `LittleAges.Simulation` references `LittleAges.Domain` only. It contains the canonical M0 engine shell, queue, snapshots, and synthetic data events.
- `LittleAges.Persistence` references Domain and Simulation. It owns EF Core SQLite, migrations, database opening, and snapshot checkpoint/load.
- `LittleAges.Server` references Domain, Simulation, and Persistence. It owns ASP.NET Core hosting, configuration, the hosted owner, health, and HTTP endpoints.
- The four test projects reference the production project under test; integration tests reference Server and exercise the real host and SQLite files.
- `LittleAges.Web` is a separate React/TypeScript/Vite application and is not a .NET project or simulation dependency.

There is no SignalR hub, PixiJS renderer, static frontend serving, or gameplay implementation in M0.

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

M0 exposes no public mutation endpoint. The only current HTTP routes are `GET /api/v1/health` and `GET /api/v1/status`. The checkpoint request exists as an internal host command for infrastructure/tests, not as a public API.

Reads do not inspect the mutable engine or database. `SimulationHost.Status` is an atomically published `ServerStatusSnapshot`; the HTTP handler returns that immutable record. The engine's `SimulationStatusSnapshot` copies event data into a read-only collection. This keeps observer reads independent of canonical mutation.

The status presentation contract exposes `WorldSeed` as an invariant decimal string, not a JSON number. This preserves every value in the `UInt64` seed range for clients such as JavaScript, including `18446744073709551615`.

## Canonical versus operational data

The canonical M0 snapshot consists of the seed, non-negative world minute, schema/rules/application version strings, world configuration JSON, deterministic counters, and the pending synthetic scheduled-event list. It is the input to restoration and deterministic continuation.

Operational data is deliberately separate: database path, listen URL, host state/error, log records, `CreatedUtc`, and `LastCheckpointUtc`. UTC checkpoint timestamps are metadata only and never enter simulation decisions. M0 is snapshot-based, not event-sourced; it has no historical gameplay event store.

## Persistence

`WorldDatabase.OpenAsync` creates the parent directory, opens one SQLite file, enables connection-level foreign keys, sets a 5,000 ms busy timeout, enables WAL, applies EF migrations, and verifies those settings. It uses a non-shared-cache connection. The initial migration creates exactly these two application tables:

### `world_meta`

One row is required, with `id = 1` enforced by a check constraint. It stores:

- `world_seed` as invariant decimal `TEXT`;
- `world_minute`;
- `world_schema_version`, `simulation_rules_version`, and `application_version`;
- `world_configuration_json`;
- `next_entity_id`, `next_historical_event_id`, and `next_scheduled_event_sequence`;
- operational `created_utc` and `last_checkpoint_utc`.

SQLite `INTEGER` is signed `Int64`, while a `WorldSeed` is the complete `UInt64` range. Therefore the seed is encoded and decoded as invariant, lossless decimal text rather than a SQLite integer.

### `scheduled_events`

Each row stores `id`, `due_world_minute`, `priority`, `entity_sort_key`, `sequence`, `event_name`, and `event_payload_json`. `id` is the primary key, `sequence` has a unique index, and a check constraint enforces `id = sequence`.

A checkpoint runs in one explicit transaction. It validates JSON and compatibility, removes the old scheduled rows and singleton metadata, writes the new snapshot, then commits. Existing `created_utc` is retained. Any failure rolls back and clears tracked state, leaving the previous committed checkpoint intact. Load requires exactly one metadata row, rejects orphaned event rows and invalid event data, orders rows by the deterministic event tuple, and validates the snapshot before returning it.

## Server lifecycle, health, and failure semantics

Configuration keys are `DataRoot`, `ListenUrls`, `ActiveWorld`, and `WorldSeed`. Defaults are a data directory below the application base directory, `default-world`, seed `0`, and `http://127.0.0.1:5274`. The active world database is `<DataRoot>\<ActiveWorld>.db` (with platform path separators).

On startup the host opens/migrates the database. It loads a valid checkpoint, or creates an engine with the configured seed and writes an initial checkpoint. A browser is not required.

The host states are `Starting`, `Running`, `Stopping`, and `Faulted`. Health maps Running to Healthy, Starting/Stopping to Degraded, and Faulted to Unhealthy. Startup errors, malformed JSON, unsupported schema/rules versions, orphaned rows, command-loop failures, and checkpoint failures are not swallowed: the host records Faulted, stops the host under the configured `StopHost` policy, and preserves the last valid checkpoint where one exists.

Normal shutdown closes the command writer, drains pending commands, performs a final checkpoint, and disposes the database. A final-checkpoint or cleanup failure is terminal and is rethrown after cleanup. A faulted host remains Faulted rather than being overwritten by Stopping. Browser disconnects have no effect on simulation state.

The `Microsoft.Extensions.Hosting.WindowsServices` package is referenced and `AddWindowsService` is registered, so the server is Windows-Service compatible at the hosting integration level. Installation, service hardening, firewall rules, production publishing, restart/reconnect hardening, and LAN deployment work are deferred to M7.

## Frontend observer boundary

The web application is a small read-only React page. It fetches relative `/api/v1/health` and `/api/v1/status` URLs, parses safe read models, and displays connection/host state and world minute. It owns no canonical state and has no map, controls, or gameplay view.

Vite proxies `/api` to `http://127.0.0.1:5274` during development. The browser is therefore an optional observer and never a prerequisite for the server's world ownership.

## CI and dependency locks

The pull-request workflow has:

- a backend job matrix on `ubuntu-latest` and `windows-latest`, each running `dotnet restore --locked-mode`, Release build with `--no-restore`, and tests with `--no-build`;
- a frontend job on Ubuntu using Node 22, `npm ci`, lint, strict typecheck, tests, and production build.

NuGet lock-file generation is enabled centrally and a `packages.lock.json` is committed for each of the eight .NET projects. The frontend uses its committed npm lock file with `npm ci`.

## Deferred milestones

M0 intentionally stops at infrastructure foundations. The following remain later work:

- **M1:** deterministic world generation, terrain, resources, and starting-site validation.
- **M2:** citizens, movement, and gameplay scheduling.
- **M3:** needs, gathering, survival, health, and mortality consequences.
- **M4:** structures, construction, settlement demand, and derived occupations.
- **M5:** relationships, households, reproduction, aging, and family systems.
- **M6:** historical gameplay events, biographies, statistics, and historical queries.
- **M7:** persistent-server hardening, service installation, LAN/firewall/deployment guidance, reconnect behavior, and production publishing.
- **M8:** headless/MAX operation, canonical fingerprints, long-run determinism evidence, profiling, tuning, and the 100-year acceptance run.
