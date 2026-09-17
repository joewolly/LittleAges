# Backup and recovery

Little Ages persists one authoritative SQLite checkpoint database per active world. The preferred backup is an offline copy made after a graceful stop and a confirmed successful final checkpoint. Backups are operator-managed; the application does not upload, synchronize, repair, or guess at a damaged database.

## SQLite WAL caveat

SQLite uses WAL mode. While the host is running, `default-world.db-wal` and `default-world.db-shm` may sit beside `default-world.db`. Copying only the main `.db` file while a writer is active can omit committed pages that are still in the WAL and can produce an unusable or stale backup. Never delete WAL files to make a copy appear simpler. Preserve the database and any sidecars as a set, or use the approved SQLite online-backup option below.

## Preferred offline backup

Replace `$backupRoot` with a local, operator-controlled directory that is not a cloud or synchronization folder. The sequence is intentionally stop → successful final checkpoint → copy the complete matching file set into one timestamped directory → verify it → restart. A backup directory contains `default-world.db` and, when present, both of its `default-world.db-wal` and `default-world.db-shm` sidecars.

```powershell
$ErrorActionPreference = 'Stop'
$serviceName = 'Little Ages'
$dataDir = 'C:\ProgramData\LittleAges\worlds'
$dbPath = Join-Path $dataDir 'default-world.db'
$walPath = "$dbPath-wal"
$shmPath = "$dbPath-shm"
$backupRoot = 'D:\LittleAgesBackups'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $backupRoot ("default-world-$stamp")
$backupDbPath = Join-Path $backupPath 'default-world.db'
$backupWalPath = Join-Path $backupPath 'default-world.db-wal'
$backupShmPath = Join-Path $backupPath 'default-world.db-shm'

Stop-Service -Name $serviceName
$service = Get-Service -Name $serviceName
if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
    throw "Refusing to copy while the service is not stopped: $($service.Status)"
}
# Confirm a successful final checkpoint in the logs before continuing.
if (-not (Test-Path -LiteralPath $dbPath -PathType Leaf)) {
    throw "The authoritative database was not found: $dbPath"
}
$walExists = Test-Path -LiteralPath $walPath -PathType Leaf
$shmExists = Test-Path -LiteralPath $shmPath -PathType Leaf
if ($walExists -xor $shmExists) {
    throw "Refusing to copy an incomplete WAL set. Preserve the database and investigate the single remaining sidecar."
}

New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
if (Test-Path -LiteralPath $backupPath) {
    throw "Refusing to overwrite an existing backup directory: $backupPath"
}
New-Item -ItemType Directory -Path $backupPath | Out-Null
Copy-Item -LiteralPath $dbPath -Destination $backupDbPath
if ($walExists) {
    Copy-Item -LiteralPath $walPath -Destination $backupWalPath
    Copy-Item -LiteralPath $shmPath -Destination $backupShmPath
}
if (-not (Test-Path -LiteralPath $backupDbPath -PathType Leaf) -or
    (Test-Path -LiteralPath $backupWalPath -PathType Leaf) -ne $walExists -or
    (Test-Path -LiteralPath $backupShmPath -PathType Leaf) -ne $shmExists) {
    throw "The backup directory does not contain the complete matching database/WAL set: $backupPath"
}

Start-Service -Name $serviceName
Get-Service -Name $serviceName
```

Do not copy until the service is stopped and the final-checkpoint success is visible in the logs. The script intentionally leaves the service stopped if any source or verification copy fails, so do not restart it until the timestamped directory contains the complete matching set. If both sidecars are absent after shutdown, the database-only set is complete. If either sidecar remains, both exact sidecars must be copied with that database; never delete a sidecar or assume that the main file alone contains its pages. Keep at least one older known-good backup until the new copy has been opened successfully.

After restart, check `GET http://127.0.0.1:5274/api/v1/health` and `/api/v1/status`. The status should show a running host and the same or later world minute than the checkpoint just copied.

## Restore while preserving the current database

Restore only with the service stopped. A selected backup is one timestamped directory, not three independently selected files. The following fail-closed sequence preserves the current database and sidecars before replacing them:

