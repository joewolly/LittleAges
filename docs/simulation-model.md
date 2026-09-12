# Little Ages M0 Simulation Model

This is the exact deterministic model implemented by M0. It is a simulation shell with synthetic data events, not the future civilization gameplay model.

## World minute and calendar

`WorldMinute` is a non-negative signed 64-bit (`Int64`/`long`) count of simulated minutes since world creation. World creation starts at `0`; it has no wall-clock relationship. `Add`/`AdvanceBy` accepts only non-negative deltas and rejects overflow. `AdvanceTo` rejects a target below the current minute, so canonical time never moves backward.

The implemented calendar constants are:

| Value | Constant |
|---|---:|
| hours per day | 24 |
| minutes per hour | 60 |
| minutes per day | 1,440 |
| days per week | 7 |
| days per month | 30 |
| months per year | 12 |
| days per season | 90 |
| days per year | 360 |
| minutes per year | 518,400 |

`WorldCalendar.FromMinute` uses integer division and remainder in this order: year, day within year, hour, then minute. Months and days are one-based; hours and minutes are zero-based. The valid calendar shape is 12 30-day months and four seasons:

```text
Spring: months 1–3
Summer: months 4–6
Autumn: months 7–9
Winter: months 10–12
```

`DayOfYear` is `((Month - 1) * 30) + Day`, so it ranges from 1 through 360. `Season` is `((Month - 1) / 3) + 1`, mapped to Spring, Summer, Autumn, or Winter.

`DayOfWeek` is continuous across year boundaries and is zero-based:

```text
((Year % 7) * (360 % 7) + DayOfYear - 1) % 7
```

Since `360 % 7` is 3, `Year 0, Day 1` is weekday 0 and `Year 1, Day 1` is weekday 3. Calendar conversion round-trips through the full signed `long` minute range supported by `WorldMinute`.

## IDs and counters

World-local IDs are strongly typed wrappers around positive signed 64-bit values: `CitizenId`, `StructureId`, `HouseholdId`, `HistoricalEventId`, and `ScheduledEventId`. Zero and negative values are invalid.

The persisted deterministic counters are three independent monotonic `long` counters:

```text
NextEntityId
NextHistoricalEventId
NextScheduledEventSequence
```

Entity IDs for citizens, structures, and households share the entity counter; historical events and scheduled events use separate counters. Counters start at 1, are part of the persistence snapshot, and restore without consuming a value. Allocation at `long.MaxValue` throws before changing state; the exhaustion attempt is observationally a no-op.

## Versions and world snapshot

The current compatibility values are:

```text
WorldSchemaVersion:       0.1
SimulationRulesVersion:   m0-rng1
Deterministic RNG version: 1
Default application version: 0.1.0
```

An M0 persistence snapshot contains the `WorldSeed`, `WorldMinute`, those metadata versions, application version, world configuration JSON, the three counter values, and the pending scheduled-event data. Loading rejects unsupported world-schema or simulation-rules versions. It also rejects invalid JSON, malformed rows, events due before the restored minute, duplicate IDs/sequences, and a next sequence that is not greater than every queued sequence.

## Stateless deterministic randomness

`DeterministicRandom` is a repository-owned, stateless SplitMix64 derivation algorithm, version 1. It never uses `System.Random`, a mutable stream, process state, or wall-clock state. Numeric `RandomDomain` values are persisted compatibility data:

```text
WorldGeneration      = 1
CitizenGeneration    = 2
DecisionVariation     = 3
Relationships         = 4
Reproduction          = 5
Mortality             = 6
ResourceRegeneration  = 7
```

For `NextUInt64(domain, keyA, keyB, keyC)`, all arithmetic is unchecked `UInt64` arithmetic. The exact derivation sequence is:

```text
state = seed
state = Mix(state XOR 0xD6E8FEB86659FD93 XOR (uint)domain)
state = Mix(state XOR keyA XOR 0xA0761D6478BD642F)
state = Mix(state XOR keyB XOR 0xE7037ED1A0B428DB)
state = Mix(state XOR keyC XOR 0x8EBC6AF09C88C6E3)
return Mix(state)
```

Each `Mix(value)` performs:

```text
value += 0x9E3779B97F4A7C15
value = (value XOR (value >> 30)) * 0xBF58476D1CE4E5B9
value = (value XOR (value >> 27)) * 0x94D049BB133111EB
return value XOR (value >> 31)
```

