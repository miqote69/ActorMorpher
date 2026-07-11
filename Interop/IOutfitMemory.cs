namespace ActorMorpher.Interop;

public interface IOutfitMemory
{
    bool TryCapture(ActorSnapshot actor, out OutfitData outfit);
    bool TryApply(ActorSnapshot actor, OutfitData outfit);
    bool IsApplied(ActorSnapshot actor, OutfitData outfit);
}
