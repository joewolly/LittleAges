[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Assert-Equal {
    param(
        [Parameter(Mandatory)] [object] $Expected,
        [Parameter(Mandatory)] [object] $Actual,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Expected -is [array] -or $Actual -is [array]) {
        if (@($Expected) -join "`n" -ne @($Actual) -join "`n") {
            throw "${Message}: expected '$Expected', got '$Actual'."
        }
        return
    }
    if ($Expected -ne $Actual) {
        throw "${Message}: expected '$Expected', got '$Actual'."
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "${Message}: '$Expected' was not found."
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $Unexpected,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Text.IndexOf($Unexpected, [StringComparison]::Ordinal) -ge 0) {
        throw "${Message}: '$Unexpected' was found."
    }
}

function Get-FunctionSource {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Name
    )

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref] $tokens, [ref] $parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "PowerShell parse failed for ${Path}: $($parseErrors[0].Message)"
    }
    $functionAst = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name
        }, $true) | Select-Object -First 1
    if ($null -eq $functionAst) { throw "Function '$Name' was not found in $Path." }
    return $functionAst.Extent.Text
}

$installerPath = Join-Path -Path $PSScriptRoot -ChildPath 'install-windows.ps1'
$uninstallerPath = Join-Path -Path $PSScriptRoot -ChildPath 'uninstall-windows.ps1'
$workflowPath = Join-Path -Path $PSScriptRoot -ChildPath '..\.github\workflows\windows-package.yml'
$installerText = Get-Content -LiteralPath $installerPath -Raw
$uninstallerText = Get-Content -LiteralPath $uninstallerPath -Raw
$workflowText = Get-Content -LiteralPath $workflowPath -Raw

# Parse the complete installer first, then load only the pure/isolated helper
# functions so these assertions never invoke administrator, service, or
# firewall operations.
$installerTokens = $null
$installerParseErrors = $null
$installerAst = [System.Management.Automation.Language.Parser]::ParseFile($installerPath, [ref] $installerTokens, [ref] $installerParseErrors)
if ($installerParseErrors.Count -gt 0) {
    throw "PowerShell parse failed for ${installerPath}: $($installerParseErrors[0].Message)"
}
$uninstallerTokens = $null
$uninstallerParseErrors = $null
$uninstallerAst = [System.Management.Automation.Language.Parser]::ParseFile($uninstallerPath, [ref] $uninstallerTokens, [ref] $uninstallerParseErrors)
if ($uninstallerParseErrors.Count -gt 0) {
    throw "PowerShell parse failed for ${uninstallerPath}: $($uninstallerParseErrors[0].Message)"
}

. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Resolve-EffectiveLan')))
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'New-InstallOwnershipMarker')))
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Read-InstallOwnershipMarker')))
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Get-InstallDirectoryOwnership')))
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Move-DirectoryAtomically')))
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Get-DeploymentLayoutState')))

$script:InstallMarkerFileName = 'littleages-install.json'
$script:InstallMarkerSchemaVersion = 1
$script:ServiceName = 'Little Ages'

