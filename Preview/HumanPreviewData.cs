using System.Collections.Immutable;

namespace ActorMorpher.Preview;

public sealed record HumanPreviewData(
    uint ModelCharaId,
    uint SourceRowId,
    ImmutableArray<byte> Customize,
    ImmutableArray<ulong> Equipment,
    ImmutableArray<ulong> Weapons,
    ImmutableArray<ushort> Glasses,
    bool HeadgearHidden,
    bool WeaponHidden,
    bool VisorClosed)
{
    public byte Race => Customize[0];
    public byte Sex => Customize[1];
    public byte BodyType => Customize[2];
}
