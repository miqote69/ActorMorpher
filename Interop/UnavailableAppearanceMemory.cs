namespace ActorMorpher.Interop;

public sealed class UnavailableAppearanceMemory : IAppearanceMemory
{
    public bool TryCapture(ActorSnapshot actor, out AppearanceData appearance)
    {
        appearance = null!;
        return false;
    }

    public bool TryWrite(ActorSnapshot actor, AppearanceData appearance)
        => false;

    public bool IsApplied(ActorSnapshot actor, AppearanceData appearance)
        => false;
}
