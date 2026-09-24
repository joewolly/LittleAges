# Little Ages v0.3.0 (Draft)

**Status:** PR #15 candidate; not published.

## Highlights

- New worlds default to `m14-rng1-migration1`, adding deterministic migration
  and one daughter settlement. Each settlement has local stocks, work, and
  knowledge. Households can found the second site by expedition, relocate
  seasonally, and make annual family visits. The read-only observer can inspect
  both sites and their shared history, and focus the map on either site; the
  simulation adds no intersite trade.
- Existing saves retain their recorded rules and behavior. There is no
  automatic upgrade to M14.

## Acceptance

The seed-17 and seed-42 100-year Release acceptance runs passed all mandatory
invariants and year-30 SQLite checkpoint equivalence. At year 100, seed 17 had
25 living citizens (17 at the original site and 8 at the daughter site); seed
42 had 16 (14 at the original site and 2 at the daughter site). These results
describe the measured seeds, not every possible world.

## After merge

Run the manual **Windows package** workflow with version `0.3.0` and review the
resulting `LittleAges-v0.3.0-win-x64.zip`. Then tag the merged commit as
`v0.3.0`, publish the GitHub release with this note and the verified package,
and confirm the published asset is available. This draft does not publish a
tag or release.
