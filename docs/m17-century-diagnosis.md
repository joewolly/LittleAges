# M17 century diagnosis

The preserved frozen-version-three exposure traces identify defects before the
final M17 relocation and ownership corrections. Separate exposure and starvation
assessments use actual final frozen version five. Version-three outcomes remain
superseded evidence.
Mortality, housing and resources are measured separately from
invariants and checkpoint equivalence; an invariant pass alone does not establish
acceptable behavior, and zero deaths is not a universal simulation requirement.

## Native relocation support

The old relocation gate checked aggregate free completed beds. Housing packs a
whole household into one shelter. A canonical frozen-V3 fixture has a native
three-person family and two destination shelters with two occupants each. The
four aggregate free beds satisfy the gate, but neither shelter fits the family.
Normal scheduled movement physically arrives at minute 2,638, clears the old
homes, deposits conserved goods and emits the relocation fact; all three
residents become unhoused. The same fixture on the exact released M16 baseline
reproduces the defect with matching full snapshots and result hashes.

The M17 correction uses one pure placement planner for reconciliation and both
relocation support checks, including actual household identity and existing
unhoused local families. Fragmented support rejects departure without changing
homes, goods, guests or events. If a whole dwelling becomes unavailable during
travel, scheduled movement returns the family and its goods to their old site.
Released M16 paths retain their behavior. Regression tests cover actual departure,
arrival, support loss, checkpoint reconstruction and different continuation chunks.

Seed 17's first four frozen-V3 exposure deaths occur between minutes 35,025,480
and 35,548,200. Recorded facts show household 574 arriving at site 2 with three
members at 34,992,140, and household 802 arriving with four at 35,510,506. These
are arrival facts, not departure times. Their shelter projects start shortly
after arrival and are already active; unrelated-project blocking is not supported
by this evidence. Resident 713 dies 658 minutes before the second shelter
finishes; children 843 and 917 die 422 and 1,142 minutes afterward. Prior and next
visitor episodes establish no active guest during any of these four deaths.
Faithful annual replay reproduces all four exact original death IDs and minutes.
Immediately before household 574 arrives, site 2 has eight vacant beds among
fifteen shelters, with at most two vacant beds in any dwelling. Before household
802 arrives, twelve beds are vacant among seventeen shelters, again with at most
two together. Neither arriving family fits. Both retain their origin homes until
arrival, then all members become unhoused. No active guest is present.

The first shelter starts with 116 wood and 73 stone already available locally.
Its delivered materials and construction work remain zero for roughly 24 days.
A bounded workforce trace sees 31 residents hauling 112,608 remaining grain
across sixteen fields and no construction targets or cargo. Construction begins
after that harvest backlog largely clears. Retained scores favor ordinary harvest
(8,500) over a shelter when aggregate beds already exceed population (4,500).
This explains the delay after an infeasible arrival; it is not a reason to change
worker priorities in this feature.
The second project's workforce supplement starts with 131,343 remaining grain
and 28 harvest workers. Construction is first observed at minute 35,546,040,
when the harvest backlog has fallen to 1,231 grain. These samples distinguish
the proven infeasible arrival from the existing work selection policy.

The later housed children still carry accumulated exposure. Resident 843 finishes
an outdoor rest six minutes after receiving the new home and gets no shelter
relief. Resident 917 physically reaches home and begins resting, but dies two
minutes before that rest would complete. A final housed census conceals the
preceding homelessness and delayed physical recovery.
Household 802's adult residents 713 and 767 had previously joined from outside;
children 843 and 917 retain their actual parent links. Absence of an active guest
does not mean that the resident population has no admitted people or descendants.

Additional frozen-V3 deaths are examined separately below. They are not
attributed to relocation merely because the isolated relocation defect exists.

## Local household housing shuffle

