# Windows Service deployment and operation

M7 supports the same `LittleAges.Server` executable in an interactive console and as a Windows Service. The service owns the simulation and SQLite writer; a browser is optional. These procedures assume an elevated 64-bit PowerShell prompt for installation, ACL, and firewall commands.

## Publish one deployable directory

From the repository root, with the .NET SDK 10.0.100-compatible toolchain and Node.js 22/npm available, run:

```powershell
.\scripts\publish-windows.ps1 -OutputDirectory .\artifacts\windows-publish
```

The script verifies `dotnet`, `node`, and `npm`; runs `npm ci` and `npm run build` in `src\LittleAges.Web`; restores the server project in locked mode for `win-x64`; and runs a Release, framework-dependent `dotnet publish` for `src\LittleAges.Server\LittleAges.Server.csproj`. It then copies the Vite `dist` contents into the published `wwwroot` and replaces only the dedicated output directory with one clean deployment. Do not copy a source checkout or a separate `dist` directory into production.

The publish is framework-dependent, so install the matching .NET 10 runtime on the service host. The output includes the server executable and all of its published assemblies; the frontend is served by the server's static-file middleware.

## Deployment and data folders

Use a read-only deployment folder and a separate writable world-data folder:

```powershell
$deployDir = Join-Path $env:ProgramFiles 'LittleAges'
$dataDir = 'C:\ProgramData\LittleAges\worlds'
$publishDir = (Resolve-Path -LiteralPath '.\artifacts\windows-publish').Path

New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
Get-ChildItem -LiteralPath $publishDir -Force | Copy-Item -Destination $deployDir -Recurse -Force
```

The active database is `$dataDir\default-world.db` when `ActiveWorld` is `default-world`. Keep the deployment and data folders separate so an application update cannot overwrite world state. SQLite may create `-wal` and `-shm` sidecars beside the database; follow [`backup-and-recovery.md`](backup-and-recovery.md) before copying or restoring them.

## Configuration

The complete example is [`windows-service.example.json`](windows-service.example.json). Copy it to the deployment directory as `appsettings.json`, or provide equivalent command-line settings in the service `BinaryPathName`:

```powershell
Copy-Item -LiteralPath '.\docs\windows-service.example.json' -Destination (Join-Path $deployDir 'appsettings.json') -Force
```

The example uses the safe loopback default `http://127.0.0.1:5274`. Its operational defaults are:

| Key | Default |
| --- | ---: |
| `SimulationMinutesPerSecond` | `10` |
| `CheckpointSimulationMinutes` | `360` simulated minutes |
| `CheckpointMinimumRealSeconds` | `30` seconds |
| `CheckpointRetryCount` | `3` retries after the initial attempt |
| `CheckpointRetryDelaySeconds` | `2` seconds |
| `BrowserUpdateIntervalMilliseconds` | `500` milliseconds |
| `Logging:LogLevel:Default` | `Information` |

`DataRoot`, `ActiveWorld`, `WorldSeed`, and `ListenUrls` are also required configuration concepts; the server defaults to a data directory below its application base directory, `default-world`, seed `0`, and `http://127.0.0.1:5274` when they are not supplied. Setting `SimulationMinutesPerSecond` to `0` disables operational advancement. The rules version remains `m6-rng1-history1`.

## Install as LocalService and start automatically

The following uses the service name `Little Ages`, matching the hosting configuration. It first creates the service with `New-Service`, then sets the built-in `LocalService` account and automatic startup explicitly with `sc.exe`:

```powershell
$serviceName = 'Little Ages'
$serviceExe = Join-Path $deployDir 'LittleAges.Server.exe'

New-Service -Name $serviceName `
    -DisplayName $serviceName `
    -Description 'Persistent Little Ages simulation host' `
    -BinaryPathName ('"{0}"' -f $serviceExe) `
    -StartupType Automatic

sc.exe config $serviceName obj= 'NT AUTHORITY\LocalService' password= '' start= auto
sc.exe qc $serviceName
```

Grant the service account Modify access only to the world-data folder. Do not grant it write access to the deployment folder:

```powershell
icacls $dataDir /grant 'NT AUTHORITY\LOCAL SERVICE:(OI)(CI)M' /T
```