```powershell
$ErrorActionPreference = 'Stop'
$serviceName = 'Little Ages'
$dataDir = 'C:\ProgramData\LittleAges\worlds'
$dbPath = Join-Path $dataDir 'default-world.db'
$walPath = "$dbPath-wal"
$shmPath = "$dbPath-shm"
$backupPath = 'D:\LittleAgesBackups\default-world-20260916-120000'
$backupDbPath = Join-Path $backupPath 'default-world.db'
$backupWalPath = Join-Path $backupPath 'default-world.db-wal'
$backupShmPath = Join-Path $backupPath 'default-world.db-shm'
$holdingRoot = 'D:\LittleAgesBackups\pre-restore'
$holdingPath = Join-Path $holdingRoot ("default-world-$((Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss'))")

if (-not (Test-Path -LiteralPath $backupDbPath -PathType Leaf)) {
    throw "The selected backup database was not found: $backupDbPath"
}
$backupWalExists = Test-Path -LiteralPath $backupWalPath -PathType Leaf
$backupShmExists = Test-Path -LiteralPath $backupShmPath -PathType Leaf
if ($backupWalExists -xor $backupShmExists) {
    throw "Refusing to restore an incomplete WAL set. Select a timestamped backup containing both sidecars or neither."
}

Stop-Service -Name $serviceName
$service = Get-Service -Name $serviceName
if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
    throw "Refusing to restore while the service is not stopped: $($service.Status)"
}
if (-not (Test-Path -LiteralPath $dbPath -PathType Leaf)) {
    throw "The current authoritative database was not found; preserve it and investigate before restoring."
}
$currentWalExists = Test-Path -LiteralPath $walPath -PathType Leaf
$currentShmExists = Test-Path -LiteralPath $shmPath -PathType Leaf
if (Test-Path -LiteralPath $holdingPath) {
    throw "Refusing to overwrite the pre-restore holding directory: $holdingPath"
}
New-Item -ItemType Directory -Path $holdingPath -Force | Out-Null

# Preserve every current file before changing the active set.
Copy-Item -LiteralPath $dbPath -Destination (Join-Path $holdingPath 'default-world.db')
if (Test-Path -LiteralPath $walPath -PathType Leaf) {
    Copy-Item -LiteralPath $walPath -Destination (Join-Path $holdingPath 'default-world.db-wal')
}
if (Test-Path -LiteralPath $shmPath -PathType Leaf) {
    Copy-Item -LiteralPath $shmPath -Destination (Join-Path $holdingPath 'default-world.db-shm')
}
if ($currentWalExists -xor $currentShmExists) {
    throw "The current active path has an incomplete WAL set; the preserved holding copy is at $holdingPath."
}

# Move the preserved files out of the active path so stale sidecars cannot be
# combined with the selected backup.
Move-Item -LiteralPath $dbPath -Destination (Join-Path $holdingPath 'default-world.db.active')
if (Test-Path -LiteralPath $walPath -PathType Leaf) {
    Move-Item -LiteralPath $walPath -Destination (Join-Path $holdingPath 'default-world.db-wal.active')
}
if (Test-Path -LiteralPath $shmPath -PathType Leaf) {
    Move-Item -LiteralPath $shmPath -Destination (Join-Path $holdingPath 'default-world.db-shm.active')
}

Copy-Item -LiteralPath $backupDbPath -Destination $dbPath
if ($backupWalExists) {
    Copy-Item -LiteralPath $backupWalPath -Destination $walPath
    Copy-Item -LiteralPath $backupShmPath -Destination $shmPath
}
if (-not (Test-Path -LiteralPath $dbPath -PathType Leaf) -or
    (Test-Path -LiteralPath $walPath -PathType Leaf) -ne $backupWalExists -or
    (Test-Path -LiteralPath $shmPath -PathType Leaf) -ne $backupShmExists) {
    throw "The active data path does not contain the complete selected backup set; leave the service stopped."
}

Start-Service -Name $serviceName
Get-Service -Name $serviceName
```

If a successful final checkpoint cannot be confirmed before restore, preserve the current database and sidecars anyway and do not overwrite them. Keep `$holdingPath` until the restored world has passed `/api/v1/health`, `/api/v1/status`, active-world, and expected-world-minute checks. Never combine a database with sidecars from another timestamped directory. The host validates the checkpoint and rejects malformed, partial, or incompatible state; a rejected restore is evidence to stop and investigate, not permission to regenerate the world, delete rows, run repair commands, or alter the M6 rules/fingerprint.

A crash or power loss resumes the last committed valid checkpoint that SQLite can open; it does not perform wall-time catch-up.

## Optional approved SQLite online backup

When an offline stop is not practical, an operator may use the SQLite online backup API through an approved, compatible `sqlite3.exe` build. The `.backup` command is an online backup operation; a raw `Copy-Item` of a live database is not equivalent:

```powershell
$dbPath = 'C:\ProgramData\LittleAges\worlds\default-world.db'
$onlineBackupPath = 'D:\LittleAgesBackups\default-world-online.db'
$backupCommand = ".backup '$onlineBackupPath'"
sqlite3.exe $dbPath $backupCommand
if ($LASTEXITCODE -ne 0) {
    throw "SQLite online backup failed with exit code $LASTEXITCODE."
}
$integrityOutput = sqlite3.exe $onlineBackupPath 'PRAGMA integrity_check;'
if ($LASTEXITCODE -ne 0 -or ($integrityOutput -join "`n").Trim() -ne 'ok') {
    throw 'SQLite online backup integrity_check did not report ok.'
}
```

Proceed only if the command exits successfully and `integrity_check` reports `ok`. Keep the source database unchanged. If the approved SQLite tool is unavailable or its compatibility is uncertain, stop the service and use the preferred offline procedure instead. Do not improvise WAL deletion, `VACUUM`, row surgery, repair utilities, cloud sync, or network-folder mirroring as a recovery strategy.

## What backups do not contain

Backups contain canonical snapshot/history state and SQLite metadata. They do not make logs canonical, recreate a missing or corrupt world by seed, or change the `m6-rng1-history1` compatibility contract. A backup is a point-in-time recovery boundary; it is not an event-sourcing log or a substitute for a separate, tested retention policy.
