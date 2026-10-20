using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher;

public sealed partial class Plugin
{
    private NpcEquipmentGroups? npcEquipmentGroups;

    private NpcEquipmentGroup? FindNpcEquipmentGroup(ObjectKind kind, uint baseId)
    {
        if (kind is not (ObjectKind.EventNpc or ObjectKind.BattleNpc)) return null;
        // A fixed sheet language makes the relation independent of the UI/client language.
        npcEquipmentGroups ??= new(GetModelSearchEntries(ClientLanguage.Japanese));
        return npcEquipmentGroups.TryGet(kind, baseId, out var group) ? group : null;
    }

    private bool ShareNpcEquipment(ActorSnapshot source, EquipmentCopyData? selected = null)
    {
        if (FindNpcEquipmentGroup(source.ObjectKind, source.BaseId) is not { } group
            || !actorRegistry.TryGet(source.LogicalKey, out var actor))
            return false;
        if (selected is null || !selected.IncludesWeapons)
        {
            var appearance = actorRegistry.CaptureCustomizeAppearance(source);
            if (appearance is null || !EquipmentCopyData.TryCreate(
                    selected is null ? appearance : selected.ApplyTo(appearance), out selected))
                return false;
        }
        if (!pinnedOutfitStore.ShareNpcEquipment(actor, selected)) return false;
        ClearSharedNpcFailedStates(group);
        return true;
    }

    private bool ShareNpcCustomize(ActorSnapshot source, AppearanceData selected)
    {
        if (FindNpcEquipmentGroup(source.ObjectKind, source.BaseId) is not { } group
            || !actorRegistry.TryGet(source.LogicalKey, out var actor)
            || !CustomizeCopyData.TryCreate(selected, out var customize)
            || !pinnedOutfitStore.ShareNpcCustomize(actor, customize))
            return false;
        ClearSharedNpcFailedStates(group);
        return true;
    }

    private void ClearSharedNpcFailedStates(NpcEquipmentGroup group)
    {
        // A new user edit is a new requested outfit, not a retry of the old failed one.
        foreach (var target in actorRegistry.Entries)
        {
            if (target.Kind != group.Kind || !group.BaseIds.Contains(target.BaseId)) continue;
            failedPinnedOutfitReapplyStates.Remove(target.Key);
            failedPinnedAppearanceStates.Remove(target.Key);
        }
    }

    internal static bool CanApplySharedCustomizeDirectly(AppearanceData current, AppearanceData desired)
        => current.Category == ModelCategory.Human && desired.Category == ModelCategory.Human
            && current.ModelCharaId == desired.ModelCharaId && current.ModelScale == desired.ModelScale
            && current.Customize.Length == 26 && desired.Customize.Length == 26
            && (!current.Customize.AsSpan().SequenceEqual(desired.Customize.AsSpan())
                || !current.CustomizeColors.AsSpan().SequenceEqual(desired.CustomizeColors.AsSpan()))
            && CustomizeLiveRequest.CanApplyCustomizeDirectly(current, desired);

    internal static AppearanceData ComposeSharedNpcAppearance(AppearanceData rendered,
        AppearanceData? individual, OutfitData? individualOutfit, EquipmentCopyData? equipment,
        CustomizeCopyData? customize)
    {
        var desired = individual ?? rendered;
        if (individual is null && individualOutfit is not null)
            desired = EquipmentCopyData.FromOutfit(individualOutfit).ApplyTo(desired);
        if (equipment is not null) desired = equipment.ApplyTo(desired);
        if (customize is not null) desired = customize.ApplyTo(desired);
        return desired.WithCurrentEarVisibility(rendered);
    }

    private bool TryApplySharedCustomizeDirectly(ActorSnapshot actor, AppearanceData current,
        CustomizeCopyData customize, out AppearanceData applied)
    {
        applied = customize.ApplyTo(current);
        var request = new CustomizeLiveRequest(Guid.Empty, actor.LogicalKey, actor.RepresentationKey,
            customize.Customize, customize.CustomizeColors);
        var written = request.CanApplyColorsDirectly(current, applied)
            ? NativeCustomizeColors.TryApply(ObjectTable, actor, applied.CustomizeColors)
            : NativeCustomizeMemory.TryApply(ObjectTable, actor, applied);
        return written && PublishAppliedAppearance(actor, applied, false);
    }

    private static bool AssignSharedOutfit(EquipmentCopyData equipment, out OutfitData outfit)
    {
        outfit = equipment.Outfit;
        return true;
    }
}
