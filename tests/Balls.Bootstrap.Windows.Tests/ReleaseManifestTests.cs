using System.Text;
using Balls.Bootstrap.Windows;

namespace Balls.Bootstrap.Windows.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class ReleaseManifestTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [TestMethod]
    public void Exact_official_manifest_is_accepted()
    {
        var manifest = ValidManifest().Replace(
            "\"platforms\": {",
            "\"platforms\": { \"macos-arm64\": { \"delivery\": \"source-only\" },",
            StringComparison.Ordinal);
        var release = ReleaseManifestReader.Read(Encoding.UTF8.GetBytes(manifest));

        Assert.AreEqual("development", release.Channel);
        Assert.AreEqual(Commit, release.Commit);
        Assert.AreEqual("self-contained", release.Runtime.Kind);
        Assert.AreEqual("balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip", release.Archive.Name);
    }

    [TestMethod]
    public void Redirected_assets_and_substituted_identity_fail_closed()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseManifestReader.Read(
            Encoding.UTF8.GetBytes(ValidManifest().Replace(
                "https://github.com/scwlkr/balls/releases/download/",
                "https://example.com/",
                StringComparison.Ordinal))));
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseManifestReader.Read(
            Encoding.UTF8.GetBytes(ValidManifest().Replace(
                $"\"commit\": \"{Commit}\",\n        \"platform\"",
                $"\"commit\": \"{new string('f', 40)}\",\n        \"platform\"",
                StringComparison.Ordinal))));
    }

    [TestMethod]
    public void Only_official_channel_and_version_manifest_uris_are_accepted()
    {
        ReleaseManifestReader.ValidateOfficialManifestUri(
            new Uri("https://balls.wlkrlabs.com/channels/development.json"));
        ReleaseManifestReader.ValidateOfficialManifestUri(
            new Uri("https://balls.wlkrlabs.com/versions/development-test.json"));

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseManifestReader.ValidateOfficialManifestUri(
                new Uri("https://example.com/channels/development.json")));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseManifestReader.ValidateOfficialManifestUri(
                new Uri("https://balls.wlkrlabs.com/channels/development.json?changed=1")));
    }

    private static string ValidManifest() => $$"""
        {
          "schemaVersion": 1,
          "channel": "development",
          "release": {
            "tag": "development-20260826T120000Z-0123456789ab",
            "commit": "{{Commit}}",
            "publishedAt": "2026-08-26T12:00:00Z",
            "url": "https://github.com/scwlkr/balls/releases/tag/development-20260826T120000Z-0123456789ab",
            "unsigned": true
          },
          "platforms": {
            "windows-x64": {
              "delivery": "package",
              "identity": {
                "product": "Balls",
                "version": "0.3.0-alpha.1",
                "commit": "{{Commit}}",
                "platform": "windows",
                "architecture": "x64"
              },
              "runtime": { "kind": "self-contained", "architecture": "x64" },
              "archive": {
                "name": "balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip",
                "url": "https://github.com/scwlkr/balls/releases/download/development-20260826T120000Z-0123456789ab/balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip",
                "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
              },
              "checksum": {
                "name": "balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip.sha256",
                "url": "https://github.com/scwlkr/balls/releases/download/development-20260826T120000Z-0123456789ab/balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip.sha256",
                "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
              },
              "installer": {
                "name": "Install-BallsCanary.ps1",
                "url": "https://github.com/scwlkr/balls/releases/download/development-20260826T120000Z-0123456789ab/Install-BallsCanary.ps1",
                "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
              }
            }
          }
        }
        """;
}
