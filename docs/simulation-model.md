# Little Ages M0/M1 Simulation Model

This is the exact deterministic model implemented by M0 plus the bounded M1 world-generation model. M0 remains a simulation shell with synthetic data events; M1 adds immutable deterministic geography and resource nodes, not civilization gameplay.

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

## M1 immutable world model

`WorldMap` is the immutable, validated geography/resource value held by the simulation engine. Its map dimensions come from the persisted `WorldGenerationConfiguration`; the default is `160 x 160` (25,600 tiles). There is no mutable map revision in M1.

`TileCoordinate` is zero-based. A coordinate `(x, y)` has the row-major linear index:

```text
index = y * width + x
x = index % width
y = index / width
```

Coordinates compare by `Y` and then `X`, and the map enumerates tiles in that same row-major order. `WorldMap` requires exactly `width * height` unique, in-range coordinates and a complete index range. `TileCoordinate` itself validates non-negative components; map-height/range completeness is enforced by `WorldMap`.

Persisted terrain values are explicit and must not be renumbered:

| Terrain | Value |
|---|---:|
| `Freshwater` | 1 |
| `Grassland` | 2 |
| `Forest` | 3 |
| `RockyGround` | 4 |
| `DenseWilderness` | 5 |

Persisted resource values are:

| Resource | Value |
|---|---:|
| `Food` | 1 |
| `Wood` | 2 |
| `Stone` | 3 |

Each `WorldTile` stores its coordinate, terrain, normalized `Elevation`, `Fertility`, and `WaterAccess` values, `Walkable`, and `MovementCost`. Normalized integer fields are inclusive `[0, 10000]`. A walkable tile has a positive movement cost; a non-walkable tile uses movement-cost sentinel `0`. The current generator assigns Freshwater and RockyGround as non-walkable, Grassland as cost 1, Forest as cost 2, and DenseWilderness as cost 3. `Buildable` is walkable and not Freshwater.

Each `ResourceNode` stores a positive initial and maximum quantity (the generator sets them equal) plus normalized `RegenerationPotential`. Generator IDs are deterministic and positive:

```text
resourceId = (tileIndex * 4) + typeCode
typeCode: Food = 1, Wood = 2, Stone = 3
```

The `WorldMap` constructor freezes tile/resource enumeration, validates map completeness and resource coordinate containment, enforces terrain walkability/movement semantics and deterministic resource IDs/ecology, orders resources by ID, validates the starting site, and computes the fingerprint.

## M1 generation configuration and canonical JSON

`WorldGenerationConfiguration.CurrentVersion` and `WorldGenerator.GenerationVersion` are both `1`. The configuration is persisted, not reconstructed from process defaults. The default values are:

| Field | Default |
|---|---:|
| `version` | 1 |
| `width` | 160 |
| `height` | 160 |
| `terrainWaterThreshold` | 2500 |
| `terrainRockThreshold` | 8200 |
| `terrainForestFertilityThreshold` | 5600 |
| `terrainDenseFertilityThreshold` | 7600 |
| `foodPlacementThreshold` | 4200 |
| `woodPlacementThreshold` | 4800 |
| `stonePlacementThreshold` | 7000 |
| `startSiteRadius` | 8 |
| `minimumNearbyFood` | 1 |
| `minimumNearbyWood` | 1 |
| `minimumNearbyStone` | 1 |
| `minimumNearbyFreshwater` | 1 |
| `minimumWalkableCount` | 24 |
| `maximumAttempts` | 8 |

`ToCanonicalJson()` emits one fixed-order compact JSON object with exactly these lower-camel-case properties, in the table order above, using invariant decimal integers and no extra properties. `FromCanonicalJson()` requires an object with exactly this key set and integer values, rejects missing/unknown/non-integer properties, then validates the ranges and threshold ordering. The parser currently accepts the properties in any input order; canonical output is fixed order. Width and height must each be 8..1024; normalized thresholds are 0..10000; water must be below rock, forest below dense, radius is 1..64, minimum counts are non-negative, minimum walkable count is at least 1, and maximum attempts is 1..64.

