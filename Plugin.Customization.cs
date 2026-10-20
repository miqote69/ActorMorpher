using System.Collections.Immutable;

namespace ActorMorpher;

public sealed partial class Plugin
{
    private readonly Dictionary<Guid, Guid> customizeOperations = new();
    private readonly CustomizeLiveApplyQueue pendingCustomizeEdits = new();
    internal event Action<Guid, bool>? CustomizeOperationCompleted;

    public bool TryBeginCustomizeEdit(ActorSnapshot target, out CustomizeEditState draft, out string message)
    {
        draft = null!;
        if (!actorResolver.TryResolve(target.LogicalKey, target.RepresentationKey, out var current)
            || actorRegistry.CaptureCurrentAppearance(current) is not { Category: ModelCategory.Human, Customize.Length: 26 } appearance)
        {
            message = "The selected actor's character customization is not available.";
            return false;
        }
        draft = new CustomizeEditState(current, appearance.Customize, appearance.CustomizeColors);
        message = string.Empty;
        return true;
    }

    public ImmutableArray<CustomizeColorOverride> CaptureCustomizeColors(ActorSnapshot target)
        => actorResolver.TryResolve(target.LogicalKey, target.RepresentationKey, out var current)
            ? NativeCustomizeColors.Capture(ObjectTable, current) : [];

    public bool TryCopyCustomize(ActorSnapshot source, out CustomizeCopyData data, out string message)
    {
        data = null!;
        if (!actorResolver.TryResolve(source.LogicalKey, source.RepresentationKey, out var current)
            || !CustomizeCopyData.TryCreate(actorRegistry.CaptureCurrentAppearance(current), out data))
        {
            message = "The selected actor's character customization is not available.";
            return false;
        }
        message = string.Empty;
        return true;
    }

    public bool TryPasteCustomize(ActorSnapshot target, CustomizeEditState draft, CustomizeCopyData data,
        Guid requestId, out string message)
    {
        if (!draft.Matches(target)
            || !actorResolver.TryResolve(target.LogicalKey, target.RepresentationKey, out var current)
            || actorRegistry.CaptureCurrentAppearance(current) is not { Category: ModelCategory.Human, Customize.Length: 26 })
        {
            message = "The selected actor's character customization is not available.";
            return false;
        }
        draft.Paste(data);
        return TryApplyCustomize(current, draft, requestId, out message);
    }

    public bool TryApplyCustomize(ActorSnapshot selectedTarget, CustomizeEditState draft,
        Guid requestId, out string message, ImmutableArray<byte> customizeMasks = default, uint colorMask = uint.MaxValue)
    {
        if (!draft.Matches(selectedTarget))
        {
            message = "The actor selected for this character customization is no longer available.";
            return false;
        }
        restoreStatus = string.Empty;
        var accepted = pendingCustomizeEdits.Submit(new(requestId, draft.Actor, draft.Representation,
            draft.ToCustomize(), draft.ToColorOverrides(), customizeMasks, colorMask), appearanceApplyService.IsPending, DispatchCustomizeEdit);
        message = accepted ? "Applying character customization." : "Character customization could not be applied.";
        return accepted;
    }

    private void ProcessPendingCustomizeEdits()
        => pendingCustomizeEdits.Process(appearanceApplyService.IsPending, DispatchCustomizeEdit);

    private bool DispatchCustomizeEdit(CustomizeLiveRequest request)
    {
        if (TryDispatchCustomizeEdit(request)) return true;
        CustomizeOperationCompleted?.Invoke(request.RequestId, false);
        return false;
    }

    private bool TryDispatchCustomizeEdit(CustomizeLiveRequest request)
    {
        // Capture only when dispatching: the preceding redraw may temporarily have no draw object.
        if (!actorResolver.TryResolve(request.Actor, request.Representation, out var current)
            || !ClientState.IsLoggedIn || current.RepresentationKey.TerritoryId != ClientState.TerritoryType
            || actorRegistry.CaptureCustomizeAppearance(current) is not { Category: ModelCategory.Human, Customize.Length: 26 } appearance)
            return false;
        var desired = request.CreateAppearance(appearance);
        var colorsOnly = request.CanApplyColorsDirectly(appearance, desired);
        if (colorsOnly || CustomizeLiveRequest.CanApplyCustomizeDirectly(appearance, desired))
        {
            var applied = colorsOnly
                ? NativeCustomizeColors.TryApply(ObjectTable, current, desired.CustomizeColors)
                : NativeCustomizeMemory.TryApply(ObjectTable, current, desired);
            if (!applied || !PublishAppliedAppearance(current, desired, true))
                return false;
            CustomizeOperationCompleted?.Invoke(request.RequestId, true);
            return true;
        }
        if (!appearanceApplyService.TryApply(current, desired, out var operationId, out _))
            return false;
        customizeOperations.Add(operationId, request.RequestId);
        return true;
    }

