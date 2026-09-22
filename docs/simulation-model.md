# Little Ages M0-M8 Simulation Model

This is the exact deterministic model implemented by M0 through M8 candidate acceptance. M1 adds immutable deterministic geography/resource definitions, M2 adds founders and movement, M3 adds the survival loop over mutable resource quantities, stockpile, needs, gathering, health, and mortality, M4 adds deterministic settlement/construction, M5 adds social/family/lifecycle systems, and M6 adds append-only factual history, structured memories, biographies, and monthly statistics. M7 adds operational hosting and delivery; M8 adds headless/MAX execution, acceptance checkpoint comparison, and the versioned sampled shortage-recovery boundary around this unchanged event engine.

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
WorldSchemaVersion:        0.1
SimulationRulesVersion:    m8-rng1-balance1 (fresh worlds)
M6 compatibility version:   m6-rng1-history1
M4 rules version:          m4-rng1-settlement1
M3 rules version:          m3-rng1-survival1
M2 rules version:          m2-rng1-citizen1
SurvivalVersion:            1
SettlementVersion:          1
SocialVersion:              1
HistoryVersion:             1
HistoricalEventSchemaVersion: 1
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

`ProcessNextEvent` advances the clock to the next due minute and dispatches canonical citizen events (or records legacy synthetic events). The M2 compatibility baseline has exactly 20 persistent founders, `CitizenGenerationVersion = 1`, `CitizenAction` values `None=0, Idle=1, Rest=2, Wander=3, Explore=4`, integer fixed-point needs/traits, and founder health `10000`; M3 extends this state with survival actions, mutable resources, health effects, and mortality; M4 adds construction actions. Founder birth minutes are signed and negative, with age derived using the 360-day calendar. Movement uses deterministic integer weighted A* (orthogonal 10, diagonal 14, destination movement cost, explicit eight-direction ordering, and no corner cutting). Citizen payload JSON is lossless. The deliberate M0/M1 input rules value `m0-rng1` is accepted only for one-time M1-to-M2 upgrade; unknown versions reject.

## Snapshot, reload, and determinism

Read snapshots freeze deep-copied event lists, citizen observations, settlement state, resource quantities, structures, and contributions for observers. Persistence snapshots freeze the same canonical mutable state, the pending event list, and counters; `SimulationEngine.FromPersistenceSnapshot` reconstructs the queue without changing order or consuming new IDs.

An M0 database upgraded to M1 is normalized exactly once at persistence-open time. The upgrade accepts arbitrary valid M0 configuration JSON, preserves the M0 seed, minute, compatibility metadata, counters, and pending event ordering, and replaces that configuration with canonical `WorldGenerationConfiguration.Default` JSON after generating the immutable M1 map. It is atomic with the metadata, event, tile, and resource rows: an interrupted upgrade leaves the M0 sentinel and no partial world rows, allowing a later open to retry. M1 rows are never regenerated during load; incomplete version-1 state is rejected.

RNG state is not serialized because there is no mutable RNG stream. After save/reload, the same seed, domain, and keys produce the same derived value without guessing how many draws occurred. Persisted event order and the next scheduled sequence are restored explicitly, so save/reload preserves subsequent event order and allocation. Operational timestamps do not affect either result.

## Deterministic prohibitions

Canonical Domain/Simulation behavior must not depend on:

- `Guid.NewGuid()` or other process-generated identity;
- `System.Random` or `Random.Shared`;
- `DateTime.Now`, `DateTime.UtcNow`, `Stopwatch`, `Environment.TickCount`, or wall-clock timing;
- unordered iteration where iteration order can affect an outcome;
- parallel mutation or task scheduling.

Wall-clock UTC is used only for operational checkpoint metadata and server lifecycle logging. The browser's refresh rate, connection state, and Vite proxy do not participate in canonical simulation state.

M5 social/family/aging and M6 historical gameplay are implemented below. M7 operational hosting, deployment, and reconnect behavior are described in the final section; M8 headless/MAX, scale fixtures, profiling, tuning, and 100-year acceptance are described after it.

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

Generation version `1`, terrain/resource integer values, configuration canonicalization, field keys, retry derivation, interpolation arithmetic, resource formulas, viability score, ordering, and fingerprint encoding are compatibility data. The RNG algorithm itself is version `1`; changing any of these without a compatibility/version decision changes canonical worlds. M2 adds `CitizenGenerationVersion = 1`; M3 adds `SurvivalVersion = 1` and current `SimulationRulesVersion = m3-rng1-survival1`, with `m2-rng1-citizen1` as predecessor. The known pre-M2 `m0-rng1` value remains accepted only as an upgrade input. Citizen roster and survival fingerprints use length-prefixed UTF-8 fields for explicit invariant state.

## M1 persistence and read boundary

