namespace TodoAgent.Tests.Support;

/// <summary>A clock the test controls. Set <see cref="Now"/> and every read returns it.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