## M1 deterministic generation

`WorldGenerator.Generate(seed, configuration)` validates the configuration and evaluates attempts `0` through `maximumAttempts - 1`. For attempt `a`, it derives an attempt seed with the repository-owned stateless `DeterministicRandom` (algorithm version 1):

```text
attemptSeed = DR(seed).NextUInt64(WorldGeneration, generationVersion, a)
random = DR(WorldSeed(attemptSeed))
```

All world values use `RandomDomain.WorldGeneration` and stable keys; there is no mutable random stream, iteration-order-dependent draw count, clock, or process state. Elevation and base fertility use an 8-tile coarse grid and integer bilinear interpolation. For coordinate `(x, y)` and field layer `L` (elevation `1`, base fertility `2`):

```text
coarseX = x / 8; coarseY = y / 8
fx = x % 8; fy = y % 8
sample(cx, cy) = DR(random).NextUInt64(WorldGeneration, L, (uint)cx, (uint)cy) % 10001
a = sample(coarseX, coarseY)
b = sample(coarseX + 1, coarseY)
c = sample(coarseX, coarseY + 1)
d = sample(coarseX + 1, coarseY + 1)
top = a + ((b - a) * fx / 8)
bottom = c + ((d - c) * fx / 8)
value = clamp(top + ((bottom - top) * fy / 8), 0, 10000)
```

The implementation uses unchecked `uint` representations for the coarse sample keys, including negative coarse coordinates if such a key is ever requested; generated map coordinates themselves are non-negative. Terrain is derived from elevation and base fertility in this order:

```text
elevation < terrainWaterThreshold       -> Freshwater
otherwise elevation >= terrainRockThreshold -> RockyGround
otherwise fertility >= terrainDenseFertilityThreshold -> DenseWilderness
otherwise fertility >= terrainForestFertilityThreshold -> Forest
otherwise                                -> Grassland
```

After terrain classification, the generator computes `WaterAccess` from the generated Freshwater geography with a deterministic multi-source 8-neighbor breadth-first search. Freshwater tiles have distance 0 and access 10000; a tile at finite Chebyshev distance `d` has access `10000 / (d + 1)` using integer division; worlds without freshwater would use 0. This is an O(width*height) stable pass with row-major seed/neighbor traversal. Fertility then combines base fertility, water access, and a terrain factor using integer weights 5/3/2 respectively; terrain factors are Freshwater 4000, Grassland 6000, Forest 8000, RockyGround 1500, and DenseWilderness 8500. Freshwater behavior is coherent: low elevation creates Freshwater, Freshwater is non-walkable with movement cost 0, it is not buildable, and no resource node is placed on it.

Resource placement is deterministic per tile/type. On non-Freshwater tiles, Food is suitable when fertility is at least `foodPlacementThreshold`, water access is at least `2600`, and an 8-neighbor Freshwater tile exists; Wood is suitable only on Forest or DenseWilderness with fertility at least `woodPlacementThreshold`; Stone is suitable on RockyGround or when elevation is at least `stonePlacementThreshold`. A suitable node is emitted when the WorldGeneration draw with keys `(100, tileIndex, typeCode)` modulo 100 is below `24`. Its quantity is `40 + (draw(101, tileIndex, typeCode) % 161)`, therefore 40..200 inclusive, and its regeneration potential is the clamped ecology input (fertility for Food/Wood, elevation for Stone). RockyGround remains non-walkable but can carry Stone.

## M1 starting-site viability and retries

Candidates must be buildable. The nearby region is a square using Chebyshev radius `startSiteRadius`: `abs(dx) <= radius` and `abs(dy) <= radius`. The generator clamps that square to map bounds while scoring. It sums resource `MaximumQuantity` by type, counts Freshwater tiles, and counts walkable tiles. A candidate is viable only when all configured thresholds pass:

