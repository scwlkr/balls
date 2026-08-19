namespace Balls.Core;

public readonly record struct CircleId(Guid Value)
{
    public static CircleId New()
    {
        return new CircleId(Guid.CreateVersion7());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public readonly record struct MemberId(Guid Value)
{
    public static MemberId New()
    {
        return new MemberId(Guid.CreateVersion7());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public readonly record struct NodeId(Guid Value)
{
    public static NodeId New()
    {
        return new NodeId(Guid.CreateVersion7());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public readonly record struct CreationRequestId(Guid Value)
{
    public override string ToString()
    {
        return Value.ToString("D");
    }
}
