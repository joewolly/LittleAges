# Seed 7 exposure diagnosis before newcomers

The released M16 planned-layout rules with household surplus sharing reproduce the documented ten-year result: 25 residents and eight exposure deaths. A daily observational replay shows a housing-demand defect, rather than an exposure balance problem.

Housing reconciliation retains valid homes and packs whole households into a single shelter (four residents). Settlement demand checks total shelter capacity against local population, and a spare-capacity check only for households otherwise ready to reproduce. A newly combined household can consequently remain unhoused while spare beds are spread across occupied homes. No shelter project is ordered. Rest away from an assigned completed home reduces tiredness but provides no shelter relief, so exposure accumulates until death. The surviving smaller household can then fit an existing home, hiding the failure in monthly census samples.

The first example is household 71, citizens 1 and 7. At day 480 population is 24 and completed capacity is 28. Shelter occupancies are 4,3,3,3,3,3,3: six spare beds but no two-bed opening. Both citizens have no home, shelter need 10,000, and there is no construction project. Citizen 1 dies at minute 703,440; citizen 7 subsequently fits a vacant slot.

Later deaths have the same demonstrated failure. Household 57 (3,16,60,79) has no assigned home on days 2083–2085, critical shelter need, and no construction project; citizen 3 dies at minute 3,002,760. Household 105 (16,20,60,79) similarly remains unhoused on days 2118–2122, and citizen 20 dies at minute 3,056,040. A globally empty shelter does not establish local availability: each settlement owns its own shelters. Monthly observations on days 2070 and 2100 miss these intervals.

The correction is gated to `m17-rng1-newcomers1`: settlement demand orders a shelter whenever a living local resident has no home. It preserves stable whole-household assignments, shelter size, costs, exposure damage, and every M16 and earlier rule path. It also leaves the existing one-project-per-settlement construction policy intact. Focused fixture tests demonstrate fragmented spare beds, the unchanged M16 decision, the M17 demand, and whole-family assignment once the new shelter completes (three passing cases). The corrected fixture also verifies that an active storehouse project is preserved, that a shelter is selected after it finishes, and that an unfinished shelter does not produce a duplicate order.

The surplus-sharing rule changes production, gathering, construction and later household histories. It exposes an existing blind spot in the aggregate capacity check; it does not directly change shelter assignment or exposure damage. Exact attribution of each trajectory difference to a particular surplus deposit is not established by this observation.

The final five M16 deaths have the same mechanism. Household 63 (6,10,64,86) is unhoused at the daughter settlement on day 3352 with shelter need 10,000 and no construction project; citizens 10,6,86 die at minutes 4,827,600 and 4,829,760. Household 69 (9,17,78) is unhoused on day 3442 while a storehouse project has only 20 of 120 wood, no stone, and no work. Citizen 9 dies at minute 4,957,920. The remaining two members then fit home 115, but citizen 78 has only 200 health and critical shelter need; death follows at minute 4,958,640 before recovery.

An isolated replay of the same M16 rules with only the household surplus cap disabled reproduces the latent defect: household 85, citizens 18 and 19, has no home on days 1213–1215, seven spare beds distributed one per shelter, critical shelter need and no project. Both die at minute 1,751,040. Thus the no-sharing case also contains the same housing bug.

Full detailed replay and isolated no-sharing comparison are recorded in ignored diagnostic artifacts. The pre-newcomer M17 run finishes ten years (minute 5,184,000) with 35 residents and zero deaths. This run enables both the housing correction and festivals, whereas the M16 planned comparator does not enable festivals. It therefore does not isolate the housing correction.

The strictly isolated diagnostic M16 planned replay, with sharing enabled, festivals disabled, and only the housing-demand predicate changed, also finishes minute 5,184,000 with 36 residents and 0 deaths. This establishes the correction independently of festivals and newcomers. The single-line copied-source patch, source comparison, SHA256 source/binary manifests, frozen binary, and daily trace are recorded under artifacts/exposure-diagnosis. The detailed M16 run finishes with 25 residents and eight exposure deaths. The no-sharing comparison also finishes at minute 5,184,000 with 34 residents and two exposure deaths, independently reproducing the published comparison. See `artifacts/exposure-diagnosis/comparison.json`, `exposure-deaths.json`, the four detailed JSONL traces, and `housing-tests.log`. These are observational runs; full checkpoint/reload acceptance is performed separately.

The exact fetched-main M16 festivals default is a separate control. Its seed-7
ten-year run finishes with 35 residents and zero deaths, and every compared
fingerprint and full-state hash matches the feature build under those legacy
rules. The eight-death result above concerns the retained M16 planned rules,
not this default festivals trajectory. The isolated housing correction remains
the matched 25/8 to 36/0 comparison.