M1 persistence adds `world_tiles` and immutable `resource_nodes` tables and extends `world_meta` with generation metadata and stored `world_fingerprint`. M2 adds `citizen_generation_version` and explicit `citizens` columns. M3 adds `survival_version`, explicit survival columns, mutable `resource_state`, and singleton `settlement_state`. Checkpointing writes metadata, map, resource definitions/states, settlement, events, and citizens transactionally. Loading rejects incomplete or inconsistent state rather than regenerating it. Citizen event payloads are canonical JSON objects with exactly `citizenId` (positive invariant decimal string) and `actionSequence` (non-negative integer); survival payloads contain exactly `citizenId`, regeneration payload is exactly `{"version":1}`, and legacy synthetic events retain `{}`. Weighted A* uses fixed N, NE, E, SE, S, SW, W, NW neighbor order, destination movement cost, and no diagonal corner cutting; queue ties are `F`, `H`, row-major tile index, then local insertion sequence.
Locked roster SHA-256 goldens for the above field encoding are seed `0`: `8550f3dcd5a30b84b78eedc51b623aa42abec73fc5da6884c66a0ddf701f24ac`; seed `42`: `6c310f306fd58939d596959acc3147371e680faea4b8e64bb5f95ffa0e84ad55`; and seed `UInt64.MaxValue`: `eaae4f6399cb5819de832012ec0c0a2ce7a3cd74b8f17265d576e2765e4efe2e`.

## M2 citizen compatibility contract

The persisted `citizens` table has one row per founder and explicit fields: positive `id`, `founder_ordinal` (0..19), given/family names, signed `birth_minute`, nullable death/lifecycle IDs, walkable location, health, four needs, six traits, six skills, `current_action`, `action_sequence`, nullable action timing/target, `needs_updated_minute`, movement counters, and M3 `health_updated_minute`, `action_phase`, `target_resource_node_id`, `carried_resource_type`, and `carried_resource_quantity`. The table uses a unique founder-ordinal index and range/action/carrying constraints; persistence orders rows by `id`.

Founder generation is version `1`, seeded by the persisted UInt64 seed and ordinal/field keys from `RandomDomain.CitizenGeneration`; fixed repository-owned given/family catalogs and deterministic family collision resolution are compatibility data. Exactly 20 founders are allocated from the shared entity counter and placed on walkable tiles sorted by squared distance to the starting site, then row-major. A founder age is selected in 18..45 and `birth_minute = checked(current_minute - checked(age * 518400 + offset))`, where offset is a deterministic minute within the year; age is derived with checked signed arithmetic. Traits are six independent integer values in 0..10000. Skills are six non-negative integer values; M2 founders start with fixed values, while M3 advances only the relevant gathering skill by 25 on a non-zero gather.

Needs are pure integer projections with Hunger `2` per minute except Winter `3`, Rest `3`, Shelter `1`, and Social `1`, saturated to 10000 with overflow-safe arithmetic. Observer reads never mutate stored needs. M2 behavior uses Idle, Rest, Wander, and Explore; M3 adds Eat, gathering, health, survival, and mortality while retaining these actions. Utility constants and action scoring are defined in the M3 section below.

`CitizenAction` is persisted as `None=0`, `Idle=1`, `Rest=2`, `Wander=3`, `Explore=4`. `action_sequence` is citizen-local and increments at each decision. Variation and target ranking use stateless `RandomDomain.DecisionVariation` keys containing citizen ID/ordinal, action sequence, and an explicit purpose key; target candidates are ranked deterministically and must be reachable. The persisted state contains only the next event, never an A* path.

Movement is pure weighted A*: fixed neighbor order is N, NE, E, SE, S, SW, W, NW; diagonal moves require both adjacent orthogonals walkable (no corner cutting); orthogonal base cost is 10 and diagonal base cost is 14, multiplied by the destination tile's MovementCost. Queue ties are F, H, row-major tile index, then final local insertion sequence. The locked seed-42 path from `(131,130)` to `(127,126)` is `(131,130),(130,130),(129,130),(128,130),(127,129),(127,128),(127,127),(127,126)`.

Stable event names are `citizen.decision.v1`, `citizen.move-step.v1`, `citizen.action-complete.v1`, `citizen.survival-check.v1`, and `resource.regenerate.v1`. Priorities are regeneration `5`, movement `10`, completion `15`, survival `18`, and decision `20`, so movement/completion/survival precedence is explicit at a shared minute. Citizen action payload JSON is canonical and lossless: exactly two properties, `{"citizenId":"<positive invariant decimal>","actionSequence":<non-negative integer>}`; survival payloads contain exactly `{"citizenId":"<positive invariant decimal>"}` and regeneration is exactly `{"version":1}`. Legacy synthetic events retain `{}`. Event identity, entity sort key, citizen existence, action sequence, current action, phase, and due-minute/state coherence are validated structurally; unknown, duplicate, missing, or past-due gameplay events reject. Processed-event accounting is a monotonic total count plus a bounded 32-entry diagnostic queue; this replaces unbounded retention while preserving legacy synthetic receipts.

