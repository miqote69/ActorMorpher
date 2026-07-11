namespace ActorMorpher.Interop;

public interface IAppearanceBackingStore
{
    bool TryNormalizeBacking(ActorSnapshot actor, AppearanceData appearance);
}
