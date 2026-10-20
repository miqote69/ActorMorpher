using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CustomizeCompletionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletionIsRetainedWithoutDrawingTheTabOrReadingTheLatestGlobalOperation(bool succeeded)
    {
        // No Draw or Plugin access: a completion delivered while the tab is hidden must remain available.
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var operation = Guid.NewGuid();
        Field("customizeOperation").SetValue(window, operation);
        Complete(window, Guid.NewGuid(), !succeeded);
        Assert.Null(Field("customizeSucceeded").GetValue(window));
        Complete(window, operation, succeeded);
        Complete(window, Guid.NewGuid(), !succeeded);
        Assert.Equal(succeeded, Field("customizeSucceeded").GetValue(window));
    }

    private static FieldInfo Field(string name)
        => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void Complete(MainWindow window, Guid operation, bool succeeded)
        => typeof(MainWindow).GetMethod("OnCustomizeOperationCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [operation, succeeded]);
}
