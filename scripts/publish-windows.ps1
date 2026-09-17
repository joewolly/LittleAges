[CmdletBinding()]
param(
    [Parameter()]
    [string] $OutputDirectory = (Join-Path -Path (Get-Location).Path -ChildPath 'artifacts\windows-publish'),

    [Parameter()]
    [string] $DotnetPath
)

$ErrorActionPreference = 'Stop'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-RequiredTool {
    param([Parameter(Mandatory)] [string] $Name)

    $command = Get-Command -Name $Name -CommandType Application -ErrorAction Stop | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($command.Source)) {
        throw "Required tool '$Name' did not resolve to an executable."
    }
    return $command.Source
}

function Test-ToolVersion {
    param([Parameter(Mandatory)] [string] $FilePath)

    Push-Location -LiteralPath $repoRoot
    try {
        & $FilePath '--version' *> $null
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
    finally {
        Pop-Location
    }
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
$serverProject = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Server\LittleAges.Server.csproj'
$frontendRoot = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Web'
$frontendDist = Join-Path -Path $frontendRoot -ChildPath 'dist'

if (-not (Test-Path -LiteralPath $serverProject -PathType Leaf)) {
    throw "Server project was not found: $serverProject"
}
if (-not (Test-Path -LiteralPath (Join-Path -Path $frontendRoot -ChildPath 'package.json') -PathType Leaf)) {
    throw "Frontend package manifest was not found: $frontendRoot"
}

$dotnet = $null
if ($PSBoundParameters.ContainsKey('DotnetPath')) {
    if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
        throw "The supplied DotnetPath was not found: $DotnetPath"
    }
    $dotnet = (Resolve-Path -LiteralPath $DotnetPath).Path
}
else {
    try {
        $dotnet = Get-RequiredTool -Name 'dotnet'
    }
    catch {
        $dotnet = $null
    }
}

# The repository pins SDK 10.0.100. On this workstation the regular dotnet
# command may be a runtime-only installation, so accept the bundled SDK when
# it is available and the regular command cannot resolve the pinned SDK.
if ([string]::IsNullOrWhiteSpace($dotnet) -or -not (Test-ToolVersion -FilePath $dotnet)) {
    $bundledDotnet = $null
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $bundledCandidate = Join-Path -Path $env:USERPROFILE -ChildPath '.codex\runtimes\dotnet-10.0.100\dotnet.exe'
        if (Test-Path -LiteralPath $bundledCandidate -PathType Leaf) {
            $bundledDotnet = (Resolve-Path -LiteralPath $bundledCandidate).Path
        }
    }
    if ([string]::IsNullOrWhiteSpace($bundledDotnet) -or -not (Test-ToolVersion -FilePath $bundledDotnet)) {
        if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
            throw "The installed dotnet command cannot resolve SDK 10.0.100 and no bundled SDK was found. Supply -DotnetPath with a compatible dotnet.exe."
        }
        throw "The supplied DotnetPath cannot resolve SDK 10.0.100: $dotnet"
    }
    Write-Host "The regular dotnet command could not resolve SDK 10.0.100; using bundled SDK: $bundledDotnet"
    $dotnet = $bundledDotnet
}

$node = Get-RequiredTool -Name 'node'
$npm = Get-RequiredTool -Name 'npm'

