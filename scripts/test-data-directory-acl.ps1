[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$installer = Join-Path $PSScriptRoot 'install-windows.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($installer, [ref] $tokens, [ref] $errors)
if ($errors.Count -ne 0) { throw 'Installer did not parse.' }
foreach ($name in @('Invoke-NativeChecked', 'Set-DataDirectoryAcl')) {
    $definition = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$aclRoot = Join-Path $temporaryParent ('LittleAges-acl-' + [Guid]::NewGuid().ToString('N'))
$users = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')
$localService = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-19')
New-Item -ItemType Directory -Path $aclRoot | Out-Null
try {
    # Reproduce ProgramData's Users create-file permission on a private fixture.
    $parentAcl = Get-Acl -LiteralPath $aclRoot
    $parentAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($users, 'Write', 'ContainerInherit', 'None', 'Allow')))
    Set-Acl -LiteralPath $aclRoot -AclObject $parentAcl
    $worldRoot = Join-Path $aclRoot 'worlds'
    New-Item -ItemType Directory -Path $worldRoot | Out-Null
    $worldFile = Join-Path $worldRoot 'world.db'
    Set-Content -LiteralPath $worldFile -Value 'preserved world fixture'
    $beforeHash = (Get-FileHash -LiteralPath $worldFile).Hash
    $beforeAcl = Get-Acl -LiteralPath $worldRoot
    $beforeRules = @($beforeAcl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference -eq $users -and ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::WriteData) -ne 0 })
    if ($beforeRules.Count -eq 0) { throw 'Fixture did not inherit the unsafe create-file permission.' }

    foreach ($iteration in @(1, 2)) {
        Set-DataDirectoryAcl -Path $worldRoot
        $acl = Get-Acl -LiteralPath $worldRoot
        $rules = @($acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]))
        $unsafeRules = @($rules | Where-Object { $_.IdentityReference -eq $users -and $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Write) -ne 0 })
        if ($unsafeRules.Count -ne 0) { throw ('Ordinary Users can still create world or SQLite sidecar files: ' + ($unsafeRules | Select-Object FileSystemRights,IsInherited,InheritanceFlags | ConvertTo-Json -Compress)) }
        $serviceRules = @($rules | Where-Object { $_.IdentityReference -eq $localService -and $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Modify) -eq [System.Security.AccessControl.FileSystemRights]::Modify })
        if ($serviceRules.Count -ne 1) { throw 'LocalService must retain one effective Modify grant.' }
        if ($acl.Owner -ne $beforeAcl.Owner) { throw 'Directory ownership changed.' }
        if ((Get-FileHash -LiteralPath $worldFile).Hash -ne $beforeHash) { throw 'World contents changed.' }
        $childRules = (Get-Acl -LiteralPath $worldFile).GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])
        if (@($childRules | Where-Object { $_.IdentityReference -eq $localService -and ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Modify) -eq [System.Security.AccessControl.FileSystemRights]::Modify }).Count -eq 0) { throw 'Existing world must remain writable by LocalService.' }
    }
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($aclRoot)
    if (-not $resolvedRoot.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedRoot) -notlike 'LittleAges-acl-*') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
Write-Host 'Windows data-directory ACL assertions passed.'