```text
foodQuantity       >= minimumNearbyFood
woodQuantity       >= minimumNearbyWood
stoneQuantity      >= minimumNearbyStone
freshwaterTileCount >= minimumNearbyFreshwater
walkableTileCount  >= minimumWalkableCount
```

For each viable candidate the exact score is:

```text
food * 100000 + wood * 10000 + stone * 1000 + freshwater * 100
  + walkable + candidateFertility
```

The highest score wins; ties use the lower row-major tile index. If no candidate survives, the current selector returns `(0, 0)`, after which `WorldMap.Validate()` rejects the map as non-viable. `WorldMap.Validate()` repeats the starting-tile and square viability checks (without the selector's boundary-clamping step, which is equivalent for in-range coordinates).

Failed attempts are retried deterministically with the next attempt number. Every `ArgumentException` from attempt generation or validation is retained as the last failure; after `maximumAttempts` failures, generation throws `WorldGenerationException` with that failure as its inner exception.

## M1 fingerprint and compatibility

`WorldMap.ComputeFingerprint()` is SHA-256 over an ordered sequence of length-prefixed UTF-8 fields. For each field, the implementation encodes the field as UTF-8 bytes, prefixes those bytes with the invariant decimal byte length and `:`, and appends both to the hash. Field order is exact:

1. Original seed as invariant decimal text.
2. Generation version as invariant decimal text.
3. Generation attempt as invariant decimal text.
4. Canonical configuration JSON.
5. Every tile in row-major order as `x,y,terrain,elevation,fertility,waterAccess,walkable,movementCost`, with terrain as its integer enum value and walkable as `1`/`0`.
6. Every resource in ascending ID order as `id,x,y,type,initialQuantity,maximumQuantity,regenerationPotential`, with type as its integer enum value.
7. Starting-site `x,y`.

The resulting lowercase hexadecimal SHA-256 string is exactly 64 characters. Locked golden values include:

```text
seed 0, default configuration -> 97eeea22c01791cef8957c6f461c7428cb59f1bb38a66d8b27c65ce8c93923bc
seed 42, default configuration -> a0568524bd2257a3126b91dadd57825b06b15d20d1f09376c62180d48b54dd81
seed UInt64.MaxValue, default configuration -> b9cbba5088e6b0968928f91ddedaf4eedb7a270840888e350d42fa0f31c1bd64
```

Generation version `1`, terrain/resource integer values, configuration canonicalization, field keys, retry derivation, interpolation arithmetic, resource formulas, viability score, ordering, and fingerprint encoding are compatibility data. The RNG algorithm itself is version `1`; changing any of these without a compatibility/version decision changes canonical worlds. The existing M0 `WorldSchemaVersion` (`0.1`) and `SimulationRulesVersion` (`m0-rng1`) remain the current engine metadata; they do not replace the M1 generation version.

## M1 persistence and read boundary

M1 persistence adds `world_tiles` and `resource_nodes` tables and extends `world_meta` with `generation_version`, `generation_attempt`, `starting_x`, `starting_y`, and stored `world_fingerprint`. Checkpointing writes map rows and all existing canonical state transactionally. Loading reconstructs and validates the map from rows without regeneration, then requires the stored fingerprint to equal the recomputed canonical fingerprint. Absent/duplicate/out-of-range rows, inconsistent coordinates/IDs/counts, malformed values, unsupported generation versions, invalid ecology or movement semantics, and fingerprint mismatches are explicit corruption/compatibility failures. The immutable `GET /api/v1/world` summary contains decimal-string seed, dimensions, tile count, generation version/attempt, starting coordinate, terrain/resource counts, and fingerprint. M1 has no rendered map, PixiJS frontend, or frontend gameplay surface. The existing Ubuntu/Windows backend matrix is the cross-platform gate for shared golden fingerprints.
