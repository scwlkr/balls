using System.ComponentModel;
using System.Security;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

public sealed class WindowsSmbReadinessInspector : ICircleFilesReadinessInspector
{
    private static readonly string[] OrderedCheckIds =
    [
        CheckIds.WindowsPlatform,
        CheckIds.SmbServer,
        CheckIds.SmbDialect,
        CheckIds.Smb1,
        CheckIds.GuestAccess,
        CheckIds.Signing,
        CheckIds.Encryption,
        CheckIds.PrivateNetwork,
        CheckIds.FirewallScope,
    ];

    private readonly IWindowsPowerShellJsonSource source;

    public WindowsSmbReadinessInspector()
        : this(new StaticWindowsPowerShellJsonSource())
    {
    }

    internal WindowsSmbReadinessInspector(IWindowsPowerShellJsonSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async ValueTask<CircleFilesReadinessReport> InspectAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await source.QueryAsync(
                WindowsPowerShellQuery.SmbReadiness,
                cancellationToken).ConfigureAwait(false);
            return Evaluate(WindowsSmbReadinessJson.Parse(json));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedInspectionFailure(exception))
        {
            return InspectionFailed();
        }
    }

    private static CircleFilesReadinessReport Evaluate(WindowsSmbReadinessObservation observation)
    {
        CircleFilesReadinessCheck[] checks =
        [
            EvaluatePlatform(observation.System),
            EvaluateSmbServer(observation.Services, observation.SmbServer),
            EvaluateDialect(observation.SmbServer),
            EvaluateSmb1(observation.SmbServer),
            EvaluateGuestAccess(observation.SmbClient),
            EvaluateSigning(observation.SmbServer),
            EvaluateEncryption(observation.SmbServer),
            EvaluatePrivateNetwork(observation.Network),
            EvaluateFirewall(observation.Services, observation.Firewall),
        ];

        return new CircleFilesReadinessReport(
            CircleFilesReadinessProviders.WindowsSmb311,
            Aggregate(checks),
            checks);
    }

    private static CircleFilesReadinessCheck EvaluatePlatform(WindowsSystemObservation? value)
    {
        if (value?.BuildNumber is null || string.IsNullOrWhiteSpace(value.InstallationType))
        {
            return Unknown(CheckIds.WindowsPlatform, "windows_version_unknown", "Windows did not report a recognized product version.");
        }

        if (value.BuildNumber < 26100)
        {
            return NotReady(CheckIds.WindowsPlatform, "windows_version_unsupported", "Windows 11 24H2 or Windows Server 2025 is required.");
        }

        if (value.InstallationType is not ("Client" or "Server" or "Server Core"))
        {
            return NotReady(CheckIds.WindowsPlatform, "windows_edition_unsupported", "This Windows edition is not approved for SMB 3.1.1 hosting.");
        }

        return Ready(CheckIds.WindowsPlatform, "windows_platform_supported", "This Windows version supports the required SMB hosting controls.");
    }

    private static CircleFilesReadinessCheck EvaluateSmbServer(
        WindowsServiceObservation? services,
        WindowsSmbServerObservation? server)
    {
        if (string.IsNullOrWhiteSpace(services?.LanmanServer))
        {
            return Unknown(CheckIds.SmbServer, "smb_server_state_unknown", "Windows did not report the SMB server service state.");
        }

        if (!string.Equals(services.LanmanServer, "Running", StringComparison.Ordinal))
        {
            return IsKnownServiceState(services.LanmanServer)
                ? NotReady(CheckIds.SmbServer, "smb_server_unavailable", "The Windows SMB server service is not running.")
                : Unknown(CheckIds.SmbServer, "smb_server_state_unknown", "Windows reported an unrecognized SMB server service state.");
        }

        return server?.EnableSmb2Protocol switch
        {
            true => Ready(CheckIds.SmbServer, "smb_server_available", "The Windows SMB 2/3 server is available."),
            false => NotReady(CheckIds.SmbServer, "smb2_disabled", "SMB 2/3 hosting is disabled."),
            null => Unknown(CheckIds.SmbServer, "smb2_state_unknown", "Windows did not report whether SMB 2/3 hosting is enabled."),
        };
    }

    private static CircleFilesReadinessCheck EvaluateDialect(WindowsSmbServerObservation? server) =>
        server?.MaximumDialect switch
        {
            WindowsSmbDialect.Smb311 or WindowsSmbDialect.NoLimit =>
                Ready(CheckIds.SmbDialect, "smb311_available", "The SMB server can negotiate SMB 3.1.1."),
            WindowsSmbDialect.Smb202 or WindowsSmbDialect.Smb210 or WindowsSmbDialect.Smb300 or WindowsSmbDialect.Smb302 =>
                NotReady(CheckIds.SmbDialect, "smb311_unavailable", "The SMB server maximum dialect is below SMB 3.1.1."),
            _ => Unknown(CheckIds.SmbDialect, "smb_dialect_unknown", "Windows did not report a recognized maximum SMB dialect."),
        };

    private static CircleFilesReadinessCheck EvaluateSmb1(WindowsSmbServerObservation? server) =>
        server?.EnableSmb1Protocol switch
        {
            false => Ready(CheckIds.Smb1, "smb1_disabled", "SMB 1 hosting is disabled."),
            true => NotReady(CheckIds.Smb1, "smb1_enabled", "SMB 1 hosting must be disabled."),
            null => Unknown(CheckIds.Smb1, "smb1_state_unknown", "Windows did not report whether SMB 1 hosting is enabled."),
        };

    private static CircleFilesReadinessCheck EvaluateGuestAccess(WindowsSmbClientObservation? client) =>
        client?.EnableInsecureGuestLogons switch
        {
            false => Ready(CheckIds.GuestAccess, "insecure_guest_disabled", "Insecure SMB guest logons are disabled."),
            true => NotReady(CheckIds.GuestAccess, "insecure_guest_enabled", "Insecure SMB guest logons must be disabled."),
            null => Unknown(CheckIds.GuestAccess, "insecure_guest_state_unknown", "Windows did not report the insecure guest logon policy."),
        };

    private static CircleFilesReadinessCheck EvaluateSigning(WindowsSmbServerObservation? server) =>
        server?.RequireSecuritySignature switch
        {
            true => Ready(CheckIds.Signing, "smb_signing_required", "The SMB server requires signing."),
            false => NotReady(CheckIds.Signing, "smb_signing_not_required", "The SMB server must require signing."),
            null => Unknown(CheckIds.Signing, "smb_signing_state_unknown", "Windows did not report the SMB signing requirement."),
        };

    private static CircleFilesReadinessCheck EvaluateEncryption(WindowsSmbServerObservation? server)
    {
        if (server?.RejectUnencryptedAccess is false)
        {
            return NotReady(CheckIds.Encryption, "unencrypted_access_accepted", "The SMB server must reject unencrypted access to encrypted shares.");
        }

        if (server?.ShareEncryptionSupported is false)
        {
            return NotReady(CheckIds.Encryption, "share_encryption_unavailable", "This Windows configuration cannot require encryption on a new SMB share.");
        }

        if (server?.EncryptionCiphers is { } ciphers
            && !ciphers.Any(cipher => cipher is "AES_128_GCM" or "AES_256_GCM"))
        {
            return NotReady(CheckIds.Encryption, "smb311_encryption_cipher_unavailable", "No approved SMB 3.1.1 encryption cipher is available.");
        }

        if (server?.RejectUnencryptedAccess is null
            || server.ShareEncryptionSupported is null
            || server.EncryptionCiphers is null)
        {
            return Unknown(CheckIds.Encryption, "smb_encryption_state_unknown", "Windows did not report all required SMB encryption controls.");
        }

        return Ready(CheckIds.Encryption, "smb_encryption_enforceable", "A new SMB share can require SMB 3.1.1 encryption and reject unencrypted access.");
    }

    private static CircleFilesReadinessCheck EvaluatePrivateNetwork(WindowsNetworkObservation? network) =>
        network?.ConnectedPrivateProfiles switch
        {
            > 0 => Ready(CheckIds.PrivateNetwork, "private_network_available", "A connected private Windows network profile is available."),
            0 => NotReady(CheckIds.PrivateNetwork, "private_network_unavailable", "No connected private Windows network profile is available."),
            _ => Unknown(CheckIds.PrivateNetwork, "network_profile_unknown", "Windows did not report the connected private network scope."),
        };

    private static CircleFilesReadinessCheck EvaluateFirewall(
        WindowsServiceObservation? services,
        WindowsFirewallObservation? firewall)
    {
        if (string.IsNullOrWhiteSpace(services?.WindowsFirewall))
        {
            return Unknown(CheckIds.FirewallScope, "windows_firewall_state_unknown", "Windows did not report the firewall service state.");
        }

        if (!string.Equals(services.WindowsFirewall, "Running", StringComparison.Ordinal))
        {
            return IsKnownServiceState(services.WindowsFirewall)
                ? NotReady(CheckIds.FirewallScope, "windows_firewall_unavailable", "Windows Firewall is not running.")
                : Unknown(CheckIds.FirewallScope, "windows_firewall_state_unknown", "Windows reported an unrecognized firewall service state.");
        }

        if (firewall?.PrivateEnabled is false)
        {
            return NotReady(CheckIds.FirewallScope, "private_firewall_disabled", "The private Windows Firewall profile must be enabled.");
        }

        if (firewall?.PublicEnabled is false)
        {
            return NotReady(CheckIds.FirewallScope, "public_firewall_disabled", "The public Windows Firewall profile must be enabled.");
        }

        if (firewall?.PrivateDefaultInboundAction is { } privateAction
            && !string.Equals(privateAction, "Block", StringComparison.Ordinal))
        {
            return NotReady(CheckIds.FirewallScope, "private_inbound_not_blocked", "The private Windows Firewall profile must block inbound traffic by default.");
        }

        if (firewall?.PublicDefaultInboundAction is { } publicAction
            && !string.Equals(publicAction, "Block", StringComparison.Ordinal))
        {
            return NotReady(CheckIds.FirewallScope, "public_inbound_not_blocked", "The public Windows Firewall profile must block inbound traffic by default.");
        }

        if (firewall?.PublicSmbInboundAllowRules > 0)
        {
            return NotReady(CheckIds.FirewallScope, "public_smb_inbound_allowed", "An enabled public-profile inbound rule allows SMB traffic.");
        }

        if (firewall?.PublicSmbInboundAllowRules < 0)
        {
            return Unknown(CheckIds.FirewallScope, "firewall_scope_unknown", "Windows reported an invalid public SMB firewall rule count.");
        }

        if (firewall?.PrivateEnabled is null
            || firewall.PublicEnabled is null
            || firewall.PrivateDefaultInboundAction is null
            || firewall.PublicDefaultInboundAction is null
            || firewall.PublicSmbInboundAllowRules is null)
        {
            return Unknown(CheckIds.FirewallScope, "firewall_scope_unknown", "Windows did not report all required private and public firewall controls.");
        }

        return Ready(CheckIds.FirewallScope, "private_firewall_scope_enforceable", "Windows Firewall can allow SMB only on the private profile while public inbound traffic remains blocked.");
    }

    private static CircleFilesReadinessReport InspectionFailed()
    {
        var checks = OrderedCheckIds
            .Select(id => Unknown(id, "inspection_failed", "Windows readiness inspection did not complete."))
            .ToArray();
        return new CircleFilesReadinessReport(
            CircleFilesReadinessProviders.WindowsSmb311,
            CircleFilesReadinessStatus.Unknown,
            checks);
    }

    private static CircleFilesReadinessStatus Aggregate(
        IEnumerable<CircleFilesReadinessCheck> checks)
    {
        var statuses = checks.Select(check => check.Status).ToArray();
        if (statuses.Contains(CircleFilesReadinessStatus.NotReady))
        {
            return CircleFilesReadinessStatus.NotReady;
        }

        return statuses.Contains(CircleFilesReadinessStatus.Unknown)
            ? CircleFilesReadinessStatus.Unknown
            : CircleFilesReadinessStatus.Ready;
    }

    private static bool IsKnownServiceState(string value) => value is
        "Stopped" or "StartPending" or "StopPending" or "ContinuePending" or "PausePending" or "Paused";

    private static CircleFilesReadinessCheck Ready(string id, string code, string summary) =>
        new(id, CircleFilesReadinessStatus.Ready, code, summary);

    private static CircleFilesReadinessCheck NotReady(string id, string code, string summary) =>
        new(id, CircleFilesReadinessStatus.NotReady, code, summary);

    private static CircleFilesReadinessCheck Unknown(string id, string code, string summary) =>
        new(id, CircleFilesReadinessStatus.Unknown, code, summary);

    private static bool IsExpectedInspectionFailure(Exception exception) => exception is
        WindowsInspectionException or
        Win32Exception or
        IOException or
        UnauthorizedAccessException or
        InvalidOperationException or
        SecurityException or
        JsonException;

    private static class CheckIds
    {
        internal const string WindowsPlatform = "windows-platform";
        internal const string SmbServer = "smb-server";
        internal const string SmbDialect = "smb-dialect";
        internal const string Smb1 = "smb1";
        internal const string GuestAccess = "guest-access";
        internal const string Signing = "signing";
        internal const string Encryption = "encryption";
        internal const string PrivateNetwork = "private-network";
        internal const string FirewallScope = "firewall-scope";
    }
}
