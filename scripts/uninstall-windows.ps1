[CmdletBinding()]
param(
    [Parameter()]
    [string] $InstallDirectory = (Join-Path -Path $(if ($env:ProgramFiles) { $env:ProgramFiles } else { 'C:\Program Files' }) -ChildPath 'LittleAges'),

    [Parameter()]
    [string] $DataDirectory = (Join-Path -Path $(if ($env:ProgramData) { $env:ProgramData } else { 'C:\ProgramData' }) -ChildPath 'LittleAges\worlds'),

    [Parameter()]
    [switch] $DeleteWorldData,

    [Parameter()]
    [switch] $ConfirmWorldDeletion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$script:ManagedFirewallDisplayPrefix = 'Little Ages (Private TCP '
$script:ManagedFirewallGroup = 'Little Ages'
$script:ManagedFirewallName = 'LittleAges-Private-LAN'
$script:InstallMarkerFileName = 'littleages-install.json'
$script:InstallMarkerSchemaVersion = 1
$script:ServiceName = 'Little Ages'
$script:ServiceWaitSeconds = 60

function Assert-Administrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        throw 'Little Ages uninstall requires an Administrator PowerShell window.'
    }
    if (-not $isAdministrator) {
        throw 'Little Ages uninstall requires an Administrator PowerShell window. Right-click PowerShell and choose "Run as administrator", then run uninstall.ps1 again.'
    }
}

function Assert-SafeDirectoryPath {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Name)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Name cannot be empty." }
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals($fullPath.TrimEnd('\', '/'), $root.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must be a dedicated directory, not a filesystem root: $fullPath"
    }
    return $fullPath.TrimEnd('\', '/')
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
    if ($null -eq $serviceProperty -or -not [string]::Equals([string] $serviceProperty.Value, $script:ServiceName, [StringComparison]::Ordinal)) {
        throw "The installer ownership marker has an unexpected service name: $MarkerPath"
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

function Resolve-InstalledDataDirectory {
    param(
        [Parameter(Mandatory)] [string] $DefaultPath,
        [Parameter(Mandatory)] [string] $ApplicationPath
    )

    $configurationPath = Join-Path -Path $ApplicationPath -ChildPath 'appsettings.json'
    if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
        return $DefaultPath
    }

    try {
        $configuration = (Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json)
        $dataRootProperty = $configuration.PSObject.Properties['DataRoot']
        if ($null -ne $dataRootProperty -and -not [string]::IsNullOrWhiteSpace([string] $dataRootProperty.Value)) {
            return Assert-SafeDirectoryPath -Path ([string] $dataRootProperty.Value) -Name 'Installed DataRoot'
        }
    }
    catch {
        throw "Installed configuration could not be safely parsed; no files were removed: $configurationPath. $($_.Exception.Message)"
    }
    return $DefaultPath
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory)] [System.ServiceProcess.ServiceControllerStatus] $Desired,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-Service -Name $script:ServiceName -ErrorAction Stop
        if ($service.Status -eq $Desired) { return $service }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Service '$($script:ServiceName)' did not reach $Desired within $TimeoutSeconds seconds."
}

function Stop-ServiceBounded {
    $service = Get-Service -Name $script:ServiceName -ErrorAction Stop
    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) { return }
    Stop-Service -Name $script:ServiceName -ErrorAction Stop
    Wait-ServiceState -Desired ([System.ServiceProcess.ServiceControllerStatus]::Stopped) -TimeoutSeconds $script:ServiceWaitSeconds | Out-Null
}

function Remove-ServiceRegistration {
    $sc = Join-Path -Path $env:SystemRoot -ChildPath 'System32\sc.exe'
    if (-not (Test-Path -LiteralPath $sc -PathType Leaf)) { throw "Service control executable was not found: $sc" }
    & $sc 'delete' $script:ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Removing Windows Service '$($script:ServiceName)' failed with exit code $LASTEXITCODE." }
}

function Remove-ManagedFirewallRules {
    try {
        $rules = @(Get-NetFirewallRule -Name $script:ManagedFirewallName -ErrorAction SilentlyContinue)
        foreach ($rule in $rules) {
            Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
        }
    }
    catch {
        throw "Windows Firewall management is unavailable: $($_.Exception.Message)"
    }
}

Assert-Administrator

$installPath = Assert-SafeDirectoryPath -Path $InstallDirectory -Name 'InstallDirectory'
$dataPath = Assert-SafeDirectoryPath -Path $DataDirectory -Name 'DataDirectory'
# Refuse to delete an existing directory unless this is an owned or clearly
# recognizable legacy Little Ages deployment. This check precedes configuration
# parsing, service changes, and all recursive deletion.
$installDirectoryOwnership = Get-InstallDirectoryOwnership -InstallPath $installPath
if ($installDirectoryOwnership.Kind -eq 'Empty') {
    throw "InstallDirectory is empty and is not a recognized Little Ages installation. No files were changed: $installPath"
}
if ($installDirectoryOwnership.Kind -in @('Invalid', 'Unrecognized')) {
    throw [string] $installDirectoryOwnership.Reason
}
$configuredDataPath = if ($PSBoundParameters.ContainsKey('DataDirectory')) { $dataPath } else { Resolve-InstalledDataDirectory -DefaultPath $dataPath -ApplicationPath $installPath }
if ([string]::Equals($installPath, $configuredDataPath, [StringComparison]::OrdinalIgnoreCase) -or
    $configuredDataPath.StartsWith($installPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $installPath.StartsWith($configuredDataPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'InstallDirectory and DataDirectory must be separate directories.'
}
if ($DeleteWorldData -and -not $ConfirmWorldDeletion) {
    throw 'World data deletion is deliberately disabled unless both -DeleteWorldData and -ConfirmWorldDeletion are supplied.'
}

$service = Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    Stop-ServiceBounded
    Remove-ServiceRegistration
    Write-Host "Removed Windows Service: $($script:ServiceName)"
}
else {
    Write-Host "Windows Service not registered: $($script:ServiceName)"
}

# Only the deterministic installer-owned rule name/group is managed.
Remove-ManagedFirewallRules

if (Test-Path -LiteralPath $installPath) {
    if (-not (Test-Path -LiteralPath $installPath -PathType Container)) { throw "InstallDirectory is not a directory: $installPath" }
    Remove-Item -LiteralPath $installPath -Recurse -Force
    Write-Host "Removed application files: $installPath"
}

if ($DeleteWorldData) {
    if (Test-Path -LiteralPath $configuredDataPath) {
        Remove-Item -LiteralPath $configuredDataPath -Recurse -Force
        Write-Host "Deleted world data after explicit confirmation: $configuredDataPath"
    }
    else {
        Write-Host "World data directory was already absent: $configuredDataPath"
    }
}
else {
    Write-Host ''
    Write-Host ('World data preserved at: {0}' -f $configuredDataPath)
    Write-Host 'Use -DeleteWorldData -ConfirmWorldDeletion only when permanent deletion is intended.'
}

Write-Host 'Little Ages uninstall completed.'
