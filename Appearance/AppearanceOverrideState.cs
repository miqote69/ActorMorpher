namespace ActorMorpher.Appearance;

public sealed record AppearanceOverrideState(
    AppearanceData BaseData,
    AppearanceData DesiredData,
    long Revision);