M2 restoration requires exactly 20 rows, ordinals 0..19, unique positive IDs and names, valid walkable locations and ranges, null future fields, valid action/timing/target state, and exactly one coherent next citizen event per founder. Checkpoint writes metadata, world rows, resources, events, and citizens in one transaction. `citizen_generation_version=0`, zero citizen rows/events, and exactly pre-M2 `m0-rng1` rules are the strict M1 sentinel. A one-time M1→M2 upgrade preserves seed, minute, map fingerprint, counters, existing events, and `CreatedUtc`, allocates 20 shared entity IDs, and queues decisions at the current minute. M2→M3 requires the complete M2 roster and no partial survival rows/events; it derives each in-flight phase (`None→None`, `Idle/Rest→Perform`, `Wander/Explore→TravelToTarget`), sets `HealthUpdatedMinute` to the M2 minute, initializes starter survival state, and schedules each survival event plus the next daily regeneration. Direct M0→M3 opens the M0→M1→M2→M3 chain. Unknown/partial/corrupt state rejects; injected failure rolls back atomically and a retry is idempotent without regenerating the map or founders. M3 applies no retroactive damage during migration.

The server keeps `SimulationHost` as the sole writer. `SimulationMinutesPerSecond` must be finite and non-negative; default is `1.44` (one 24-hour world day every 16 minutes 40 seconds), and `0` disables operational advancement. Fractional rates accumulate and floor whole minutes; advancement is serialized through the host loop, and shutdown performs a final checkpoint. The M2/M3 citizen and settlement observation rules are preserved; M4 adds the immutable map and structure observation contract described below. M7 adds an observer-only SignalR channel; the current presentation layer uses it for compact immutable live frames and coalesced slower-detail invalidations while the frontend remains a safe string-ID observer with no canonical-state ownership or citizen mutation endpoint.

## M3 survival contract

`SurvivalVersion = 1` and M3-versioned snapshots use `m3-rng1-survival1`; the predecessor is `m2-rng1-citizen1`. World and citizen generation versions remain `1`. M3's settlement state is separate from the immutable M1 world: `resource_nodes` retain initial/max/regeneration definitions, `resource_state` stores current quantity per node, and singleton `settlement_state` (`id=1`) stores Food/Wood/Stone. The historical M3 starter stockpile is `400/0/0`, at the world `StartingSite`, with no capacity limit; M4 replaces that capacity rule only in M4-versioned state.

`CitizenAction` compatibility values are `None=0`, `Idle=1`, `Rest=2`, `Wander=3`, `Explore=4`, `Eat=5`, `GatherFood=6`, `GatherWood=7`, `GatherStone=8`, `Dead=9`. `CitizenActionPhase` values are `None=0`, `TravelToTarget=1`, `Perform=2`, `ReturnToStockpile=3`. Citizen survival columns are explicit (`health_updated_minute`, `action_phase`, `target_resource_node_id`, `carried_resource_type`, `carried_resource_quantity`) and are constrained with the existing health/need/action/timing/location invariants. A dead citizen remains in the roster but has zero health, a canonical death cause, no active phase/target/timing/carrying state, and no reserved events.

Needs use fixed-point integer values. Hunger increases by 2 per minute in Spring/Summer/Autumn and 3 per minute in Winter; Rest +3, Shelter +1, Social +1. Eat lasts 30 minutes, consumes `min(10, FoodStored)`, and reduces hunger by `5000 * consumed / 10`; Rest lasts 120 minutes and reduces Rest by 4000. Gathering takes `max(60, 180 - relevantSkill/100)` minutes and yields `base + relevantSkill/1000` (Food 12, Wood 10, Stone 8), capped by current node quantity. A non-zero gather depletes the mutable resource state, adds 25 XP only to the relevant gathering skill, carries actual goods, and deposits at the starting-site stockpile; zero yield adds neither goods nor XP.

Target ranking is reachable path cost ascending, current quantity descending, node ID ascending. A* uses orthogonal cost 10 and diagonal cost 14 times destination movement cost, fixed N/NE/E/SE/S/SW/W/NW neighbor order, and no corner cutting. Decision scores include variation `[-50,50]` and use: Eat `3000 + hunger*4 - travelCost`; Rest `rest*2`; GatherFood `hunger*3`; GatherWood `4`; GatherStone `3`; each gather adds `industriousness/20 + relevantSkill/1000 + stockpileContribution - travelCost/10`; Explore `1000 + curiosity/4 + riskTolerance/8`; Wander `700 + curiosity/10`; Idle `500 + (10000-industriousness)/20`. Stockpile contribution is 1800 below target or 500 at/above target (Food 400, Wood 120, Stone 100). Ties are Eat, Rest, GatherFood, GatherWood, GatherStone, Explore, Wander, Idle.

Gameplay event priorities are regeneration 5, movement 10, completion 15, survival 18, decision 20. Survival checks occur every 360 minutes from `health_updated_minute`; regeneration occurs at each next strict 1,440-minute day boundary. Action completion is phase based: move events advance travel one step, Perform completes at `action_completes_minute`, and ReturnToStockpile deposits before the next decision. Survival damage is 300 when Hunger >=9000 plus 120 when Rest >=9500, multiplied by `(10000 - resilience/4) / 10000` with integer division. If no damage and Hunger <6000 and Rest <7000, health recovers `120 + resilience/1000`, capped at 10000. Death causes are starvation, exhaustion, or deprivation (both), and age derives from DeathMinute thereafter. Carried goods are lost at death.

