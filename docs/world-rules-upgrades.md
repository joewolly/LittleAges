# Automatic feature upgrades for existing worlds

After installing a release and restarting the server, `AutoUpgradeWorldRules`
defaults to `true`. It is independent of `NewWorldRules`, which only selects the
rules of a newly created world. Set the boolean to `false` in server configuration
to continue existing worlds under their saved rules.
Pending schema changes to any supported M14–M17 world require a backup, including
M17 worlds and preservation mode. Current rules and preservation mode do not
create another backup when no schema change is needed.

The initial converter registry supports:

`m14-rng1-migration1` → `m15-rng1-roads1` → `m16-rng1-planned1` →
`m16-rng1-festivals1` → `m17-rng1-newcomers1`.

A world may enter at any stage and skip application releases. Each source and
converted checkpoint is validated. The complete chain is prepared in memory and
committed once, before simulation advancement or a Running status. Subsequent
restarts are no-ops. Ordinary database loading and headless commands continue to
honor their existing compatibility rules; they do not opt into this host policy.

## What is preserved

Conversion does not advance time or regenerate citizens, terrain, families,
holdings, buildings, resources, or recorded history. Roads start without invented
historical wear. M14 journeys retain the old pathfinder's remaining route; M15
routes are retained. Planned districts and surplus sharing govern future work;
existing buildings remain where they are.

Festivals and newcomer opportunities begin at the next scheduled occurrence
after activation. Existing festival state, including active attendance and
reserved food, survives the M16 festivals conversion. Legacy farm-return journeys
that need a local destination under M17 retain their position, cargo, and event
identity, but receive a physical route and revised movement timing to their
resident stockpile. All other scheduled identities and timing are preserved.

## Backups, receipts, and failure

Before eligible schema or rules changes, the host creates a consistent SQLite
backup containing committed WAL pages, checks integrity and foreign keys, and
records a SHA-256 hash. Backups are retained under `rules-upgrade-backups` beside
the active database, without automatic deletion. Preserve enough disk space.
Each backup includes a `manifest.json` hash/reference for independent verification.

The dedicated upgrade write path verifies the committed source against the
planned source and revalidates the registered conversion. It writes the final
checkpoint and an operational `world_rules_upgrades` receipt in one transaction.
Receipts contain source/target rules, activation minute, converter versions,
checkpoint fingerprints, backup path/hash, and UTC commit time. They are outside
canonical simulation history; creation metadata and saved-world identity persist.
Ordinary checkpoints still cannot change recent simulation rulesets.

The host attempts once per startup. If backup, conversion, or transaction work
fails, it reloads the committed checkpoint and resumes the old rules only when
the original checkpoint validates and matches. A fully committed target can be
recognized from its fingerprint and matching receipt. Corruption, unsupported
future formats, and uncertain commit outcomes stop startup. Cancellation stops
startup; an uncommitted transaction rolls back.

`/api/v1/status` includes `simulationRulesVersion` and `rulesUpgrade`. Outcomes are
`Current`, `Preserved`, `Upgraded`, `Failed`, or `Unsupported`. The observer shows
a concise notice for failed or unavailable upgrades. Logs include diagnostics
and the backup path. Worlds older than M14 continue their supported behavior and
report that this path is unavailable.

## Release and recovery requirements

Future default simulation rules must register an explicit converter from the
previous default and pass preservation, deterministic continuation, and SQLite
reload tests before becoming the default. Observer/UI releases apply immediately.
Terrain-generation changes preserve saved maps; features needing new geography
must supply an additive conversion.

Test releases against consistent disposable copies of stored worlds before
installation. Never upgrade installed originals during validation. Publication
and installation are separate actions. Restoring an earlier binary requires the
matching pre-upgrade database backup; application rollback alone is insufficient.
See [backup and recovery](backup-and-recovery.md) and
[Windows service upgrades](windows-service.md). Implementation evidence is in
[the acceptance record](world-rules-upgrades-acceptance.md).
