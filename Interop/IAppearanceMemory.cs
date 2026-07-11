namespace ActorMorpher.Interop;

public interface IAppearanceMemory
{
    bool TryCapture(ActorSnapshot actor, out AppearanceData appearance);
    bool TryWrite(ActorSnapshot actor, AppearanceData appearance);
    bool IsApplied(ActorSnapshot actor, AppearanceData appearance);
}

public interface IAppearanceFinalizer
{
    bool TryFinalize(ActorSnapshot actor, AppearanceData appearance);
}
