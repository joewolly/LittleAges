[CmdletBinding()]
param(
    [Parameter()]
    [switch] $EnableLan,

    [Parameter()]
    [ValidateRange(1, 65535)]
    [int] $Port = 5274,

    [Parameter()]
    [string] $InstallDirectory = (Join-Path -Path $(if ($env:ProgramFiles) { $env:ProgramFiles } else { 'C:\Program Files' }) -ChildPath 'LittleAges'),

    [Parameter()]
    [string] $DataDirectory = (Join-Path -Path $(if ($env:ProgramData) { $env:ProgramData } else { 'C:\ProgramData' }) -ChildPath 'LittleAges\worlds'),

    [Parameter()]
    [string] $ServiceName = 'Little Ages',

    [Parameter()]
    [string] $ActiveWorld = 'default-world',

    [Parameter()]
    [UInt64] $WorldSeed = 0,

    [Parameter()]
    [ValidateRange(0, 1000)]
    [double] $SimulationMinutesPerSecond = 10
)

$ErrorActionPreference = 'Stop'

Set-StrictMode -Version 2.0

$script:ManagedFirewallDisplayPrefix = 'Little Ages (Private TCP '
$script:ManagedFirewallGroup = 'Little Ages'
$script:ManagedFirewallName = 'LittleAges-Private-LAN'
$script:InstallMarkerFileName = 'littleages-install.json'
$script:InstallMarkerSchemaVersion = 1
$script:ServiceWaitSeconds = 60
$script:HealthWaitAttempts = 30

function Assert-Administrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        throw 'Little Ages installation requires an Administrator PowerShell window.'
    }

    if (-not $isAdministrator) {
        throw 'Little Ages installation requires an Administrator PowerShell window. Right-click PowerShell and choose "Run as administrator", then run install.ps1 again.'
    }
}

