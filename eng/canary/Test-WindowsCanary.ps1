[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [string] $ChecksumPath,

    [Parameter(Mandatory)]
    [string] $BootstrapPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$daemon = $null

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$installRoot = Join-Path $temporaryBase ("balls-canary-smoke-{0}" -f [guid]::NewGuid().ToString('N'))
$pipeName = "balls-canary-smoke-{0}" -f [guid]::NewGuid().ToString('N')

function Invoke-CliJson([string] $cliPath, [string[]] $arguments) {
    $output = (& $cliPath @arguments | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Canary CLI failed with exit code $LASTEXITCODE."
    }
    return $output | ConvertFrom-Json
}

function Wait-CanaryReady([string] $cliPath) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            return Invoke-CliJson $cliPath @('--output', 'json', '--pipe-name', $pipeName, 'status')
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
    }
    throw 'Restarted Windows Canary daemon did not become ready.'
}

function Request-BrowserLaunch {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(
        '.',
        $pipeName,
        [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(5000)
        $crlf = [string][char]13 + [string][char]10
        $requestText = "POST /control/v1/ui/launch HTTP/1.1" + $crlf +
            "Host: localhost" + $crlf +
            "Content-Length: 0" + $crlf +
            "Connection: close" + $crlf + $crlf
        $request = [Text.Encoding]::ASCII.GetBytes($requestText)
        $pipe.Write($request, 0, $request.Length)
        $pipe.Flush()

        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 1024, $true)
        $statusLine = $reader.ReadLine()
        if ($statusLine -notmatch '^HTTP/1\.1 200 ') {
            throw "Browser launch request failed: $statusLine"
        }
        $contentLength = $null
        $chunked = $false
        while ($true) {
            $line = $reader.ReadLine()
            if ([string]::IsNullOrEmpty($line)) {
                break
            }
            if ($line -match '^Content-Length:\s*([0-9]+)$') {
                $contentLength = [int]$Matches[1]
            }
            if ($line -match '^Transfer-Encoding:\s*chunked$') {
                $chunked = $true
            }
        }
        if ($chunked) {
            $body = [Text.StringBuilder]::new()
            while ($true) {
                $chunkLength = [Convert]::ToInt32($reader.ReadLine(), 16)
                if ($chunkLength -eq 0) {
                    break
                }
                $chunk = [char[]]::new($chunkLength)
                $offset = 0
                while ($offset -lt $chunk.Length) {
                    $offset += $reader.Read($chunk, $offset, $chunk.Length - $offset)
                }
                $null = $body.Append($chunk)
                $null = $reader.ReadLine()
            }
            return $body.ToString() | ConvertFrom-Json
        }
        if ($null -eq $contentLength) {
            return $reader.ReadToEnd() | ConvertFrom-Json
        }
        $buffer = [char[]]::new([int]$contentLength)
        $offset = 0
        while ($offset -lt $buffer.Length) {
            $read = $reader.Read($buffer, $offset, $buffer.Length - $offset)
            if ($read -eq 0) {
                throw 'Browser launch response ended before its JSON body.'
            }
            $offset += $read
        }
        return (-join $buffer) | ConvertFrom-Json
    }
    finally {
        $pipe.Dispose()
    }
}

function Test-BrowserWorkspace([string] $launchUrl, [string] $outputName) {
    $uri = [Uri]$launchUrl
    if ($uri.Scheme -ne 'http' -or $uri.Host -ne '127.0.0.1' -or
        [string]::IsNullOrWhiteSpace($uri.Fragment) -or
        -not [string]::IsNullOrWhiteSpace($uri.Query)) {
        throw "Browser launch URL is not loopback-only and fragment-capability based: $uri"
    }
    $listeners = @(Get-NetTCPConnection -OwningProcess $daemon.Id -State Listen)
    if ($listeners.Count -eq 0 -or
        $listeners.Where({ $_.LocalPort -eq $uri.Port }).Count -eq 0 -or
        $listeners.Where({ $_.LocalAddress -notin @('127.0.0.1', '::1') }).Count -ne 0) {
        throw 'Windows Canary browser listener is not exclusively loopback-bound.'
    }

    $chromeCandidates = @(
        (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Google\Chrome\Application\chrome.exe')
    )
    $chromePath = $chromeCandidates.Where({
        -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_)
    }) | Select-Object -First 1
    if ($null -eq $chromePath) {
        throw 'Google Chrome is required for the Windows Canary UI smoke.'
    }

    $browserOutput = Join-Path $installRoot "$outputName.html"
    $browserError = Join-Path $installRoot "$outputName.err"
    $profileRoot = Join-Path $installRoot "$outputName-profile"
    $startInfo = [Diagnostics.ProcessStartInfo]::new($chromePath)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
        '--headless=new',
        '--disable-gpu',
        '--no-first-run',
        "--user-data-dir=$profileRoot",
        '--virtual-time-budget=5000',
        '--dump-dom',
        $launchUrl)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $chrome = [Diagnostics.Process]::Start($startInfo)
    try {
        $standardOutput = $chrome.StandardOutput.ReadToEnd()
        $standardError = $chrome.StandardError.ReadToEnd()
        if (-not $chrome.WaitForExit(30000)) {
            $chrome.Kill($true)
            throw 'Chrome did not finish the Windows Canary UI smoke.'
        }
        [IO.File]::WriteAllText($browserOutput, $standardOutput)
        [IO.File]::WriteAllText($browserError, $standardError)
        if ($chrome.ExitCode -ne 0) {
            throw "Chrome failed the Windows Canary UI smoke with exit code $($chrome.ExitCode)."
        }
        foreach ($expected in @('Canary Circle', 'Canary Owner', 'Balls Windows Canary Smoke')) {
            if (-not $standardOutput.Contains($expected, [StringComparison]::Ordinal)) {
                throw "Windows Canary browser output did not contain '$expected'."
            }
        }
    }
    finally {
        $chrome.Dispose()
    }
}

