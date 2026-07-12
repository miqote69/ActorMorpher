using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace ActorMorpher.Preview;

public sealed class ModelPreviewTextureCache(
    ITextureProvider textureProvider,
    ModelPreviewTextureSource textureSource) : IDisposable
{
    private readonly Dictionary<string, CacheEntry?> cache = new(StringComparer.Ordinal);
    private ModelPreviewTextureContext context = ModelPreviewTextureContext.Default;

    public void Select(ModelSearchEntry? model)
    {
        Clear();
        context = textureSource.CreateContext(model);
    }

    public ImTextureID GetHandle(string materialPath)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
            return default;
        if (!cache.TryGetValue(materialPath, out var entry))
        {
            entry = Create(materialPath);
            cache.Add(materialPath, entry);
        }
        if (entry?.Owned is { } owned)
            return owned.Handle;
        return entry?.Shared?.GetWrapOrEmpty().Handle ?? default;
    }

    public void Clear()
    {
        foreach (var entry in cache.Values)
            entry?.Owned?.Dispose();
        cache.Clear();
    }

    public void Dispose() => Clear();

    private CacheEntry? Create(string materialPath)
    {
        try
        {
            var payload = textureSource.Load(materialPath, context);
            if (payload?.GamePath is { } gamePath)
                return new CacheEntry(textureProvider.GetFromGame(gamePath), null);
            if (payload is not { BgraPixels: { } pixels, Width: > 0, Height: > 0 })
                return null;
            var owned = textureProvider.CreateFromRaw(
                RawImageSpecification.Bgra32(payload.Width, payload.Height),
                pixels,
                $"ActorMorpher preview {Path.GetFileName(materialPath)}");
            return new CacheEntry(null, owned);
        }
        catch
        {
            return null;
        }
    }

    private sealed record CacheEntry(ISharedImmediateTexture? Shared, IDalamudTextureWrap? Owned);
}