function Assert-SafeDirectoryPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Name cannot be empty."
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals($fullPath.TrimEnd('\', '/'), $root.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must be a dedicated directory, not a filesystem root: $fullPath"
    }

    return $fullPath.TrimEnd('\', '/')
}

function New-InstallOwnershipMarker {
    param(
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $ServiceName
    )

    return [ordered]@{
        ProductIdentifier = 'LittleAges'
        InstallerSchemaVersion = $script:InstallMarkerSchemaVersion
        ServiceName = $ServiceName
        InstallDirectory = $InstallPath
    }
}

function Read-InstallOwnershipMarker {
    param(
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $MarkerPath
    )

    try {
        $raw = Get-Content -LiteralPath $MarkerPath -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) { throw 'The marker is empty.' }
        $marker = $raw | ConvertFrom-Json
    }
    catch {
        throw "The installer ownership marker could not be safely parsed: $MarkerPath. $($_.Exception.Message)"
    }

    $productProperty = $marker.PSObject.Properties['ProductIdentifier']
    $schemaProperty = $marker.PSObject.Properties['InstallerSchemaVersion']
    $serviceProperty = $marker.PSObject.Properties['ServiceName']
    $installDirectoryProperty = $marker.PSObject.Properties['InstallDirectory']
    if ($null -eq $productProperty -or [string] $productProperty.Value -ne 'LittleAges') {
        throw "The installer ownership marker has an unexpected product identifier: $MarkerPath"
    }
    if ($null -eq $schemaProperty -or [int] $schemaProperty.Value -ne $script:InstallMarkerSchemaVersion) {
        throw "The installer ownership marker has an unsupported schema version: $MarkerPath"
    }
    if ($null -eq $serviceProperty -or [string]::IsNullOrWhiteSpace([string] $serviceProperty.Value)) {
        throw "The installer ownership marker has no service name: $MarkerPath"
    }
    if ($null -eq $installDirectoryProperty -or [string]::IsNullOrWhiteSpace([string] $installDirectoryProperty.Value)) {
        throw "The installer ownership marker has no install directory: $MarkerPath"
    }
    try {
        $normalizedMarkerPath = ([System.IO.Path]::GetFullPath([string] $installDirectoryProperty.Value)).TrimEnd('\', '/')
        $normalizedInstallPath = ([System.IO.Path]::GetFullPath($InstallPath)).TrimEnd('\', '/')
    }
    catch {
        throw "The installer ownership marker has an invalid install directory: $MarkerPath. $($_.Exception.Message)"
    }
    if (-not [string]::Equals($normalizedMarkerPath, $normalizedInstallPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The installer ownership marker belongs to a different install directory: $MarkerPath"
    }

    return $marker
}

function Get-InstallDirectoryOwnership {
    param([Parameter(Mandatory)] [string] $InstallPath)

    if (-not (Test-Path -LiteralPath $InstallPath)) {
        return [pscustomobject]@{ Kind = 'Missing'; Reason = $null; Marker = $null }
    }
    if (-not (Test-Path -LiteralPath $InstallPath -PathType Container)) {
        return [pscustomobject]@{ Kind = 'Invalid'; Reason = "InstallDirectory is not a directory: $InstallPath"; Marker = $null }
    }

    $markerPath = Join-Path -Path $InstallPath -ChildPath $script:InstallMarkerFileName
    if (Test-Path -LiteralPath $markerPath) {
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
            return [pscustomobject]@{ Kind = 'Invalid'; Reason = "The installer ownership marker is not a file: $markerPath"; Marker = $null }
        }
        try {
            $marker = Read-InstallOwnershipMarker -InstallPath $InstallPath -MarkerPath $markerPath
            return [pscustomobject]@{ Kind = 'Owned'; Reason = $null; Marker = $marker }
        }
        catch {
            return [pscustomobject]@{ Kind = 'Invalid'; Reason = $_.Exception.Message; Marker = $null }
        }
    }

    try {
        $legacyExecutable = Join-Path -Path $InstallPath -ChildPath 'LittleAges.Server.exe'
        if (Test-Path -LiteralPath $legacyExecutable -PathType Leaf) {
            return [pscustomobject]@{ Kind = 'Legacy'; Reason = $null; Marker = $null }
        }
        $entries = @(Get-ChildItem -LiteralPath $InstallPath -Force -ErrorAction Stop)
        if ($entries.Count -eq 0) {
            return [pscustomobject]@{ Kind = 'Empty'; Reason = $null; Marker = $null }
        }
        return [pscustomobject]@{ Kind = 'Unrecognized'; Reason = "InstallDirectory exists but is not recognized as a Little Ages installation. No files were changed: $InstallPath"; Marker = $null }
    }
    catch {
        return [pscustomobject]@{ Kind = 'Invalid'; Reason = "InstallDirectory could not be inspected safely: $InstallPath. $($_.Exception.Message)"; Marker = $null }
    }
}

function Get-ConfigProperty {
    param(
        [Parameter()] [object] $Configuration,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter()] [object] $Fallback
    )

    if ($null -ne $Configuration -and $null -ne $Configuration.PSObject.Properties[$Name]) {
        $value = $Configuration.PSObject.Properties[$Name].Value
        if ($null -ne $value) { return $value }
    }
    return $Fallback
}

function Read-ExistingConfiguration {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        $raw = Get-Content -LiteralPath $Path -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            throw 'The file is empty.'
        }
        return $raw | ConvertFrom-Json
    }
    catch {
        throw "Existing configuration could not be safely parsed and was left untouched: $Path. $($_.Exception.Message)"
    }
}

function Convert-ToInvariantString {
    param([Parameter(Mandatory)] [object] $Value)

    if ($Value -is [System.IFormattable]) {
        return $Value.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
    }
    return [string] $Value
}

function Get-ListenPort {
    param(
        [Parameter()] [object] $Configuration,
        [Parameter(Mandatory)] [int] $Fallback
    )

    $listenUrls = [string] (Get-ConfigProperty -Configuration $Configuration -Name 'ListenUrls' -Fallback '')
    foreach ($value in ($listenUrls -split ';')) {
        $uri = $null
        if ([Uri]::TryCreate($value.Trim(), [UriKind]::Absolute, [ref] $uri) -and $uri.Port -gt 0) {
            return $uri.Port
        }
    }
    return $Fallback
}

