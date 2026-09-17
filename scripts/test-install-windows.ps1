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
$workflowPath = Join-Path -Path $PSScriptRoot -ChildPath '..\.github\workflows\windows-package.yml'
$installerText = Get-Content -LiteralPath $installerPath -Raw
$workflowText = Get-Content -LiteralPath $workflowPath -Raw

# Parse the complete installer first, then load only the pure/isolated helper
# functions so these assertions never invoke administrator, service, or
# firewall operations.
$installerTokens = $null
$installerParseErrors = $null
[void] [System.Management.Automation.Language.Parser]::ParseFile($installerPath, [ref] $installerTokens, [ref] $installerParseErrors)
if ($installerParseErrors.Count -gt 0) {
    throw "PowerShell parse failed for ${installerPath}: $($installerParseErrors[0].Message)"
}

. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Resolve-EffectiveLan')))

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

# Exercise the service argument construction with the production path that
# contains spaces, while intercepting the native call before it reaches sc.exe.
$script:NativeInvocation = $null
function Invoke-NativeChecked {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $Operation
    )
    $script:NativeInvocation = [pscustomobject]@{ FilePath = $FilePath; Arguments = $Arguments; Operation = $Operation }
}
. ([scriptblock]::Create((Get-FunctionSource -Path $installerPath -Name 'Set-ServiceRegistration')))
$spaceExecutable = 'C:\Program Files\LittleAges\LittleAges.Server.exe'
Set-ServiceRegistration -Name 'Little Ages' -ExecutablePath $spaceExecutable -SetAccount
Assert-Equal -Expected ('"{0}"' -f $spaceExecutable) -Actual $script:NativeInvocation.Arguments[3] -Message 'Service path with spaces remains quoted'

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

Write-Host 'Windows installer hardening assertions passed.'