Food regeneration is `(regenerationPotential * basisPoints) / 10000` with Spring/Summer/Autumn/Winter basis points `12500/15000/10000/2500`; Wood is `regenerationPotential / 8`; Stone is exactly zero. All resource quantities cap at immutable maximums.

The survival fingerprint includes, in canonical order, seed/minute/schema/world/configuration/rules/citizen-generation/survival, settlement stores, all deterministic counters, ID-sorted resource quantities, ID-sorted citizen identity/lifecycle/location/health/death/needs/boundaries/traits/skills/action-phase-sequence/timing/targets/carrying/movement fields, and queued events (ID/due/priority/entity/sequence/name/payload), each as length-prefixed UTF-8 SHA-256 input. Locked goldens are seed 42 day 7 (`3671fac075c73d433f2b42344db4f2502e6558382152a36330a1b227854dd33a`), seed 0 day 1 (`028677a72fa65710a064dd5573c6bc5dafbf0377faed0e10597db652c3238950`), and UInt64.MaxValue day 1 (`3ff6cfe4eea7240784a1b46365a637a74fb0785607edd5e2276ffb3b529a501f`). M1 world, founder, and A* goldens are unchanged.

Acceptance evidence covers strict migration/corruption rejection, rollback and idempotence, concurrent read contention, seven checkpoint points (mid-eat travel/perform, mid-gather outbound/perform/return, before survival, before regeneration), chunk/save-reload equivalence, scarcity death, and server smoke. Recorded 30-day seed-42 metrics are 20→20 population, 0 deaths, 17,318 consumed food, 17,598 gathered food, final food 0, wood 126, stone 107, 70,588 depletion observations, 274 regeneration observations, health 10,000..10,000, and skill progression for all 20. The suite is cross-platform configured; this branch records local Windows evidence only and does not claim hosted Linux execution.

M3 non-goals are structures/shelters (M4), social/family/aging (M5), history (M6), carrying capacity/trade, map mutation, and player controls or mutation APIs.

## M4 settlement and construction contract

M4 is the settlement compatibility layer: `SimulationRulesVersion = m4-rng1-settlement1` and `SettlementVersion = 1`. M6 remains the explicit compatibility boundary (`m6-rng1-history1`), while fresh worlds use M8 `m8-rng1-balance1`; both preserve the same gameplay engine outside the versioned shortage-history boundary. The retained M3 predecessor is `m3-rng1-survival1`; M2 remains `m2-rng1-citizen1`. World, RNG, and founder generation versions are unchanged. A fresh M4 engine starts with 20 founders, settlement stores `Food=400`, `Wood=0`, `Stone=0`, base storage capacity `800`, and exposure consequences beginning exactly `7 * 1440 = 10080` minutes after its creation minute.

### Structures, capacity, and demand

Persisted enum values are compatibility data:

```text
StructureType:   Shelter=1, Stockpile=2, Workshop=3
StructureStatus: UnderConstruction=1, Complete=2
```

Canonical structure requirements are fixed; a checkpoint rejects substitutions:

| Type | Wood | Stone | Work | Completed effect |
|---|---:|---:|---:|---|
| Shelter | 40 | 10 | 600 | capacity for 4 citizens |
| Stockpile | 60 | 30 | 900 | adds 800 storage capacity |
| Workshop | 80 | 50 | 1,200 | construction work multiplier 12,500 basis points |

An under-construction structure has no completion minute and derived condition `0`. A complete structure has its full canonical delivered materials/work, a completion minute, and derived condition `10000`. Structures are ID ordered, use positive shared entity-counter IDs, and must occupy unique reachable buildable non-Freshwater tiles that are neither the starting site nor a resource tile.

Settlement capacity is `baseStorageCapacity + completedStockpiles * 800`; stores are non-negative and their sum cannot exceed that capacity. There is a single active under-construction project. The `settlement.evaluate-demand.v1` event has priority `7`, entity key `0`, and exact payload `{"version":1}`. It occurs every `360` minutes, updates the demand boundary, reconciles housing, and schedules one next demand event. With no active project, demand is ordered as follows:

1. Build a Shelter while completed shelter capacity is below living population.
2. Otherwise build a Stockpile once stored material is at least 80% of capacity.
3. Otherwise build one Workshop after at least three completed structures, only if no completed workshop exists.

The chosen site is the reachable eligible tile ordered by path cost from the starting site, then Manhattan distance, then row, then column. This makes demand, sites, and structure IDs independent of host timing and observer reads.

## M5 social, family, and lifecycle model

M5 adds `Socialize=12` without renumbering earlier actions. It is a local Perform action lasting 60 minutes and only succeeds while its living target remains within Chebyshev radius 2. Successful interactions update normalized relationship pairs using the Relationships RNG domain, satisfy initiator/target social needs, and may form partnerships. M5 tie order is Eat, Rest, GatherFood, Socialize, HaulConstruction, Build, GatherWood, GatherStone, Explore, Wander, Idle; social behavior is excluded when Hunger or Rest is critical.

