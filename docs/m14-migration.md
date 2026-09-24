# M14 — Migration and second settlement rules

**Status:** implemented; `m14-rng1-migration1` is the current default for new
worlds. Existing saves keep their recorded rules and behavior. The final
seed-17 and seed-42 100-year Release acceptance runs are complete; see the
measured status below.

## Compatibility and scope

M14 extends the merged M13 rules with migration and one autonomous daughter
settlement. Newly created worlds default to `m14-rng1-migration1`. Existing
saves keep their recorded rules and behavior; migrating an existing world to
M14 is out of scope. Earlier M13 design and
acceptance records remain historical evidence for M13 and are not rewritten by
this contract.

The world keeps its existing 160 × 160 map and original settlement. At most one
daughter settlement may be founded in a world. Settlement IDs are stable and
world-wide within the settlement kind: the original site has ID 1 and the
daughter has ID 2. IDs remain stable and unique world-wide within each entity
kind, and family and kinship relationships can span both settlements. Facility
and order IDs retain their existing shared Living namespace across sites.

## Settlement-local life

Each settlement has its own stocks, housing, farms, work, barter, and available
knowledge. Citizens use the stocks and opportunities of their current site.
Households belong to one settlement at a time. Founding the daughter does not
combine inventories or make either settlement's knowledge automatically
available at the other site. M14 has no intersite trade; goods move with a
household only as part of a founding expedition or relocation.

The world remains one historical record. Events identify the people and sites
involved using world-wide IDs, and family history remains connected when
relatives live in different settlements. The observer can inspect factual
history across both sites.

## Founding an expedition

After year 5, run household selection once per season. Use stable household IDs
for deterministic candidate ordering and start no more than one founding
expedition in a season. A household qualifies when it has at least two capable
adults and enough provisions for 14 days per traveler, and either:

- it has experienced housing or usable-land pressure for the full preceding
  season; or
- at least one adult has curiosity greater than 7,500.

Choose a viable, reachable site at least 32 travel-cost units from the original
settlement. Site evaluation and tie-breaking must be deterministic, so the same
world state always selects the same best site. The founding household travels
as one visible expedition; its members remain subject to ordinary needs and
mortality while traveling. Departure or candidate selection alone does not
create a settlement. A daughter site is founded only when at least one founding
adult physically arrives there.

The expedition records its location, travelers, provisions, cargo, and progress
in canonical state so it can continue after a save is reopened. If an expedition
fails or returns, it does not leave a settlement behind. Carried goods and
remaining provisions are accounted for exactly once; any consumed provisions
are explained by the travelers' ordinary needs. A successful founding transfers
the household and its remaining cargo without duplicating or losing goods.

## Relocation and family visits

After the daughter is founded, households may relocate seasonally in either
direction. A move is for the whole household and is allowed only when the
destination can support it and offers a better opportunity under deterministic
rules. A cooldown after relocation prevents repeated moves between the two
sites as their relative opportunity changes.

Relatives in different settlements can make an annual physical family visit.
The visit is visible travel to the other site followed by a return home; it
transfers no trade goods and does not change household membership. Visitors
continue to follow ordinary needs and mortality while traveling.

## Observer and history contract

History reports what happened: expedition departures and outcomes, founding,
relocation, and family visits. It uses the stable world-wide citizen and
settlement identities and does not infer events that were not recorded.

The observer exposes all sites through read-only `GET /api/v1/settlements` and
`GET /api/v1/settlements/{id}` routes. The existing singular
`GET /api/v1/settlement` route remains bound to original settlement ID 1 for
compatibility. The map can focus on either settlement so the observer can show
both sites and their local activity.

## Explicitly deferred

M14 does not add roads, trade between settlements, outsiders or newcomers,
disasters, or ruins. Those remain later design possibilities.

## Acceptance criteria

M14 implementation is acceptable when evidence shows:

- deterministic results are identical when a run is advanced in different
  chunk sizes, including founding, travel, relocation, and visits;
- SQLite close/reopen continuation matches uninterrupted execution at
  mid-travel, expedition failure/return, founding, and family-visit states;
- multiseed 10-year and 100-year runs preserve identity, cargo, household,
  settlement, and travel invariants, including worlds where no second site is
  founded;
- both settlements and their local activity can be inspected in the observer,
  with map focus for each site, factual history, the plural list/detail routes,
  and the singular route still reporting the original site.

### Measured status

- The Release solution build completed with 0 errors and 0 warnings.
- The latest non-long .NET test run passed 627/627 tests.
- A focused 100-year constrained no-daughter test passed. Its in-memory
  year-50 and year-51 restore checks matched uninterrupted execution. These
  in-memory checks do not establish SQLite close/reopen parity across the
  listed states.
- The seed-17 and seed-42 10-year headless acceptance checkpoint at year 8
  passed all invariants and canonical equivalence. The reported living counts
  (site 1/site 2) were 27/5 for seed 17 and 29/5 for seed 42. Population
  survival is a separate observation from passing the invariants.
- Browser QA verified the two-site list and detail views, the original
  singular route, and map focus in both 2D and 3D.
- A focused storage diagnostic found that a Gather action waiting for storage
  could keep a citizen from responding to urgent Eat or Rest needs. The M14
  fix lets those needs preempt the blocked action through `FinishAction`, which
  recovers carried cargo. M14 living-work output and new Gather pickups both
  account for live residents' carried M12 cargo when checking local free
  capacity; Deposit behavior is unchanged. Focused regression coverage
  exercises daughter-site food gathering with 14 and 4 units of free storage,
  preserves the M13 exact-yield Gather case, checks seed-17 survival through
  minute 34,200, and validates the year-5 storage-boundary snapshot.
- The final seed-17 and seed-42 100-year Release acceptance runs used current
  source with `--checkpoint-year 30`. Both exited 0 with
  `mandatoryInvariantsPassed=true`, `acceptance.equivalent=true`, and no failed
  invariants or mismatches. At year 100, seed 17 had 25 living citizens
  globally (17 at site 1 and 8 at daughter site 2); seed 42 had 16 (14 at site
  1 and 2 at site 2). The reports are
  `artifacts/m14-acceptance/seed-17-storagefix-100/report.json` and
  `artifacts/m14-acceptance/seed-42-storagefix-100/report.json`.

Both runs retained living populations at both sites. Population survival is a
separate observation from passing the invariant and equivalence checks.
