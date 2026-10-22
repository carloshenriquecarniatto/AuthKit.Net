using Xunit;

namespace AuthKit.Clave.Tests;

public sealed class ServerStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_pending_request_is_consumed_once()
    {
        var store = new InMemoryClaveRequestStore(new FakeTimeProvider(Now));
        await store.SaveAsync(NewRequest());

        var first = await store.TakeAsync("req-1");
        var second = await store.TakeAsync("req-1");

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Unknown_requests_return_null()
    {
        var store = new InMemoryClaveRequestStore(new FakeTimeProvider(Now));

        Assert.Null(await store.TakeAsync("missing"));
    }

    [Fact]
    public async Task Expired_requests_are_swept()
    {
        var time = new FakeTimeProvider(Now);
        var store = new InMemoryClaveRequestStore(time);
        await store.SaveAsync(NewRequest());

        time.Advance(TimeSpan.FromMinutes(20));
        await store.SaveAsync(NewRequest("req-2", time.GetUtcNow()));

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task A_first_assertion_registers_and_a_second_is_a_replay()
    {
        var cache = new InMemoryClaveReplayCache(new FakeTimeProvider(Now));
        var notOnOrAfter = Now.AddMinutes(5);

        Assert.True(await cache.TryRegisterAsync("assertion-1", notOnOrAfter));
        Assert.False(await cache.TryRegisterAsync("assertion-1", notOnOrAfter));
    }

    [Fact]
    public async Task An_expired_replay_entry_can_be_reused()
    {
        var time = new FakeTimeProvider(Now);
        var cache = new InMemoryClaveReplayCache(time);

        Assert.True(await cache.TryRegisterAsync("assertion-1", Now.AddMinutes(1)));
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await cache.TryRegisterAsync("assertion-1", time.GetUtcNow().AddMinutes(1)));
    }

    [Fact]
    public async Task Distinct_assertions_are_registered_independently()
    {
        var cache = new InMemoryClaveReplayCache(new FakeTimeProvider(Now));

        Assert.True(await cache.TryRegisterAsync("a", Now.AddMinutes(5)));
        Assert.True(await cache.TryRegisterAsync("b", Now.AddMinutes(5)));
        Assert.Equal(2, cache.Count);
    }

    private static PendingClaveRequest NewRequest(string id = "req-1", DateTimeOffset? now = null)
    {
        var created = now ?? Now;
        return new PendingClaveRequest
        {
            RequestId = id,
            State = "state-" + id,
            Level = AuthAssuranceLevel.Substantial,
            Tenant = "caib",
            CreatedAt = created,
            ExpiresAt = created.AddMinutes(15)
        };
    }
}

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
