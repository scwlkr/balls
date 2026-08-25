# Two-laptop LAN Circle Files pilot

**Date:** 2026-08-25  
**Artifact commit:** `67974f2de6502d99a55378e9da5aabf5e4293cc7`  
**Artifact:** `balls-0.3.0-alpha.1-canary-windows-x64-67974f2de650.zip`  
**SHA-256:** `96e742abcf1a35efb5722d54dc88dc26471cafdeb501672997de49e5749613b5`

This is an owner-run proof on two Windows laptops connected to the same trusted private LAN. One
laptop hosts a new folder while it remains on; the other joins the Circle and maps that folder in
Explorer. This is not a release, installer, backup, or always-on service test.

Both laptops must be x64 Windows 11 machines. The current Windows host readiness contract recognizes
Windows 11 build 26100 or newer.

The binaries are an unsigned development Canary. If Windows application control blocks them, stop
and report `BLOCKED — application trust`; do not bypass or weaken the policy.

## PASS

The Balls pilot passes when:

1. the host creates one Circle and one new hosted folder;
2. the second laptop joins using the private single-use invitation;
3. the second laptop opens the folder through the guided Explorer button; and
4. both laptops create, open, edit, rename, and delete ordinary files in that folder.

A disposable Revit project may also be opened and saved from the folder. That is an observed Revit
smoke test, not a guarantee about Revit worksharing, central/local models, synchronization, or
recovery.

## Roles

- **Host laptop:** creates the Circle and folder. Administrator approval is used only for the
  explicit private-LAN firewall and Windows share/account operations.
- **Joining laptop:** launches Balls normally, pastes the invitation, and opens the folder. The
  join and mapping flow itself is not elevated.

Use a fresh extraction and the fresh state paths below. Do not reuse a prior Balls development
state or overwrite an existing pilot folder.

## Checkpoint 1 — Put the exact package on both laptops

Transfer the exact ZIP to each laptop through LocalSend, USB, or another existing channel. Do not
send the Circle invitation with a public package link.

On each laptop, open Windows PowerShell and paste the following as one complete block. It searches
the current Windows profile so redirected Downloads, Desktop, and LocalSend destinations do not
have to be guessed. If no match is found, put the ZIP somewhere below the current user's profile
and rerun the block:

