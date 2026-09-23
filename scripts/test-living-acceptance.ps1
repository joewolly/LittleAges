param(
    [string]$DotNetPath = 'dotnet',
    [string]$OutputDirectory = '',
    [switch]$SkipBuild,
    [ValidateSet('v02-rng1-living1', 'v02-rng1-living2')]
    [string]$Rules = 'v02-rng1-living1'
)
$ErrorActionPreference = 'Stop'

function Assert-CenturyAcceptanceQuality {
    param(
        [Parameter(Mandatory)]$Report,
        [Parameter(Mandatory)][string]$Rules
    )

    if (-not $Report.acceptance.equivalent -or $Report.maximumAncestryDepth -lt 2) {
        throw 'Century acceptance requires identical SQLite continuation and a second descendant generation.'
    }
    if ($Rules -eq 'v02-rng1-living2') {
        if ($null -eq $Report.livingCitizens -or [int]$Report.livingCitizens -le 0) {
            throw 'Living2 century acceptance requires seed 42 to remain populated at year 100.'
        }
        if ($null -eq $Report.shortageStarts -or $null -eq $Report.shortageEnds) {
            throw 'Living2 century acceptance requires shortage start and end counts in the report.'
        }
        if ([int]$Report.shortageStarts -gt [int]$Report.shortageEnds) {
            throw 'Living2 century acceptance requires all food shortages to be resolved by year 100.'
        }
    }
}

$repository = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository ('artifacts/living-settlement/' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$runnerDirectory = Join-Path $outputRoot 'runtime'
if (Test-Path -LiteralPath $runnerDirectory) { throw "Acceptance runtime already exists: $runnerDirectory" }
if (-not $SkipBuild) {
    & $DotNetPath restore (Join-Path $repository 'LittleAges.sln') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & $DotNetPath build (Join-Path $repository 'src/LittleAges.Headless') -c Release --no-restore --output $runnerDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
else {
    Copy-Item -LiteralPath (Join-Path $repository 'src/LittleAges.Headless/bin/Release/net10.0') -Destination $runnerDirectory -Recurse
}
$runner = Join-Path $runnerDirectory 'LittleAges.Headless.dll'
$runs = foreach ($seed in @(42, 7, 12345)) {
    $command = if ($seed -eq 42) { 'acceptance' } else { 'run' }
    $years = if ($seed -eq 42) { 100 } else { 10 }
    $runnerArguments = @(('"' + $runner + '"'), $command, '--seed', "$seed", '--years', "$years",
        '--rules', $Rules, '--output', ('"' + $outputRoot + '"'))
    if ($seed -eq 42) {
        $database = Join-Path $outputRoot 'century.db'
        if (Test-Path -LiteralPath $database) { throw "Acceptance database already exists: $database" }
        $runnerArguments += @('--checkpoint-year', '50', '--database', ('"' + $database + '"'))
    }
    $progress = Join-Path $outputRoot "seed-$seed-progress.jsonl"
    $process = Start-Process -FilePath $DotNetPath -ArgumentList $runnerArguments -WorkingDirectory $repository -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $outputRoot "seed-$seed-output.json") -RedirectStandardError $progress
    [pscustomobject]@{ Seed = $seed; Years = $years; Command = $command; Process = $process; Progress = $progress; ReportedLines = 0 }
}
$runs | Select-Object Seed, Years, @{ Name='ProcessId'; Expression={ $_.Process.Id } } | ConvertTo-Json | Set-Content (Join-Path $outputRoot 'processes.json')
while ($runs.Where({ -not $_.Process.HasExited }).Count -gt 0) {
    Start-Sleep -Seconds 10
    foreach ($run in $runs) {
        $lines = @(Get-Content -LiteralPath $run.Progress)
        if ($lines.Count -gt $run.ReportedLines) {
            # A read can overlap a redirected write. Retry an incomplete last
            # line on the next poll; the final process exit remains authoritative.
            try { $sample = $lines[-1] | ConvertFrom-Json -ErrorAction Stop }
            catch { continue }
            Write-Host "Seed $($run.Seed) ($($sample.run)): year $($sample.year), population $($sample.population), completed work $($sample.CompletedOrders)"
            $run.ReportedLines = $lines.Count
        }
    }
}
$summaries = foreach ($run in $runs) {
    $run.Process.WaitForExit()
    if ($run.Process.ExitCode -ne 0) { throw "Seed $($run.Seed) exited with $($run.Process.ExitCode). See $($run.Progress)." }
    $reportPath = Join-Path $outputRoot "headless-$($run.Command)-seed-$($run.Seed)-years-$($run.Years).json"
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $report.mandatoryInvariantsPassed) { throw "Seed $($run.Seed) failed canonical invariants." }
    if ($run.Seed -eq 42) { Assert-CenturyAcceptanceQuality -Report $report -Rules $Rules }
    [pscustomobject]@{ Seed=$run.Seed; Years=$run.Years; Population=$report.livingCitizens; Births=$report.births; Deaths=$report.deaths; Extinct=($report.livingCitizens -eq 0); AncestryDepth=$report.maximumAncestryDepth; Fingerprint=$report.historyFingerprint; Invariants=$report.mandatoryInvariantsPassed }
}
$summaries | ConvertTo-Json | Set-Content (Join-Path $outputRoot 'summary.json')
$summaries | Format-Table
Write-Host "Acceptance artifacts: $outputRoot"
