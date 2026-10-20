using ActorMorpher.Customization;

namespace ActorMorpher;

public sealed partial class Plugin
{
    private CustomizeCatalog? customizeCatalog;
    public CustomizeCatalog CustomizeCatalog => customizeCatalog ??= new CustomizeCatalog(DataManager);
}
