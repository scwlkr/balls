#Requires -Version 5.1

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Equal {
    param(
        [Parameter(Mandatory)] [object] $Actual,
        [Parameter(Mandatory)] [object] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Actual -cne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)] [scriptblock] $Action,
        [Parameter(Mandatory)] [string] $ExpectedMessage
    )

    $failure = $null
    try {
        & $Action
    }
    catch {
        $failure = $_
    }
    if ($null -eq $failure) {
        throw "Expected failure: $ExpectedMessage"
    }
    Assert-Equal $failure.Exception.Message $ExpectedMessage 'The failure message should match.'
}

function New-TestExecutable {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [uint16] $Machine
    )

    $bytes = [byte[]]::new(70)
    $bytes[0] = 0x4d
    $bytes[1] = 0x5a
    [BitConverter]::GetBytes([int32] 64).CopyTo($bytes, 0x3c)
    $bytes[0x40] = 0x50
    $bytes[0x41] = 0x45
    [BitConverter]::GetBytes($Machine).CopyTo($bytes, 0x44)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

$bootstrapPath = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\public\install.ps1'))
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $bootstrapPath,
    [ref] $tokens,
    [ref] $parseErrors)
if ($parseErrors.Count -ne 0) {
    throw "The Windows bootstrap did not parse: $($parseErrors.Message -join ' | ')"
}

$requiredFunctions = @(
    'Assert-OfficialManifestUri',
    'Assert-ManifestAsset',
    'Assert-PackageIdentity',
    'Assert-ChecksumBinding',
    'Test-X64PortableExecutable',
    'Get-X64DotnetRoot',
    'Test-RuntimeInventory',
    'Get-RuntimeRequirementLabel',
    'Assert-RuntimeRequirements',
    'Write-BallsLauncher'
)
foreach ($name in $requiredFunctions) {
    $definition = @($ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq $name
        }, $true))
    if ($definition.Count -ne 1) {
        throw "Expected exactly one $name function."
    }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'balls-download-runtime-test-{0}' -f [guid]::NewGuid().ToString('N'))