try {
    if (-not (Test-Path -LiteralPath $BootstrapPath -PathType Leaf) -or
        [IO.Path]::GetFileName($BootstrapPath) -notmatch '^balls-bootstrap-windows-x64-[0-9a-f]{12}\.exe$') {
        throw 'The native Windows bootstrap is missing or has an invalid release identity.'
    }
    $policyBefore = [Environment]::GetEnvironmentVariable(
        'PSExecutionPolicyPreference',
        [EnvironmentVariableTarget]::Process)
    try {
        [Environment]::SetEnvironmentVariable(
            'PSExecutionPolicyPreference',
            'Restricted',
            [EnvironmentVariableTarget]::Process)
        if ((Get-ExecutionPolicy) -ne 'Restricted') {
            throw 'The Windows Canary could not establish the clean-client Restricted policy precondition.'
        }
        & $BootstrapPath `
            --package-path $PackagePath `
            --checksum-path $ChecksumPath `
            --install-root $installRoot `
            --pipe-name $pipeName `
            --node-name 'Balls Windows Canary Smoke' `
            --open-ui false `
            --create-shortcut false
        if ($LASTEXITCODE -ne 0) {
            throw "The native Windows bootstrap failed with exit code $LASTEXITCODE under Restricted policy."
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            'PSExecutionPolicyPreference',
            $policyBefore,
            [EnvironmentVariableTarget]::Process)
    }

    $pidPath = Join-Path $installRoot 'ballsd.pid'
    $daemonPid = [int](Get-Content -LiteralPath $pidPath -Raw)
    $daemon = Get-Process -Id $daemonPid
    if ($daemon.HasExited) {
        throw 'The installed Canary daemon exited after its readiness check.'
    }

    $versionRoot = Get-ChildItem -LiteralPath (Join-Path $installRoot 'versions') -Directory |
        Select-Object -First 1 -ExpandProperty FullName
    $cliPath = Join-Path $versionRoot 'balls\balls.exe'
    $daemonPath = Join-Path $versionRoot 'ballsd\ballsd.exe'
    if (-not (Test-Path -LiteralPath (Join-Path $versionRoot 'ballsd\wwwroot\index.html'))) {
        throw 'Installed Windows Canary is missing the browser bundle.'
    }

    $statusBefore = Invoke-CliJson $cliPath @('--output', 'json', '--pipe-name', $pipeName, 'status')
    $created = Invoke-CliJson $cliPath @(
        '--output', 'json', '--pipe-name', $pipeName,
        'circle', 'create', 'Canary Circle',
        '--owner', 'Canary Owner',
        '--request-id', '0198c2d8-b000-7000-8000-000000000602')
    $listedBefore = Invoke-CliJson $cliPath @('--output', 'json', '--pipe-name', $pipeName, 'circle', 'list')
    if ($statusBefore.result.node.displayName -ne 'Balls Windows Canary Smoke' -or
        $created.result.circle.name -ne 'Canary Circle' -or
        $created.result.members[0].displayName -ne 'Canary Owner' -or
        $created.result.nodes[0].id -ne $statusBefore.result.node.id -or
        $listedBefore.result.circles[0].id -ne $created.result.circle.id) {
        throw 'Windows Canary structured CLI outcome did not match the expected Circle and Node.'
    }
    $nodeId = $statusBefore.result.node.id
    $circleId = $created.result.circle.id

    $uiOutput = (& $cliPath --pipe-name $pipeName ui | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $uiOutput -ne 'Opened the local Balls workspace.') {
        throw 'balls ui did not launch the Windows Canary workspace.'
    }
    Test-BrowserWorkspace (Request-BrowserLaunch).url 'browser-before'

    $daemon.Kill($true)
    $daemon.WaitForExit()
    $daemon = $null

    $startInfo = [Diagnostics.ProcessStartInfo]::new($daemonPath)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @(
        '--data-directory', (Join-Path $installRoot 'state'),
        '--pipe-name', $pipeName,
        '--node-name', 'Renamed Windows Host')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $daemon = [Diagnostics.Process]::Start($startInfo)
    Set-Content -LiteralPath $pidPath -Value $daemon.Id -NoNewline

    $statusAfter = Wait-CanaryReady $cliPath
    $listedAfter = Invoke-CliJson $cliPath @('--output', 'json', '--pipe-name', $pipeName, 'circle', 'list')
    if ($statusAfter.result.node.id -ne $nodeId -or
        $statusAfter.result.node.displayName -ne 'Balls Windows Canary Smoke' -or
        $listedAfter.result.circles[0].id -ne $circleId) {
        throw 'Windows Canary did not preserve its Node and Circle identities across restart.'
    }
    Test-BrowserWorkspace (Request-BrowserLaunch).url 'browser-after'

    Write-Output 'Windows Canary install, structured CLI, browser, and restart smoke passed from fresh state.'
}
finally {
    if ($null -ne $daemon -and -not $daemon.HasExited) {
        $daemon.Kill($true)
        $daemon.WaitForExit()
    }
    if (Test-Path -LiteralPath $installRoot) {
        $resolvedInstallRoot = [IO.Path]::GetFullPath($installRoot)
        if (-not $resolvedInstallRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected smoke path: $resolvedInstallRoot"
        }
        Remove-Item -LiteralPath $resolvedInstallRoot -Recurse -Force
    }
}
