using ActorMorpher.Interop;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Redraw;

public sealed class RedrawCoordinator : IDisposable
{
    private const int TimeoutFrames = 180;

    private readonly IFramework framework;
    private readonly IActorResolver resolver;
    private readonly IAppearanceMemory appearanceMemory;
    private readonly IRedrawBackend redrawBackend;
    private readonly IClientContext clientContext;
    private readonly Queue<RedrawOperation> queue = new();
    private RedrawOperation? current;
    private bool disposed;

    public RedrawCoordinator(
        IFramework framework,
        IActorResolver resolver,
        IAppearanceMemory appearanceMemory,
        IRedrawBackend redrawBackend,
        IClientContext clientContext)
    {
        this.framework = framework;
        this.resolver = resolver;
        this.appearanceMemory = appearanceMemory;
        this.redrawBackend = redrawBackend;
        this.clientContext = clientContext;
        framework.Update += OnFrameworkUpdate;
    }

    public RedrawOperation? Current => current;
    public RedrawOperation? LastResult { get; private set; }

    public bool Enqueue(RedrawOperation operation)
    {
        if (disposed || operation.TerritoryId != clientContext.TerritoryId)
            return false;

        queue.Enqueue(operation);
        return true;
    }

    public void CancelAll(string reason)
    {
        queue.Clear();
        if (current is not null)
            Finish(current with { Stage = RedrawStage.Cancelled, Error = reason });
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        framework.Update -= OnFrameworkUpdate;
        CancelAll("Plugin disposed.");
    }

    internal void AdvanceOneFrame()
    {
        if (disposed)
            return;

        if (!clientContext.IsLoggedIn)
        {
            CancelAll("Logged out.");
            return;
        }

        current ??= queue.Count > 0 ? queue.Dequeue() : null;
        if (current is null)
            return;

        var operation = current with { FrameCount = current.FrameCount + 1 };
        if (operation.TerritoryId != clientContext.TerritoryId)
        {
            Finish(operation with { Stage = RedrawStage.Cancelled, Error = "Territory changed." });
            return;
        }

        if (operation.FrameCount > TimeoutFrames)
        {
            if (operation.Stage is RedrawStage.Rollback or RedrawStage.RollbackEnable)
                Finish(operation with { Stage = RedrawStage.Failed, Error = "Rollback timed out." });
            else
                current = operation with { Stage = RedrawStage.Rollback, FrameCount = 0, Error = "Redraw timed out." };
            return;
        }

        if (!resolver.TryResolve(operation.Actor, out var actor))
        {
            Finish(operation with { Stage = RedrawStage.Cancelled, Error = "Actor is no longer available." });
            return;
        }

        current = operation.Stage switch
        {
            RedrawStage.Pending => operation with { Stage = RedrawStage.Disable },
            RedrawStage.Disable when redrawBackend.TryDisable(actor) => operation with { Stage = RedrawStage.Apply },
            RedrawStage.Apply when appearanceMemory.TryWrite(operation.Actor, operation.Desired) => operation with { Stage = RedrawStage.Enable },
            RedrawStage.Enable when redrawBackend.TryEnable(actor) => operation with { Stage = RedrawStage.Verify },
            RedrawStage.Verify when appearanceMemory.IsApplied(operation.Actor, operation.Desired) => Complete(operation),
            RedrawStage.Rollback when appearanceMemory.TryWrite(operation.Actor, operation.Rollback) => operation with { Stage = RedrawStage.RollbackEnable },
            RedrawStage.RollbackEnable when redrawBackend.TryEnable(actor) => Fail(operation),
            RedrawStage.Disable or RedrawStage.Apply or RedrawStage.Enable or RedrawStage.Verify
                => operation with { Stage = RedrawStage.Rollback, Error = operation.Error ?? "Redraw stage failed." },
            RedrawStage.Rollback or RedrawStage.RollbackEnable => Fail(operation),
            _ => operation,
        };
    }

    private void OnFrameworkUpdate(IFramework _)
        => AdvanceOneFrame();

    private RedrawOperation Complete(RedrawOperation operation)
    {
        Finish(operation with { Stage = RedrawStage.Completed });
        return null!;
    }

    private RedrawOperation Fail(RedrawOperation operation)
    {
        Finish(operation with { Stage = RedrawStage.Failed, Error = operation.Error ?? "Rollback failed." });
        return null!;
    }

    private void Finish(RedrawOperation operation)
    {
        LastResult = operation;
        current = null;
    }
}
