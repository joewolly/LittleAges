# Windows Service deployment and operation

The supported v0.1 deployment is a self-contained `win-x64` directory plus a
Windows Service. The release ZIP contains the application, static site,
installer, uninstaller, and this workflow's equivalent package contents. The
target host does not need Git, Node.js, the .NET SDK, or a .NET runtime.

## Recommended release installation

Download and extract `LittleAges-v0.1.0-win-x64.zip`, open an Administrator
PowerShell in the extracted folder, and run:

```powershell
.\install.ps1 -EnableLan
```

The installer validates `LittleAges.Server.exe` and `wwwroot\index.html` before
stopping an existing service. It creates:

```text
C:\Program Files\LittleAges
C:\ProgramData\LittleAges\worlds
```

The application is installed as `Little Ages`, starts automatically as
`NT AUTHORITY\LocalService`, and receives Modify access only to the world-data
directory. The installer writes `appsettings.json` in the application folder
with the current server defaults, an active `default-world`, seed `0`, a
10-minute-per-second operational speed, and the selected listener.

Without `-EnableLan`, the listener is loopback-only:

```text
http://127.0.0.1:5274
```

With `-EnableLan`, it listens on `http://0.0.0.0:5274` and the installer owns
one inbound Windows Firewall rule for TCP 5274 on the `Private` profile, scoped
to `LocalSubnet`. The installer prints a private IPv4 address for convenience;
that address is display-only and is not the security boundary. LAN mode is for
a trusted local network only. Little Ages v0.1 has no built-in authentication
or TLS and must not be exposed to the public Internet.

The installer starts the service and waits for both:

```text
GET http://127.0.0.1:5274/api/v1/health
GET http://127.0.0.1:5274/api/v1/status
```

to report a running, healthy host before reporting success.

## Upgrade and data safety

Extract the new ZIP and run the installer again from that folder:

```powershell
.\install.ps1 -EnableLan
```

The package is validated first. The existing service is stopped and waited on,
the old application directory is moved to a temporary rollback backup, and the
new directory is installed. If copying, service startup, or health verification
fails, the old application and service are restored and restarted when they
were previously running. A failed upgrade never regenerates or repairs a
world. The temporary application backup is removed only after successful
health/status verification.

World data is never under Program Files. The installer preserves the configured
`DataRoot`, `ActiveWorld`, `WorldSeed`, operational speed, checkpoint settings,
browser update interval, and user-owned configuration such as logging when
upgrading. Installer arguments explicitly supplied on the command line take
precedence. The normal installer mode is loopback; pass `-EnableLan` on an
upgrade when LAN access is wanted.

If the data directory already contains a world, the installer reports it as
preserved and lets the server validate/open it normally. It never deletes,
initializes, or regenerates existing world files. SQLite `*.db`, `*.db-wal`, and
`*.db-shm` files remain in the separate data directory.

Useful supported installer parameters are:

```text
-EnableLan
-Port <1..65535>
-InstallDirectory <path>
-DataDirectory <path>
-ServiceName <name>
-ActiveWorld <name>
-WorldSeed <integer>
-SimulationMinutesPerSecond <0..1000>
```

The installer does not expose simulation rules as an option. Fresh worlds use
the current `m8-rng1-balance1` rules boundary; M6 remains a compatibility
boundary for existing persisted worlds.

## Uninstall

Run from an Administrator PowerShell in the extracted package (or use a copy
of the uninstaller):

```powershell
.\uninstall.ps1
```

This stops and removes the `Little Ages` service, removes the application
files, and removes only the installer-owned private-LAN firewall rule. World
data is preserved and the script prints:

```text
World data preserved at: C:\ProgramData\LittleAges\worlds
```

Deleting world data is never part of normal uninstall. It requires both
explicit switches:

```powershell
.\uninstall.ps1 -DeleteWorldData -ConfirmWorldDeletion
```

