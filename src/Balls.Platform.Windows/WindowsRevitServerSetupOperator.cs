using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal sealed record WindowsRevitServerHelperRequest(
    string MediaPath,
    string PlanDigest,
    string OwnerSid);

internal sealed record WindowsRevitServerHelperResponse(
    string? Status,
    string? ErrorCode,
    string Message);

[SupportedOSPlatform("windows")]
public sealed class WindowsRevitServerSetupOperator : IRevitServerSetupOperator
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(20);
    private const int MaximumMessageBytes = 64 * 1024;
    private readonly IRevitServerReadinessInspector inspector;

    public WindowsRevitServerSetupOperator()
        : this(new WindowsRevitServerReadinessInspector())
    {
    }

    internal WindowsRevitServerSetupOperator(IRevitServerReadinessInspector inspector)
    {
        this.inspector = inspector;
    }

    public async ValueTask<RevitServerSetupPreparationResult> PrepareAsync(
        RevitServerSetupPreparationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var report = await inspector.InspectAsync(request.MediaPath, cancellationToken).ConfigureAwait(false);
        if (report.Status != RevitServerReadinessStatus.Ready || report.Snapshot is null)
        {
            throw new RevitServerSetupException(
                "setup_plan_drift",
                "The server or installer changed. Inspect the setup again before approving it.");
        }

        var digest = RevitServerSetupPlanFactory.ComputeDigest(report.Snapshot);
        if (!FixedDigestEquals(digest, request.PlanDigest))
        {
            throw new RevitServerSetupException(
                "setup_plan_drift",
                "The approved setup plan is no longer current. Inspect it again.");
        }

        var helperPath = Path.Combine(AppContext.BaseDirectory, "balls-windows-helper.exe");
        if (!File.Exists(helperPath))
        {
            throw new RevitServerSetupException("setup_helper_unavailable", "The Windows setup helper is unavailable.");
        }

        var ownerSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new RevitServerSetupException("setup_identity_unavailable", "The current Windows identity is unavailable.");
        var pipeName = $"balls-revit-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))}";
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var owner = new SecurityIdentifier(ownerSid);
        pipeSecurity.SetOwner(owner);
        pipeSecurity.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        await using var pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("--revit-pipe-name");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--server-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        using var helper = new Process { StartInfo = startInfo };
        try
        {
            if (!helper.Start())
            {
                throw new RevitServerSetupException("setup_helper_unavailable", "The Windows setup helper could not start.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new RevitServerSetupException("setup_consent_cancelled", "Windows administrator approval was cancelled.");
        }
        catch (Win32Exception)
        {
            throw new RevitServerSetupException("setup_helper_unavailable", "The Windows setup helper could not start.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        try
        {
            await WindowsCircleFilesHelperProcess.WaitForConnectionAsync(pipe, helper, timeout.Token).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetClientProcessId(pipe, out var clientPid) || clientPid != helper.Id)
            {
                throw new RevitServerSetupException("setup_helper_authentication_failed", "The elevated setup helper could not be authenticated.");
            }

            await WindowsCircleFilesHelperProtocol.WriteAsync(
                pipe,
                new WindowsRevitServerHelperRequest(request.MediaPath, digest, ownerSid),
                MaximumMessageBytes,
                timeout.Token).ConfigureAwait(false);
            var response = await WindowsCircleFilesHelperProtocol.ReadAsync<WindowsRevitServerHelperResponse>(
                pipe,
                MaximumMessageBytes,
                timeout.Token).ConfigureAwait(false);
            if (response.ErrorCode is not null)
            {
                throw new RevitServerSetupException(response.ErrorCode, response.Message);
            }

            return response.Status switch
            {
                "applied" => new(RevitServerSetupMutationStatus.Applied, response.Message),
                "already-applied" => new(RevitServerSetupMutationStatus.AlreadyApplied, response.Message),
                "restart-required" => new(RevitServerSetupMutationStatus.RestartRequired, response.Message),
                _ => throw new RevitServerSetupException("setup_helper_invalid_response", "The Windows setup helper returned an invalid response."),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RevitServerSetupException(
                "setup_helper_timeout",
                "Windows preparation did not finish within 20 minutes. Reopen Balls to inspect the resulting state.");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new RevitServerSetupException("setup_helper_invalid_response", "The Windows setup helper returned an invalid response.");
        }
    }

    public ValueTask LaunchAutodeskAsync(string mediaPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            throw new RevitServerSetupException("installer_unavailable", "Choose the official Autodesk installer again.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(mediaPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(mediaPath),
            });
            if (process is null)
            {
                throw new RevitServerSetupException("installer_launch_failed", "Windows did not start Autodesk setup.");
            }
        }
        catch (Win32Exception)
        {
            throw new RevitServerSetupException("installer_launch_failed", "Windows did not start Autodesk setup.");
        }

        return ValueTask.CompletedTask;
    }

    internal static bool FixedDigestEquals(string left, string right)
    {
        if (left.Length != 64 || right.Length != 64)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }
}

[SupportedOSPlatform("windows")]
public static class WindowsRevitServerHelperCommand
{
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(25);
    private const int MaximumMessageBytes = 64 * 1024;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 4
            || args[0] != "--revit-pipe-name"
            || string.IsNullOrWhiteSpace(args[1])
            || args[1].Length > 128
            || args[2] != "--server-pid"
            || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var serverPid)
            || serverPid <= 0
            || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            return 2;
        }

        if (!WindowsProcessIdentity.TryGetExpectedDaemonUserSid(serverPid, out var daemonUserSid))
        {
            return 3;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(MaximumLifetime);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                args[1],
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(10_000, lifetime.Token).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetServerProcessId(pipe, out var actualPid) || actualPid != serverPid)
            {
                return 3;
            }

            var request = await WindowsCircleFilesHelperProtocol.ReadAsync<WindowsRevitServerHelperRequest>(
                pipe,
                MaximumMessageBytes,
                lifetime.Token).ConfigureAwait(false);
            if (request.OwnerSid != daemonUserSid || !WindowsRevitServerSetupOperator.FixedDigestEquals(request.PlanDigest, request.PlanDigest))
            {
                await WriteErrorAsync(pipe, "setup_helper_authentication_failed", lifetime.Token).ConfigureAwait(false);
                return 4;
            }

            var report = await new WindowsRevitServerReadinessInspector()
                .InspectAsync(request.MediaPath, lifetime.Token).ConfigureAwait(false);
            if (report.Status != RevitServerReadinessStatus.Ready
                || report.Snapshot is null
                || !WindowsRevitServerSetupOperator.FixedDigestEquals(
                    RevitServerSetupPlanFactory.ComputeDigest(report.Snapshot),
                    request.PlanDigest))
            {
                await WriteErrorAsync(
                    pipe,
                    "setup_plan_drift",
                    lifetime.Token,
                    "The server or installer changed. Inspect the setup again before approving it.").ConfigureAwait(false);
                return 4;
            }

            try
            {
                var result = await WindowsRevitServerSystemOperations.ApplyAsync(lifetime.Token).ConfigureAwait(false);
                await WindowsCircleFilesHelperProtocol.WriteAsync(
                    pipe,
                    new WindowsRevitServerHelperResponse(result.Status, null, result.Message),
                    MaximumMessageBytes,
                    lifetime.Token).ConfigureAwait(false);
                return 0;
            }
            catch (RevitServerSetupException exception)
            {
                await WriteErrorAsync(pipe, exception.Code, lifetime.Token, SafeMessage(exception.Code)).ConfigureAwait(false);
                return 6;
            }
        }
        catch (OperationCanceledException)
        {
            return 7;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return 5;
        }
    }

    private static Task WriteErrorAsync(
        Stream pipe,
        string code,
        CancellationToken cancellationToken,
        string message = "The Windows setup helper refused the operation.") =>
        WindowsCircleFilesHelperProtocol.WriteAsync(
            pipe,
            new WindowsRevitServerHelperResponse(null, code, message),
            MaximumMessageBytes,
            cancellationToken).AsTask();

    private static string SafeMessage(string code) => code switch
    {
        "setup_apply_timeout" => "Windows preparation is still running. Reopen Balls and inspect the resulting state.",
        _ => "Windows preparation did not complete. Inspect the server and retry.",
    };
}

