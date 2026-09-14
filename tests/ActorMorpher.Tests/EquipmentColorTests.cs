using System;
using System.Linq;
using System.Buffers.Binary;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Xunit;

namespace ActorMorpher.Tests;

public class EquipmentColorTests
{
    private static readonly System.Numerics.Vector3 Blue = new(80f / 255, 82f / 255, 217f / 255);
    private static readonly System.Numerics.Vector3 Silver = new(167f / 255);

    private static Half[] BlueTemplate(int length)
    {
        // Actual legacy stainingtemplate.stm template 200, Metallic Blue 119.
        var table = Enumerable.Repeat((Half)0.2f, length).ToArray();
        table[0] = (Half)0.08880615234375f;
        table[1] = (Half)0.06298828125f;
        table[2] = (Half)0.492919921875f;
        table[4] = (Half)0.044952392578125f;
        table[5] = (Half)0.459716796875f;
        table[6] = (Half)0.6591796875f;
        table[8] = table[9] = table[10] = (Half)0;
        table[3] = (Half)2; table[7] = (Half)1;
        return table;
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void MetallicBlueReproducesGameColorsAndOffRestoresWithoutAccumulation(int channel, bool nativeTint)
    {
        var original = Enumerable.Repeat((Half)0.25f, 64).ToArray();
        original[3] = (Half)4;
        var blue = BlueTemplate(64);
        var silver = Enumerable.Repeat((Half)0.71f, 64).ToArray();
        var dyes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(dyes, 0x00C8000Bu | ((uint)channel << 27));
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(4), 0x00C8000Bu | ((uint)(1 - channel) << 27));
        foreach (var metallic in new bool?[] { true, false, true, null })
        {
            var table = (Half[])original.Clone();
            DyeColor? color = nativeTint && metallic is null ? null :
                new DyeColor(nativeTint ? 1 : Blue.X, nativeTint ? 1 : Blue.Y, nativeTint ? 1 : Blue.Z)
                    { Metallic = metallic, UseOriginalColor = nativeTint };
            var armor = new ArmorAppearance(6072, 1, 119, 119).WithDye(channel, color);
            var profile = NativeEquipmentColors.BlendMetallicTables(original, 8, 2, dyes, 4,
                armor, [Silver, Blue], [silver, blue], Blue, Blue);
            Assert.True(NativeEquipmentColors.Transform(table, 8, 2, dyes, 4, armor, channel, true, profile, true));
            for (var c = 0; c < 3; ++c)
            {
                var selected = c == 0 ? Blue.X : c == 1 ? Blue.Y : Blue.Z;
                Assert.Equal(metallic == true ? blue[c] : nativeTint ? original[c] : (Half)(selected * selected), table[c]);
                Assert.Equal(metallic == true ? blue[4 + c] : original[4 + c], table[4 + c]);
            }
            Assert.Equal(metallic == true ? blue[3] : original[3], table[3]);
            for (var i = 0; i < table.Length; ++i)
                if (i is not (0 or 1 or 2 or 3 or 4 or 5 or 6)) Assert.Equal(original[i], table[i]);
            var weapon = new WeaponDyes().WithDye(channel, color).AsArmor(119UL << 48 | 119UL << 56);
            Assert.Equal(armor.Color1, weapon.Color1);
            Assert.Equal(armor.Color2, weapon.Color2);
        }
    }

