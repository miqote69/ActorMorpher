namespace ActorMorpher.Interop;

public interface IOutfitMemory
{
    bool TryCapture(ActorSnapshot actor, out OutfitData outfit);
    bool TryCaptureRendered(ActorSnapshot actor, out OutfitData outfit);
    bool TryApply(ActorSnapshot actor, OutfitData outfit);
}