The faithful seed-17 continuation identifies the fifth exposure death as native
resident 452 at minute 43,304,040. At the ordinary daily family check at
43,267,680, residents 357 and 452 in household 918 lose shelter 537 at site 2.
That shelter had held their two-person household and the reproductive pair
797/1024 in household 1510. The family check creates child 1511; the canonical
before/after observations show household 918 becoming unhoused.

A separate, explicitly private-method diagnostic of that same pre-event state
identifies the faulty intermediate selection. The local reproductive housing
helper moves both donor residents and household 918's dwelling to shelter 44,
which belongs to site 1, while their residence and household ownership remain
site 2. Local housing reconciliation then rejects that foreign dwelling, and
the new three-person household leaves only one local bed for the two donors.
This is an illegal implicit housing move, distinct from the recorded physical
relocation support defect above.

Resident 452 remains unhoused for approximately 25.25 days before the exposure
death. The saved risk observations show no active unjoined visitor or reserved
guest bed during that interval. A long-running storehouse project, with 52 of
60 stone supplied and no construction work yet completed, also limits new
construction. That material constraint does not excuse the prior erroneous
cross-site housing selection. The narrow M17 correction requires the candidate,
donor household, all donor residents and destination shelter to share the same
settlement. Its foreign-site negative and same-site positive Release runtime
regressions pass. Both invoke the ordinary family-check body directly, write
before/after SQLite checkpoints, reopen, and compare a one-day continuation
advanced in one chunk versus 37-minute chunks. A separate frozen-V4 fixture
reproduces the uncorrected four-housed-to-two-unhoused transition after a birth.
Released legacy housing exchange tests retain their behavior. The other late
deaths have separate relocation evidence below.

The audited paired artifacts are in
`seed-17-100/exposure-fifth/housing-cause/` (canonical queued family check) and
`seed-17-100/exposure-fifth/reproductive-stage/` (controlled intermediate
helper). Their purposes and event ordering are recorded separately.
The independent regression proof and Release results are recorded in
`local-housing-shuffle/validation-summary.json`.

## Later whole-household relocation

A faithful late continuation proves four further seed-17 deaths in household
996. At its pre-departure minute 44,841,599, pre-arrival 44,841,717 and arrival
44,841,718, site 2 has eight free beds, split across shelters with at most two
free beds in any one. No dwelling fits the four-person family. The old support
gate nevertheless returns true for the actual incoming 324 food, 320 wood and
179 stone. All four residents retain their original home during travel and
become unhoused at physical arrival. Resident 565 dies at 44,878,320; residents
503, 1533 and 1578 die at 44,879,040. No active guest is present.

An existing storehouse needs thirteen more stone and has zero completed work,
so a replacement shelter cannot begin under the retained single-project policy.
After the parents' deaths, the two children briefly fit a two-bed gap but die
at the same survival boundary with health already exhausted. This directly
establishes fragmented support, delayed replacement and accumulated exposure;
it does not attribute every remaining death to that mechanism. A valid nearby
SQLite checkpoint plus the preserved authoritative V3 migration shadow reopens
and reproduces the arrival exactly. Its projection and raw-shadow requirements
are explicit in `exposure-fifth/relocation-fragmentation-v2/`.

The final three seed-17 deaths belong to household 1098: resident 591 at
48,504,960, 1601 at 48,506,400 and 1574 at 48,507,120. An exact saved-prefix
probe observes ten free destination beds at both pre-departure 48,470,399 and
pre-arrival 48,470,461, with at most two free beds in any dwelling and zero
guest reservations. The frozen-V3 support predicate accepts the actual incoming
324 food, 288 wood and 166 stone. Normal physical arrival at 48,470,462 clears
home 42 for all four residents, including surviving member 521. This is the
same aggregate-versus-whole-household support defect, proved at its actual
transition rather than inferred from later mortality.

An unfinished storehouse 1585, started at 44,882,280, retains 120 supplied wood,
32/60 stone and zero work. No new shelter completes across the fatal interval.
After the first two deaths, 521 and 1574 fit shelter 1121. Resident 521 reaches
it and survives with minimum observed health 80; 1574 remains at the stockpile
(122,149) and dies with the newly assigned home. A late assignment is again
insufficient physical recovery. No active guest overlaps these risk frames.