function Test-LanListenUrl {
    param([Parameter()] [object] $Configuration)

    $listenUrls = [string] (Get-ConfigProperty -Configuration $Configuration -Name 'ListenUrls' -Fallback '')
    foreach ($value in ($listenUrls -split ';')) {
        $uri = $null
        if ([Uri]::TryCreate($value.Trim(), [UriKind]::Absolute, [ref] $uri) -and
            -not $uri.IsLoopback -and
            -not [string]::Equals($uri.Host, 'localhost', [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Resolve-EffectiveLan {
    param(
        [Parameter(Mandatory)] [bool] $ExplicitChoice,
        [Parameter(Mandatory)] [bool] $RequestedLan,
        [Parameter(Mandatory)] [bool] $ExistingInstall,
        [Parameter(Mandatory)] [bool] $ExistingLan
    )

    if ($ExplicitChoice) { return $RequestedLan }
    if ($ExistingInstall) { return $ExistingLan }
    return $false
}

function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $Operation
    )

    & $FilePath @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Get-ServiceDetails {
    param([Parameter(Mandatory)] [string] $Name)

    try {
        return @(Get-CimInstance -ClassName Win32_Service -ErrorAction Stop | Where-Object { $_.Name -eq $Name } | Select-Object -First 1)[0]
    }
    catch {
        # Windows PowerShell 5.1 has Get-WmiObject even when the newer CIM
        # cmdlets are unavailable. This is read-only rollback metadata.
        try {
            return @(Get-WmiObject -Class Win32_Service -ErrorAction Stop | Where-Object { $_.Name -eq $Name } | Select-Object -First 1)[0]
        }
        catch {
            return $null
        }
    }
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [System.ServiceProcess.ServiceControllerStatus] $Desired,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-Service -Name $Name -ErrorAction Stop
        if ($service.Status -eq $Desired) { return $service }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    $current = (Get-Service -Name $Name -ErrorAction Stop).Status
    throw "Service '$Name' did not reach $Desired within $TimeoutSeconds seconds (current state: $current)."
}

function Stop-ServiceBounded {
    param([Parameter(Mandatory)] [string] $Name)

    $service = Get-Service -Name $Name -ErrorAction Stop
    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) { return }
    Stop-Service -Name $Name -ErrorAction Stop
    Wait-ServiceState -Name $Name -Desired ([System.ServiceProcess.ServiceControllerStatus]::Stopped) -TimeoutSeconds $script:ServiceWaitSeconds | Out-Null
}

function Set-ServiceRegistration {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [Parameter()] [string] $StartMode = 'auto',
        [Parameter()] [string] $Account = 'NT AUTHORITY\LocalService',
        [Parameter()] [switch] $SetAccount
    )

    $sc = Join-Path -Path $env:SystemRoot -ChildPath 'System32\sc.exe'
    if (-not (Test-Path -LiteralPath $sc -PathType Leaf)) { throw "Service control executable was not found: $sc" }
    $quotedExecutable = '"{0}"' -f $ExecutablePath
    $arguments = @('config', $Name, 'binPath=', $quotedExecutable, 'start=', $StartMode)
    if ($SetAccount) {
        $arguments += @('obj=', $Account)
    }
    Invoke-NativeChecked -FilePath $sc -Arguments $arguments -Operation "Updating Windows Service '$Name'"
}

function Remove-ServiceRegistration {
    param([Parameter(Mandatory)] [string] $Name)

    $sc = Join-Path -Path $env:SystemRoot -ChildPath 'System32\sc.exe'
    Invoke-NativeChecked -FilePath $sc -Arguments @('delete', $Name) -Operation "Removing Windows Service '$Name'"
}

function Get-ManagedFirewallRules {
    try {
        return @(Get-NetFirewallRule -Name $script:ManagedFirewallName -ErrorAction SilentlyContinue)
    }
    catch {
        throw "Windows Firewall management is unavailable: $($_.Exception.Message)"
    }
}

function Get-ManagedFirewallState {
    try {
        $rules = @(Get-ManagedFirewallRules)
        if ($rules.Count -eq 0) {
            return [pscustomobject]@{
                Exists = $false
                Rules = @()
            }
        }

        $capturedRules = foreach ($rule in $rules) {
            $portFilter = @(Get-NetFirewallPortFilter -AssociatedNetFirewallRule $rule -ErrorAction Stop | Select-Object -First 1)[0]
            $addressFilter = @(Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $rule -ErrorAction Stop | Select-Object -First 1)[0]
            if ($null -eq $portFilter -or $null -eq $addressFilter) {
                throw "The installer-owned firewall rule '$($rule.Name)' has no readable port/address filters."
            }

            [pscustomobject]@{
                Name = [string] $rule.Name
                DisplayName = [string] $rule.DisplayName
                Group = [string] $rule.Group
                Enabled = $rule.Enabled
                Direction = $rule.Direction
                Action = $rule.Action
                Protocol = $portFilter.Protocol
                LocalPort = $portFilter.LocalPort
                Profile = $rule.Profile
                RemoteAddress = $addressFilter.RemoteAddress
            }
        }

        return [pscustomobject]@{
            Exists = $true
            Rules = @($capturedRules)
        }
    }
    catch {
        throw "Windows Firewall state could not be captured safely: $($_.Exception.Message)"
    }
}

function Remove-ManagedFirewallRules {
    foreach ($rule in @(Get-ManagedFirewallRules)) {
        Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
    }
}

function Set-ManagedFirewallRule {
    param(
        [Parameter(Mandatory)] [bool] $Enable,
        [Parameter(Mandatory)] [int] $PortNumber
    )

    # Remove all rules owned by this installer first. This also cleans up a
    # previous port, so rerunning with a new -Port never creates duplicates.
    Remove-ManagedFirewallRules
    if (-not $Enable) { return }

    $displayName = '{0}{1})' -f $script:ManagedFirewallDisplayPrefix, $PortNumber
    New-NetFirewallRule -Name $script:ManagedFirewallName -DisplayName $displayName -Group $script:ManagedFirewallGroup `
        -Direction Inbound -Action Allow -Protocol TCP -LocalPort $PortNumber `
        -Profile Private -RemoteAddress 'LocalSubnet' -ErrorAction Stop | Out-Null
}

function Restore-ManagedFirewallState {
    param([Parameter()] [object] $State)

    Remove-ManagedFirewallRules
    if ($null -eq $State -or -not $State.Exists) { return }

    foreach ($rule in @($State.Rules)) {
        $restoreArguments = @{
            Name = $script:ManagedFirewallName
            DisplayName = $rule.DisplayName
            Group = $rule.Group
            Direction = $rule.Direction
            Action = $rule.Action
            Protocol = $rule.Protocol
            LocalPort = $rule.LocalPort
            Profile = $rule.Profile
            RemoteAddress = $rule.RemoteAddress
            ErrorAction = 'Stop'
        }
        if ($null -ne $rule.Enabled) {
            $restoreArguments['Enabled'] = $rule.Enabled
        }
        New-NetFirewallRule @restoreArguments | Out-Null
    }
}

function Set-DataDirectoryAcl {
    param([Parameter(Mandatory)] [string] $Path)

    $icacls = Join-Path -Path $env:SystemRoot -ChildPath 'System32\icacls.exe'
    if (-not (Test-Path -LiteralPath $icacls -PathType Leaf)) { throw "ACL utility was not found: $icacls" }
    # /grant:r replaces this installer's explicit entry instead of accumulating
    # duplicate LocalService ACEs on every upgrade.
    Invoke-NativeChecked -FilePath $icacls -Arguments @($Path, '/grant:r', 'NT AUTHORITY\LOCAL SERVICE:(OI)(CI)M', '/T', '/C') -Operation "Granting LocalService access to '$Path'"
}

function Start-AndVerifyService {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [int] $PortNumber
    )

    Start-Service -Name $Name -ErrorAction Stop
    Wait-ServiceState -Name $Name -Desired ([System.ServiceProcess.ServiceControllerStatus]::Running) -TimeoutSeconds $script:ServiceWaitSeconds | Out-Null

    $baseUri = 'http://127.0.0.1:{0}' -f $PortNumber
    $lastFailure = $null
    for ($attempt = 1; $attempt -le $script:HealthWaitAttempts; $attempt++) {
        try {
            $health = Invoke-WebRequest -UseBasicParsing -Uri ($baseUri + '/api/v1/health') -TimeoutSec 3
            if ($health.StatusCode -ne 200) { throw "health returned HTTP $($health.StatusCode)" }
            $status = Invoke-RestMethod -Uri ($baseUri + '/api/v1/status') -TimeoutSec 3
            if ($status.State -ne 'Running') { throw "status state is $($status.State)" }
            if ($status.PersistenceState -ne 'Healthy') { throw "persistence state is $($status.PersistenceState)" }
            return $status
        }
        catch {
            $lastFailure = $_.Exception.Message
            if ($attempt -lt $script:HealthWaitAttempts) { Start-Sleep -Seconds 1 }
        }
    }

    throw "Little Ages did not pass local health/status verification at $baseUri within $script:HealthWaitAttempts attempts. Last error: $lastFailure"
}

function Get-PrivateIPv4Address {
    try {
        $address = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.PrefixOrigin -ne 'WellKnown' } |
            Select-Object -First 1 -ExpandProperty IPAddress
        if (-not [string]::IsNullOrWhiteSpace($address)) { return $address }
    }
    catch {
        # Display-only convenience; LAN security is always the firewall rule.
    }
    return $null
}

