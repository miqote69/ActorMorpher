namespace ActorMorpher.Interop;

public sealed class UnavailableAppearanceMemory : IAppearanceMemory
{
    public bool TryCapture(ActorSnapshot actor, out AppearanceData appearance)
    {
        appearance = null!;
        return false;
    }
}
