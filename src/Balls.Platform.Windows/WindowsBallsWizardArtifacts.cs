using System.Runtime.InteropServices;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal static class WindowsBallsWizardSupport
{
    public static bool IsSupported(
        int build,
        string? installationType,
        Architecture operatingSystemArchitecture,
        Architecture processArchitecture)
    {
        return build >= 22_000
            && string.Equals(installationType, "Client", StringComparison.OrdinalIgnoreCase)
            && operatingSystemArchitecture == Architecture.X64
            && processArchitecture == Architecture.X64;
    }
}

internal static class WindowsBallsWizardArtifacts
{
    public const string WizardVersion = "wizard-v0";
    public const long MinimumMemoryBytes = 8L * 1024 * 1024 * 1024;
    public const long RequiredStorageBytes = 5L * 1024 * 1024 * 1024;

    public static readonly BallsWizardArtifact Runtime = new(
        "runtime",
        "Balls-managed llama.cpp runtime",
        "b10516",
        new Uri(
            "https://github.com/ggml-org/llama.cpp/releases/download/b10516/"
            + "llama-b10516-bin-win-cpu-x64.zip"),
        18_506_923,
        "fbbbc55e0eb2e1b07f9dcb9488616c98ed47d9003b90e15e7c8c7812c4307cd3",
        "MIT");

    public static readonly BallsWizardArtifact Model = new(
        "model",
        "Google Gemma 4 E2B instruction model (QAT Q4, text only)",
        "675cff42a74c774d6cb76f76d8eacb49b48c9b93",
        new Uri(
            "https://huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf/resolve/"
            + "675cff42a74c774d6cb76f76d8eacb49b48c9b93/"
            + "gemma-4-E2B_q4_0-it.gguf"),
        3_349_516_256,
        "fa401b55b07ee70a54c6dae3903c783a6e65064312529ea57175cb5f8dec6634",
        "Apache-2.0");

    public static readonly IReadOnlyList<BallsWizardArtifact> All = [Runtime, Model];
}

internal sealed record WindowsBallsWizardPaths(
    string Root,
    string Downloads,
    string RuntimeArchive,
    string RuntimeDirectory,
    string RuntimeExecutable,
    string ModelFile,
    string InstallationRecord)
{
    public static WindowsBallsWizardPaths FromRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("The Wizard directory must be absolute.", nameof(root));
        }

        var fullRoot = Path.GetFullPath(root);
        if (!string.Equals(Path.GetFileName(fullRoot), "wizard", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The Wizard directory must be the dedicated 'wizard' data directory.",
                nameof(root));
        }

        var downloads = Path.Combine(fullRoot, "downloads");
        var runtimeDirectory = Path.Combine(fullRoot, "runtime");
        return new WindowsBallsWizardPaths(
            fullRoot,
            downloads,
            Path.Combine(downloads, "llama-b10516-bin-win-cpu-x64.zip"),
            runtimeDirectory,
            Path.Combine(runtimeDirectory, "llama-server.exe"),
            Path.Combine(fullRoot, "gemma-4-E2B_q4_0-it.gguf"),
            Path.Combine(fullRoot, "installed.v1.json"));
    }
}