For every nearby candidate with a non-null relationship, social target score subtracts the compatibility term `max(0, 4000 - min(4000, (CurrentMinute - LastInteractionMinute) / 10))`. A null relationship has zero recency penalty. This applies equally to ordinary, family, partner, and rival edges; it does not inspect age, household, partnership eligibility, or interaction count. All candidates remain ranked by score descending, Chebyshev distance ascending, then citizen ID ascending.

Children are created only from eligible partnered households at daily family checks, use a global citizen ID, nullable founder ordinal, canonical parent ordering, `BirthMinute = CurrentMinute`, and zero inherited skills. Given names and trait variation use `RandomDomain.Reproduction`; the founder catalog/keying is unchanged. Parent/child and living-sibling relations are initialized deterministically. Households pack as groups into completed shelters, capped at four living members; M5 demand includes a four-slot family-growth shelter buffer.

Age stages are Young Child 0-5, Child 6-12, Adolescent 13-17, Adult 18-59, and Elder 60+. M5 productive output is age-scaled (0/5000/7500/10000/7500 basis points) before the existing workshop multiplier. The daily lifecycle event applies integer natural mortality by age, health, and resilience using `RandomDomain.Mortality`; natural death preserves permanent identity, relationship, family, partnership, household, home, and work history while clearing active gameplay events.

### Hauling, building, and action state

M4 extends persisted actions and phases without renumbering M2/M3 values:

```text
CitizenAction: HaulConstruction=10, Build=11
CitizenActionPhase: TravelToStockpile=4, TransportToConstruction=5, WaitingForStorage=6
```

A hauling citizen travels to the shared stockpile, takes only wood or stone still required after delivered and in-transit reservations, then travels to the active project. Carry capacity is `20 + min(20, HaulingSkill / 1000)`. Delivery is capped at the unreserved remaining material; it records a per-structure/per-citizen cumulative contribution, adds 15 Hauling XP, and increments hauling work time by actual action duration. A citizen with unsold gathered goods cannot exceed capacity: it enters `WaitingForStorage`, retains the remainder, and retries after exactly 60 minutes.

A build shift is 180 minutes. Applied work is the remaining requirement capped at `100 + ConstructionSkill / 1000`; if any completed workshop exists, it is multiplied with integer arithmetic by 12,500/10,000. Each completed shift grants 25 Construction XP and 180 Construction work minutes. When work reaches the fixed requirement, the structure becomes complete and housing is immediately reconciled. Construction contribution rows are canonical state, not a derived audit log.

M4 decision ordering adds construction alternatives and defines ties as: Eat, Rest, GatherFood, HaulConstruction, Build, GatherWood, GatherStone, Explore, Wander, Idle. Existing needs/survival/gathering rules remain M3-compatible under M3 snapshots.

### Shelter, exposure, and occupation

Completed shelters assign homes deterministically: valid existing assignments are retained while capacity remains, then unassigned living citizens are considered by citizen ID and shelters by structure ID, with at most four citizens per shelter. A resting citizen travels to a valid assigned home when necessary. Rest at that shelter reduces Shelter need by `7000` in addition to normal Rest reduction.

After the grace boundary, projected Shelter need at least `9000` adds exposure damage on each normal 360-minute survival check: `150` outside Winter or `300` in Winter, then the existing resilience multiplier applies. Recovery requires Hunger `<6000`, Rest `<7000`, and Shelter `<8000`. Zero health records `exposure` where it is the sole cause, otherwise uses the existing `deprivation` combination; death clears active action/targets/carrying and triggers reassignment.

The five persisted lifetime work counters are Foraging, Woodcutting, Stoneworking, Construction, and Hauling minutes. `Occupation` is derived only: it is `Generalist` below 360 total work minutes or when no category reaches 40% of the total. Otherwise the largest category gives Forager, Lumberjack, Stoneworker, Builder, or Hauler; ties use that listed stable order.

### Persistence, fingerprints, and observer boundary

M4 persists `settlement_version`, settlement storage/demand/exposure fields, citizens' home/target structure IDs and five work counters, structures, and contribution rows. M3→M4 accepts only the exact M3 sentinel: M3 rules, settlement version zero, one valid old settlement row, no structures/contributions/demand event, and no M4 citizen fields/actions/phases. It upgrades atomically by preserving stores, using `max(800, prior total stores)` as base capacity, establishing the exposure grace, setting M4 compatibility metadata, and scheduling demand for 360 minutes later. Direct M0 opens follow the M0→M1→M2→M3→M4 chain. Partial sentinels, corrupted rows/events, impossible transit reservations, excess homes/storage, or malformed canonical costs reject; injected failure rolls back and retry is idempotent.

The M4 settlement fingerprint remains SHA-256 over length-prefixed UTF-8 canonical fields and extends the M3 fingerprint with settlement version/boundaries, sorted structures, sorted contributions, citizen M4 targets/homes/work counters, and the demand event. M1 map and M2 roster goldens remain unchanged; M3 fingerprint goldens remain locked evidence for explicitly M3-versioned snapshots.