    [Fact]
    public void FreeColorInterpolatesInsteadOfSelectingOneGameDye()
    {
        var original = new Half[32];
        var blue = BlueTemplate(32);
        var silver = Enumerable.Repeat((Half)0.71f, 32).ToArray();
        blue[27] = (Half)3; silver[27] = (Half)7; // Sphere-map indices cannot be averaged to 5.
        var dyes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(dyes, 0xfff);
        Half[] At(System.Numerics.Vector3 rgb)
        {
            var armor = new ArmorAppearance(1, 1, 0, 0)
                { Color1 = new DyeColor(rgb.X, rgb.Y, rgb.Z) { Metallic = true } };
            var profile = NativeEquipmentColors.BlendMetallicTables(original, 8, 1, dyes, 4,
                armor, [Silver, Blue], [silver, blue], null, null);
            var result = (Half[])original.Clone();
            NativeEquipmentColors.Transform(result, 8, 1, dyes, 4, armor, metallicTable: profile);
            return result;
        }
        var midpoint = (Blue + Silver) / 2;
        var middle = At(midpoint);
        foreach (var c in new[] { 0, 1, 2, 4, 5, 6, 8, 9, 10, 16, 18 })
            Assert.InRange(Math.Abs((float)middle[c] - ((float)blue[c] + (float)silver[c]) / 2), 0, 0.001f);
        Assert.Contains(middle[27], new[] { blue[27], silver[27] });
        var nearby = At(midpoint + new System.Numerics.Vector3(0.0001f));
        Assert.InRange(Math.Abs((float)nearby[0] - (float)middle[0]), 0, 0.001f);
        Assert.NotEqual(At(Blue)[0], middle[0]);
        Assert.NotEqual(At(Silver)[0], middle[0]);
    }

    [Fact]
    public void UndyedOriginalUsesMaterialColorInsteadOfTheWhitePlaceholder()
    {
        var original = Enumerable.Repeat((Half)0.25f, 32).ToArray();
        var dyes = new byte[] { 3, 0 };
        var blue = BlueTemplate(32);
        var silver = Enumerable.Repeat((Half)0.71f, 32).ToArray();
        var armor = new ArmorAppearance(1, 1, 0, 0)
            { Color1 = new DyeColor(1, 1, 1) { Metallic = true, UseOriginalColor = true } };
        var fromOriginal = NativeEquipmentColors.BlendMetallicTables(original, 8, 1, dyes, 2,
            armor, [Silver, Blue], [silver, blue], null, null);
        var explicitColor = armor with { Color1 = new DyeColor(0.5f, 0.5f, 0.5f) { Metallic = true } };
        var expected = NativeEquipmentColors.BlendMetallicTables(original, 8, 1, dyes, 2,
            explicitColor, [Silver, Blue], [silver, blue], null, null);
        Assert.Equal(expected, fromOriginal);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public unsafe void ActualShaderChoosesFinishLayoutIndependentlyOfModernTableStorage(int channel, bool legacy)
    {
        var utility = new FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterUtility();
        var legacyPackage = new FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ShaderPackageResourceHandle();
        var modernPackage = new FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ShaderPackageResourceHandle();
        utility.ResourceHandles[86] = (FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ResourceHandle*)&legacyPackage;
        var material = new FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.MaterialResourceHandle();
        material.ShaderPackageResourceHandle = legacy ? &legacyPackage : &modernPackage;
        var legacyShader = NativeEquipmentColors.UsesLegacyShader(&material, &utility);
        Assert.Equal(legacy, legacyShader);

        // e6072 live snapshot: modern 8x32 table, 4-byte dye records, but legacy shader.
        // Template 200, flags 0x0B: diffuse + specular + shininess, no specular-mask flag.
        var original = Enumerable.Repeat((Half)0.25f, 1024).ToArray();
        original[3] = (Half)4; original[7] = (Half)1;
        original[4] = original[5] = original[6] = (Half)0.0625f;
        original[11] = (Half)0;
        var profile = (Half[])original.Clone();
        profile[3] = (Half)2; // Actual legacy STM template 200 + stain 112.
        profile[7] = (Half)0.5f; // Deliberately differs: disabled field must not be copied.
        profile[11] = (Half)1.25f; // Sentinel distinguishes the modern shader route.
        profile[4] = profile[5] = profile[6] = (Half)0.71044921875f;
        var dyes = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(dyes, 0x00C8000Bu | ((uint)channel << 27));
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(4), 0x00C8000Bu | ((uint)(1 - channel) << 27));
        var armor = new ArmorAppearance(6072, 1, 0, 123);
        foreach (var metallic in new[] { true, false })
        {
            var table = (Half[])original.Clone();
            var color = new DyeColor(1, 1, 1) { Metallic = metallic, UseOriginalColor = true };
            Assert.True(NativeEquipmentColors.Transform(table, 8, 32, dyes, 4,
                armor.WithDye(channel, color), channel, true, profile, legacyShader));
            Assert.Equal(metallic && legacy ? (Half)2 : original[3], table[3]);
            Assert.Equal(metallic && !legacy ? profile[11] : original[11], table[11]);
            Assert.Equal(original[7], table[7]);
            Assert.Equal(original[..3], table[..3]);
            Assert.Equal(original[8..11], table[8..11]);
            Assert.Equal(original[12..], table[12..]);
        }
    }

