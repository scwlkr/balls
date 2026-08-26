using Balls.Daemon;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class BrowserUuidTests
{
    [TestMethod]
    public void Parser_accepts_only_canonical_lowercase_nonempty_UUIDs()
    {
        const string canonical = "0198d000-5000-7000-8000-000000000012";

        Assert.IsTrue(BrowserUuid.TryParse(canonical, out var parsed));
        Assert.AreEqual(canonical, parsed.ToString("D"));
        Assert.IsFalse(BrowserUuid.TryParse(canonical.ToUpperInvariant(), out _));
        Assert.IsFalse(BrowserUuid.TryParse("0198d000500070008000000000000012", out _));
        Assert.IsFalse(BrowserUuid.TryParse(Guid.Empty.ToString("D"), out _));
        Assert.IsFalse(BrowserUuid.TryParse(null, out _));
    }
}
