using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Balls.Core;
using Balls.Storage.Sqlite;

namespace Balls.Daemon;

internal readonly record struct BrowserCircleFilesGrantApprovalFingerprint(string Value)
{
    internal static BrowserCircleFilesGrantApprovalFingerprint Create(
        CircleFilesContribution contribution,
        Member member,
        CircleFilesHostedFolderBinding hosted)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(hosted);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, contribution.CircleId.ToString());
        Append(hash, contribution.Id.ToString());
        Append(hash, contribution.Provider.Id.ToString());
        Append(hash, contribution.Provider.NodeId.ToString());
        Append(hash, contribution.DisplayName);
        Append(hash, ((int)contribution.Lifecycle).ToString(CultureInfo.InvariantCulture));
        Append(hash, contribution.Generation.ToString(CultureInfo.InvariantCulture));
        Append(hash, contribution.Authorization.OwnerMemberId.ToString());
        Append(hash, contribution.Authorization.AuthorityGeneration.ToString(
            CultureInfo.InvariantCulture));
        Append(hash, contribution.Authorization.Transcript);
        Append(hash, contribution.Authorization.MemberSignature);
        Append(hash, contribution.Authorization.CircleAuthoritySignature);
        Append(hash, member.Id.ToString());
        Append(hash, member.DisplayName);
        Append(hash, ((int)member.Role).ToString(CultureInfo.InvariantCulture));
        Append(hash, member.JoinedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        Append(hash, hosted.FolderPath);
        return new BrowserCircleFilesGrantApprovalFingerprint(
            Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, string value) =>
        Append(hash, Encoding.UTF8.GetBytes(value));

    private static void Append(IncrementalHash hash, byte[] value)
    {
        hash.AppendData(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value.Length)));
        hash.AppendData(value);
    }
}
