namespace ActorMorpher.Interop;

public interface IClientContext
{
    uint TerritoryId { get; }
    bool IsLoggedIn { get; }
    bool IsGPosing { get; }
}
