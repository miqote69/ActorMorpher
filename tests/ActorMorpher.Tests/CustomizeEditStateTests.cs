using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CustomizeEditStateTests
{
    [Fact]
    public void ObservingRenderedColorsDoesNotCreateAnEditOrAnOverride()
    {
        var draft = new CustomizeEditState(Actor(), Appearance().Customize);
        Assert.Null(draft.GetColor(8));
        draft.UpdateRenderedColors([new(8, 0.2f, 0.4f, 0.7f)]);
        draft.UpdateRenderedColors([]);
        draft.SetValue(6, draft.GetValue(6));
        Assert.Equal(new Vector3(0.2f, 0.4f, 0.7f), draft.GetColor(8));
        Assert.Equal(0, draft.Revision);
        Assert.False(draft.HasChanges);
        Assert.Empty(draft.ToColorOverrides());
    }

    [Fact]
    public void FreeColorIsExactAndPaletteSelectionOfTheSameIdRemovesIt()
    {
        var original = Appearance().Customize;
        var draft = new CustomizeEditState(Actor(), original);
        var rgb = new Vector3(0.123f, 0.456f, 0.789f);
        draft.SetColor(8, rgb);
        var submitted = draft.ToColorOverrides();
        draft.SetColor(8, rgb);
        Assert.Equal(1, draft.Revision);
        Assert.Equal<byte>(original, draft.ToCustomize());
        Assert.Equal<CustomizeColorOverride>([new(8, rgb.X, rgb.Y, rgb.Z)], submitted);
        Assert.True(draft.HasChanges);
        draft.SetValue(8, original[8]);
        Assert.Equal(2, draft.Revision);
        Assert.Empty(draft.ToColorOverrides());
        Assert.Single(submitted);
        Assert.False(draft.HasChanges);
    }

    [Fact]
    public void ResetRestoresOriginalOwnedColorsWithoutOwningCapturedChannels()
    {
        var original = Appearance() with { CustomizeColors = [new(10, 0.2f, 0.3f, 0.4f)] };
        var draft = new CustomizeEditState(Actor(), original.Customize, original.CustomizeColors);
        draft.UpdateRenderedColors([new(8, 0.5f, 0.6f, 0.7f)]);
        draft.SetColor(8, new(0.12f, 0.34f, 0.56f));
        draft.SetValue(6, 255);
        var payload = draft.CreateAppearance(original);
        Assert.Equal(original.Mainhand, payload.Mainhand);
        Assert.Equal(original.ColoredEquipment, payload.ColoredEquipment);
        Assert.Equal(2, payload.CustomizeColors.Length);
        draft.Reset();
        Assert.False(draft.HasChanges);
        var reset = draft.CreateAppearance(original);
        Assert.Equal<byte>(original.Customize, reset.Customize);
        Assert.Equal<CustomizeColorOverride>(original.CustomizeColors, reset.CustomizeColors);
        Assert.Equal(original, reset with { Customize = original.Customize, CustomizeColors = original.CustomizeColors });
        Assert.Equal(3, draft.Revision);
        draft.Reset();
        Assert.Equal(3, draft.Revision);
    }

    [Fact]
    public void OwnedRgbColorsSurvivePinConfigurationAndOldDataHasNoOverrides()
    {
        var actor = Actor();
        var entry = new ActorEntry(actor.LogicalKey, actor.Name, actor.ObjectKind, false, [actor]);
        var configuration = new Configuration();
        var store = new PinnedOutfitStore(configuration, () => { }, () => 1);
        var appearance = Appearance();
        store.PinCurrent([entry], _ => appearance);
        store.UpdateCustomize(entry, appearance.Customize, [new(8, 0.123f, 0.456f, 0.789f)]);
        var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(
            Newtonsoft.Json.JsonConvert.SerializeObject(configuration))!;
        var restored = new PinnedOutfitStore(loaded, () => { }, () => 1);
        Assert.True(restored.TryGetAppearance(entry, out var retained));
        Assert.Equal<CustomizeColorOverride>([new(8, 0.123f, 0.456f, 0.789f)], retained.CustomizeColors);
        Assert.Equal<ulong>(appearance.Equipment, retained.Equipment);
        Assert.Equal(appearance.Mainhand, retained.Mainhand);
        var oldData = Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(appearance));
        oldData.Remove(nameof(AppearanceData.CustomizeColors));
        var oldAppearance = oldData.ToObject<AppearanceData>()!;
        Assert.Empty(oldAppearance.CustomizeColors);
        Assert.Equal<byte>(appearance.Customize, oldAppearance.Customize);
    }

    [Fact]
    public void RegistryRefreshRetainsRgbSoPinsConvergeAndPaletteRemovalUsesNativeCustomizeUpdate()
    {
        var appearance = Appearance() with { CustomizeColors = [new(8, 0.123f, 0.456f, 0.789f)] };
        var raw = Actor() with { CurrentAppearance = appearance with { CustomizeColors = [] } };
        var retained = new System.Collections.Generic.Dictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData>
            { [(raw.LogicalKey, raw.RepresentationKey)] = appearance };
        var refreshed = ActorRegistry.RetainManagedAppearance(raw, retained, _ => null);
        var captured = ActorRegistry.DescribeRenderedAppearance(refreshed, raw)!;
        Assert.True(PinnedOutfitStore.AppearanceEquals(captured, appearance));
        Assert.True(PinnedOutfitStore.CanMaintainWithOutfit(captured, appearance));
        var draft = new CustomizeEditState(refreshed, captured.Customize, captured.CustomizeColors);
        Assert.Equal(new Vector3(0.123f, 0.456f, 0.789f), draft.GetColor(8));
        draft.SetValue(8, draft.GetValue(8));
        var request = new CustomizeLiveRequest(System.Guid.NewGuid(), draft.Actor, draft.Representation,
            draft.ToCustomize(), draft.ToColorOverrides());
        Assert.False(request.CanApplyColorsDirectly(captured));
        Assert.True(CustomizeLiveRequest.CanApplyCustomizeDirectly(captured, request.CreateAppearance(captured)));
        Assert.Empty(request.Colors);
    }

    [Fact]
    public void DraftEditsPreserveFlagsAndResetToTheEditingBaseline()
    {
        var original = Enumerable.Range(0, 26).Select(i => (byte)i).ToImmutableArray().SetItem(16, 0x83);
        var draft = new CustomizeEditState(Actor(), original);
        draft.SetValue(16, 5, 0x7F);
        draft.SetValue(7, 0x80, 0x80);
        Assert.Equal(0x85, draft.GetValue(16));
        Assert.Equal(0x87, draft.GetValue(7));
        Assert.Equal(original, draft.Original);
        Assert.True(draft.HasChanges);
        var submitted = draft.ToCustomize();
        draft.Reset();
        Assert.Equal<byte>(original, draft.ToCustomize());
        Assert.False(draft.HasChanges);
        Assert.Equal(0x85, submitted[16]);
    }

    [Fact]
    public void ReturningToThePlayerAfterEditingAndPinningAnotherActorKeepsTheOriginalCustomize()
    {
        var state = new ActorAppearancePersistence(() => 1);
        var player = Actor();
        var original = Appearance() with
        {
            Customize = Appearance().Customize.SetItem(0, 4),
            CustomizeColors = [new(10, .1f, .2f, .3f)],
        };
        var firstDraft = state.CreateCustomizeDraft(player, original);
        firstDraft.SetValue(0, 5);
        firstDraft.SetColor(10, new(.4f, .5f, .6f));
        var changedPlayer = firstDraft.CreateAppearance(original);
        state.RememberOriginalCustomize(firstDraft);
        state.RecordModel(player, changedPlayer);

        var other = player with
        {
            LogicalKey = new(2, 2, 2, 20, ObjectKind.EventNpc, 30) { Lifetime = 2 },
            RepresentationKey = new(2, 2, 2, false, 30),
            ObjectKind = ObjectKind.EventNpc,
            BaseId = 20,
            IsLocalPlayer = false,
        };
        var otherOriginal = Appearance() with { Customize = Appearance().Customize.SetItem(1, 0) };
        var otherDraft = state.CreateCustomizeDraft(other, otherOriginal);
        otherDraft.SetValue(1, 1);
        var changedOther = otherDraft.CreateAppearance(otherOriginal);
        state.RememberOriginalCustomize(otherDraft);
        state.RecordModel(other, changedOther);
        var otherEntry = new ActorEntry(other.LogicalKey, other.Name, other.ObjectKind, false, [other]);
        var pins = new PinnedOutfitStore(new Configuration(), () => { }, () => 1);
        Assert.Equal((1, 0), pins.PinCurrent([otherEntry], _ => changedOther));

        var reopened = state.CreateCustomizeDraft(player, changedPlayer);
        Assert.Equal(5, reopened.GetValue(0));
        Assert.Equal(new Vector3(.4f, .5f, .6f), reopened.GetColor(10));
        reopened.Reset();
        var restored = reopened.CreateAppearance(changedPlayer);
        Assert.Equal<byte>(original.Customize, restored.Customize);
        Assert.Equal<CustomizeColorOverride>(original.CustomizeColors, restored.CustomizeColors);
        Assert.Equal(changedPlayer, restored with
        { Customize = changedPlayer.Customize, CustomizeColors = changedPlayer.CustomizeColors });
        Assert.True(pins.TryGetAppearance(otherEntry, out var pinned));
        Assert.Equal<byte>(changedOther.Customize, pinned.Customize);
        Assert.Equal(0, state.CreateCustomizeDraft(other, changedOther).Original[1]);

        state.Restore(player.LogicalKey);
        Assert.Equal<byte>(changedPlayer.Customize, state.CreateCustomizeDraft(player, changedPlayer).Original);
        Assert.Equal(0, state.CreateCustomizeDraft(other, changedOther).Original[1]);
    }

    [Fact]
    public void DraftTargetsOneLogicalActorAndOneRepresentation()
    {
        var actor = Actor();
        var draft = new CustomizeEditState(actor, Appearance().Customize);
        Assert.True(draft.Matches(actor));
        Assert.False(draft.Matches(actor with { LogicalKey = actor.LogicalKey with { Lifetime = 2 } }));
        Assert.False(draft.Matches(actor with { RepresentationKey = actor.RepresentationKey with { IsGPoseRepresentation = true } }));
    }

    [Fact]
    public void ApplyPayloadPreservesCurrentEquipmentWeaponsDyesHeadSettingsAndScale()
    {
        var original = Appearance();
        var draft = new CustomizeEditState(Actor(), original.Customize);
        draft.SetValue(6, 255);
        // Another explicit equipment action can happen while the draft is open.
        var current = original with { Mainhand = 0x123456, ModelScale = 1.7f, HatVisible = false };
        var desired = draft.CreateAppearance(current);
        Assert.Equal<CustomizeColorOverride>(current.CustomizeColors, desired.CustomizeColors);
        Assert.Equal(current, desired with { Customize = current.Customize, CustomizeColors = current.CustomizeColors });
        Assert.Equal(255, desired.Customize[6]);
        Assert.Equal(original.Customize, draft.Original);
    }

    [Fact]
    public void ExistingPublicationRetainsWeaponsAndFreeColorsWhenCustomizeIsApplied()
    {
        var actor = Actor();
        var state = new ActorAppearancePersistence();
        var rendered = Appearance();
        state.RecordWeapon(actor, false, rendered.Mainhand!.Value, rendered.MainhandDyes);
        state.RecordWeapon(actor, true, rendered.Offhand!.Value, rendered.OffhandDyes);
        var draft = new CustomizeEditState(actor, rendered.Customize);
        draft.SetValue(6, 200);
        var published = Plugin.CreatePublishedAppearance(draft.CreateAppearance(rendered));
        state.RecordModel(actor, published);
        var retained = state.GetCreateAppearance(actor.LogicalKey, 0, out var outfitOnly)!;
        Assert.False(outfitOnly);
        Assert.Equal(published, retained);
        Assert.Equal(rendered.MainhandDyes, state.GetWeaponDyes(actor.LogicalKey, false, rendered.Mainhand));
        Assert.Equal(rendered.OffhandDyes, state.GetWeaponDyes(actor.LogicalKey, true, rendered.Offhand));
        Assert.Equal(rendered.ColoredEquipment, state.GetColorOutfit(actor.LogicalKey)!.Equipment);
    }

    [Fact]
    public void CustomizeUpdatesOnlyAnExistingPinsCustomize()
    {
        var actor = Actor();
        var entry = new ActorEntry(actor.LogicalKey, actor.Name, actor.ObjectKind, false, [actor]);
        var configuration = new Configuration();
        var saves = 0;
        var store = new PinnedOutfitStore(configuration, () => saves++, () => 1);
        var current = Appearance();
        var edited = current.Customize.SetItem(6, 255);
        store.UpdateCustomize(entry, edited);
        Assert.Equal(0, store.Count);
        Assert.Equal(0, saves);
        store.PinCurrent([entry], _ => current);
        store.UpdateCustomize(entry, edited);
        Assert.True(store.TryGetAppearance(entry, out var pinned));
        Assert.Equal(current, pinned with { Customize = current.Customize });
        Assert.Equal(edited, pinned.Customize);
        Assert.Equal(1, store.Count);
        Assert.Equal(2, saves);
    }

    private static ActorSnapshot Actor()
    {
        var key = new LogicalActorKey(1, 1, 1, 0, ObjectKind.Pc, 30) { Lifetime = 1 };
        return new(key, new(1, 1, 1, false, 30), "Actor", ObjectKind.Pc, 0, 0, 1, 0, 1, 19, 90, true);
    }

    private static AppearanceData Appearance()
        => AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(0x020101UL, 10),
            1.2f, 123, 456, true, 7, true) with
        {
            ColoredEquipment = Enumerable.Repeat(new ArmorAppearance(257, 2, 0, 0)
                { Color1 = new DyeColor(1, 0, 0), Color2 = new DyeColor(0, 1, 0) }, 10).ToImmutableArray(),
            MainhandDyes = new(new DyeColor(1, 0, 0), new DyeColor(0, 1, 0)),
            OffhandDyes = new(null, new DyeColor(0, 0, 1)),
            VieraEarsHidden = true,
        };
}
