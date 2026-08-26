using System.Runtime.Versioning;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesFolderPickerTests
{
    [TestMethod]
    public async Task Selected_existing_folder_preserves_the_exact_path_and_human_name()
    {
        var picker = new WindowsCircleFilesFolderPicker(
            new StubDialog(@"C:\BallsDemo\Projects"));

        var selection = await picker.SelectAsync(CancellationToken.None);

        Assert.IsNotNull(selection);
        Assert.AreEqual(@"C:\BallsDemo\Projects", selection.FolderPath);
        Assert.AreEqual("Projects", selection.DisplayName);
    }

    [TestMethod]
    public async Task Cancelled_dialog_returns_no_selection()
    {
        var picker = new WindowsCircleFilesFolderPicker(new StubDialog(null));

        var selection = await picker.SelectAsync(CancellationToken.None);

        Assert.IsNull(selection);
    }

    private sealed class StubDialog(string? result) : IWindowsCircleFilesFolderDialog
    {
        public string? Show(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }
}