```powershell
& {
  $packagePattern = 'balls-0.3.0-alpha.1-canary-windows-x64-67974f2de650*.zip'
  $packageFiles = @(Get-ChildItem -LiteralPath $env:USERPROFILE -Filter $packagePattern `
    -File -Recurse -ErrorAction SilentlyContinue)
  if ($packageFiles.Count -eq 0) { throw "Balls ZIP not found below $env:USERPROFILE" }
  $archive = ($packageFiles | Sort-Object FullName | Select-Object -First 1).FullName
  Write-Host "Using $archive"

  $expectedArchiveHash = '96E742ABCF1A35EFB5722D54DC88DC26471CAFDEB501672997DE49E5749613B5'
  $actualArchiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
  if ($actualArchiveHash -ne $expectedArchiveHash) {
    throw "Balls package hash mismatch: $actualArchiveHash"
  }

  $ballsRoot = Join-Path $env:USERPROFILE 'Balls-Pilot-67974f2'
  if (-not (Test-Path -LiteralPath $ballsRoot)) {
    Expand-Archive -LiteralPath $archive -DestinationPath $ballsRoot
  }

  $canaryPath = Join-Path $ballsRoot 'canary.json'
  $checksumPath = Join-Path $ballsRoot 'SHA256SUMS'
  if (-not (Test-Path -LiteralPath $canaryPath -PathType Leaf) `
      -or -not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Existing pilot folder is incomplete: $ballsRoot"
  }

  $canary = Get-Content -LiteralPath $canaryPath -Raw | ConvertFrom-Json
  $expectedCommit = '67974f2de6502d99a55378e9da5aabf5e4293cc7'
  if ($canary.commit -ne $expectedCommit) {
    throw "Extracted Canary commit mismatch: $($canary.commit)"
  }

  $failures = New-Object System.Collections.Generic.List[string]
  $checked = 0
  foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$') {
      throw "Invalid internal checksum entry: $line"
    }
    $expectedFileHash = $Matches[1]
    $relativePath = $Matches[2].Replace('/', [IO.Path]::DirectorySeparatorChar)
    $filePath = Join-Path $ballsRoot $relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
      $failures.Add("missing: $relativePath")
      continue
    }
    $actualFileHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
    if ($actualFileHash -ne $expectedFileHash) {
      $failures.Add("mismatch: $relativePath")
    }
    $checked++
  }
  if ($failures.Count -ne 0) {
    $failures | Select-Object -First 10
    throw "$($failures.Count) internal package file(s) failed verification."
  }
  if ($checked -ne 748) { throw "Expected 748 internal files; verified $checked." }

  Write-Host "PASS checkpoint 1 - verified $checked internal files at $ballsRoot"
  $canary | Format-List product, version, commit, platform, architecture, runtimeSupported
}
```

PASS means the archive hash, full commit identity, and all 748 packaged-file checksums match.

## Checkpoint 2 — Start the host safely

On the host laptop, first inspect the connected network in ordinary Windows PowerShell:

```powershell
Get-NetConnectionProfile |
  Format-Table InterfaceAlias, NetworkCategory, IPv4Connectivity
Get-NetIPConfiguration |
  Where-Object IPv4DefaultGateway |
  Format-Table InterfaceAlias, @{n='IPv4';e={$_.IPv4Address.IPAddress}}
```

Use the IPv4 address belonging to the trusted office/home connection. Its network category must be
`Private`. Stop if there is no single clear private-LAN address.

Open Windows PowerShell **as Administrator** on the host and create only the two pilot listener
rules plus the empty parent directory. Replace `192.168.1.20` nowhere in this block; the rules are
limited by port, program, Private profile, and LocalSubnet rather than one changing local address:

```powershell
$ballsRoot = Join-Path $env:USERPROFILE 'Balls-Pilot-67974f2'
$daemon = (Resolve-Path (Join-Path $ballsRoot 'ballsd\ballsd.exe')).Path
$ruleNames = @('Balls-Pilot-Admission-67974f2', 'Balls-Pilot-Sync-67974f2')
$existingRules = @(Get-NetFirewallRule -Name $ruleNames -ErrorAction SilentlyContinue)
if ($existingRules.Count -ne 0) { throw 'A Balls pilot listener rule already exists.' }
if (Test-Path -LiteralPath 'C:\BallsPilotData') { throw 'C:\BallsPilotData already exists.' }

New-NetFirewallRule -Name 'Balls-Pilot-Admission-67974f2' `
  -DisplayName 'Balls Pilot admission 67974f2' -Direction Inbound -Action Allow `
  -Profile Private -RemoteAddress LocalSubnet -Protocol TCP -LocalPort 46321 -Program $daemon
New-NetFirewallRule -Name 'Balls-Pilot-Sync-67974f2' `
  -DisplayName 'Balls Pilot file sync 67974f2' -Direction Inbound -Action Allow `
  -Profile Private -RemoteAddress LocalSubnet -Protocol TCP -LocalPort 46322 -Program $daemon
New-Item -ItemType Directory -Path 'C:\BallsPilotData' -ErrorAction Stop
```

Run that block once. If either named rule or `C:\BallsPilotData` already exists, stop rather than
adopting or replacing it.

Close the administrator window. In ordinary Windows PowerShell, replace the example `$hostIp`
with the private IPv4 address observed above, then start the host Node:

```powershell
$ballsRoot = Join-Path $env:USERPROFILE 'Balls-Pilot-67974f2'
$hostIp = '192.168.1.20'
$pipe = 'balls-two-laptop-host'
$state = Join-Path $env:LOCALAPPDATA 'Balls-TwoLaptopPilot-Host\state'
$daemon = Join-Path $ballsRoot 'ballsd\ballsd.exe'
$cli = Join-Path $ballsRoot 'balls\balls.exe'
$daemonArguments = @(
  '--data-directory', ('"{0}"' -f $state),
  '--pipe-name', $pipe,
  '--node-name', $env:COMPUTERNAME,
  '--admission-listen', "${hostIp}:46321",
  '--message-listen', "${hostIp}:46322"
)
Start-Process -FilePath $daemon -ArgumentList $daemonArguments -WindowStyle Minimized
Start-Sleep -Seconds 3
& $cli --pipe-name $pipe status
& $cli --pipe-name $pipe files readiness
```

The last command must say `Circle Files readiness: READY`. `NOT READY` or `UNKNOWN` is a clean
`BLOCKED` result: stop and preserve the full redacted readiness output for diagnosis. Do not change
unrelated SMB, firewall, application-control, or execution policies.

If readiness is `READY`, open the host workspace:

```powershell
& $cli --pipe-name $pipe ui
```

Create one Circle in the browser. Use a test name and your own name.

## Checkpoint 3 — Invite the second laptop

On the host workspace:

1. select **Create invitation**;
2. leave **Advanced network settings** empty;
3. select **Copy invitation**; and
4. transfer that private value directly to the second laptop.

On the joining laptop:

1. open the extracted package;
2. double-click **Open Balls.cmd**;
3. choose **Join a Circle**;
4. paste the invitation and enter the test Member name; and
5. select **Join Circle**.

If the host cannot be reached, run this on the joining laptop with the actual host address:

```powershell
Test-NetConnection -ComputerName 192.168.1.20 -Port 46321
Test-NetConnection -ComputerName 192.168.1.20 -Port 46322
```

Both tests must report `TcpTestSucceeded : True`. Never post or save the invitation in an issue,
log, screenshot, or public message.

## Checkpoint 4 — Create the host folder and Member access

After the joining Member appears on the host, paste the complete block below into the same
ordinary host PowerShell session used in Checkpoint 2. It expects exactly one Circle and exactly
one non-Owner Member in the fresh pilot state.

```powershell
$ballsRoot = Join-Path $env:USERPROFILE 'Balls-Pilot-67974f2'
$pipe = 'balls-two-laptop-host'
$cli = Join-Path $ballsRoot 'balls\balls.exe'
$folder = 'C:\BallsPilotData\Shared'

$circleEnvelope = & $cli --output json --pipe-name $pipe circle list | ConvertFrom-Json
$circles = @($circleEnvelope.result.circles)
if ($circles.Count -ne 1) { throw "Expected one pilot Circle; found $($circles.Count)." }
$circleId = $circles[0].id

$memberEnvelope = & $cli --output json --pipe-name $pipe member list --circle $circleId | ConvertFrom-Json
$joiningMembers = @($memberEnvelope.result.members | Where-Object role -eq 'member')
if ($joiningMembers.Count -ne 1) { throw "Expected one joining Member; found $($joiningMembers.Count)." }
$memberId = $joiningMembers[0].id

$contribution = (& $cli --output json --pipe-name $pipe files contribution create `
  --circle $circleId --name 'Pilot Files' | ConvertFrom-Json).result
