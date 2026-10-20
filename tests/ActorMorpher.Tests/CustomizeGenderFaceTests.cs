using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using ActorMorpher.Customization;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CustomizeGenderFaceTests
{
    // Fixed paths from the current game's SetupIdsAndVfxScale and Human.ResolveMdlPath,
    // not from the preview resolver or an implementation of their formulas in this test.
    // EXE SHA256: 5BBC501DD5C7F22FD61A11D08C25356041D878DB7CD83203ADAE393E4DFACC44.
    // Evidence: .scratch/customize-crash/20261004-132444/native-face/{setup-ids,resolve-mdl}.log.
    // A wrong lookup here would replace an existing NPC face with face 1 on a gender click.
    [Theory]
    [InlineData(1, 1, 1, 1, 6, 0, "chara/human/c0201/obj/face/f0006/model/c0201f0006_fac.mdl", false)]
    [InlineData(1, 1, 1, 1, 1, 0, "chara/human/c0201/obj/face/f0001/model/c0201f0001_fac.mdl", true)]
    [InlineData(1, 1, 1, 1, 209, 0, "chara/human/c0201/obj/face/f0209/model/c0201f0209_fac.mdl", true)]
    [InlineData(8, 16, 1, 1, 4, 0, "chara/human/c1801/obj/face/f0004/model/c1801f0004_fac.mdl", true)]
    [InlineData(4, 8, 1, 4, 4, 0, "chara/human/c0804/obj/face/f0004/model/c0804f0004_fac.mdl", true)]
    [InlineData(2, 4, 1, 3, 3, 0, "chara/human/c0601/obj/face/f0193/model/c0601f0193_fac.mdl", true)]
    [InlineData(1, 1, 1, 1, 1, 128, "chara/human/c9201/obj/face/f0001/model/c9201f0001_fac.mdl", true)]
    public void FaceExistenceUsesTheNativeGoldenPathExactlyOnce(int race, int tribe, int sex, int body,
        int face, int extraFeature, string expectedPath, bool exists)
    {
        var catalog = (CustomizeCatalog)RuntimeHelpers.GetUninitializedObject(typeof(CustomizeCatalog));
        var calls = 0;
        Func<string, bool> fileExists = path =>
        {
            calls++;
            Assert.Equal(expectedPath, path);
            return exists;
        };
        typeof(CustomizeCatalog).GetField("fileExists", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(catalog, fileExists);
        var customize = new byte[26];
        customize[0] = (byte)race;
        customize[1] = (byte)sex;
        customize[2] = (byte)body;
        customize[4] = (byte)tribe;
        customize[5] = (byte)face;
        customize[22] = (byte)extraFeature;

        Assert.Equal(exists, catalog.HasFaceModel(customize));
        Assert.Equal(1, calls);
    }
}
