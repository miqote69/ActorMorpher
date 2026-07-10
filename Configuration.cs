using Dalamud.Configuration;

namespace ActorMorpher;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
}