$hostPlan = (& $cli --output json --pipe-name $pipe files host preview `
  --circle $circleId --contribution $contribution.id --path $folder | ConvertFrom-Json).result
& $cli --pipe-name $pipe files host apply `
  --circle $circleId --contribution $contribution.id --path $folder --plan $hostPlan.planId
if ($LASTEXITCODE -ne 0) { throw 'Balls host apply failed.' }

$grant = (& $cli --output json --pipe-name $pipe files grant create `
  --circle $circleId --contribution $contribution.id --member $memberId --access read-write | ConvertFrom-Json).result
$credentialPlan = (& $cli --output json --pipe-name $pipe files grant credential-preview `
  --circle $circleId --contribution $contribution.id --grant $grant.id --path $folder | ConvertFrom-Json).result
& $cli --pipe-name $pipe files grant credential-apply `
  --circle $circleId --contribution $contribution.id --grant $grant.id `
  --path $folder --plan $credentialPlan.planId
if ($LASTEXITCODE -ne 0) { throw 'Balls Member credential apply failed.' }
```

Accept only the expected Balls UAC prompts. The result must say that the dedicated host and limited
Member credential were created. No password should appear. If Windows refuses the protected helper
because of application control or script execution policy, stop and report the exact error; do not
use a bypass flag or weaken a managed policy.

## Checkpoint 5 — Open and use the folder

On the joining laptop's Balls workspace:

1. select **Check again** if the folder is still waiting;
2. select **Open shared folder in Explorer**; and
3. confirm that the mapped drive opens without entering an IP address, account, password, grant ID,
   or drive letter.

Complete this short test:

- [ ] Host creates `host-test.txt`; joining laptop opens and edits it.
- [ ] Joining laptop creates `joiner-test.txt`; host opens and edits it.
- [ ] Joining laptop renames `joiner-test.txt` and both laptops see the new name.
- [ ] Each laptop deletes one disposable test file and the other sees the deletion.
- [ ] Optional: open and save a disposable `.rvt` project; record only what was observed.

Record the final result as exactly one of:

- `PASS — two-laptop Circle Files pilot`
- `WARNING — file sharing passed; Revit observation differed`
- `BLOCKED — <checkpoint and exact safe error>`

Do not include the invitation or any provider credential in the result.
