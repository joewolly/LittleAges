LITTLE AGES — WINDOWS INSTALLATION

1. Extract this ZIP.
2. Open PowerShell as Administrator.
3. Change to this folder.
4. Run:

   .\install.ps1 -EnableLan

5. Open the URL printed by the installer.

Your civilization is stored separately in:
C:\ProgramData\LittleAges\worlds

Running install.ps1 again upgrades Little Ages without deleting the world.
New worlds default to M17 visitors and newcomers. On restart, existing M14–M16
worlds automatically receive M17 features while retaining their civilization
and history. Older worlds continue under their supported saved rules.

The server creates verified SQLite backups under the data directory's
rules-upgrade-backups folder before supported schema or rules changes. These
backups are retained without automatic cleanup. Restoring an earlier binary
requires its matching pre-upgrade database backup.

To preserve saved rules, set AutoUpgradeWorldRules to false in appsettings.json
before restarting. The installer preserves this user-owned setting on upgrade.
NewWorldRules selects rules only when creating a world.

To uninstall while preserving the world:

   .\uninstall.ps1

LAN access is for a trusted local network only. Do not expose Little Ages to
the public Internet. Little Ages has no built-in authentication or TLS.