    [Fact]
    public unsafe void MissingWeaponMaterialsDoNotReportApplied()
    {
        var dyes = new WeaponDyes(new DyeColor(1, 0, 0) { Metallic = true }, null);
        Assert.False(NativeOutfitMemory.ApplyWeaponColors(null, 123, dyes, true).Applied);
        var weapon = new FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Weapon();
        Assert.False(NativeOutfitMemory.ApplyWeaponColors(&weapon, 123, dyes, true).Applied);
    }

    [Fact]
    public void OppositeChannelCannotConfirmTheRequestedColorOrItsClear()
    {
        var table = Enumerable.Repeat((Half)0.75f, 32).ToArray();
        var dye = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(dye, 1u | (1u << 27));
        var armor = new ArmorAppearance(1, 1, 0, 0) { Color1 = new(1, 0, 0), Color2 = new(0, 0, 1) };
        Assert.False(NativeEquipmentColors.Transform(table, 8, 1, dye, 4, armor, 0, true));
        Assert.Equal(new Half[] { (Half)0, (Half)0, (Half)1 }, table[..3]);
        Assert.False(NativeEquipmentColors.Transform(table, 8, 1, dye, 4,
            armor with { Color1 = null }, 0, true));
        Assert.True(NativeEquipmentColors.Transform(table, 8, 1, dye, 4, armor, 1, true));
        Assert.True(NativeEquipmentColors.Transform(table, 8, 1, dye, 4,
            armor with { Color2 = null }, 1, true));
    }