The exact probe writes a valid neutral pre-departure checkpoint and preserves
the authoritative migration shadow. A separate real SQLite close/reopen,
ordinary constructor and explicit shadow restoration reproduce the arrival's
citizens, households, Living, economy and migration exactly. Input, source,
binary and output hashes, including packaging-only failed starts, are retained
in `exposure-fifth/late-relocation-fragmentation/`. The annual mortality mirror
reproduces all eight later seed-17 deaths at their original IDs and minutes;
all annual census observations through year 100 match both original arms. The
complete diagnostic finishes at 51,840,000 with 115 residents, 51 natural deaths
and twelve exposure deaths. Its canonical persistence snapshot fingerprint is
`aa92aefd549ff58b333bc197ff05a41502ae23fcf903f9f9d7582cdc0538db8a`,
identical to both original acceptance arms; survival, settlement, social and
history fingerprints also match. The actual annual-100 checkpoint is reopened
from a private copy and inspected with the immutable V3 Headless comparer,
without gameplay advancement or another capture. This is final canonical
equivalence, beyond the earlier annual-observation parity. The separate raw DTO
JSON hash is retained, with evidence in
`seed-17-100/exposure-followon/final-compare/evidence.json` (SHA256
`8B68DF6A0A21C46A10B8AFC14B6E0C4B0133EBE7DB046F665E55F43E30BD2322`).

A bounded final-V5 check uses that same valid HH1098 pre-departure state as a
fixture. The corrected four-argument support gate rejects the actual goods and
ten fragmented vacancies. Normal advancement through 48,470,462 retains source
home 42 for all four residents. Snapshot restoration preserves survival, Living,
history and economy fingerprints with no rejection. This is direct final-build
gate evidence, not a claim that the fixture follows the original V3 trajectory
or establishes a century mortality counterfactual. Its separate provenance and
hashes are in `exposure-fifth/late-relocation-final-v5/`. The analogous bounded
HH996 saved-prefix check also rejects its actual goods and eight fragmented
vacancies, preserving source home 54 for all four residents through the old
arrival minute. It has matching restored fingerprints and no rejection, with
separate provenance in `exposure-fifth/hh996-relocation-final-v5/`.

All twelve original seed-17 exposure deaths are now individually classified:

| Residents | Household | Exact death minutes | Observed initiating housing condition |
| --- | ---: | --- | --- |
| 283 | 574 | 35,025,480 | Three-person relocation into gaps of at most two beds |
| 713, 843, 917 | 802 | 35,546,400; 35,547,480; 35,548,200 | Four-person relocation into gaps of at most two beds |
| 452 | 918 | 43,304,040 | Reproductive housing helper selects a foreign-site dwelling |
| 565, 503, 1533, 1578 | 996 | 44,878,320; 44,879,040 (last three) | Four-person relocation into gaps of at most two beds |
| 591, 1601, 1574 | 1098 | 48,504,960; 48,506,400; 48,507,120 | Four-person relocation into gaps of at most two beds |

The M17 feasibility and locality corrections address those initiating defects.
Construction/material delays and accumulated needs explain later outcomes in
these observed trajectories. This does not establish a baseline mortality
counterfactual, guarantee zero deaths or attribute every future exposure event
to one mechanism.

## Retained native partnership housing constraint

Frozen-V3 seed 7's two exposure deaths are residents 857 and 1060, both at
minute 47,696,040. Their local partnership creates household 1489 at site 2 at
47,672,377. They first appear unhoused at 47,672,400, with full health, and
remain so for about 16.43 days. There is no recent physical relocation. Site 2
has six vacant beds, each in a different dwelling: no whole dwelling fits the
new two-person household while existing household assignments are retained.

