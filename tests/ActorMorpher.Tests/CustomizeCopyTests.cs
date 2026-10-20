using System;
using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CustomizeCopyTests
{
    [Theory]
    [InlineData(ModelCategory.Demihuman)]
    [InlineData(ModelCategory.Monster)]
    public void CopyRequiresHumanEvenWhenOtherModelsHaveTwentySixBytes(ModelCategory category)
    {
        Assert.False(CustomizeCopyData.TryCreate(Appearance() with { Category = category }, out _));
    }

    [Fact]
    public void CopyRequiresTheCurrentHumanCustomizeBlock()
    {
        Assert.False(CustomizeCopyData.TryCreate(null, out _));
        Assert.False(CustomizeCopyData.TryCreate(Appearance() with { Customize = [1, 2, 3] }, out _));
    }

    [Fact]
    public void CopyUsesFreshRenderedBytesAndOnlyTheSourcesOwnedColors()
    {
        var source = Actor();
        var managed = Appearance() with { CustomizeColors = [new(10, .123f, .456f, .789f)] };
        var fresh = Appearance() with
        {
            Customize = Enumerable.Range(0, 26).Select(i => (byte)(255 - i)).ToImmutableArray(),
        };
        var described = ActorRegistry.DescribeRenderedAppearance(
            source with { IsAppearanceManaged = true, CurrentAppearance = managed },
            source with { CurrentAppearance = fresh });
        Assert.True(CustomizeCopyData.TryCreate(described, out var copied));
        Assert.Equal<byte>(fresh.Customize, copied.Customize);
        Assert.Equal<CustomizeColorOverride>(managed.CustomizeColors, copied.CustomizeColors);

        // Reading the displayed palette colors is not a request to own those RGB channels.
        var draft = new CustomizeEditState(source, fresh.Customize, managed.CustomizeColors);
        draft.UpdateRenderedColors([new(8, .9f, .8f, .7f)]);
        draft.SetValue(0, 1);
        draft.SetColor(10, new(1, 0, 0));
        Assert.Equal<byte>(fresh.Customize, copied.Customize);
        Assert.Equal<CustomizeColorOverride>([new(10, .123f, .456f, .789f)], copied.CustomizeColors);
    }

    [Fact]
    public void PasteTransfersAllBytesAndColorsWhileResetKeepsTheDestinationBaseline()
    {
        var destination = Actor();
        var baseline = Appearance() with { CustomizeColors = [new(8, .1f, .2f, .3f)] };
        var source = Appearance() with
        {
            Customize = Enumerable.Range(0, 26).Select(i => (byte)(128 + i)).ToImmutableArray(),
            CustomizeColors = [new(25, .9f, .7f, .5f), new(10, .123f, .456f, .789f)],
        };
        Assert.True(CustomizeCopyData.TryCreate(source, out var copied));
        var draft = new CustomizeEditState(destination, baseline.Customize, baseline.CustomizeColors);
        draft.Paste(copied);
        Assert.Equal<byte>(source.Customize, draft.ToCustomize());
        Assert.Equal<CustomizeColorOverride>(source.CustomizeColors.OrderBy(color => color.Index), draft.ToColorOverrides());
        Assert.Equal<byte>(baseline.Customize, draft.Original);
        Assert.True(draft.Matches(destination));
        Assert.Equal(1, draft.Revision);
        draft.Paste(copied);
        Assert.Equal(1, draft.Revision);
        draft.Reset();
        Assert.Equal<byte>(baseline.Customize, draft.ToCustomize());
        Assert.Equal<CustomizeColorOverride>(baseline.CustomizeColors, draft.ToColorOverrides());
        Assert.False(draft.HasChanges);
        Assert.Equal(2, draft.Revision);
        Assert.Equal<byte>(source.Customize, copied.Customize);
    }

    [Fact]
    public void PastedRequestUsesDestinationCurrentEquipmentWeaponsDyesHeadSettingsAndScale()
    {
        var actor = Actor();
        var baseline = Appearance();
        var source = Appearance() with { Customize = baseline.Customize.SetItem(24, 0xFF), CustomizeColors = [] };
        Assert.True(CustomizeCopyData.TryCreate(source, out var copied));
        var draft = new CustomizeEditState(actor, baseline.Customize, [new(8, 1, 0, 0)]);
        draft.Paste(copied);
        var current = baseline with
        {
            Equipment = Enumerable.Repeat(0x020101UL, 10).ToImmutableArray(),
            Mainhand = 0x123456,
            Offhand = 0x987654,
            ModelScale = 1.7f,
            HatVisible = false,
            VisorToggled = true,
            FacewearModelId = 12,
            VieraEarsHidden = true,
            ColoredEquipment = [new ArmorAppearance(257, 2, 0, 0) { Color1 = new DyeColor(1, 0, 0) }],
            MainhandDyes = new(new DyeColor(1, 0, 0), new DyeColor(0, 1, 0)),
            OffhandDyes = new(null, new DyeColor(0, 0, 1)),
            CustomizeColors = [new(8, 1, 0, 0)],
        };
        var request = new CustomizeLiveRequest(Guid.NewGuid(), draft.Actor, draft.Representation,
            draft.ToCustomize(), draft.ToColorOverrides());
        var desired = request.CreateAppearance(current);
        Assert.Equal<byte>(source.Customize, desired.Customize);
        Assert.Empty(desired.CustomizeColors);
        Assert.Equal(current, desired with { Customize = current.Customize, CustomizeColors = current.CustomizeColors });
    }

    private static ActorSnapshot Actor()
        => new(new(1, 1, 1, 0, ObjectKind.Pc, 30), new(1, 1, 1, false, 30),
            "Actor", ObjectKind.Pc, 0, 0, 1, 0, 1, 19, 90, true);

    private static AppearanceData Appearance()
        => AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(0UL, 10));
}