`SimulationStatusSnapshot` deep-copies sorted structures and contributions alongside other read state. The host projects a single immutable observation; `/api/v1/structures` and its canonical-ID detail route expose copied, ID-sorted structures with sorted occupants/contributions, while `/api/v1/map` exposes row-major terrain and starting site. Citizens and settlement observations include the M4 fields. The read-only browser fetches the map once, consumes connected live frames, and serially polls only as a fallback; its canvas is an observer, never a canonical simulation owner.

At the M4 boundary, M5 relationships, households, reproduction, aging, and families, plus M6 historical gameplay events, biographies, statistics, and queries, were not yet enabled. Those compatibility layers are described below. M4 adds no player mutation API, map mutation, trade system, SignalR hub, or PixiJS renderer.

## M6 history and statistics contract

M6 is the locked compatibility boundary: `SimulationRulesVersion = m6-rng1-history1`, `HistoryVersion = 1`, and `HistoricalEventSchemaVersion = 1`. Fresh M8 worlds use `m8-rng1-balance1` with the same history/schema sentinels. Explicit predecessor values remain stable and selectable: `M2SimulationRulesVersion = m2-rng1-citizen1`, `M3SimulationRulesVersion = m3-rng1-survival1`, `M4SimulationRulesVersion = m4-rng1-settlement1`, and `M5SimulationRulesVersion = m5-rng1-social1`. M5 behavior is enabled for M5, M6, and M8 snapshots; history behavior is enabled for M6 and M8. Unknown versions reject, and there is no silent M6→M8 migration.

### Historical event vocabulary and schema

Historical event type values are persisted compatibility data and must never be renumbered:

```text
WorldCreated=1                  SettlementFounded=2
CitizenBorn=3                   CitizenDied=4
PartnershipFormed=5             FriendshipFormed=6
RivalryFormed=7                 HouseholdCreated=8
StructureStarted=9              StructureCompleted=10
PopulationMilestone=11          ResourceShortageStarted=12
ResourceShortageEnded=13        CitizenSpecializationChanged=14
SeasonStarted=15
```

Importance is `Debug=0`, `Routine=1`, `Personal=2`, `Notable=3`, `Major=4`, `Historic=5`. The canonical importance is world/settlement 5, birth/death/partnership/completion/shortage 3, friendship/rivalry/household/structure-start/specialization 2, population milestone 4, and season 1. The normal observer timeline requests importance `>=2`; routine events remain queryable. Origin is `Live=1` or `MigrationBackfill=2`.

An event is an immutable record of `HistoricalEventId`, non-negative `WorldMinute`, type, importance, origin, optional `TileCoordinate`, canonical `PayloadJson`, and schema version. Citizen links contain `(HistoricalEventId, CitizenId, Role)`; roles are exactly `subject`, `parent`, `partner`, `founder`, `participant`, `member`, or `contributor`. Structure links contain `(HistoricalEventId, StructureId, Role)` with `subject`. Links reference real persistent entities, allow dead citizens/completed structures, and are composite-unique. IDs are invariant decimal strings at the API boundary and are never converted to JavaScript numbers.

Payload builders/parsers are event-specific and schema-controlled. They use invariant numeric formatting, stable property order, decimal-string entity IDs, and no prose, wall-clock timestamp, or arbitrary fields. Display summaries are derived templates using only event type, structured payload, and linked names/types; they never infer motive, dialogue, emotion, or unrecorded achievement. Routine meals, gathering, movement, decisions, and construction shifts emit no historical event.

### Live emission and ordering

Fresh M6 state immediately emits `WorldCreated`, `SettlementFounded`, and `SeasonStarted(Spring/Year 0)` at minute 0. Live births emit `CitizenBorn` with child subject and both parent links, household payload, and birth location; population milestones are emitted after the birth event in threshold order. Every survival/natural death path emits one `CitizenDied` with subject link, exact cause/age payload, and persistent death location. Partnership emits `PartnershipFormed` then `HouseholdCreated` at the same minute, with partner/member links. A social interaction emits one `FriendshipFormed` or `RivalryFormed` only when the ordinary relationship label newly crosses the threshold and no formation event exists for that pair; a pair may legitimately have both over time. Occupation updates compare the derived label before/after work and emit `CitizenSpecializationChanged` only on an actual change.

Demand creation emits `StructureStarted` once with subject structure link and canonical type/cost/work payload. Completion emits `StructureCompleted` with structure subject plus non-zero contributor links in ascending citizen-ID order; individual build shifts have no history. M6 shortage history uses immediate hysteresis: start when `FoodStored < living population × 10`, end when `FoodStored >= living population × 20`, and inactive when living population is zero. M8 starts at the same threshold, remains active through ordinary food/population reevaluations, and confirms a non-zero-population end only at the 30-day statistics sample when food reaches `living population × 20`; zero population ends immediately. Emit only transitions, including a single `preexistingAtHistoryStart=true` start at M5→M6 upgrade when already below threshold. `SeasonStarted` uses exact calendar boundaries and shares its minute with the monthly sample.

### Statistics scheduler and state