The sole active project is storehouse 1466, started at 46,419,120. Its supplied
wood remains 120/120, stone 0/60 and construction work 0/1,800 throughout the
fatal interval. There is no shelter project. Ordinary eating, resting, farm
and other work continue; sampled victim work claims have no blocked or resting
marker. Rest without a home does not relieve shelter need. Shelter reaches
10,000 and health eventually falls to lethal levels. Resident 1060's archived
home 430 is assigned only after 857 dies at that same survival boundary, so it
does not demonstrate an earlier opportunity for sheltered rest.

There is no active visitor or guest reservation during the fatal interval.
Independent review classifies this as the retained whole-household placement
and existing-home retention policy, combined with the nonpreemptive single
construction project and missing material. No residual newcomer transition
defect is established. The relocation and locality corrections do not claim
to prevent these deaths. Partnership housing, compaction or project priority
changes require a separate policy decision; absence of an active guest does
not exclude indirect effects of earlier admissions or their descendants.

The complete diagnostic reaches year 100 with 121 residents, 53 natural deaths
and two exposure deaths. All twenty annual observations and all four subsystem
fingerprints match the original run. Its full canonical persistence fingerprint
`128c28445a0f2efcf47cbca0dd6a00996bf946a822d0da72dc57d5e1a8cfa51e`
matches both original acceptance arms. The original final checkpoint remains
physically unchanged; the private postprocessing copy's EF lock/free-page byte
changes are recorded separately from canonical content and raw DTO serialization.
`seed-7-100/late-exposure-annual/final-canonical-comparison.json` has SHA256
`EB8B510ABD175C77342AF0EE627E714B16A40556690084EB4210B528DFC59A9F`.
The initially opened private year-80 prefix has a physical provenance caveat:
its recorded launch SHA256 is
`282CD71B9721AF6FC0123792BFA70C00D9151A30F1CAE35DF15FA76CD08BA6E0`,
whereas its current opened-file hash is
`D2C9037083661AE70E7952AB86EB0EF92B8AAA65D6B359CD1330FFCE6A59F4F8`.
That private prefix is not claimed byte-immutable. Initial-context observations
and final exact canonical parity establish the reproduced trajectory; the
original final-year-100 checkpoint's separate before/after physical hashes match.
`seed-7-100/late-exposure-annual/` preserves the reviewed facts, raw needs and
action traces, history, annual comparisons, valid risk/death checkpoints and
their authoritative migration shadows.

## Capture-dependent ownership

A separate frozen-V3 paired replay begins from a preserved diagnostic snapshot
at year 83.25. This controlled input comes from the earlier extra-observation
branch, not the original annual acceptance trajectory. At minute 43,187,045,
native resident 1024 has just become independent in household 1509 at site 2.
Ordinary persistence capture publishes that previously missing household owner.
Control, extra-capture, and capture-with-authoritative-state-restoration arms
have identical input. By year 83.5, extra capture changes survival, Living and
history fingerprints; restoring the exact prior migration reference after the
observational call preserves all three control results.

The economic divergence is observed at minute 43,199,765. A normal partnership between
797 and 1024 creates household 1510 at site 2 and empties household 1509. Without
an explicit retained owner, the empty household's fallback changes to site 1:
its 126 food and 20 wood take an inheritance disposition with no recipient.
With the earlier capture, its retained site-2 owner selects household 1510 for
the normal household merge. The new account differs by exactly those goods;
resident 797 chooses GatherFood versus Idle. This proves a real read-dependent
economic decision, beyond a metadata difference. It does not by itself assign
any particular exposure death to that decision.

M17 records ownership when an independent household actually forms and captures
ownership as a pure projection using the live site lookup rules. Completed and
cancelled work also retires ownership at the actual transition, so observation
is not required to bound the owner table. Older rules retain their original
capture behavior. Unit regressions and an actual saved-prefix paired check
verify pure capture, ordinary household ownership and equal differently observed
continuation from that same controlled input. The final frozen-V5 three-arm
proof also passes using a read-only copied master and a separately writable
runtime copy. Its complete initial canonical snapshot SHA256 is
`630042D290DF74C99BBD5E767E14B55E633F82B335FAA116C044354816233CEF`.