    [Fact]
    public void MetallicNeedsNoMetalnessFlagAndRespectsDyeFieldsAndChannels()
    {
        var original = Enumerable.Repeat((Half)0.75f, 128).ToArray();
        var profile = Enumerable.Repeat((Half)0.375f, 128).ToArray();
        var dyes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(dyes, 0x22); // Specular + roughness, no metalness.
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(4), 0x22u | (1u << 27));
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(8), 0); // No dye fields.
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(12), 0x22u | (2u << 27));
        var armor = new ArmorAppearance(1, 1, 0, 0) { Color1 = new DyeColor(1, 0.5f, 0) { Metallic = true } };
        foreach (var reportChannel in new[] { 0, 1 })
        {
            var table = (Half[])original.Clone();
            Assert.Equal(reportChannel == 0, NativeEquipmentColors.Transform(table, 8, 4, dyes, 4,
                armor, reportChannel, metallicTable: profile));
            Assert.Equal((Half)0.375f, table[4]);
            Assert.Equal((Half)0.375f, table[5]);
            Assert.Equal((Half)0.375f, table[6]);
            Assert.Equal((Half)0.375f, table[16]);
            for (var i = 0; i < table.Length; ++i)
                if (i is not (4 or 5 or 6 or 16)) Assert.Equal(original[i], table[i]);
        }
        var clear = (Half[])original.Clone();
        Assert.True(NativeEquipmentColors.Transform(clear, 8, 4, dyes, 4, armor with { Color1 = null }, 0, true));
        Assert.Equal(original, clear);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ClearDyeKeepsTheOtherChannelAndOriginalDyeRetainsStain(int channel)
    {
        var original = new ArmorAppearance(9005, 3, 7, 8)
        { Color1 = new DyeColor(1, 0, 0) { Metallic = true }, Color2 = new(0, 0, 1) };
        var native = original.WithDye(channel, null);
        Assert.Equal((byte)7, native.Stain1);
        Assert.Equal((byte)8, native.Stain2);
        var clear = original.WithDye(channel, null, true);
        Assert.Equal(channel == 0 ? original with { Stain1 = 0, Color1 = null }
            : original with { Stain2 = 0, Color2 = null }, clear);
        var weapon = new WeaponDyes(original.Color1, original.Color2).WithDye(channel, null);
        var tint = weapon.AsArmor(9005UL | (7UL << 48) | (8UL << 56));
        Assert.Equal(native.Color1, tint.Color1);
        Assert.Equal(native.Color2, tint.Color2);
        Assert.Equal(native.Stain1, tint.Stain1);
        Assert.Equal(native.Stain2, tint.Stain2);
    }

    [Fact]
    public void ModernRowsRespectChannelsAndLeaveNonDiffuseValuesUntouched()
    {
        var table = Enumerable.Repeat((Half)0.75f, 128).ToArray();
        var dyes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(dyes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(4), 1u | (1u << 27));
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(12), 1u | (2u << 27));
        var armor = new ArmorAppearance(51, 2, 0, 0)
        { Color1 = new(0, 0.5f, 1), Color2 = new(1, 0, 0.5f) };
        Assert.True(NativeEquipmentColors.Transform(table, 8, 4, dyes, 4, armor));
        Assert.Equal(new Half[] { (Half)0, (Half)0.25f, (Half)1 }, table[..3]);
        Assert.Equal(new Half[] { (Half)1, (Half)0, (Half)0.25f }, table[32..35]);
        Assert.All(table[3..32].Concat(table[35..]), item => Assert.Equal((Half)0.75f, item));
    }

    [Fact]
    public void LegacyAndMissingDyeRowsDoNotInventDyeableRegions()
    {
        var table = Enumerable.Repeat((Half)1, 16).ToArray();
        var armor = new ArmorAppearance(1, 1, 0, 0) { Color1 = new(0, 0, 0), Color2 = new(1, 0, 0) };
        Assert.False(NativeEquipmentColors.Transform(table, 4, 1, [], 2, armor));
        Assert.All(table, value => Assert.Equal((Half)1, value));
        Assert.True(NativeEquipmentColors.Transform(table, 4, 1, new byte[] { 1, 0 }, 2, armor));
        Assert.All(table[..3], value => Assert.Equal((Half)0, value));
        Assert.All(table[3..], value => Assert.Equal((Half)1, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppearanceAndPinSerializationPreserveBothColors(bool originalColor)
    {
        var armor = new ArmorAppearance(51, 2, 3, 4)
        { Color1 = new DyeColor(0.1f, 0.2f, 0.3f) { Metallic = true, UseOriginalColor = originalColor }, Color2 = new(0, 0, 0) };
        var outfit = OutfitData.Create(Enumerable.Repeat(armor, 10), new(true, 0), true, false);
        var appearance = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            new byte[26], Enumerable.Repeat(0UL, 10), visorToggled: false, facewearModelId: 0, hatVisible: true)
            with { ColoredEquipment = outfit.Equipment,
                MainhandDyes = new(armor.Color1, armor.Color2),
                OffhandDyes = new(new DyeColor(1, 1, 1) { Metallic = false }, null) };
        var pin = new PinnedOutfitConfiguration { Appearance = appearance };
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(pin);
        var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<PinnedOutfitConfiguration>(json)!;
        Assert.True(copy.TryCreateOutfit(out var restored));
        Assert.Equal(armor, restored.Equipment[0]);
        Assert.True(PinnedOutfitStore.AppearanceEquals(appearance, copy.Appearance!));
        Assert.Equal(appearance.MainhandDyes, copy.Appearance!.MainhandDyes);
        Assert.Equal(appearance.OffhandDyes, copy.Appearance.OffhandDyes);
        Assert.False(PinnedOutfitStore.AppearanceEquals(appearance, appearance with { MainhandDyes = default }));
        Assert.False(PinnedOutfitStore.AppearanceEquals(appearance, appearance with { ColoredEquipment = [] }));
    }
}
