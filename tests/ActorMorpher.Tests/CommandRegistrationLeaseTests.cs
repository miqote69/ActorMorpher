using Xunit;

namespace ActorMorpher.Tests;

public sealed class CommandRegistrationLeaseTests
{
    [Fact]
    public void NonOwnerDoesNotRemoveOccupiedCommand()
    {
        var registry = new FakeCommandRegistry { Occupied = true };
        using var lease = registry.CreateLease();

        lease.EnsureRegistered();
        lease.Dispose();

        Assert.False(lease.OwnsRegistration);
        Assert.Equal(0, registry.AddCount);
        Assert.Equal(0, registry.RemoveCount);
        Assert.True(registry.Occupied);
    }

    [Fact]
    public void RemainingInstanceAcquiresCommandAfterOwnerUnloads()
    {
        var registry = new FakeCommandRegistry();
        using var first = registry.CreateLease();
        using var second = registry.CreateLease();

        first.EnsureRegistered();
        second.EnsureRegistered();
        first.Dispose();
        second.EnsureRegistered();

        Assert.True(second.OwnsRegistration);
        Assert.Equal(2, registry.AddCount);
        Assert.Equal(1, registry.RemoveCount);

        second.Dispose();
        Assert.Equal(2, registry.RemoveCount);
        Assert.False(registry.Occupied);
    }

    [Fact]
    public void NormalLifecycleRegistersAndRemovesOnce()
    {
        var registry = new FakeCommandRegistry();
        using var lease = registry.CreateLease();

        lease.EnsureRegistered();
        lease.EnsureRegistered();

        Assert.True(lease.OwnsRegistration);
        Assert.Equal(1, registry.AddCount);

        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, registry.RemoveCount);
        Assert.False(registry.Occupied);
    }

    private sealed class FakeCommandRegistry
    {
        public bool Occupied { get; set; }
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }

        public CommandRegistrationLease CreateLease()
            => new(
                () => Occupied,
                () =>
                {
                    AddCount++;
                    if (Occupied)
                        return false;
                    Occupied = true;
                    return true;
                },
                () =>
                {
                    RemoveCount++;
                    if (!Occupied)
                        return false;
                    Occupied = false;
                    return true;
                });
    }
}