Assert-Administrator

$packageSource = [System.IO.Path]::GetFullPath($PSScriptRoot)
$packageExecutable = Join-Path -Path $packageSource -ChildPath 'LittleAges.Server.exe'
$packageIndex = Join-Path -Path $packageSource -ChildPath 'wwwroot\index.html'
if (-not (Test-Path -LiteralPath $packageExecutable -PathType Leaf)) {
    throw "The release package is incomplete: LittleAges.Server.exe was not found under $packageSource."
}
if (-not (Test-Path -LiteralPath $packageIndex -PathType Leaf)) {
    throw "The release package is incomplete: wwwroot\index.html was not found under $packageSource."
}

$installPath = Assert-SafeDirectoryPath -Path $InstallDirectory -Name 'InstallDirectory'
$dataPath = Assert-SafeDirectoryPath -Path $DataDirectory -Name 'DataDirectory'
$installParent = Split-Path -Path $installPath -Parent
if ([string]::Equals($installPath, $dataPath, [StringComparison]::OrdinalIgnoreCase) -or
    $dataPath.StartsWith($installPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $installPath.StartsWith($dataPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'InstallDirectory and DataDirectory must be separate directories.'
}
if ([string]::IsNullOrWhiteSpace($ActiveWorld) -or $ActiveWorld -in @('.', '..') -or $ActiveWorld -notmatch '^[^\\/:*?"<>|]+$') {
    throw "ActiveWorld must be a simple file-safe world name: $ActiveWorld"
}

# Validate the existing application directory before reading its configuration
# or touching its service. An unrecognized directory is never moved or deleted.
$installDirectoryOwnership = Get-InstallDirectoryOwnership -InstallPath $installPath
if ($installDirectoryOwnership.Kind -in @('Invalid', 'Unrecognized')) {
    throw [string] $installDirectoryOwnership.Reason
}

$configPath = Join-Path -Path $installPath -ChildPath 'appsettings.json'
# Package validation happens before reading or changing an existing install.
$existingConfiguration = Read-ExistingConfiguration -Path $configPath
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$existingServiceDetails = if ($null -ne $existingService) { Get-ServiceDetails -Name $ServiceName } else { $null }
$existingServiceWasRunning = $null -ne $existingService -and $existingService.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running
$oldBinaryPath = if ($null -ne $existingServiceDetails) { [string] $existingServiceDetails.PathName } else { $null }
$oldStartMode = if ($null -ne $existingServiceDetails) { [string] $existingServiceDetails.StartMode } else { 'Auto' }
$oldAccount = if ($null -ne $existingServiceDetails -and -not [string]::IsNullOrWhiteSpace([string] $existingServiceDetails.StartName)) { [string] $existingServiceDetails.StartName } else { 'NT AUTHORITY\LocalService' }
$oldPort = Get-ListenPort -Configuration $existingConfiguration -Fallback $Port
$oldLan = Test-LanListenUrl -Configuration $existingConfiguration
$existingInstall = (Test-Path -LiteralPath $installPath -PathType Container) -or $null -ne $existingService
$effectiveEnableLan = Resolve-EffectiveLan -ExplicitChoice $PSBoundParameters.ContainsKey('EnableLan') -RequestedLan $EnableLan.IsPresent -ExistingInstall $existingInstall -ExistingLan $oldLan
$oldFirewallState = Get-ManagedFirewallState

$dataValue = if ($PSBoundParameters.ContainsKey('DataDirectory')) { $dataPath } else { [string] (Get-ConfigProperty -Configuration $existingConfiguration -Name 'DataRoot' -Fallback $dataPath) }
if ([string]::IsNullOrWhiteSpace($dataValue)) { $dataValue = $dataPath }
$dataValue = Assert-SafeDirectoryPath -Path $dataValue -Name 'DataRoot'
$effectiveDataPath = $dataValue
if ([string]::Equals($installPath, $effectiveDataPath, [StringComparison]::OrdinalIgnoreCase) -or
    $effectiveDataPath.StartsWith($installPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $installPath.StartsWith($effectiveDataPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'InstallDirectory and DataRoot must be separate directories.'
}
$activeWorldValue = if ($PSBoundParameters.ContainsKey('ActiveWorld')) { $ActiveWorld } else { [string] (Get-ConfigProperty -Configuration $existingConfiguration -Name 'ActiveWorld' -Fallback $ActiveWorld) }
$seedValue = if ($PSBoundParameters.ContainsKey('WorldSeed')) { $WorldSeed } else { Get-ConfigProperty -Configuration $existingConfiguration -Name 'WorldSeed' -Fallback $WorldSeed }
$speedValue = if ($PSBoundParameters.ContainsKey('SimulationMinutesPerSecond')) { $SimulationMinutesPerSecond } else { Get-ConfigProperty -Configuration $existingConfiguration -Name 'SimulationMinutesPerSecond' -Fallback $SimulationMinutesPerSecond }
$selectedPort = if ($PSBoundParameters.ContainsKey('Port')) { $Port } else { $oldPort }
$listenHost = if ($effectiveEnableLan) { '0.0.0.0' } else { '127.0.0.1' }
$listenValue = 'http://{0}:{1}' -f $listenHost, $selectedPort

$checkpointSimulationMinutes = Get-ConfigProperty -Configuration $existingConfiguration -Name 'CheckpointSimulationMinutes' -Fallback 360
$checkpointMinimumRealSeconds = Get-ConfigProperty -Configuration $existingConfiguration -Name 'CheckpointMinimumRealSeconds' -Fallback 30
$checkpointRetryCount = Get-ConfigProperty -Configuration $existingConfiguration -Name 'CheckpointRetryCount' -Fallback 3
$checkpointRetryDelaySeconds = Get-ConfigProperty -Configuration $existingConfiguration -Name 'CheckpointRetryDelaySeconds' -Fallback 2
$browserUpdateIntervalMilliseconds = Get-ConfigProperty -Configuration $existingConfiguration -Name 'BrowserUpdateIntervalMilliseconds' -Fallback 500

# Preserve unknown/user-owned configuration (notably Logging) while replacing
# only the installer-owned deployment values. ConvertTo-Json keeps this merge
# deterministic and does not write any source-checkout paths.
$configuration = [ordered]@{}
if ($null -ne $existingConfiguration) {
    foreach ($property in $existingConfiguration.PSObject.Properties) {
        $configuration[$property.Name] = $property.Value
    }
}
$configuration['DataRoot'] = $dataValue
$configuration['ActiveWorld'] = $activeWorldValue
$configuration['WorldSeed'] = Convert-ToInvariantString -Value $seedValue
$configuration['ListenUrls'] = $listenValue
$configuration['SimulationMinutesPerSecond'] = $speedValue
$configuration['CheckpointSimulationMinutes'] = $checkpointSimulationMinutes
$configuration['CheckpointMinimumRealSeconds'] = $checkpointMinimumRealSeconds
$configuration['CheckpointRetryCount'] = $checkpointRetryCount
$configuration['CheckpointRetryDelaySeconds'] = $checkpointRetryDelaySeconds
$configuration['BrowserUpdateIntervalMilliseconds'] = $browserUpdateIntervalMilliseconds
$configurationJson = $configuration | ConvertTo-Json -Depth 10

$runId = [Guid]::NewGuid().ToString('N')
$newDeployment = Join-Path -Path $installParent -ChildPath ('.LittleAges.new-' + $runId)
$backupDeployment = Join-Path -Path $installParent -ChildPath ('.LittleAges.rollback-' + $runId)
$deploymentMoved = $false
$newDeploymentMoved = $false
$serviceCreated = $false
$serviceRegistrationTouched = $false
$firewallTouched = $false
$installSucceeded = $false
$rollbackErrors = New-Object System.Collections.Generic.List[string]
$existingWorldData = Test-Path -LiteralPath $effectiveDataPath -PathType Container
if ($existingWorldData) {
    Write-Host ('Existing world data will be preserved: {0}' -f $effectiveDataPath)
}

try {
    New-Item -ItemType Directory -Path $installParent -Force | Out-Null
    New-Item -ItemType Directory -Path $effectiveDataPath -Force | Out-Null
    Set-DataDirectoryAcl -Path $effectiveDataPath

    if ($null -ne $existingService) {
        Stop-ServiceBounded -Name $ServiceName
    }

    New-Item -ItemType Directory -Path $newDeployment -Force | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $packageSource -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $newDeployment -Recurse -Force
    }
    Set-Content -LiteralPath (Join-Path -Path $newDeployment -ChildPath 'appsettings.json') -Value $configurationJson -Encoding UTF8
    $markerJson = (New-InstallOwnershipMarker -InstallPath $installPath -ServiceName $ServiceName) | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path -Path $newDeployment -ChildPath $script:InstallMarkerFileName) -Value $markerJson -Encoding UTF8

    if (Test-Path -LiteralPath $installPath) {
        if (-not (Test-Path -LiteralPath $installPath -PathType Container)) { throw "InstallDirectory exists but is not a directory: $installPath" }
        Move-Item -LiteralPath $installPath -Destination $backupDeployment
        $deploymentMoved = $true
    }
    Move-Item -LiteralPath $newDeployment -Destination $installPath
    $newDeploymentMoved = $true
    Set-DataDirectoryAcl -Path $effectiveDataPath

    $serviceExecutable = Join-Path -Path $installPath -ChildPath 'LittleAges.Server.exe'
    if ($null -eq $existingService) {
        New-Service -Name $ServiceName -DisplayName $ServiceName -Description 'Persistent Little Ages simulation host' -BinaryPathName ('"{0}"' -f $serviceExecutable) -StartupType Automatic | Out-Null
        $serviceCreated = $true
    }
    $serviceRegistrationTouched = $true
    Set-ServiceRegistration -Name $ServiceName -ExecutablePath $serviceExecutable -SetAccount

    # This is the only firewall resource owned by the installer. Local mode
    # removes a prior installer rule, while LAN mode replaces it deterministically.
    $firewallTouched = $true
    Set-ManagedFirewallRule -Enable $effectiveEnableLan -PortNumber $selectedPort

    $null = Start-AndVerifyService -Name $ServiceName -PortNumber $selectedPort
    $installSucceeded = $true
}
catch {
    $failureMessage = $_.Exception.Message
    Write-Warning "Little Ages installation failed: $failureMessage"

    try {
        if ($serviceRegistrationTouched -or $serviceCreated) {
            $current = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
            if ($null -ne $current -and $current.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
                Stop-ServiceBounded -Name $ServiceName
            }
        }
    }
    catch { [void] $rollbackErrors.Add("Could not stop the failed service: $($_.Exception.Message)") }

    try {
        if ($firewallTouched) {
            Restore-ManagedFirewallState -State $oldFirewallState
        }
    }
    catch { [void] $rollbackErrors.Add("Could not restore the previous firewall rule: $($_.Exception.Message)") }

    try {
        if ($serviceCreated) {
            Remove-ServiceRegistration -Name $ServiceName
        }
    }
    catch { [void] $rollbackErrors.Add("Could not remove the new service registration: $($_.Exception.Message)") }

    try {
        if ($newDeploymentMoved -and (Test-Path -LiteralPath $installPath)) {
            Remove-Item -LiteralPath $installPath -Recurse -Force
        }
        elseif (Test-Path -LiteralPath $newDeployment) {
            Remove-Item -LiteralPath $newDeployment -Recurse -Force
        }
        if ($deploymentMoved -and (Test-Path -LiteralPath $backupDeployment)) {
            Move-Item -LiteralPath $backupDeployment -Destination $installPath
        }
    }
    catch { [void] $rollbackErrors.Add("Could not restore the previous application files: $($_.Exception.Message)") }

    try {
        if ($null -ne $existingService -and $serviceRegistrationTouched -and -not [string]::IsNullOrWhiteSpace($oldBinaryPath)) {
            $restoreStart = switch -Regex ($oldStartMode) {
                '^Disabled$' { 'disabled'; break }
                '^Manual$' { 'demand'; break }
                default { 'auto' }
            }
            Set-ServiceRegistration -Name $ServiceName -ExecutablePath $oldBinaryPath.Trim('"') -StartMode $restoreStart -Account $oldAccount -SetAccount
        }
        if ($null -ne $existingService -and $existingServiceWasRunning) {
            Start-Service -Name $ServiceName -ErrorAction Stop
            Wait-ServiceState -Name $ServiceName -Desired ([System.ServiceProcess.ServiceControllerStatus]::Running) -TimeoutSeconds $script:ServiceWaitSeconds | Out-Null
        }
    }
    catch { [void] $rollbackErrors.Add("Could not fully restore the previous service: $($_.Exception.Message)") }

    if ($rollbackErrors.Count -gt 0) {
        throw "Little Ages installation failed and rollback reported: $($rollbackErrors -join ' | ') Original error: $failureMessage"
    }
    throw "Little Ages installation failed; the previous deployment was restored. World data was not modified. Original error: $failureMessage"
}
finally {
    if (-not $installSucceeded -and (Test-Path -LiteralPath $newDeployment)) {
        Remove-Item -LiteralPath $newDeployment -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($installSucceeded -and (Test-Path -LiteralPath $backupDeployment)) {
        Remove-Item -LiteralPath $backupDeployment -Recurse -Force
    }
}

$localUrl = 'http://127.0.0.1:{0}' -f $selectedPort
Write-Host ''
Write-Host 'Little Ages installed successfully.'
Write-Host ''
Write-Host ('Service:       {0}' -f (Get-Service -Name $ServiceName).Status)
Write-Host 'Persistence:   Healthy'
Write-Host 'Version:       v0.1.0 candidate'
Write-Host ('World:         {0}' -f $activeWorldValue)
Write-Host ''
Write-Host ('Application:   {0}' -f $installPath)
Write-Host ('World data:    {0}' -f $dataValue)
Write-Host ''
Write-Host ('Local:         {0}' -f $localUrl)
if ($effectiveEnableLan) {
    $privateAddress = Get-PrivateIPv4Address
    if ($null -ne $privateAddress) { Write-Host ('LAN:           http://{0}:{1}' -f $privateAddress, $selectedPort) }
    else { Write-Host ('LAN:           enabled on port {0} (private address could not be detected)' -f $selectedPort) }
}
else {
    Write-Host 'LAN:           disabled'
}
Write-Host ''
Write-Host 'World data is preserved automatically during upgrades.'
