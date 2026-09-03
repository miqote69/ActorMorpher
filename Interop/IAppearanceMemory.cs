namespace ActorMorpher.Interop;

public interface IAppearanceMemory
{
    bool TryCapture(ActorSnapshot actor, out AppearanceData appearance);
}
