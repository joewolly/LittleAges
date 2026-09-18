[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [Parameter()]
    [string] $ArtifactDirectory = (Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'artifacts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$publishScript = Join-Path -Path $PSScriptRoot -ChildPath 'publish-windows.ps1'
$installScript = Join-Path -Path $PSScriptRoot -ChildPath 'install-windows.ps1'
$uninstallScript = Join-Path -Path $PSScriptRoot -ChildPath 'uninstall-windows.ps1'
$readmeSource = Join-Path -Path $PSScriptRoot -ChildPath 'README-install.txt'
foreach ($required in @($publishScript, $installScript, $uninstallScript, $readmeSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Packaging input was not found: $required" }
}

if ([System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $artifactPath = [System.IO.Path]::GetFullPath($ArtifactDirectory)
}
else {
    $artifactPath = [System.IO.Path]::GetFullPath((Join-Path -Path (Get-Location).Path -ChildPath $ArtifactDirectory))
}
$artifactPath = $artifactPath.TrimEnd('\', '/')
$root = [System.IO.Path]::GetPathRoot($artifactPath)
if ([string]::Equals($artifactPath.TrimEnd('\', '/'), $root.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($artifactPath.TrimEnd('\', '/'), $repoRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactDirectory must be a dedicated directory: $artifactPath"
}

$artifactName = 'LittleAges-v{0}-win-x64.zip' -f $Version
$artifactFile = Join-Path -Path $artifactPath -ChildPath $artifactName
$runId = [Guid]::NewGuid().ToString('N')
$publishPath = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('LittleAges-package-publish-' + $runId)
$packageParent = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('LittleAges-package-' + $runId)
$packageRoot = Join-Path -Path $packageParent -ChildPath ('LittleAges-v{0}-win-x64' -f $Version)

New-Item -ItemType Directory -Path $packageParent -Force | Out-Null
try {
    # Keep publish logic in one place. The publish script performs the locked
    # restore, frontend build, self-contained RID publish, and wwwroot copy.
    & $publishScript -OutputDirectory $publishPath
    if ($LASTEXITCODE -ne 0) { throw "Windows publish failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath (Join-Path -Path $publishPath -ChildPath 'LittleAges.Server.exe') -PathType Leaf)) {
        throw 'Self-contained publish did not produce LittleAges.Server.exe.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path -Path $publishPath -ChildPath 'wwwroot\index.html') -PathType Leaf)) {
        throw 'Self-contained publish did not produce wwwroot\index.html.'
    }

    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $publishPath -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $packageRoot -Recurse -Force
    }
    Copy-Item -LiteralPath $installScript -Destination (Join-Path -Path $packageRoot -ChildPath 'install.ps1') -Force
    Copy-Item -LiteralPath $uninstallScript -Destination (Join-Path -Path $packageRoot -ChildPath 'uninstall.ps1') -Force
    Copy-Item -LiteralPath $readmeSource -Destination (Join-Path -Path $packageRoot -ChildPath 'README-install.txt') -Force

    New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
    if (Test-Path -LiteralPath $artifactFile) { Remove-Item -LiteralPath $artifactFile -Force }
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $artifactFile -CompressionLevel Optimal

    $archive = Get-Item -LiteralPath $artifactFile
    $hash = Get-FileHash -LiteralPath $artifactFile -Algorithm SHA256
    Write-Host ''
    Write-Host ('Windows package: {0}' -f $archive.FullName)
    Write-Host ('Size:            {0} bytes' -f $archive.Length)
    Write-Host ('SHA-256:         {0}' -f $hash.Hash)
    Write-Host ('Package root:    LittleAges-v{0}-win-x64' -f $Version)
    Write-Host 'Contents:        LittleAges.Server.exe, wwwroot\, install.ps1, uninstall.ps1, README-install.txt'
}
finally {
    if (Test-Path -LiteralPath $publishPath) { Remove-Item -LiteralPath $publishPath -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $packageParent) { Remove-Item -LiteralPath $packageParent -Recurse -Force -ErrorAction SilentlyContinue }
}