$installerParameters = @($installerAst.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
$uninstallerParameters = @($uninstallerAst.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
if ($installerParameters -contains 'ServiceName') { throw 'Install script must not expose a ServiceName parameter.' }
if ($uninstallerParameters -contains 'ServiceName') { throw 'Uninstall script must not expose a ServiceName parameter.' }
if ($installerText -match '\$ServiceName\b' -or $uninstallerText -match '\$ServiceName\b') {
    throw 'Deployment scripts must not use a caller-controlled ServiceName variable.'
}
Assert-Contains -Text $installerText -Expected "`$script:ServiceName = 'Little Ages'" -Message 'Installer fixes the owned service name'
Assert-Contains -Text $uninstallerText -Expected "`$script:ServiceName = 'Little Ages'" -Message 'Uninstaller fixes the owned service name'
Assert-Contains -Text $installerText -Expected '[double] $SimulationMinutesPerSecond = 1.44' -Message 'Fresh installs use the 16:40 normal day pace'
Assert-Contains -Text $installerText -Expected "`$PSBoundParameters.ContainsKey('SimulationMinutesPerSecond')" -Message 'Upgrades preserve or explicitly override the configured pace'

# Load the uninstaller's own standalone ownership helpers under test-only names
# so this fixture exercises both release scripts without invoking their main
# administrator/service/firewall paths.
$uninstallerReadSource = (Get-FunctionSource -Path $uninstallerPath -Name 'Read-InstallOwnershipMarker').Replace('function Read-InstallOwnershipMarker', 'function Read-UninstallInstallOwnershipMarker')
$uninstallerOwnershipSource = (Get-FunctionSource -Path $uninstallerPath -Name 'Get-InstallDirectoryOwnership').Replace('function Get-InstallDirectoryOwnership', 'function Get-UninstallInstallDirectoryOwnership').Replace('Read-InstallOwnershipMarker', 'Read-UninstallInstallOwnershipMarker')
. ([scriptblock]::Create($uninstallerReadSource))
. ([scriptblock]::Create($uninstallerOwnershipSource))

$lanCases = @(
    [pscustomobject]@{ Name = 'fresh + omitted'; ExplicitChoice = $false; RequestedLan = $false; ExistingInstall = $false; ExistingLan = $false; Expected = $false }
    [pscustomobject]@{ Name = 'fresh + true'; ExplicitChoice = $true; RequestedLan = $true; ExistingInstall = $false; ExistingLan = $false; Expected = $true }
    [pscustomobject]@{ Name = 'existing LAN + omitted'; ExplicitChoice = $false; RequestedLan = $false; ExistingInstall = $true; ExistingLan = $true; Expected = $true }
    [pscustomobject]@{ Name = 'existing LAN + false'; ExplicitChoice = $true; RequestedLan = $false; ExistingInstall = $true; ExistingLan = $true; Expected = $false }
    [pscustomobject]@{ Name = 'existing local + omitted'; ExplicitChoice = $false; RequestedLan = $false; ExistingInstall = $true; ExistingLan = $false; Expected = $false }
    [pscustomobject]@{ Name = 'existing local + true'; ExplicitChoice = $true; RequestedLan = $true; ExistingInstall = $true; ExistingLan = $false; Expected = $true }
)
foreach ($case in $lanCases) {
    $actual = Resolve-EffectiveLan -ExplicitChoice $case.ExplicitChoice -RequestedLan $case.RequestedLan `
        -ExistingInstall $case.ExistingInstall -ExistingLan $case.ExistingLan
    Assert-Equal -Expected $case.Expected -Actual $actual -Message "LAN resolution ($($case.Name))"
}
Assert-Contains -Text $installerText -Expected "`$PSBoundParameters.ContainsKey('EnableLan')" -Message 'LAN tri-state checks parameter binding'
Assert-Contains -Text $installerText -Expected '$effectiveEnableLan' -Message 'LAN effective state is used'

# Ownership checks use only an isolated temporary directory. No Program Files,
# service, firewall, or world-data path is touched by this fixture.
$ownershipTestRoot = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('LittleAges-ownership-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $ownershipTestRoot -Force | Out-Null
try {
    $missingPath = Join-Path -Path $ownershipTestRoot -ChildPath 'missing'
    Assert-Equal -Expected 'Missing' -Actual (Get-InstallDirectoryOwnership -InstallPath $missingPath).Kind -Message 'Nonexistent install path is allowed'

    $emptyPath = Join-Path -Path $ownershipTestRoot -ChildPath 'empty'
    New-Item -ItemType Directory -Path $emptyPath -Force | Out-Null
    Assert-Equal -Expected 'Empty' -Actual (Get-InstallDirectoryOwnership -InstallPath $emptyPath).Kind -Message 'Empty install directory is allowed'

    $ownedPath = Join-Path -Path $ownershipTestRoot -ChildPath 'owned'
    New-Item -ItemType Directory -Path $ownedPath -Force | Out-Null
    $ownedMarker = New-InstallOwnershipMarker -InstallPath $ownedPath | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path -Path $ownedPath -ChildPath $script:InstallMarkerFileName) -Value $ownedMarker -Encoding UTF8
    Assert-Equal -Expected 'Owned' -Actual (Get-InstallDirectoryOwnership -InstallPath $ownedPath).Kind -Message 'Installer-owned directory is allowed'
    Assert-Equal -Expected 'Owned' -Actual (Get-UninstallInstallDirectoryOwnership -InstallPath $ownedPath).Kind -Message 'Uninstaller accepts installer-owned directory'

    $wrongServicePath = Join-Path -Path $ownershipTestRoot -ChildPath 'wrong-service'
    New-Item -ItemType Directory -Path $wrongServicePath -Force | Out-Null
    $wrongServiceMarker = [ordered]@{
        ProductIdentifier = 'LittleAges'
        InstallerSchemaVersion = 1
        ServiceName = 'Spooler'
        InstallDirectory = $wrongServicePath
    } | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path -Path $wrongServicePath -ChildPath $script:InstallMarkerFileName) -Value $wrongServiceMarker -Encoding UTF8
    Assert-Equal -Expected 'Invalid' -Actual (Get-InstallDirectoryOwnership -InstallPath $wrongServicePath).Kind -Message 'Installer rejects marker for another service'
    Assert-Equal -Expected 'Invalid' -Actual (Get-UninstallInstallDirectoryOwnership -InstallPath $wrongServicePath).Kind -Message 'Uninstaller rejects marker for another service'

    $legacyPath = Join-Path -Path $ownershipTestRoot -ChildPath 'legacy'
    New-Item -ItemType Directory -Path $legacyPath -Force | Out-Null
    Set-Content -LiteralPath (Join-Path -Path $legacyPath -ChildPath 'LittleAges.Server.exe') -Value 'legacy placeholder' -Encoding UTF8
    Assert-Equal -Expected 'Legacy' -Actual (Get-InstallDirectoryOwnership -InstallPath $legacyPath).Kind -Message 'Recognized legacy directory is adoptable'

    $unrelatedPath = Join-Path -Path $ownershipTestRoot -ChildPath 'unrelated'
    New-Item -ItemType Directory -Path $unrelatedPath -Force | Out-Null
    Set-Content -LiteralPath (Join-Path -Path $unrelatedPath -ChildPath 'notes.txt') -Value 'unrelated' -Encoding UTF8
    Assert-Equal -Expected 'Unrecognized' -Actual (Get-InstallDirectoryOwnership -InstallPath $unrelatedPath).Kind -Message 'Unrelated non-empty directory is rejected'
    Assert-Equal -Expected 'Unrecognized' -Actual (Get-UninstallInstallDirectoryOwnership -InstallPath $unrelatedPath).Kind -Message 'Uninstaller refuses unrelated directory'

    $programFilesStylePath = Join-Path -Path $ownershipTestRoot -ChildPath 'Program Files'
    New-Item -ItemType Directory -Path $programFilesStylePath -Force | Out-Null
    Set-Content -LiteralPath (Join-Path -Path $programFilesStylePath -ChildPath 'other-app.txt') -Value 'unrelated' -Encoding UTF8
    Assert-Equal -Expected 'Unrecognized' -Actual (Get-InstallDirectoryOwnership -InstallPath $programFilesStylePath).Kind -Message 'Populated Program Files-style directory is rejected'

    $malformedPath = Join-Path -Path $ownershipTestRoot -ChildPath 'malformed'
    New-Item -ItemType Directory -Path $malformedPath -Force | Out-Null
    Set-Content -LiteralPath (Join-Path -Path $malformedPath -ChildPath $script:InstallMarkerFileName) -Value '{not-json' -Encoding UTF8
    Assert-Equal -Expected 'Invalid' -Actual (Get-InstallDirectoryOwnership -InstallPath $malformedPath).Kind -Message 'Malformed ownership marker is rejected'

    $wrongProductPath = Join-Path -Path $ownershipTestRoot -ChildPath 'wrong-product'
    New-Item -ItemType Directory -Path $wrongProductPath -Force | Out-Null
    $wrongProductMarker = [ordered]@{
        ProductIdentifier = 'OtherProduct'
        InstallerSchemaVersion = 1
        ServiceName = 'Little Ages'
        InstallDirectory = $wrongProductPath
    } | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path -Path $wrongProductPath -ChildPath $script:InstallMarkerFileName) -Value $wrongProductMarker -Encoding UTF8
    Assert-Equal -Expected 'Invalid' -Actual (Get-InstallDirectoryOwnership -InstallPath $wrongProductPath).Kind -Message 'Wrong-product ownership marker is rejected'

    $mismatchedPath = Join-Path -Path $ownershipTestRoot -ChildPath 'mismatched-path'
    New-Item -ItemType Directory -Path $mismatchedPath -Force | Out-Null
    $mismatchedMarker = New-InstallOwnershipMarker -InstallPath (Join-Path -Path $ownershipTestRoot -ChildPath 'some-other-install') | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path -Path $mismatchedPath -ChildPath $script:InstallMarkerFileName) -Value $mismatchedMarker -Encoding UTF8
    Assert-Equal -Expected 'Invalid' -Actual (Get-InstallDirectoryOwnership -InstallPath $mismatchedPath).Kind -Message 'Mismatched-path ownership marker is rejected'
    Assert-Equal -Expected 'Invalid' -Actual (Get-UninstallInstallDirectoryOwnership -InstallPath $mismatchedPath).Kind -Message 'Uninstaller rejects mismatched-path ownership marker'
}
finally {
    if (Test-Path -LiteralPath $ownershipTestRoot) {
        Remove-Item -LiteralPath $ownershipTestRoot -Recurse -Force
    }
}

