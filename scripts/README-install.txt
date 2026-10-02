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
New worlds default to M17 visitors and newcomers. Existing worlds retain their
saved rules and history; upgrading does not add visitors to an older world.

To uninstall while preserving the world:

   .\uninstall.ps1

LAN access is for a trusted local network only. Do not expose Little Ages to
the public Internet. Little Ages has no built-in authentication or TLS.