The uninstaller rejects `-DeleteWorldData` without the second confirmation.

## Build and package a candidate

The build machine needs .NET SDK `10.0.100`, Node.js 22, and npm. From the
repository root, build the exact release shape with:

```powershell
.\scripts\package-windows.ps1 -Version 0.1.0
```

This invokes the shared publish script, runs the frontend build, publishes a
self-contained `win-x64` directory (without single-file, trimming, or
NativeAOT), adds `install.ps1`, `uninstall.ps1`, and `README-install.txt`, and
writes:

```text
artifacts\LittleAges-v0.1.0-win-x64.zip
```

The ZIP has this root layout:

```text
LittleAges-v0.1.0-win-x64\
├── LittleAges.Server.exe
├── required self-contained runtime/application files
├── wwwroot\
├── install.ps1
├── uninstall.ps1
└── README-install.txt
```

The manual `workflow_dispatch` workflow `.github/workflows/windows-package.yml`
uses the exact SDK and Node 22, runs this same package script, and uploads the
ZIP as a workflow artifact. It does not publish a GitHub Release and is not part
of normal pull-request CI.

## Advanced/manual service operation

The commands below are for diagnostics or a hand-built deployment. The release
installer is preferred for normal installation.

### Publish one deployable directory

```powershell
.\scripts\publish-windows.ps1 -OutputDirectory .\artifacts\windows-publish
```

The script verifies `dotnet`, `node`, and `npm`, runs `npm ci` and `npm run
build` in `src\LittleAges.Web`, restores the server project for `win-x64`, and
runs a Release self-contained directory publish for
`src\LittleAges.Server\LittleAges.Server.csproj`. It then copies the Vite
`dist` contents into the published `wwwroot`. It does not enable single-file
publishing, trimming, or NativeAOT.

### Manual service registration

```powershell
$deployDir = Join-Path $env:ProgramFiles 'LittleAges'
$dataDir = 'C:\ProgramData\LittleAges\worlds'
$serviceName = 'Little Ages'
$serviceExe = Join-Path $deployDir 'LittleAges.Server.exe'

New-Service -Name $serviceName `
  -DisplayName $serviceName `
  -Description 'Persistent Little Ages simulation host' `
  -BinaryPathName ('"{0}"' -f $serviceExe) `
  -StartupType Automatic

sc.exe config $serviceName binPath= ('"{0}"' -f $serviceExe) start= auto `
  obj= 'NT AUTHORITY\LocalService' password= ''
icacls.exe $dataDir /grant:r 'NT AUTHORITY\LOCAL SERVICE:(OI)(CI)M' /T /C
```

Use `Start-Service`, `Stop-Service`, `Get-Service`, and the health/status URLs
above for bounded operational checks. Do not grant the LocalService account
write access to the application directory and do not broaden a firewall rule
to `Any` or the Public profile.

### Configuration reference

The generated configuration contains:

```text
DataRoot
ActiveWorld
WorldSeed
ListenUrls
SimulationMinutesPerSecond
CheckpointSimulationMinutes
CheckpointMinimumRealSeconds
CheckpointRetryCount
CheckpointRetryDelaySeconds
BrowserUpdateIntervalMilliseconds
```

Current defaults are `10` simulation minutes per second, checkpoints every
`360` simulated minutes subject to `30` real seconds, three retries with a
two-second delay, and observer invalidation every `500` milliseconds. These are
operational values, not simulation-rule controls.

### Logs and troubleshooting

The server emits structured JSON console logs for startup, world open/resume,
checkpoints, host state, and cleanup. If startup fails, inspect the JSON
configuration, absolute `DataRoot`, LocalService ACL, selected world, and port
ownership. Do not delete WAL files, regenerate a database, or broaden the
firewall as a diagnostic shortcut. Preserve the current world files and follow
[`backup-and-recovery.md`](backup-and-recovery.md) for approved backup/restore.