The reserved `history.statistics-sample.v1` scheduled event has priority 19, after lifecycle 17 and survival 18 but before decisions 20. It samples every exact 43,200 minutes (30 simulated days) at `((WorldMinute / 43200) + 1) * 43200`, then schedules `current + 43200` without drift. At a season boundary it emits `SeasonStarted` before/alongside the sample. Sampling observes living population, births/deaths since the period start, stored Food/Wood/Stone, Food produced/consumed, shelter capacity, floor average health, and floor projected average hunger; with no living citizens both averages are zero. It does not materialize projected needs. Counters then reset and `PeriodStartMinute` becomes the sample minute.

`HistoryState` is one persisted singleton:

```text
HistoryStartMinute, HistoryStartEventId
PeriodStartMinute, BirthsSinceSample, DeathsSinceSample
FoodProducedSinceSample, FoodConsumedSinceSample
ActiveFoodShortage, PopulationMilestoneWatermark
```

`StatisticsSample` is keyed by `WorldMinute` and stores `PeriodStartMinute`, population, births/deaths, Food stored/produced/consumed, Wood, Stone, shelter capacity, average health, and average hunger. Food production counts only the amount accepted into shared storage; consumption counts the amount removed. The historical counter is independent from entity and scheduled-event counters; only scheduling the statistics event consumes an ordinary scheduled sequence.

### Structured memories and biography

`CitizenMemory` is keyed by `(CitizenId, HistoricalEventId, MemoryType)` and stores `MemoryType`, `Importance`, `EmotionalValence`, and `CreatedMinute`. Stable memory values are `ChildBorn=1`, `PartnerDied=2`, `PartnershipFormed=3`, `FriendshipFormed=4`, `RivalryFormed=5`, and `StructureCompleted=6`. Parent memories for a child birth use valence +8000; partner death -9000; partnership +8000; friendship +6000; rivalry -7000; and structure completion +4000, all clamped to -10000..10000. Memories are observational and never influence decisions. ChildBorn, PartnerDied, and PartnershipFormed memories are permanent. Friendship, rivalry, and structure-completion memories are capped at 64 per citizen, retaining importance descending, created minute descending, and event ID descending; pruning never removes the historical event.

Biography is a deterministic read model of citizen identity/name, birth/death and cause/age, life stage, parents, partner, children, household, derived occupation, linked notable events (`importance >= 2`, minute/ID ascending), and important structured memories. It remains available after death. No logs, AI, inferred dialogue, speculative motive, or fictional prose are inputs.

### M5→M6 migration and fingerprints

The direct upgrade requires a complete M5 snapshot with `SimulationRulesVersion=m5-rng1-social1`, `SocialVersion=1`, `HistoryVersion=0`, and no history rows, links, memories, statistics, `HistoryState`, or statistics scheduler. It initializes history exactly once at the pre-M6 `NextHistoricalEventId`; it does not reset the counter. Backfill candidates are built before allocation and sorted by world minute, canonical event rank, primary entity ID, then secondary ID. Exact backfills are world creation, settlement founding, mathematically exact seasons through the snapshot minute, descendant births with `BirthMinute >= 0`, deaths, partnerships/households, structures, and reconstructible population milestones. Founder births (negative minutes), friendship, rivalry, specialization, and pre-M6 aggregate statistics are intentionally excluded because their exact historical transition/value is unknowable. Missing optional fields remain omitted; no later location is substituted for a backfilled birth location. Upgrade and ordinary checkpoint writes are atomic, append-only, prefix-validating, and idempotent; any conflict rejects rather than repairs.

`HistoryFingerprint` extends the M5 canonical fingerprint with HistoryVersion, HistoryState, historical events (ID ascending, raw payload JSON), citizen/structure links, samples (minute ascending), and memories sorted by citizen ID/event ID/memory type. It excludes summaries, UI filters, pagination, and wall-clock metadata and uses invariant formatting. M0→M6 follows the real M1→M2→M3→M4→M5→M6 chain, preserving seed/world/founders and initializing/backfilling only once. History is alongside canonical snapshot state and the deterministic scheduled queue, never an event-sourced replacement.

### Immutable query/API boundary and observer UI

The host atomically publishes an immutable history observation from the simulation; HTTP never reads the mutable engine or latest SQLite checkpoint directly. `GET /api/v1/history` defaults to `minimumImportance=2`, `limit=50`, and descending `WorldMinute`/event ID ordering. It accepts bounded `fromMinute`, `toMinute`, `eventType`, `minimumImportance`, `citizenId`, `familyCitizenId`, `structureId`, `beforeEventId`, and `limit` (maximum 100). Family closure is root, ancestors, descendants, siblings sharing a parent, and persistent partner. Detail, biography, and memory routes validate canonical positive decimal IDs; malformed IDs are 400 and unknown IDs 404. `GET /api/v1/statistics` accepts bounded minute ranges and limit and returns samples ascending by minute. Every route is read-only.