    private bool PublishAppliedAppearance(ActorSnapshot current, AppearanceData appearance, bool customizeEdit)
    {
        if (!actorRegistry.RecordAppliedAppearance(current.LogicalKey, current.RepresentationKey, appearance))
            return false;
        appearancePersistence.RecordModel(current, appearance);
        drawObjectInjector.EnablePersistentAppearance(current);
        if (customizeEdit && !ShareNpcCustomize(current, appearance)
            && actorRegistry.TryGet(current.LogicalKey, out var entry))
            pinnedOutfitStore.UpdateCustomize(entry, appearance.Customize, appearance.CustomizeColors);
        return true;
    }
}

internal sealed record CustomizeLiveRequest(Guid RequestId, LogicalActorKey Actor, ActorRepresentationKey Representation,
    ImmutableArray<byte> Customize, ImmutableArray<CustomizeColorOverride> Colors,
    ImmutableArray<byte> CustomizeMasks = default, uint ColorMask = uint.MaxValue)
{
    internal AppearanceData CreateAppearance(AppearanceData current)
    {
        var customize = Customize;
        if (!CustomizeMasks.IsDefault)
        {
            var merged = current.Customize.ToBuilder();
            for (var index = 0; index < merged.Count; index++)
                merged[index] = (byte)((merged[index] & ~CustomizeMasks[index]) | (Customize[index] & CustomizeMasks[index]));
            customize = merged.ToImmutable();
        }
        var colors = ColorMask == uint.MaxValue ? Colors
            : current.CustomizeColors.Where(color => (ColorMask & (1u << color.Index)) == 0)
                .Concat(Colors.Where(color => (ColorMask & (1u << color.Index)) != 0))
                .OrderBy(color => color.Index).ToImmutableArray();
        return current with { Customize = customize, CustomizeColors = colors };
    }

    internal bool CanApplyColorsDirectly(AppearanceData current, AppearanceData? desired = null)
    {
        desired ??= CreateAppearance(current);
        return desired.Customize.AsSpan().SequenceEqual(current.Customize.AsSpan())
            && current.CustomizeColors.All(previous => desired.CustomizeColors.Any(next => next.Index == previous.Index));
    }

    internal static bool CanApplyCustomizeDirectly(AppearanceData current, AppearanceData desired)
    {
        // These fields change the skeleton/model family; other Customize fields can
        // be updated on the existing Human without DisableDraw/Create.
        ReadOnlySpan<int> redrawFields = [0, 1, 2, 4, 5];
        foreach (var index in redrawFields)
            if (current.Customize[index] != desired.Customize[index]) return false;
        return true;
    }
}
internal sealed class CustomizeLiveApplyQueue
{
    private readonly Dictionary<LogicalActorKey, CustomizeLiveRequest> latest = new();

    internal bool Submit(CustomizeLiveRequest request, Func<LogicalActorKey, bool> isPending,
        Func<CustomizeLiveRequest, bool> dispatch)
    {
        if (latest.Remove(request.Actor, out var previous) && previous.Representation == request.Representation)
        {
            var customize = request.Customize;
            if (!request.CustomizeMasks.IsDefault)
            {
                var merged = customize.ToBuilder();
                for (var index = 0; index < merged.Count; index++)
                {
                    var previousMask = previous.CustomizeMasks.IsDefault ? 0xff : previous.CustomizeMasks[index];
                    var carry = previousMask & ~request.CustomizeMasks[index];
                    merged[index] = (byte)((merged[index] & ~carry) | (previous.Customize[index] & carry));
                }
                customize = merged.ToImmutable();
            }
            var carryColors = previous.ColorMask & ~request.ColorMask;
            var colors = carryColors == 0 ? request.Colors
                : request.Colors.Where(color => (carryColors & (1u << color.Index)) == 0)
                    .Concat(previous.Colors.Where(color => (carryColors & (1u << color.Index)) != 0))
                    .OrderBy(color => color.Index).ToImmutableArray();
            request = request with
            {
                Customize = customize,
                Colors = colors,
                CustomizeMasks = request.CustomizeMasks.IsDefault || previous.CustomizeMasks.IsDefault ? default
                    : request.CustomizeMasks.Select((mask, index) => (byte)(mask | previous.CustomizeMasks[index])).ToImmutableArray(),
                ColorMask = request.ColorMask | previous.ColorMask,
            };
        }
        if (!isPending(request.Actor)) return dispatch(request);
        latest[request.Actor] = request;
        return true;
    }

    internal void Process(Func<LogicalActorKey, bool> isPending, Func<CustomizeLiveRequest, bool> dispatch)
    {
        foreach (var actor in latest.Keys.ToArray())
            if (!isPending(actor) && latest.Remove(actor, out var request))
                dispatch(request);
    }

    internal CustomizeLiveRequest? GetLatest(LogicalActorKey actor, ActorRepresentationKey representation)
        => latest.TryGetValue(actor, out var request) && request.Representation == representation ? request : null;

    internal void Remove(LogicalActorKey actor) => latest.Remove(actor);
    internal void Clear() => latest.Clear();
}