$oldX64Root = $env:DOTNET_ROOT_X64
$oldRoot = $env:DOTNET_ROOT
$oldPath = $env:PATH
try {
    Assert-OfficialManifestUri ([uri]'https://balls.wlkrlabs.com/channels/alpha.json')
    Assert-OfficialManifestUri ([uri]'https://balls.wlkrlabs.com/channels/development.json')
    Assert-OfficialManifestUri ([uri]'https://balls.wlkrlabs.com/versions/development-20260826T120000Z-0123456789ab.json')
    Assert-Throws {
        Assert-OfficialManifestUri ([uri]'https://example.com/channels/alpha.json')
    } 'Balls installs only from an official channel or immutable version manifest.'
    Assert-Throws {
        Assert-OfficialManifestUri ([uri]'https://balls.wlkrlabs.com/channels/alpha.json?next=development')
    } 'Balls installs only from an official channel or immutable version manifest.'

    $tag = 'development-20260826T120000Z-0123456789ab'
    $asset = [pscustomobject]@{
        name = 'balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip'
        url = "https://github.com/scwlkr/balls/releases/download/$tag/balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip"
        sha256 = ('a' * 64)
    }
    Assert-ManifestAsset $asset $tag '^balls-.+\.zip$'
    $unsafeAsset = [pscustomobject]@{
        name = '..\balls.zip'
        url = "https://github.com/scwlkr/balls/releases/download/$tag/balls.zip"
        sha256 = ('a' * 64)
    }
    Assert-Throws {
        Assert-ManifestAsset $unsafeAsset $tag '^balls-.+\.zip$'
    } 'The Balls manifest contains an invalid asset name: ..\balls.zip'

    $identity = [pscustomobject]@{
        product = 'Balls'
        version = '0.3.0-alpha.1'
        commit = '0123456789abcdef0123456789abcdef01234567'
        platform = 'windows'
        architecture = 'x64'
    }
    $packageIdentity = [pscustomobject]@{
        product = 'Balls'
        version = '0.3.0-alpha.1'
        commit = '0123456789abcdef0123456789abcdef01234567'
        platform = 'windows'
        architecture = 'x64'
        runtimeSupported = $true
    }
    Assert-PackageIdentity $packageIdentity $identity
    $wrongPackageIdentity = $packageIdentity.PSObject.Copy()
    $wrongPackageIdentity.commit = 'ffffffffffffffffffffffffffffffffffffffff'
    Assert-Throws {
        Assert-PackageIdentity $wrongPackageIdentity $identity
    } 'The Windows package identity does not match the selected Balls manifest.'

    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $checksumPath = Join-Path $testRoot 'package.sha256'
    Set-Content -LiteralPath $checksumPath -Value "$('a' * 64)  $($asset.name)"
    Assert-ChecksumBinding $checksumPath $asset
    Set-Content -LiteralPath $checksumPath -Value "$('b' * 64)  $($asset.name)"
    Assert-Throws {
        Assert-ChecksumBinding $checksumPath $asset
    } 'The package checksum file does not bind the selected Windows archive.'

    $launcher = Write-BallsLauncher $testRoot '0.3.0-alpha.1-0123456789ab'
    $launcherText = Get-Content -LiteralPath $launcher -Raw
    if ($launcherText -notmatch 'BALLS_PIPE=balls' -or
        $launcherText -notmatch 'versions\\0\.3\.0-alpha\.1-0123456789ab' -or
        $launcherText -match 'ExecutionPolicy') {
        throw 'The generated normal launcher did not preserve the supported startup contract.'
    }

    $requirements = @(
        [pscustomobject]@{ name = 'Microsoft.NETCore.App'; major = 10 },
        [pscustomobject]@{ name = 'Microsoft.AspNetCore.App'; major = 10 }
    )
    $runtimeUnderTest = [pscustomobject]@{
        kind = 'framework-dependent'
        architecture = 'x64'
        frameworks = $requirements
    }
    $usableRoot = Get-X64DotnetRoot
    $env:DOTNET_ROOT_X64 = $usableRoot
    Assert-RuntimeRequirements $runtimeUnderTest

    $x64Root = Join-Path $testRoot 'x64-root'
    $genericRoot = Join-Path $testRoot 'generic-root'
    $pathRoot = Join-Path $testRoot 'path-only-root'
    New-Item -ItemType Directory -Path $x64Root, $genericRoot, $pathRoot | Out-Null

    $x64Executable = Join-Path $x64Root 'dotnet.exe'
    $x86Executable = Join-Path $genericRoot 'dotnet.exe'
    New-TestExecutable $x64Executable 0x8664
    New-TestExecutable $x86Executable 0x014c

    Assert-Equal (Test-X64PortableExecutable $x64Executable) $true 'The x64 PE should pass.'
    Assert-Equal (Test-X64PortableExecutable $x86Executable) $false 'The x86 PE should fail.'

    $runtimeError = 'This Balls package requires the x64 .NET 10 and ASP.NET Core 10 runtimes.'
    $env:DOTNET_ROOT_X64 = $genericRoot
    Assert-Throws { Assert-RuntimeRequirements $runtimeUnderTest } $runtimeError

    $env:DOTNET_ROOT_X64 = $x64Root
    $env:DOTNET_ROOT = $genericRoot
    $env:PATH = "$pathRoot;$oldPath"
    Assert-Equal (Get-X64DotnetRoot) $x64Root 'DOTNET_ROOT_X64 must win over generic and PATH-only hosts.'

    $validInventory = @(
        'Microsoft.NETCore.App 10.0.11 [C:\dotnet\shared\Microsoft.NETCore.App]',
        'Microsoft.AspNetCore.App 10.0.11 [C:\dotnet\shared\Microsoft.AspNetCore.App]'
    )
    $wrongMajorInventory = @(
        'Microsoft.NETCore.App 9.0.11 [C:\dotnet\shared\Microsoft.NETCore.App]',
        'Microsoft.AspNetCore.App 9.0.11 [C:\dotnet\shared\Microsoft.AspNetCore.App]'
    )
    Assert-Equal (Test-RuntimeInventory $validInventory $requirements) $true 'Both required 10.x frameworks should pass.'
    Assert-Equal (Test-RuntimeInventory $validInventory @($requirements[1])) $true 'One explicitly required framework should pass.'
    Assert-Equal (Test-RuntimeInventory $wrongMajorInventory $requirements) $false 'The wrong runtime major should fail.'
    Assert-Equal (Test-RuntimeInventory @($validInventory[1]) $requirements) $false 'A missing Microsoft.NETCore.App requirement should fail.'
    Assert-Equal (Get-RuntimeRequirementLabel $requirements) '.NET 10 and ASP.NET Core 10' 'The error label should come from the manifest requirements.'

    $futureRequirements = @(
        [pscustomobject]@{ name = 'Microsoft.NETCore.App'; major = 11 },
        [pscustomobject]@{ name = 'Microsoft.AspNetCore.App'; major = 11 }
    )
    Assert-Equal (Get-RuntimeRequirementLabel $futureRequirements) '.NET 11 and ASP.NET Core 11' 'The error label should not pin the current runtime major.'

    $missingRequirements = @(
        [pscustomobject]@{ name = 'Microsoft.NETCore.App'; major = 999 },
        [pscustomobject]@{ name = 'Microsoft.AspNetCore.App'; major = 999 }
    )
    $wrongMajorRuntime = [pscustomobject]@{
        kind = 'framework-dependent'
        architecture = 'x64'
        frameworks = $missingRequirements
    }
    $env:DOTNET_ROOT_X64 = $usableRoot
    Assert-Throws {
        Assert-RuntimeRequirements $wrongMajorRuntime
    } 'This Balls package requires the x64 .NET 999 and ASP.NET Core 999 runtimes.'

    $selfContained = [pscustomobject]@{
        kind = 'self-contained'
        architecture = 'x64'
    }
    Assert-RuntimeRequirements $selfContained
}
finally {
    $env:DOTNET_ROOT_X64 = $oldX64Root
    $env:DOTNET_ROOT = $oldRoot
    $env:PATH = $oldPath
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
