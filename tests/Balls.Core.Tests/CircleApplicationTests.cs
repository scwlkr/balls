using Balls.Core;

namespace Balls.Core.Tests;

[TestClass]
public sealed class CircleApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CreateCircle_creates_founder_and_enrolls_local_node_as_one_persisted_circle()
    {
        var store = new InMemoryLocalStateStore();
        var application = new CircleApplication(
            store,
            new FixedTimeProvider(Now),
            "Alice-PC");
        var requestId = new CreationRequestId(Guid.Parse("0198c2d8-b000-7000-8000-000000000001"));

        var created = await application.CreateCircleAsync(
            new CreateCircleCommand(requestId, "  Example Studio  ", "  Alice  "));

        Assert.AreEqual("Example Studio", created.Circle.Name);
        Assert.AreEqual(Now, created.Circle.CreatedAtUtc);
        Assert.AreNotEqual(Guid.Empty, created.Circle.Id.Value);

        Assert.AreEqual(1, created.Members.Count);
        var founder = created.Members[0];
        Assert.AreEqual(created.Circle.Id, founder.CircleId);
        Assert.AreEqual("Alice", founder.DisplayName);
        Assert.AreEqual(MemberRole.Owner, founder.Role);
        Assert.AreNotEqual(Guid.Empty, founder.Id.Value);

        Assert.AreEqual(1, created.Nodes.Count);
        var enrolledNode = created.Nodes[0];
        Assert.AreEqual(created.Circle.Id, enrolledNode.CircleId);
        Assert.AreEqual("Alice-PC", enrolledNode.DisplayName);
        Assert.AreNotEqual(Guid.Empty, enrolledNode.NodeId.Value);
        Assert.AreNotEqual(founder.Id.Value, enrolledNode.NodeId.Value);

        Assert.IsNotNull(store.Node);
        Assert.AreEqual(store.Node.Id, enrolledNode.NodeId);
        Assert.AreEqual(created, store.Circle);
    }

    [TestMethod]
    [DataRow("", "Alice", "circle_name_required")]
    [DataRow("Example Studio", "   ", "owner_display_name_required")]
    public async Task CreateCircle_rejects_blank_required_names_without_persisting_a_circle(
        string circleName,
        string ownerDisplayName,
        string expectedCode)
    {
        var store = new InMemoryLocalStateStore();
        var application = new CircleApplication(
            store,
            new FixedTimeProvider(Now),
            "Alice-PC");
        var command = new CreateCircleCommand(
            new CreationRequestId(Guid.Parse("0198c2d8-b000-7000-8000-000000000002")),
            circleName,
            ownerDisplayName);

        var error = await Assert.ThrowsExactlyAsync<InputValidationException>(
            () => application.CreateCircleAsync(command));

        Assert.AreEqual(expectedCode, error.Code);
        Assert.IsNull(store.Circle);
    }

    [TestMethod]
    public async Task GetLocalNode_reuses_the_identity_already_persisted_for_this_installation()
    {
        var store = new InMemoryLocalStateStore();
        var firstApplication = new CircleApplication(
            store,
            new FixedTimeProvider(Now),
            "Alice-PC");
        var first = await firstApplication.GetLocalNodeAsync();

        var restartedApplication = new CircleApplication(
            store,
            new FixedTimeProvider(Now.AddDays(1)),
            "Renamed-PC");
        var afterRestart = await restartedApplication.GetLocalNodeAsync();

        Assert.AreEqual(first, afterRestart);
        Assert.AreEqual("Alice-PC", afterRestart.DisplayName);
        Assert.AreEqual(Now, afterRestart.CreatedAtUtc);
    }

    [TestMethod]
    public async Task CreateCircle_rejects_names_longer_than_one_hundred_characters()
    {
        var store = new InMemoryLocalStateStore();
        var application = new CircleApplication(
            store,
            new FixedTimeProvider(Now),
            "Alice-PC");
        var command = new CreateCircleCommand(
            new CreationRequestId(Guid.Parse("0198c2d8-b000-7000-8000-000000000003")),
            new string('C', 101),
            "Alice");

        var error = await Assert.ThrowsExactlyAsync<InputValidationException>(
            () => application.CreateCircleAsync(command));

        Assert.AreEqual("circle_name_too_long", error.Code);
        Assert.IsNull(store.Circle);
    }

    [TestMethod]
    public async Task CreateCircle_rejects_owner_names_longer_than_one_hundred_characters()
    {
        var store = new InMemoryLocalStateStore();
        var application = new CircleApplication(
            store,
            new FixedTimeProvider(Now),
            "Alice-PC");
        var command = new CreateCircleCommand(
            new CreationRequestId(Guid.Parse("0198c2d8-b000-7000-8000-000000000004")),
            "Example Studio",
            new string('M', 101));

        var error = await Assert.ThrowsExactlyAsync<InputValidationException>(
            () => application.CreateCircleAsync(command));

        Assert.AreEqual("owner_display_name_too_long", error.Code);
        Assert.IsNull(store.Circle);
    }

    [TestMethod]
    public async Task GetLocalNode_rejects_a_blank_node_display_name_without_persisting_identity()
    {
        var store = new InMemoryLocalStateStore();
        var application = new CircleApplication(
            store,
            new FixedTimeProvider(Now),
            "   ");

        var error = await Assert.ThrowsExactlyAsync<InputValidationException>(
            () => application.GetLocalNodeAsync());

        Assert.AreEqual("node_display_name_required", error.Code);
        Assert.IsNull(store.Node);
    }

    [TestMethod]
    public async Task GetLocalNode_rejects_an_overlong_node_display_name()
    {
        var store = new InMemoryLocalStateStore();
        var application = new CircleApplication(
            store,
            new FixedTimeProvider(Now),
            new string('n', 101));

        var error = await Assert.ThrowsExactlyAsync<InputValidationException>(
            () => application.GetLocalNodeAsync());

        Assert.AreEqual("node_display_name_too_long", error.Code);
        Assert.IsNull(store.Node);
    }

    private sealed class InMemoryLocalStateStore : ILocalStateStore
    {
        public NodeIdentity? Node { get; private set; }

        public CircleDetails? Circle { get; private set; }

        public Task<NodeIdentity?> GetNodeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Node);
        }

        public Task SaveNodeAsync(NodeIdentity node, CancellationToken cancellationToken = default)
        {
            Node = node;
            return Task.CompletedTask;
        }

        public Task<CircleDetails> CreateCircleAsync(
            CreationRequestId requestId,
            CircleDetails circle,
            CancellationToken cancellationToken = default)
        {
            Circle = circle;
            return Task.FromResult(circle);
        }

        public Task<CircleDetails?> GetCircleAsync(
            CircleId circleId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Circle?.Circle.Id == circleId ? Circle : null);
        }

        public Task<IReadOnlyList<CircleDetails>> ListCirclesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CircleDetails> circles = Circle is null ? [] : [Circle];
            return Task.FromResult(circles);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