The React observer uses strict DTO parsers, retains decimal IDs as strings, limits requests to server pagination, and protects state from stale responses/unmounts. It provides a newest-first factual history feed with citizen/family/type/importance/structure/time filters and older-page loading; selected citizen biography facts, notable timeline, and structured memories; a bounded monthly statistics table plus lightweight population trend; and operational pause/resume/safe-positive-speed controls. Control requests are host commands and never canonical simulation writes. M7 owns persistent-host/reconnect/service hardening; M8 owns headless/MAX, scale, performance tuning, long-run profiling, and 100-year acceptance. M6 does not add AI narration, full-text search, replay/event sourcing, user accounts, cloud sync, SignalR mutation, or population retuning.

## M7 persistent-host timing and observer contract

M7 changes operational delivery around the unchanged canonical engine boundary. M6 snapshots retain `m6-rng1-history1`; fresh M8 snapshots use `m8-rng1-balance1`. The operational driver defaults to `SimulationMinutesPerSecond=1.44`, or one 24-hour world day every 16 minutes 40 seconds; a zero startup speed is paused, and bounded host commands can pause, resume, or change to a finite positive speed without changing canonical state. A periodic checkpoint is considered after serialized advancement when both boundaries are met: at least `360` simulation minutes since the last successful checkpoint and at least `30` real seconds since the last checkpoint attempt. Checkpoints are transactional and batched at the host boundary, not emitted per simulation event. `CheckpointRetryCount=3` means three bounded retries after the initial attempt, with `CheckpointRetryDelaySeconds=2` between attempts. The first failed attempt publishes persistence `Degraded`; exhausting the bounded attempts publishes `Faulted` and stops the host under the configured host policy. `BrowserUpdateIntervalMilliseconds=500` controls observer invalidation cadence; `ObserverStreamIntervalMilliseconds=100` controls the noncanonical connected live-frame stream.

Graceful shutdown drains queued commands, performs a final checkpoint, and then disposes the database. A crash, forced termination, or power loss has no final-checkpoint guarantee; SQLite may recover its WAL on the next open, and the host resumes only the last committed valid snapshot. Suspended or stopped wall time never becomes simulation-minute catch-up. Browser disconnects do not affect simulation; after reconnect, REST `GET /api/v1/*` reads are authoritative and the browser can fall back to them if the observer-only SignalR connection is unavailable.

The published ASP.NET Core server serves the Vite build from static `wwwroot` files and uses the published `index.html` for non-API/non-hub client routes. `/hubs/world` is an observer-only SignalR endpoint. `StreamWorld` sends compact immutable status, citizen, and structure frames at the configured live cadence; coalesced `worldChanged` messages remain available for slower ledger invalidation. Neither path carries canonical state ownership or gameplay mutation commands. The default listen URL remains loopback-only at `http://127.0.0.1:5274`. A trusted-LAN deployment must explicitly bind, for example, `http://0.0.0.0:5274`, and separately configure a Private-profile firewall rule restricted to the actual private subnet. There is no built-in authentication or TLS, and the server is not designed for public Internet exposure.

The fast pull-request CI path excludes tests tagged `Category=Long` on Ubuntu and Windows while retaining the existing frontend checks. The separate long-test path is manual or weekly only, uses locked restore and a Release no-restore build, runs `Category=Long` on both operating systems, and has a 180-minute job timeout. See [`docs/windows-service.md`](windows-service.md), [`docs/backup-and-recovery.md`](backup-and-recovery.md), and [`docs/sleep-resume-checklist.md`](sleep-resume-checklist.md) for operational procedures.

## M8 headless/MAX and acceptance model

`LittleAges.Headless` calls the same `SimulationEngine.AdvanceUntil` path as
the host. MAX removes wall-clock pacing only; it does not alter event ordering,
RNG derivation, decisions, mutation ownership, or history. The CLI validates
invariant numeric input and supports only 1, 10, 100, and 500-year public
horizons. `run` and `benchmark` emit stable JSON/Markdown projections with
operational timing clearly separated from canonical fingerprints.

The `acceptance` command performs two runs for a requested checkpoint: Run A
continues uninterrupted; Run B writes a real SQLite persistence snapshot at the
checkpoint, disposes and reopens it, then constructs the existing engine from
the loaded snapshot and continues. Equivalence compares world minute, counters,
resources, citizens, settlement/social state, queue, complete history and links,
statistics, memories, rules/schema sentinels, and both persistence and history
fingerprints. Invariant failures or mismatches are non-zero outcomes. The
manual workflow uses `workflow_dispatch` on Windows with locked restore, a
Release build, a 180-minute timeout, seed 42, M8 rules, a 100-year target, and
a year-37 checkpoint; its candidate evidence is in
[`v0.1-acceptance-report.md`](v0.1-acceptance-report.md).

M8 reports exact peak living population from the minute-zero founder baseline
and ordered retained birth/death facts. It reports decade population trajectory,
shortage starts and ends by year, deterministic representative evidence, and
the historical event/statistics/memory totals without adding reporting data to
canonical state. Bounded 250/500 synthetic-adult scale fixtures measure engine
advance, read-snapshot, and server projection costs separately; they are
non-persistent one-day tests and do not represent 100/500-year runs.