## Measured final-build exposure

The actual frozen-V5 seed-7 century records three exposure deaths. A bounded
replay from its real year-80 checkpoint, continued through separately saved
year-85 and year-88 states, identifies each initiating transition. These are
actual V5 cases, separate from the superseded V3 deaths above.

| Citizen | Death minute | Initiating local partnership | Housing consequence |
| ---: | ---: | ---: | --- |
| 822 | 43,958,160 | 43,936,917, household 1357 | Three residents lose their valid local homes; nine vacancies remain, with at most two in one shelter. |
| 1071 | 46,085,040 | 46,062,532, household 1430 | Two residents lose homes 1059/481; three vacancies remain across three shelters. |
| 1091 | 46,085,400 | Same partnership | Home 1059 is assigned only after 1071 dies; no physical home rest occurs before the next fatal survival check. |

Citizen 822 remains unhoused until death; the surviving two-person household
then fits shelter 441. Citizens 1071 and 1091 had earlier completed valid native
relocations, retained local homes and health 10,000, and lost those homes only
at their later local partnership. At 1071's death, 1091 has health 133 and receives
home 1059. During the remaining 360 minutes, 1091 stays idle at the stockpile;
Rest is only 2,840, below the existing 4,000 eligibility threshold, while Shelter
remains 10,000. A newly assigned home alone does not provide shelter relief.

Active storehouses 1337 and 1396 block another construction project. Their wood
is delivered, but stone advances only 13 to 23 of 60, and 23 to 33 of 60,
respectively; construction work remains zero. Each receives a limited ten-unit
claimed delivery after the partnership. Communal daughter stone is zero at both
pre-partnership states, but is ten at 1091's final death boundary; it is not zero
throughout the entire second window.
Positive geographically assigned stone nodes still exist, but all 50/59 local
residents have zero reachable positive stone nodes, no eligible stone gathering
target, and `CanProcureStone` is false. Storage has 149,804/119,999 free units, so
space is not the blocker. Separately owned household/order holdings contain
210/247 stone; this is not a claim that all settlement stone has disappeared.
The measured private household stocks have at most ten stone each: 21 of 22
households hold ten in the first case, and 24 of 29 hold ten in the later case,
with one more holding seven. The retained procurement policy requires a local
supplier to have more than ten available stone and preserves that supplier's
first ten. Trade reservations do not alter these available totals, and food and
payment capacity are ample. Protected private reserves are not pooled, so none
is an eligible construction supplier.

There is no active approaching, visiting or leaving guest, reservation or
concurrent migration party in either fatal transition. Guest/reservation audits
cover 709 and 762 distinct fine-grained frames and all ten exact boundaries.
Twenty annual observable comparisons for years 81-90 against both official
acceptance arms match census, food, wood, Living bytes and death counts.
The diagnostic's own year-85 canonical handoff and ordered-table logical hashes
match; official midpoint canonical hashes were not published, so this bounded
assessment does not claim a separate full-century canonical replay.

The sealed individual boundary/checkpoint and material-access proofs are in
`exposure-triage-v5-seed7/` and `exposure-triage-v5-seed7-85-90/`.
`exposure-triage-v5-seed7/final-assessment.json` binds the three causal records.
Independent behavioral review is separate from the nine horizon
invariant/equivalence passes. These cases retain the native partnership,
whole-household packing, single-project material access and physical-rest
limits. Changing household compaction, partnership housing preflight, project
preemption, stock reserves or rest eligibility requires a separate policy and
compatibility decision. No active-guest or reservation defect is identified in
these windows. No baseline mortality counterfactual or exclusion of indirect
demographic effects is claimed.

## Measured final-build starvation

