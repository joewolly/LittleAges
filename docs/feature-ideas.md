# Little Ages feature ideas

This is a living list of things that could make Little Ages more interesting to
watch over many generations. It is a place to add, remove, combine, and reorder
ideas, not a release schedule or an implementation promise.

The test for each idea is: **what new behavior or story could emerge from it?**
Ideas that change simulation rules need their own versioned ruleset so existing
worlds keep their history and behavior. Observer features should report facts
from the simulation without inventing events.

## History worth revisiting

- **AI historian and historical questions.** Ask what happened to a person,
  family, or settlement and get an answer grounded in recorded events, with
  links back to the evidence. Keep generated prose outside canonical history.
- **Periodic newspaper.** Publish a readable account of births, deaths,
  discoveries, shortages, and changes in settlement life for each season or
  year. Different periods would feel distinct when revisited later.
- **Family tree and lineage view.** Follow descendants across generations and
  see how homes, occupations, relationships, and possessions changed.
- **Time travel for observers.** Scrub through saved historical snapshots or
  reconstructable events to see how the map and settlement changed. This needs
  an honest distinction between recorded state and anything inferred.
- **Places with memory.** Show what happened at a house, field, road, or ruin,
  so the landscape itself becomes a way to browse history.

## People and society

- **Traditions and festivals.** Repeated shared experiences could become local
  customs that affect social bonds, work, and how later generations remember a
  place.
- **Apprenticeship and schools.** Skilled citizens could teach particular
  crafts to younger people, changing what a settlement can make after its
  founders die.
- **Leadership and councils.** Citizens could coordinate communal choices in
  response to shortages, growth, or disputes, with consequences that persist.
- **Disputes and reconciliation.** Property, work, and relationships could
  produce conflicts that citizens resolve, ignore, or pass on to descendants.
- **Illness and care.** Contagion, recovery, and care capacity could reshape
  households and work without reducing health to a random death roll.

## A larger, changing world

- **Migration and second settlements.** Implemented in [M14](m14-migration.md),
  with one autonomous daughter settlement, seasonal household migration, and
  family visits. New worlds use M14 by default; existing saves keep their
  recorded rules.
- **Roads and trade routes.** Repeated travel could justify paths and exchange
  between settlements, making geography matter to prosperity and contact.
- **Visitors and newcomers.** Travelers could bring skills, goods, and stories
  from beyond the starting settlement, then choose whether to stay.
- **Disasters and recovery.** Fires, floods, or severe seasons could destroy
  useful structures and create a visible recovery story shaped by preparation.
- **Changing landscape and ruins.** Abandoned buildings, depleted areas, and
  rebuilt sites could leave physical evidence of earlier generations.

## Optional player involvement

- **Carefully bounded interventions.** An optional God Mode could trigger a
  drought, resource discovery, or unusual event. Each intervention would become
  a canonical historical fact so the resulting world remains explainable.

## Editing this list

Add an idea with one sentence about the behavior it could create. Move an idea
to a separate design document once its rules, save compatibility, observer
presentation, and acceptance criteria are ready to decide. Remove or merge
ideas freely as the simulation reveals what is actually fun to watch.

The preserved [v0.1 design baseline](design-v0.1.md) and the
[Living Settlement design](living-settlement-v0.2.md) contain earlier future
directions. Some of their proposed features, including property, barter, and
inheritance, are already implemented; the [README](../README.md) is the current
feature summary.
