namespace ActorMorpher.Interop;

public interface IRedrawBackend
{
    bool TryDisable(ActorSnapshot actor);
    bool TryEnable(ActorSnapshot actor);
}
