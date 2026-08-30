namespace ActorMorpher;

public sealed class CommandRegistrationLease : IDisposable
{
    private readonly Func<bool> isOccupied;
    private readonly Func<bool> tryRegister;
    private readonly Func<bool> tryRemove;

    public CommandRegistrationLease(
        Func<bool> isOccupied,
        Func<bool> tryRegister,
        Func<bool> tryRemove)
    {
        this.isOccupied = isOccupied;
        this.tryRegister = tryRegister;
        this.tryRemove = tryRemove;
    }

    public bool OwnsRegistration { get; private set; }

    public void EnsureRegistered()
    {
        if (OwnsRegistration || isOccupied())
            return;

        OwnsRegistration = tryRegister();
    }

    public void Dispose()
    {
        if (!OwnsRegistration)
            return;

        tryRemove();
        OwnsRegistration = false;
    }
}
