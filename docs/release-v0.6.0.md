# Little Ages v0.6.0 - Visitors and newcomers

New worlds now use `m17-rng1-newcomers1`. M17 builds on M16 planned villages,
roads, trade and festivals, adding rare adult outsiders who physically arrive
from a reachable map edge, visit an inhabited settlement, and decide whether
to join or leave. Only one guest can be active worldwide at a time.

Guests use their own provisions and a spare completed shelter. They make real
social contact but cannot work, teach, partner or have children while visiting.
Admission checks actual relationships, personality, food, shelter and goods
capacity again. A newcomer who joins receives one household and account and
participates in the existing work, teaching, family and inheritance systems.
External origins and unknown outside parents are recorded factually; descendants
link to the same stable identity. Departure is distinct from death, and departed
people remain inspectable as last observed alive.

The painted observer shows physical arrival, stays and departure, visitor
badges, separate resident and guest counts, factual timelines, and select/follow
controls. Movement remains server-authoritative through pause, high speed,
reduced motion, live-stream recovery and map fallback.

M17 also fixes proven housing defects: fragmented spare beds no longer suppress
needed resident shelter construction; local household housing changes remain
within their settlement; relocation checks whole-household support before
departure and on arrival, physically returning if support is lost. These fixes
are versioned under M17. Exposure damage, construction priorities, drought and
food policies retain their existing calibration.

## Existing worlds and upgrades

Installing v0.6.0 preserves world data and recorded rules, behavior and history.
It does not migrate M16 or earlier worlds to M17. An explicit `NewWorldRules`
configuration still selects the requested rules for a fresh world; saved rules
take precedence when an existing world is reopened. The server, headless commands
and public fresh-engine constructor select M17 when no rules are supplied.

Download and extract `LittleAges-v0.6.0-win-x64.zip`, then run the included
`install.ps1` as described in [Windows operations](windows-service.md).
The package is self-contained and includes the observer and installer.

## Validation and limits

The [M17 validation report](m17-validation.md) records all six requested ten-year
seeds and three century seeds, with chunk-size and SQLite reopen parity,
balanced provisions and all 33 invariants passing. The default promotion
changes declarations only, so these completed explicit-M17 horizons remain
valid. Focused tests cover fresh defaults and existing M16 SQLite saves.

Mortality is measured separately from invariants. Century seed 7 had three
exposure deaths and seed 17 had thirteen starvation deaths under retained
native housing, construction and drought policies; the report explains the
traces and remaining policy limits. This release does not rebalance those
policies or guarantee extinction rescue. There are no caravans, third settlement,
return visits, off-map economy, player admission controls or new technology tree.

Browser coverage includes visitor presence and archives, select/follow,
pause/high speed, reduced motion, stream recovery, map fallback and mobile
layout. Admission, death and native housing edge cases have backend coverage;
they were not all exercised interactively. If every Canvas2D context is denied,
both map renderers are blank while the observer controls remain usable.
