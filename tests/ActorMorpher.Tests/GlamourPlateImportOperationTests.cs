using ActorMorpher.BulkOutfit;
using Xunit;

namespace ActorMorpher.Tests;

public class GlamourPlateImportOperationTests
{
    [Fact]
    public void OneClickWaitsWithoutResendingAndCompletesSelectedPlateOnce()
    {
        var operation = new GlamourPlateImportOperation();
        var requests = 0;
        Assert.True(operation.Start(1, 42, 339, () => { ++requests; return true; }));
        Assert.False(operation.Start(5, 42, 339, () => { ++requests; return true; }));
        for (var frame = 0; frame < 1000; ++frame)
            Assert.Equal(PlateImportProgress.Waiting, operation.Poll(42, 339, false, out _));
        Assert.True(operation.IsPending);
        Assert.Equal(1, requests);
        Assert.Equal(PlateImportProgress.Ready, operation.Poll(42, 339, true, out var plate));
        Assert.Equal(1, plate);
        Assert.False(operation.IsPending);
        Assert.Equal(PlateImportProgress.Idle, operation.Poll(42, 339, true, out _));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(0ul, 339u)]
    [InlineData(43ul, 339u)]
    [InlineData(42ul, 340u)]
    public void ChangedContextCannotCompleteFromLoadedData(ulong owner, uint territory)
    {
        var operation = new GlamourPlateImportOperation();
        Assert.True(operation.Start(1, 42, 339, () => true));
        Assert.Equal(PlateImportProgress.ContextChanged, operation.Poll(owner, territory, true, out _));
        Assert.False(operation.IsPending);
        Assert.Equal(PlateImportProgress.Idle, operation.Poll(42, 339, true, out _));
    }

    [Fact]
    public void CancellationIgnoresLateDataAndAllowsNextExplicitImport()
    {
        var operation = new GlamourPlateImportOperation();
        Assert.True(operation.Start(1, 42, 339, () => true));
        operation.Cancel();
        Assert.Equal(PlateImportProgress.Idle, operation.Poll(42, 339, true, out _));
        Assert.True(operation.Start(4, 42, 339, () => true));
        Assert.Equal(PlateImportProgress.Ready, operation.Poll(42, 339, true, out var plate));
        Assert.Equal(4, plate);
    }

    [Fact]
    public void UnavailableRequestDoesNotLeaveSourceBusy()
    {
        var operation = new GlamourPlateImportOperation();
        Assert.False(operation.Start(1, 42, 339, () => false));
        Assert.False(operation.IsPending);
        Assert.Equal(PlateImportProgress.Idle, operation.Poll(42, 339, true, out _));
    }
}
