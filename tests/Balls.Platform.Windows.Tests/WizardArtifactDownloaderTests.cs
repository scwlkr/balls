using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class WizardArtifactDownloaderTests
{
    [TestMethod]
    public void Artifact_manifest_pins_the_accepted_immutable_sources_and_digests()
    {
        Assert.AreEqual("b10516", WindowsBallsWizardArtifacts.Runtime.Version);
        Assert.AreEqual(18_506_923, WindowsBallsWizardArtifacts.Runtime.SizeBytes);
        Assert.AreEqual(
            "fbbbc55e0eb2e1b07f9dcb9488616c98ed47d9003b90e15e7c8c7812c4307cd3",
            WindowsBallsWizardArtifacts.Runtime.Sha256);
        StringAssert.Contains(
            WindowsBallsWizardArtifacts.Runtime.Source.AbsoluteUri,
            "/releases/download/b10516/llama-b10516-bin-win-cpu-x64.zip");

        Assert.AreEqual(
            "675cff42a74c774d6cb76f76d8eacb49b48c9b93",
            WindowsBallsWizardArtifacts.Model.Version);
        Assert.AreEqual(3_349_516_256, WindowsBallsWizardArtifacts.Model.SizeBytes);
        Assert.AreEqual(
            "fa401b55b07ee70a54c6dae3903c783a6e65064312529ea57175cb5f8dec6634",
            WindowsBallsWizardArtifacts.Model.Sha256);
        StringAssert.Contains(
            WindowsBallsWizardArtifacts.Model.Source.AbsoluteUri,
            "/675cff42a74c774d6cb76f76d8eacb49b48c9b93/gemma-4-E2B_q4_0-it.gguf");
    }

    [TestMethod]
    public void Support_contract_accepts_only_Windows_11_x64_client_processes()
    {
        Assert.IsTrue(WindowsBallsWizardSupport.IsSupported(
            22_000,
            "Client",
            Architecture.X64,
            Architecture.X64));
        Assert.IsFalse(WindowsBallsWizardSupport.IsSupported(
            26_100,
            "Server",
            Architecture.X64,
            Architecture.X64));
        Assert.IsFalse(WindowsBallsWizardSupport.IsSupported(
            19_045,
            "Client",
            Architecture.X64,
            Architecture.X64));
        Assert.IsFalse(WindowsBallsWizardSupport.IsSupported(
            26_100,
            "Client",
            Architecture.Arm64,
            Architecture.X64));
    }

    [TestMethod]
    public async Task Download_resumes_a_partial_and_activates_only_the_verified_artifact()
    {
        using var directory = new TemporaryDirectory();
        var content = "wizard-bytes"u8.ToArray();
        var artifact = CreateArtifact(content);
        var finalPath = Path.Combine(directory.Path, "model.gguf");
        await File.WriteAllBytesAsync(finalPath + ".partial", content[..4]);
        var handler = new RangeHandler(content);
        using var client = new HttpClient(handler);
        var progress = new List<BallsWizardInstallProgress>();

        await new WizardArtifactDownloader(client).DownloadAsync(
            artifact,
            finalPath,
            new Progress<BallsWizardInstallProgress>(item => progress.Add(item)),
            CancellationToken.None);

        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(finalPath));
        Assert.IsFalse(File.Exists(finalPath + ".partial"));
        Assert.AreEqual(4, handler.RequestedRangeStart);
        Assert.IsTrue(
            await WizardArtifactDownloader.IsVerifiedAsync(
                artifact,
                finalPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Hash_mismatch_never_activates_the_download()
    {
        using var directory = new TemporaryDirectory();
        var content = "wizard-bytes"u8.ToArray();
        var artifact = CreateArtifact(content) with { Sha256 = new string('0', 64) };
        var finalPath = Path.Combine(directory.Path, "model.gguf");
        using var client = new HttpClient(new RangeHandler(content));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new WizardArtifactDownloader(client).DownloadAsync(
                artifact,
                finalPath,
                new Progress<BallsWizardInstallProgress>(),
                CancellationToken.None));

        Assert.IsFalse(File.Exists(finalPath));
        Assert.IsTrue(File.Exists(finalPath + ".partial"));
    }

    [TestMethod]
    public async Task Server_ignoring_range_restarts_the_partial_instead_of_appending_duplicate_bytes()
    {
        using var directory = new TemporaryDirectory();
        var content = "wizard-bytes"u8.ToArray();
        var artifact = CreateArtifact(content);
        var finalPath = Path.Combine(directory.Path, "model.gguf");
        await File.WriteAllBytesAsync(finalPath + ".partial", content[..5]);
        using var client = new HttpClient(new RangeHandler(content, ignoreRange: true));

        await new WizardArtifactDownloader(client).DownloadAsync(
            artifact,
            finalPath,
            new Progress<BallsWizardInstallProgress>(),
            CancellationToken.None);

        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(finalPath));
    }

    [TestMethod]
    public async Task Full_sized_corrupt_partial_is_replaced_from_zero_on_retry()
    {
        using var directory = new TemporaryDirectory();
        var content = "wizard-bytes"u8.ToArray();
        var artifact = CreateArtifact(content);
        var finalPath = Path.Combine(directory.Path, "model.gguf");
        await File.WriteAllBytesAsync(finalPath + ".partial", new byte[content.Length]);
        var handler = new RangeHandler(content);
        using var client = new HttpClient(handler);

        await new WizardArtifactDownloader(client).DownloadAsync(
            artifact,
            finalPath,
            new Progress<BallsWizardInstallProgress>(),
            CancellationToken.None);

        Assert.IsNull(handler.RequestedRangeStart);
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(finalPath));
    }

    private static BallsWizardArtifact CreateArtifact(byte[] content)
    {
        return new BallsWizardArtifact(
            "model",
            "Test model",
            "test",
            new Uri("https://example.invalid/model"),
            content.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(content)),
            "Apache-2.0");
    }

    private sealed class RangeHandler(byte[] content, bool ignoreRange = false) : HttpMessageHandler
    {
        public long? RequestedRangeStart { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedRangeStart = request.Headers.Range?.Ranges.Single().From;
            var offset = ignoreRange ? 0 : checked((int)(RequestedRangeStart ?? 0));
            var response = new HttpResponseMessage(
                offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content[offset..]),
            };
            if (offset > 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                    offset,
                    content.Length - 1,
                    content.Length);
            }
            return Task.FromResult(response);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-wizard-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
