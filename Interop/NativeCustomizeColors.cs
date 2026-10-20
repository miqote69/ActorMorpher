using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

internal static unsafe class NativeCustomizeColors
{
    private static readonly int[] Indices = [8, 9, 10, 11, 13, 15, 20, 25];

    internal static ImmutableArray<CustomizeColorOverride> Capture(IObjectTable objects, ActorSnapshot actor)
    {
        var model = Resolve(objects, actor);
        if (model is null) return [];
        var result = ImmutableArray.CreateBuilder<CustomizeColorOverride>();
        foreach (var index in Indices)
        {
            var rgb = GetColor((Human*)model, index);
            if (rgb is not null)
                result.Add(new(index, Root(rgb[0]), Root(rgb[1]), Root(rgb[2])));
        }
        return result.ToImmutable();
    }

    internal static bool TryApply(IObjectTable objects, ActorSnapshot actor,
        ImmutableArray<CustomizeColorOverride> colors)
        => Apply(Resolve(objects, actor), colors);

    internal static bool Apply(CharacterBase* model, ImmutableArray<CustomizeColorOverride> colors)
    {
        if (colors.IsDefaultOrEmpty) return true;
        if (model is null || model->GetModelType() != CharacterBase.ModelType.Human) return false;
        var human = (Human*)model;
        // Resolve every requested channel before touching the current buffer.
        foreach (var color in colors)
            if (GetColor(human, color.Index) is null) return false;
        foreach (var color in colors)
        {
            var rgb = GetColor(human, color.Index);
            rgb[0] = Square(color.R);
            rgb[1] = Square(color.G);
            rgb[2] = Square(color.B);
        }
        return true;
    }

    internal static CharacterBase* Resolve(IObjectTable objects, ActorSnapshot actor)
    {
        var key = actor.RepresentationKey;
        var current = objects[key.ObjectIndex];
        if (current is null || current.Address == 0 || current.GameObjectId != key.GameObjectId
            || current.EntityId != key.EntityId) return null;
        var model = ((GameObject*)current.Address)->GetCharacterBase();
        return model is not null && model->GetModelType() == CharacterBase.ModelType.Human ? model : null;
    }

    private static float* GetColor(Human* human, int index)
    {
        var offset = index switch { 8 => 0, 20 => 4, 10 => 8, 11 => 12, 15 => 16, 9 => 20, 13 => 24, 25 => 0, _ => -1 };
        if (offset < 0) return null;
        var buffer = index == 25 ? human->DecalColorCBuffer : human->CustomizeParameterCBuffer;
        return buffer is null || buffer->UnsafeSourcePointer is null || buffer->ByteSize < (offset + 3) * sizeof(float)
            ? null : (float*)buffer->UnsafeSourcePointer + offset;
    }

    private static float Square(float value) => value < 0 ? -value * value : value * value;
    private static float Root(float value) => value < 0 ? -MathF.Sqrt(-value) : MathF.Sqrt(value);
}
