using Dalamud.Plugin.Services;

namespace ActorMorpher.Interop;

public sealed class DalamudClientContext(IClientState clientState) : IClientContext
{
    public uint TerritoryId => clientState.TerritoryType;
    public bool IsLoggedIn => clientState.IsLoggedIn;
    public bool IsGPosing => clientState.IsGPosing;
}