`NextUnitDouble` maps the upper 53 bits to `[0, 1)` exactly as:

```text
(NextUInt64(domain, keyA, keyB, keyC) >> 11) / 9007199254740992d
```

The golden vectors in `LittleAges.Domain.Tests` lock the algorithm version, seed/domain/key derivation, zero and maximum seeds, maximum keys, and the resulting IEEE-754 double bit patterns. This is the stability boundary: changing the sequence, constants, domain numbers, or 53-bit mapping changes compatibility data and must not happen silently.

The currently locked vectors include:

```text
seed 0x0123456789ABCDEF, WorldGeneration, (1, 2, 3)
  -> 0x165253D2BA50D131
seed 0x0123456789ABCDEF, Relationships, (1, 2, 3)
  -> 0x4EAC0CAF529D7777
seed 0, WorldGeneration, (0, 0, 0)
  -> 0xBB28B580F325675B; double bits 0x3FE76516B01E64AC
seed 0xFFFFFFFFFFFFFFFF, WorldGeneration, (0, 0, 0)
  -> 0xA16E0DD4A3BC86C3; double bits 0x3FE42DC1BA947790
seed 0xFFFFFFFFFFFFFFFF, ResourceRegeneration, (0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF)
  -> 0x3A8123A19F81310C; double bits 0x3FCD4091D0CFC098
seed 0x0123456789ABCDEF, DecisionVariation, (0xFFFFFFFFFFFFFFFF, 0, 0xFFFFFFFFFFFFFFFF)
  -> 0x7F91404FFE7C254F; double bits 0x3FDFE45013FF9F08
```

## Scheduled events

M0 scheduled events are pure data. A `ScheduledEventSnapshot` contains a positive `ScheduledEventId`, an order tuple, and a stable non-empty name. There are no delegates, callbacks, object references, or executable event payloads. Persistence stores the name and the current placeholder JSON payload `{}`.

The complete ordering tuple is, in this exact order:

```text
DueWorldMinute
Priority
EntitySortKey
Sequence
```

The engine stores pending events in a `SortedSet` compared by that tuple. `Sequence` is allocated from the persisted scheduled-event counter when an event is scheduled. M0 enforces the identity invariant:

```text
ScheduledEventId.Value == ScheduledEventOrder.Sequence
```

IDs and sequences are unique in a persistence snapshot; the SQLite primary key, unique sequence index, and `id = sequence` check constraint reinforce the same rule. Events at the same minute are therefore deterministic, including priority and entity-key ties.

`ProcessNextEvent` advances the clock to the next due minute and records a `SyntheticEventExecution`. `AdvanceUntil` processes all events due at or before the target and then advances to the target. No citizen, terrain, resource, survival, construction, relationship, family, mortality, or historical gameplay rule exists in this engine.

## Snapshot, reload, and determinism

Read snapshots freeze their event lists for observers. Persistence snapshots freeze the pending event list and counters, and `SimulationEngine.FromPersistenceSnapshot` reconstructs the queue without changing order or consuming new IDs.

RNG state is not serialized because there is no mutable RNG stream. After save/reload, the same seed, domain, and keys produce the same derived value without guessing how many draws occurred. Persisted event order and the next scheduled sequence are restored explicitly, so save/reload preserves subsequent event order and allocation. Operational timestamps do not affect either result.

## Deterministic prohibitions

Canonical Domain/Simulation behavior must not depend on:

- `Guid.NewGuid()` or other process-generated identity;
- `System.Random` or `Random.Shared`;
- `DateTime.Now`, `DateTime.UtcNow`, `Stopwatch`, `Environment.TickCount`, or wall-clock timing;
- unordered iteration where iteration order can affect an outcome;
- parallel mutation or task scheduling.

Wall-clock UTC is used only for operational checkpoint metadata and server lifecycle logging. The browser's refresh rate, connection state, and Vite proxy do not participate in canonical simulation state.

Gameplay systems and long-run behavior are future work: world generation is M1; citizens and movement M2; survival M3; settlement M4; social/family systems M5; historical gameplay facts and queries M6; server hardening/deployment M7; and headless/MAX, scale, fingerprint, and 100-year validation M8.