# Resolve a relative output against the caller's current directory while keeping
# all subsequent file operations on explicit, literal paths.
if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    $outputPath = [System.IO.Path]::GetFullPath((Join-Path -Path (Get-Location).Path -ChildPath $OutputDirectory))
}
$repoRootFullPath = [System.IO.Path]::GetFullPath($repoRoot)
$rootOfOutput = [System.IO.Path]::GetPathRoot($outputPath)
if ([string]::Equals($outputPath.TrimEnd('\', '/'), $repoRootFullPath.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($outputPath, $rootOfOutput, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a dedicated child directory, not the repository or a filesystem root: $outputPath"
}

$runId = [Guid]::NewGuid().ToString('N')
$stagingPath = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ("LittleAges-publish-$runId")
$restoreLockDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ("LittleAges-restore-lock-$runId")
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
New-Item -ItemType Directory -Path $restoreLockDirectory -Force | Out-Null

$restoreProjects = @(
    [pscustomobject]@{
        Name = 'Domain'
        Project = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Domain\LittleAges.Domain.csproj'
        CheckedInLock = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Domain\packages.lock.json'
        DisposableLock = Join-Path -Path $restoreLockDirectory -ChildPath 'Domain.packages.lock.json'
    },
    [pscustomobject]@{
        Name = 'Simulation'
        Project = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Simulation\LittleAges.Simulation.csproj'
        CheckedInLock = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Simulation\packages.lock.json'
        DisposableLock = Join-Path -Path $restoreLockDirectory -ChildPath 'Simulation.packages.lock.json'
    },
    [pscustomobject]@{
        Name = 'Persistence'
        Project = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Persistence\LittleAges.Persistence.csproj'
        CheckedInLock = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Persistence\packages.lock.json'
        DisposableLock = Join-Path -Path $restoreLockDirectory -ChildPath 'Persistence.packages.lock.json'
    },
    [pscustomobject]@{
        Name = 'Server'
        Project = $serverProject
        CheckedInLock = Join-Path -Path $repoRoot -ChildPath 'src\LittleAges.Server\packages.lock.json'
        DisposableLock = Join-Path -Path $restoreLockDirectory -ChildPath 'Server.packages.lock.json'
    }
)

function Invoke-RidRestore {
    param([Parameter(Mandatory)] [ValidateSet('--locked-mode', '--force-evaluate')] [string] $RestoreMode)

    foreach ($restoreProject in $restoreProjects) {
        Invoke-CheckedCommand -FilePath $dotnet -Arguments @(
            'restore',
            $restoreProject.Project,
            $RestoreMode,
            '--runtime',
            'win-x64',
            '--no-dependencies',
            "/p:NuGetLockFilePath=$($restoreProject.DisposableLock)"
        ) -WorkingDirectory $repoRoot
    }
}

try {
    Write-Host "Using dotnet: $dotnet"
    Write-Host "Using node: $node"
    Write-Host "Using npm: $npm"
    Invoke-CheckedCommand -FilePath $dotnet -Arguments @('--version') -WorkingDirectory $repoRoot
    Invoke-CheckedCommand -FilePath $node -Arguments @('--version') -WorkingDirectory $repoRoot
    Invoke-CheckedCommand -FilePath $npm -Arguments @('--version') -WorkingDirectory $repoRoot

    Invoke-CheckedCommand -FilePath $npm -Arguments @('ci') -WorkingDirectory $frontendRoot
    Invoke-CheckedCommand -FilePath $npm -Arguments @('run', 'build') -WorkingDirectory $frontendRoot
    if (-not (Test-Path -LiteralPath $frontendDist -PathType Container)) {
        throw "Vite build did not produce the expected dist directory: $frontendDist"
    }
    if (-not (Test-Path -LiteralPath (Join-Path -Path $frontendDist -ChildPath 'index.html') -PathType Leaf)) {
        throw "Vite build did not produce dist\index.html: $frontendDist"
    }

    foreach ($restoreProject in $restoreProjects) {
        if (-not (Test-Path -LiteralPath $restoreProject.CheckedInLock -PathType Leaf)) {
            throw "Checked-in lock file was not found: $($restoreProject.CheckedInLock)"
        }
        Copy-Item -LiteralPath $restoreProject.CheckedInLock -Destination $restoreProject.DisposableLock -Force
    }

    # Prefer the checked-in RID graph when it is complete. Each project gets its
    # own disposable lock path because project-to-project restore otherwise
    # overwrites a shared path with the dependency project's graph.
    $lockedRidRestoreSucceeded = $true
    try {
        Write-Host 'Attempting locked win-x64 restore from copied checked-in lock graphs.'
        Invoke-RidRestore -RestoreMode '--locked-mode'
    }
    catch {
        $lockedRidRestoreSucceeded = $false
        Write-Host 'Checked-in lock graphs do not contain the complete win-x64 graph.'
    }

    if (-not $lockedRidRestoreSucceeded) {
        # This is a disposable two-phase fallback. Package versions are
        # resolved from the configured feeds at publish time, so this path has
        # less reproducibility than a checked-in RID lock; the generated locks
        # are immediately locked-restored and removed in finally.
        Write-Host 'Generating disposable win-x64 lock graphs with --force-evaluate; package resolution may vary with configured feeds.'
        Invoke-RidRestore -RestoreMode '--force-evaluate'
        Write-Host 'Revalidating the generated win-x64 lock graphs in locked mode.'
        Invoke-RidRestore -RestoreMode '--locked-mode'
    }

    Invoke-CheckedCommand -FilePath $dotnet -Arguments @('publish', $serverProject, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '--no-restore', '--output', $stagingPath) -WorkingDirectory $repoRoot

    $wwwRoot = Join-Path -Path $stagingPath -ChildPath 'wwwroot'
    New-Item -ItemType Directory -Path $wwwRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $frontendDist -Force | Copy-Item -Destination $wwwRoot -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path -Path $wwwRoot -ChildPath 'index.html') -PathType Leaf)) {
        throw "The published wwwroot is missing the Vite index.html."
    }

    $outputParent = Split-Path -Path $outputPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($outputParent)) {
        New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
    }
    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Recurse -Force
    }
    Move-Item -LiteralPath $stagingPath -Destination $outputPath
    Write-Host "Windows deployment directory: $outputPath"
}
finally {
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
    if (Test-Path -LiteralPath $restoreLockDirectory) {
        Remove-Item -LiteralPath $restoreLockDirectory -Recurse -Force
    }
}