The frozen-V5 seed-17 run records thirteen starvation deaths between days 116
and 118 of year 31, first at minute 16,236,720. A bounded continuation from its
actual year-8 checkpoint and a finer replay from year 31 reproduce all victim
IDs and exact death minutes. All thirteen victims belong to origin settlement 1
and have valid local homes. Their household food, accessible communal food and
eligible emergency donors are exhausted; they are idle or resting without work
orders or cargo. No active guest or migration party is present during the onset.

The existing weather policy produces drought for days 96-116. Every one of 385
wild-food nodes is empty after each daily drought pulse. The retained subtraction
is `max(1, RegenerationPotential / 4)`; for example, a maximum-stock-92 node with
potential 4,983 loses up to 1,245. Regeneration potential is normalized ecology
metadata; ordinary regeneration derives additions through seasonal basis points
and caps stock at each node's maximum. Origin communal food is zero on days 104-117.
Its eight grain are below the twenty-unit cooking/preservation batch, nine crops
are tending with no harvest ready, and ample storage cannot supply missing food.
Forage returns when fair weather resumes on day 117; gathering resumes by day
118, after most deaths. The daughter settlement's 61,830 food are physically
separate stock and do not establish origin access. Annual origin abundance after
recovery does not establish food availability during the lethal interval.

The drought formula is identical in the baseline. Independent review finds no
proven M17 logic or unit defect in these transitions; documentation and tests do
not establish whether total forage loss was the intended severity. This feature
retains that severe resource policy and records the measured deaths. Changing
severity or reserve policy requires a separate design and compatibility decision.
This is not a baseline counterfactual and does not exclude indirect effects of
the actual newcomer population or its descendants. The audited raw daily traces,
source, immutable input hashes and year-31/32 checkpoints are in
`starvation-triage-v5/run1/`.

## Canonical harvest checkpoint validation

Frozen V3 also rejects a legitimate autumn return checkpoint because the
agriculture validator requires the original stockpile even for site-2 workers.
The preserved year-80 continuation reproduces the rejection. Actual farm work,
the completed farm, food cargo and the route to the daughter stockpile are
coherent; this is a validator defect, not ghost work.

After correcting the target validation, the same class of state exposes a
second validator error: the old unified cargo cap accepts at most four grain
units, while a daughter load of 120 legitimately contains 108 food and twelve
grain. The M17 validator checks canonical order/farm/carrier ownership, exact
stockpile, site-specific capacity and a possible cumulative tenth-grain split.
It rejects oversized, impossible-split and wrong-site loads. Origin and legacy
caps remain four. The saved-state proof writes minute 41,744,160 to SQLite,
reopens it and obtains equal continuation to 41,745,600 with different chunk
sizes; wrong targets are rejected. Farm production is unchanged.

## Replay provenance

The original Headless harness captures once at each full year. Initial diagnostic
helpers captured at different frequencies and produced an altered branch;
those results are retained but excluded from actual causal conclusions. Corrected
helpers use the original annual capture cadence, reflection reads between those
boundaries, and exact authoritative-reference restoration for additional
checkpoint observations. Raw mappings accompany extra diagnostic checkpoints.
Annual observed fields are compared with the original acceptance logs before
using the traces. Original frozen source, binaries and checkpoints remain
unchanged.

Detailed fixtures, immutable proofs, JSONL traces, canonical snapshots and SHA256
manifests are local ignored artifacts under `artifacts/m17-acceptance/`. Both
seed-7 and seed-17 diagnostic end-to-end canonical parity pass. All nine final
V5 horizons also pass invariant and full canonical equivalence checks; their
measured deaths and causal assessments above remain distinct from those checks.
All three preserved V3 centuries finish
with invariant and canonical equivalence passes: seed 7 has 121 residents,
53 natural deaths and two exposure deaths; seed 17 has 115 residents, 51 natural
deaths and twelve exposure deaths; seed 42 has 105 residents and 47 natural
deaths. These are superseded ecological observations, not final behavioral
acceptance.