internal static class WindowsRevitServerSystemOperations
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(20);

    internal static async ValueTask<(string Status, string Message)> ApplyAsync(CancellationToken cancellationToken)
    {
        var script = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(script);

        using var process = Process.Start(start)
            ?? throw new RevitServerSetupException("setup_apply_failed", "Windows preparation could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ServerManager/DISM may still be committing features. Do not kill it.
            throw new RevitServerSetupException(
                "setup_apply_timeout",
                "Windows preparation is still running. Reopen Balls and inspect the resulting state.");
        }

        var output = await stdout.ConfigureAwait(false);
        _ = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0 || output.Length is 0 or > 16_384)
        {
            throw new RevitServerSetupException("setup_apply_failed", "Windows preparation did not complete. Inspect the server and retry.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<MutationOutput>(output)
                ?? throw new JsonException();
            return result.RestartRequired
                ? ("restart-required", "Windows installed prerequisites but requires a restart. Restart, then begin a fresh inspection.")
                : (result.Changed ? "applied" : "already-applied", "Windows and IIS are prepared. Complete Autodesk setup next.");
        }
        catch (JsonException)
        {
            throw new RevitServerSetupException("setup_apply_failed", "Windows preparation returned an invalid result.");
        }
    }

    private sealed record MutationOutput(bool Changed, bool RestartRequired);

    internal const string Script = """
        $ErrorActionPreference = 'Stop'
        Import-Module ServerManager
        $required = @(
          'Web-Server',
          'Web-Asp-Net45',
          'NET-WCF-HTTP-Activation45',
          'NET-WCF-TCP-Activation45',
          'Web-ASP',
          'Web-CGI',
          'Web-Includes',
          'Web-Mgmt-Compat',
          'Web-Metabase',
          'Web-Lgcy-Scripting',
          'Web-WMI'
        )
        $features = @(Get-WindowsFeature -Name $required)
        if ($features.Count -ne $required.Count -or @($features | Where-Object { -not $_.Name }).Count -ne 0) {
          throw 'Required Windows Server features are unavailable.'
        }
        $missing = @($features | Where-Object { -not $_.Installed } | Select-Object -ExpandProperty Name)
        $changed = $missing.Count -gt 0
        $restart = $false
        if ($missing.Count -gt 0) {
          $installed = Install-WindowsFeature -Name $missing -IncludeManagementTools -Restart:$false
          if (-not $installed.Success) { throw 'Windows Server feature installation failed.' }
          $restart = $installed.RestartNeeded -eq 'Yes'
        }

        Import-Module WebAdministration
        if (-not (Test-Path 'IIS:\Sites\Default Web Site')) {
          $webRoot = Join-Path $env:SystemDrive 'inetpub\wwwroot'
          New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
          New-Website -Name 'Default Web Site' -PhysicalPath $webRoot -Port 80 | Out-Null
          $changed = $true
        }

        $root = 'D:\RevitServer\2027'
        foreach ($path in $root, "$root\Projects", "$root\Cache") {
          if (-not (Test-Path -LiteralPath $path)) {
            New-Item -ItemType Directory -Path $path | Out-Null
            $changed = $true
          }
          $item = Get-Item -LiteralPath $path -Force
          if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Repository path is a reparse point.' }
        }
        foreach ($path in "$root\Projects", "$root\Cache") {
          $acl = Get-Acl -LiteralPath $path
          $acl.SetAccessRuleProtection($false, $true)
          $networkService = New-Object Security.AccessControl.FileSystemAccessRule(
            (New-Object Security.Principal.SecurityIdentifier('S-1-5-20')),
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
          $creatorOwner = New-Object Security.AccessControl.FileSystemAccessRule(
            (New-Object Security.Principal.SecurityIdentifier('S-1-3-0')),
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
            [Security.AccessControl.PropagationFlags]::InheritOnly,
            [Security.AccessControl.AccessControlType]::Allow)
          $acl.SetAccessRule($networkService)
          $acl.SetAccessRule($creatorOwner)
          Set-Acl -LiteralPath $path -AclObject $acl
        }

        # Autodesk's documented server-local host list. The live acceptance run verifies that
        # Autodesk setup preserves this exact file before health can pass.
        $config = 'C:\ProgramData\Autodesk\Revit Server 2027\Config'
        New-Item -ItemType Directory -Path $config -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $config 'RSN.ini'), "$env:COMPUTERNAME`r`n", (New-Object Text.UTF8Encoding($false)))

        $rules = @(
          @{ Name='Balls-RevitServer-2027-HTTP'; Protocol='TCP'; Port='80,808'; Icmp=$null },
          @{ Name='Balls-RevitServer-2027-ICMPv4'; Protocol='ICMPv4'; Port=$null; Icmp='8' }
        )
        foreach ($rule in $rules) {
          $existing = Get-NetFirewallRule -Name $rule.Name -ErrorAction SilentlyContinue
          if ($existing) {
            if ($existing.Description -ne 'Balls Revit Server 2027 setup v1') { throw 'A conflicting firewall rule exists.' }
            Remove-NetFirewallRule -Name $rule.Name
          }
          $parameters = @{
            Name=$rule.Name; DisplayName=$rule.Name; Description='Balls Revit Server 2027 setup v1';
            Direction='Inbound'; Action='Allow'; Profile='Private'; RemoteAddress='LocalSubnet'; Protocol=$rule.Protocol
          }
          if ($rule.Port) { $parameters.LocalPort = $rule.Port }
          if ($rule.Icmp) { $parameters.IcmpType = $rule.Icmp }
          New-NetFirewallRule @parameters | Out-Null
        }
        [pscustomobject]@{ Changed=$changed; RestartRequired=$restart } | ConvertTo-Json -Compress
        """;
}
