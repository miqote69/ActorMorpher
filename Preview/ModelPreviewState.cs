namespace ActorMorpher.Preview;

public enum ModelPreviewState
{
    Idle,
    Suspended,
    Unsupported,
    Loading,
    Ready,
    Failed,
}

public sealed record ModelPreviewSnapshot(long Generation, ModelPreviewState State, uint? ModelId, string Status);