# Exercise the production atomic directory helper and deployment-state
# transitions using isolated temporary directories only. These tests never
# touch Program Files, the Windows service, firewall rules, or world data.
$atomicTestRoot = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('LittleAges-atomic-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $atomicTestRoot -Force | Out-Null
try {
    $sourcePath = Join-Path -Path $atomicTestRoot -ChildPath 'source'
    $destinationPath = Join-Path -Path $atomicTestRoot -ChildPath 'destination'
    New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null
    Set-Content -LiteralPath (Join-Path -Path $sourcePath -ChildPath 'payload.txt') -Value 'atomic payload' -Encoding UTF8
    Move-DirectoryAtomically -SourcePath $sourcePath -DestinationPath $destinationPath
    Assert-Equal -Expected $false -Actual (Test-Path -LiteralPath $sourcePath) -Message 'Atomic rename removes source path'
    Assert-Equal -Expected $true -Actual (Test-Path -LiteralPath (Join-Path -Path $destinationPath -ChildPath 'payload.txt') -PathType Leaf) -Message 'Atomic rename preserves destination contents'

    $lockedSource = Join-Path -Path $atomicTestRoot -ChildPath 'locked-source'
    $lockedDestination = Join-Path -Path $atomicTestRoot -ChildPath 'locked-destination'
    New-Item -ItemType Directory -Path $lockedSource -Force | Out-Null
    $lockedFile = Join-Path -Path $lockedSource -ChildPath 'held.txt'
    Set-Content -LiteralPath $lockedFile -Value 'held payload' -Encoding UTF8
    $heldStream = [System.IO.File]::Open($lockedFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $lockedFailure = $false
        try {
            Move-DirectoryAtomically -SourcePath $lockedSource -DestinationPath $lockedDestination -RetryCount 2 -RetryDelayMilliseconds 25
        }
        catch {
            $lockedFailure = $true
        }
        Assert-Equal -Expected $true -Actual $lockedFailure -Message 'Locked atomic rename reports failure'
        Assert-Equal -Expected $true -Actual (Test-Path -LiteralPath $lockedSource -PathType Container) -Message 'Locked rename preserves complete source'
        Assert-Equal -Expected $false -Actual (Test-Path -LiteralPath $lockedDestination) -Message 'Locked rename leaves destination absent'
        Assert-Equal -Expected 'held payload' -Actual (Get-Content -LiteralPath $lockedFile -Raw).Trim() -Message 'Locked rename preserves source file content'
    }
    finally {
        $heldStream.Dispose()
    }
    Move-DirectoryAtomically -SourcePath $lockedSource -DestinationPath $lockedDestination
    Assert-Equal -Expected $false -Actual (Test-Path -LiteralPath $lockedSource) -Message 'Atomic rename succeeds after handle release'
    Assert-Equal -Expected $true -Actual (Test-Path -LiteralPath (Join-Path -Path $lockedDestination -ChildPath 'held.txt') -PathType Leaf) -Message 'Released rename preserves file'

    $stateInstall = Join-Path -Path $atomicTestRoot -ChildPath 'install'
    $stateNew = Join-Path -Path $atomicTestRoot -ChildPath 'new'
    $stateBackup = Join-Path -Path $atomicTestRoot -ChildPath 'backup'
    $stateFailed = Join-Path -Path $atomicTestRoot -ChildPath 'failed'
    New-Item -ItemType Directory -Path $stateInstall -Force | Out-Null
    Assert-Equal -Expected 'OLD' -Actual (Get-DeploymentLayoutState -InstallPath $stateInstall -NewPath $stateNew -BackupPath $stateBackup -FailedPath $stateFailed) -Message 'Deployment state recognizes OLD'
    New-Item -ItemType Directory -Path $stateNew -Force | Out-Null
    Assert-Equal -Expected 'NEW' -Actual (Get-DeploymentLayoutState -InstallPath $stateInstall -NewPath $stateNew -BackupPath $stateBackup -FailedPath $stateFailed) -Message 'Deployment state recognizes NEW'
    Move-DirectoryAtomically -SourcePath $stateInstall -DestinationPath $stateBackup
    Assert-Equal -Expected 'BACKUP' -Actual (Get-DeploymentLayoutState -InstallPath $stateInstall -NewPath $stateNew -BackupPath $stateBackup -FailedPath $stateFailed) -Message 'Deployment state recognizes BACKUP'
    Move-DirectoryAtomically -SourcePath $stateNew -DestinationPath $stateInstall
    Assert-Equal -Expected 'BACKUP' -Actual (Get-DeploymentLayoutState -InstallPath $stateInstall -NewPath $stateNew -BackupPath $stateBackup -FailedPath $stateFailed) -Message 'Backup remains authoritative until commit cleanup'
    Move-DirectoryAtomically -SourcePath $stateInstall -DestinationPath $stateFailed
    Assert-Equal -Expected 'FAILED' -Actual (Get-DeploymentLayoutState -InstallPath $stateInstall -NewPath $stateNew -BackupPath $stateBackup -FailedPath $stateFailed) -Message 'Deployment state recognizes FAILED'
    Move-DirectoryAtomically -SourcePath $stateBackup -DestinationPath $stateInstall
    Assert-Equal -Expected 'FAILED' -Actual (Get-DeploymentLayoutState -InstallPath $stateInstall -NewPath $stateNew -BackupPath $stateBackup -FailedPath $stateFailed) -Message 'Failed tree is preserved after previous deployment restore'
}
finally {
    if (Test-Path -LiteralPath $atomicTestRoot) {
        Remove-Item -LiteralPath $atomicTestRoot -Recurse -Force
    }
}

Assert-Contains -Text $installerText -Expected '[System.IO.Directory]::Move' -Message 'Installer uses the atomic directory move primitive'
Assert-Contains -Text $installerText -Expected 'Move-DirectoryAtomically' -Message 'Installer routes deployment swaps through atomic helper'
Assert-Contains -Text $installerText -Expected 'Wait-ProcessExitBounded' -Message 'Installer waits for service process exit'
Assert-Contains -Text $installerText -Expected "Write-DeploymentState -State 'FAILED'" -Message 'Installer records failed deployment state'
Assert-Contains -Text $installerText -Expected "Write-DeploymentState -State 'BACKUP'" -Message 'Installer records backup deployment state'
Assert-NotContains -Text $installerText -Unexpected 'Move-Item -LiteralPath $installPath' -Message 'Installer does not live-swap the install directory with Move-Item'
Assert-NotContains -Text $installerText -Unexpected 'Move-Item -LiteralPath $newDeployment' -Message 'Installer does not promote staging with Move-Item'
Assert-NotContains -Text $installerText -Unexpected 'Remove-Item -LiteralPath $installPath -Recurse -Force' -Message 'Installer never recursively deletes the live install before restore'
$stagingIndex = $installerText.IndexOf('New-Item -ItemType Directory -Path $newDeployment', [StringComparison]::Ordinal)
$stopIndex = $installerText.IndexOf('$serviceProcessId = Stop-ServiceBounded', [StringComparison]::Ordinal)
if ($stagingIndex -lt 0 -or $stopIndex -lt 0 -or $stagingIndex -ge $stopIndex) {
    throw 'Installer must finish staging before stopping the service.'
}

$ownershipCheckIndex = $installerText.IndexOf('$installDirectoryOwnership', [StringComparison]::Ordinal)
$serviceLookupIndex = $installerText.IndexOf('$existingService = Get-Service', [StringComparison]::Ordinal)
if ($ownershipCheckIndex -lt 0 -or $serviceLookupIndex -lt 0 -or $ownershipCheckIndex -ge $serviceLookupIndex) {
    throw 'Installer ownership validation must precede service inspection.'
}
$uninstallerOwnershipCheckIndex = $uninstallerText.IndexOf('$installDirectoryOwnership', [StringComparison]::Ordinal)
$uninstallerConfigIndex = $uninstallerText.IndexOf('$configuredDataPath =', [StringComparison]::Ordinal)
$uninstallerDeleteIndex = $uninstallerText.IndexOf('Remove-Item -LiteralPath $installPath -Recurse -Force', [StringComparison]::Ordinal)
if ($uninstallerOwnershipCheckIndex -lt 0 -or $uninstallerConfigIndex -lt 0 -or $uninstallerDeleteIndex -lt 0 -or
    $uninstallerOwnershipCheckIndex -ge $uninstallerConfigIndex -or $uninstallerOwnershipCheckIndex -ge $uninstallerDeleteIndex) {
    throw 'Uninstaller ownership validation must precede configuration inference and application deletion.'
}
Assert-Contains -Text $uninstallerText -Expected 'Get-InstallDirectoryOwnership -InstallPath $installPath' -Message 'Uninstaller checks app ownership before deletion'
Assert-Contains -Text $uninstallerText -Expected 'InstallDirectory exists but is not recognized as a Little Ages installation' -Message 'Uninstaller refuses unrelated directories'
Assert-Contains -Text $uninstallerText -Expected "Kind = 'Owned'" -Message 'Uninstaller recognizes installer-owned directories'
Assert-Contains -Text $uninstallerText -Expected "Kind = 'Legacy'" -Message 'Uninstaller recognizes legacy Little Ages directories'
Assert-Contains -Text $uninstallerText -Expected "Remove-Item -LiteralPath `$installPath -Recurse -Force" -Message 'Uninstaller recursively deletes only after ownership guard'
Assert-Contains -Text $uninstallerText -Expected '-DeleteWorldData and -ConfirmWorldDeletion' -Message 'World deletion remains explicitly guarded'
Assert-Contains -Text $uninstallerText -Expected 'Resolve-InstalledDataDirectory' -Message 'Uninstaller keeps installed data-root handling explicit'

# Capture and restore state with mocked cmdlets. The real Windows firewall is
# never queried or modified by this test.
$script:ManagedFirewallName = 'LittleAges-Private-LAN'
$script:FakeFirewallRules = @()
$script:FakePortFilter = $null
$script:FakeAddressFilter = $null
$script:RemoveCallCount = 0
$script:CreatedRules = New-Object System.Collections.Generic.List[object]

function Get-ManagedFirewallRules { return @($script:FakeFirewallRules) }
function Get-NetFirewallPortFilter { return $script:FakePortFilter }
function Get-NetFirewallAddressFilter { return $script:FakeAddressFilter }
function Remove-ManagedFirewallRules { $script:RemoveCallCount++ }
function New-NetFirewallRule {
    [CmdletBinding()]
    param(
        [string] $Name,
        [string] $DisplayName,
        [string] $Group,
        [object] $Direction,
        [object] $Action,
        [object] $Protocol,
        [object] $LocalPort,
        [object] $Profile,
        [object] $RemoteAddress,
        [object] $Enabled
    )
    $script:CreatedRules.Add([pscustomobject]@{
            Name = $Name; DisplayName = $DisplayName; Group = $Group; Direction = $Direction
            Action = $Action; Protocol = $Protocol; LocalPort = $LocalPort; Profile = $Profile
            RemoteAddress = $RemoteAddress; Enabled = $Enabled
        })
}

. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Get-ManagedFirewallState')))
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Restore-ManagedFirewallState')))

$absentState = Get-ManagedFirewallState
Assert-Equal -Expected $false -Actual $absentState.Exists -Message 'Absent firewall state is captured as absent'
$script:RemoveCallCount = 0
$script:CreatedRules.Clear()
Restore-ManagedFirewallState -State $absentState
Assert-Equal -Expected 1 -Actual $script:RemoveCallCount -Message 'Absent rollback removes managed rule'
Assert-Equal -Expected 0 -Actual $script:CreatedRules.Count -Message 'Absent rollback leaves rule absent'

$script:FakeFirewallRules = @([pscustomobject]@{
        Name = 'LittleAges-Private-LAN'; DisplayName = 'Little Ages (Private TCP 5299)'; Group = 'Little Ages'
        Enabled = 'False'; Direction = 'Inbound'; Action = 'Allow'; Profile = 'Private'
    })
$script:FakePortFilter = [pscustomobject]@{ Protocol = 'TCP'; LocalPort = '5299' }
$script:FakeAddressFilter = [pscustomobject]@{ RemoteAddress = 'LocalSubnet' }
$presentState = Get-ManagedFirewallState
Assert-Equal -Expected $true -Actual $presentState.Exists -Message 'Present firewall state is captured as present'
$capturedRule = @($presentState.Rules)[0]
Assert-Equal -Expected 'False' -Actual $capturedRule.Enabled -Message 'Firewall enabled state is captured'
Assert-Equal -Expected '5299' -Actual $capturedRule.LocalPort -Message 'Firewall local port is captured'
Assert-Equal -Expected 'Private' -Actual $capturedRule.Profile -Message 'Firewall profile is captured'
Assert-Equal -Expected 'LocalSubnet' -Actual $capturedRule.RemoteAddress -Message 'Firewall remote scope is captured'
$script:RemoveCallCount = 0
$script:CreatedRules.Clear()
Restore-ManagedFirewallState -State $presentState
Assert-Equal -Expected 1 -Actual $script:RemoveCallCount -Message 'Present rollback replaces managed rule'
Assert-Equal -Expected 1 -Actual $script:CreatedRules.Count -Message 'Present rollback restores managed rule'
$restoredRule = $script:CreatedRules[0]
Assert-Equal -Expected 'LittleAges-Private-LAN' -Actual $restoredRule.Name -Message 'Present rollback restores owned name'
Assert-Equal -Expected '5299' -Actual $restoredRule.LocalPort -Message 'Present rollback restores prior port'
Assert-Equal -Expected 'False' -Actual $restoredRule.Enabled -Message 'Present rollback restores prior enabled state'
Assert-Equal -Expected 'Private' -Actual $restoredRule.Profile -Message 'Present rollback restores prior profile'
Assert-Equal -Expected 'LocalSubnet' -Actual $restoredRule.RemoteAddress -Message 'Present rollback restores prior remote scope'
Assert-Contains -Text $installerText -Expected 'Restore-ManagedFirewallState -State $oldFirewallState' -Message 'Installer rollback uses captured firewall state'

# Exercise the service API boundary without changing the machine. Structured
# CIM arguments preserve quotes on both supported PowerShell versions.
$script:ServiceChange = $null
$script:ServiceChangeResult = 0
function Get-CimInstance {
    [CmdletBinding()]
    param(
        [string] $ClassName,
        [string] $Filter
    )
    Assert-Equal -Expected 'Win32_Service' -Actual $ClassName -Message 'Service class'
    Assert-Equal -Expected "Name='Little Ages'" -Actual $Filter -Message 'Fixed service identity'
    return [pscustomobject]@{ Name = 'Little Ages' }
}
function Invoke-CimMethod {
    [CmdletBinding()]
    param([object] $InputObject, [string] $MethodName, [hashtable] $Arguments)
    Assert-Equal -Expected 'Little Ages' -Actual $InputObject.Name -Message 'Service change identity'
    Assert-Equal -Expected 'Change' -Actual $MethodName -Message 'Service change method'
    $script:ServiceChange = $Arguments
    return [pscustomobject]@{ ReturnValue = $script:ServiceChangeResult }
}
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Set-ServiceRegistration')))
$spaceExecutable = 'C:\Program Files\LittleAges\LittleAges.Server.exe'
Set-ServiceRegistration -ExecutablePath $spaceExecutable -SetAccount
Assert-Equal -Expected ('"{0}"' -f $spaceExecutable) -Actual $script:ServiceChange.PathName -Message 'Service path with spaces remains quoted at the OS API boundary'
Assert-Equal -Expected 'Automatic' -Actual $script:ServiceChange.StartMode -Message 'Automatic startup'
Assert-Equal -Expected 'NT AUTHORITY\LocalService' -Actual $script:ServiceChange.StartName -Message 'LocalService identity'
$oldCommandLine = '"C:\Program Files\LittleAges\LittleAges.Server.exe" --ActiveWorld "my world"'
Set-ServiceRegistration -BinaryPathName $oldCommandLine -StartMode demand
Assert-Equal -Expected $oldCommandLine -Actual $script:ServiceChange.PathName -Message 'Rollback preserves the full command line'
Assert-Equal -Expected 'Manual' -Actual $script:ServiceChange.StartMode -Message 'Rollback preserves manual startup'
$script:ServiceChangeResult = 2
$changeFailed = $false
try { Set-ServiceRegistration -ExecutablePath $spaceExecutable } catch { $changeFailed = $true }
Assert-Equal -Expected $true -Actual $changeFailed -Message 'Service API failure is propagated'

foreach ($scriptPath in @($installerPath, $uninstallerPath)) {
    . ([scriptblock]::Create((Get-FunctionSource -Path $scriptPath -Name 'Assert-ServiceInstallation')))
    foreach ($commandLine in @($spaceExecutable, ('"{0}"' -f $spaceExecutable), $oldCommandLine)) {
        Assert-ServiceInstallation -Details ([pscustomobject]@{ PathName = $commandLine }) -InstallPath 'C:\Program Files\LittleAges'
    }
    foreach ($commandLine in @('C:\Other\LittleAges.Server.exe', '"C:\Other\LittleAges.Server.exe" --anything',
        'C:\Program Files\LittleAges\LittleAges.Server.exe.evil', 'LittleAges.Server.exe', '')) {
        $rejected = $false
        try { Assert-ServiceInstallation -Details ([pscustomobject]@{ PathName = $commandLine }) -InstallPath 'C:\Program Files\LittleAges' }
        catch { $rejected = $true }
        Assert-Equal -Expected $true -Actual $rejected -Message "Reject service path mismatch ($commandLine)"
    }
    $rejected = $false
    try { Assert-ServiceInstallation -Details $null -InstallPath 'C:\Program Files\LittleAges' } catch { $rejected = $true }
    Assert-Equal -Expected $true -Actual $rejected -Message 'Unreadable registration fails closed'
}

$guardIndex = $installerText.IndexOf('Assert-ServiceInstallation -Details $existingServiceDetails', [StringComparison]::Ordinal)
if ($guardIndex -lt 0 -or $guardIndex -ge $stagingIndex) { throw 'Installer must verify service ownership before staging or service mutation.' }
$guardIndex = $uninstallerText.IndexOf('Assert-ServiceInstallation -Details $serviceDetails', [StringComparison]::Ordinal)
$stopIndex = $uninstallerText.LastIndexOf('    Stop-ServiceBounded', [StringComparison]::Ordinal)
if ($guardIndex -lt 0 -or $guardIndex -ge $stopIndex) { throw 'Uninstaller must verify service ownership before stopping it.' }

# Keep workflow input interpolation out of executable PowerShell and artifact
# paths. The version may enter through env, but paths are derived after the
# package script validates it.
foreach ($line in ($workflowText -split "`r?`n")) {
    if ($line -match '^\s*(?:run|path|name):.*\$\{\{\s*inputs\.version\s*\}\}') {
        throw "Workflow directly interpolates inputs.version into executable or upload configuration: $line"
    }
}
Assert-Contains -Text $workflowText -Expected 'LITTLEAGES_PACKAGE_VERSION: ${{ inputs.version }}' -Message 'Workflow passes version through environment'
Assert-Contains -Text $workflowText -Expected '.\scripts\package-windows.ps1 -Version $version' -Message 'Workflow invokes package script with environment value'
Assert-Contains -Text $workflowText -Expected 'artifact_path=$artifactPath' -Message 'Workflow emits validated artifact path'
Assert-Contains -Text $workflowText -Expected 'path: ${{ steps.package.outputs.artifact_path }}' -Message 'Workflow uploads known artifact output'
Assert-NotContains -Text $workflowText -Unexpected 'artifacts/LittleAges-v${{ inputs.version }}-win-x64.zip' -Message 'Workflow does not build upload path from raw input'

& (Join-Path $PSScriptRoot 'test-data-directory-acl.ps1')
Write-Host 'Windows installer hardening assertions passed.'