The ACL is required because `LocalService` must create/open the SQLite database and its WAL sidecars. Confirm the service executable and data path before starting.

## Loopback and trusted-LAN access

Keep `ListenUrls` at `http://127.0.0.1:5274` unless remote observers are required. For a trusted private LAN, explicitly change it to:

```json
"ListenUrls": "http://0.0.0.0:5274"
```

Binding `0.0.0.0` is not an access-control policy. If remote access is needed, add a separately reviewed Windows Firewall rule on the `Private` profile and replace the example subnet with the actual remote subnet:

```powershell
$remoteSubnet = '192.168.50.0/24'
New-NetFirewallRule -DisplayName 'Little Ages (Private TCP 5274)' `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5274 `
    -Profile Private -RemoteAddress $remoteSubnet
```

Do not use `-RemoteAddress Any` for this service. The application does not create or alter firewall rules. Little Ages has no built-in authentication or TLS and is not designed for public Internet exposure; treat a LAN binding as trusted-network-only.

## Start, stop, and restart

```powershell
Start-Service -Name 'Little Ages'
Get-Service -Name 'Little Ages'
Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:5274/api/v1/health'

Stop-Service -Name 'Little Ages'
Get-Service -Name 'Little Ages'

Restart-Service -Name 'Little Ages'
```

An ordinary stop drains queued commands and attempts a final checkpoint before closing SQLite. Treat a stop as complete only after the service reports `Stopped` and the logs show a successful final checkpoint. A crash, forced termination, or power loss cannot provide that final-checkpoint guarantee; on restart the host validates and resumes the last committed checkpoint. Suspension and downtime do not produce wall-time catch-up.

## Logs and troubleshooting

The server emits structured JSON console logs for startup, world open/resume, checkpoint attempts/completions/failures, host state, and cleanup. When diagnosing interactively, stop the service and run the same executable in a console so the JSON records remain visible:

```powershell
& $serviceExe --DataRoot $dataDir --ListenUrls 'http://127.0.0.1:5274' --ActiveWorld 'default-world' --WorldSeed '0'
```

When installed as a service, inspect the Windows Application event log if the host environment is configured to capture service logs, and use the service manager plus the health endpoint as the primary liveness checks. `/api/v1/status` reports `State`, `PersistenceState`, `LastSuccessfulCheckpointWorldMinute`, `LastSuccessfulCheckpointUtc`, and `ConsecutiveCheckpointFailures`.

Common checks:

- `Starting`/`Stopping` health is Degraded, `Running` is Healthy, and `Faulted` is Unhealthy. A fault is retained and is not silently rewritten as a normal stop.
- If startup fails, check JSON syntax, the absolute `DataRoot`, the `LocalService` ACL, the selected `ActiveWorld`, and whether another process owns the database.
- If the port is unavailable, inspect `Get-NetTCPConnection -State Listen -LocalPort 5274` and the configured `ListenUrls`; do not broaden the firewall rule as a diagnostic shortcut.
- Periodic checkpointing requires both `360` simulation minutes and `30` real seconds. A failed attempt marks persistence Degraded, waits the configured two seconds between bounded retries, and becomes Faulted after the configured three retries are exhausted.
- Browser or SignalR disconnects do not stop simulation. Reconnect the observer and refetch authoritative `GET /api/v1/*` REST reads; `/hubs/world` is only a coalesced `worldChanged` invalidation channel.
- Do not delete WAL files, run repair/vacuum commands, or regenerate a database to clear an error. Preserve the current files and follow the approved backup/restore procedure.

## Uninstall

Stop the service normally first so it can attempt its final checkpoint, then remove the service registration. Preserve the data folder unless deletion is explicitly intended:

```powershell
Stop-Service -Name 'Little Ages'
sc.exe delete 'Little Ages'

# Only if this exact rule was created for Little Ages:
Remove-NetFirewallRule -DisplayName 'Little Ages (Private TCP 5274)'

# Remove the deployment directory only after retaining the world data:
Remove-Item -LiteralPath $deployDir -Recurse -Force
```

`C:\ProgramData\LittleAges\worlds` is not removed by uninstall. Archive it using the backup runbook before any intentional cleanup.
