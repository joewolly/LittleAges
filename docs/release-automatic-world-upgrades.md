# Automatic world upgrades — unreleased notes

Existing civilizations can now receive new simulation features after installing
a release and restarting the server. The first supported path upgrades M14
through roads, planned construction, festivals, and M17 newcomers. Time, terrain,
people, families, existing buildings, holdings, and factual history are preserved.

`AutoUpgradeWorldRules` defaults to `true`. Set it to `false` to retain saved
rules. `NewWorldRules` continues to select rules only for newly created worlds.
Worlds older than M14 retain their supported behavior and display an unavailable
upgrade notice. UI improvements continue to apply through application releases.

Before eligible changes, the server retains a SQLite backup with committed WAL
contents and verifies integrity and SHA-256. Conversion and its operational
receipt commit together. Failed preparation or conversion resumes the validated
original checkpoint; uncertain outcomes and corruption stop startup. The observer
shows a concise failure notice while the world continues under old rules.

Backups under `rules-upgrade-backups` are retained without automatic cleanup.
Downgrading the application requires its matching pre-upgrade database backup;
restoring an earlier binary alone cannot reverse a committed rules conversion.
See [upgrade behavior](world-rules-upgrades.md),
[backup and recovery](backup-and-recovery.md), and
[Windows installation](windows-service.md).

Publication and installation are separate actions. Release validation must use
consistent disposable copies of stored worlds and migrated seed-17/seed-42
century acceptance. Future defaults require an explicit converter from the prior
default and passing preservation, continuation, and SQLite reload checks.
See the [implementation acceptance record](world-rules-upgrades-acceptance.md).
