using System.Numerics;
using ActorMorpher.Actors;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ActorMarkerProjectionTests
{
    [Fact]
    public void MarkerUsesVisibleCopyWhileAppearanceResolverRemainsUnchanged()
    {
        var original = Snapshot(1, false);
        var copy = Snapshot(200, false);
        var actor = Entry(original, copy);
        Assert.True(ActorMarkerProjection.TryGetScreenPosition(actor, false, Project, out var screen));
        Assert.Equal(new Vector2(200, 200), screen);
        Assert.Same(original, RegistryActorResolver.SelectRepresentation(actor, false));
        Assert.False(ActorMarkerProjection.TryGetScreenPosition(Entry(original), false, Project, out _));

        bool Project(ActorSnapshot candidate, out Vector2 position)
        {
            position = new Vector2(200, 200);
            return candidate == copy;
        }
    }

    [Fact]
    public void MarkerDoesNotReplaceMissingGPoseCopyWithVisibleFieldActor()
    {
        var normal = Snapshot(1, false);
        var gpose = Snapshot(201, true);
        var actor = Entry(normal, gpose);
        Assert.False(ActorMarkerProjection.TryGetScreenPosition(actor, true, ProjectField, out _));
        Assert.True(ActorMarkerProjection.TryGetScreenPosition(Entry(normal), true, ProjectGPose,
            out var screen, gpose));
        Assert.Equal(new Vector2(201, 201), screen);

        bool ProjectField(ActorSnapshot candidate, out Vector2 position)
        {
            position = new Vector2(1, 1);
            return candidate == normal;
        }
        bool ProjectGPose(ActorSnapshot candidate, out Vector2 position)
        {
            position = new Vector2(201, 201);
            return candidate == gpose;
        }
    }

    [Fact]
    public void OffScreenCopyCannotHideLaterOnScreenRepresentation()
    {
        var offScreen = Snapshot(200, false) with { Position = new Vector3(2, 0, 0) };
        var onScreen = Snapshot(201, false) with { Position = new Vector3(0.5f, 0, 0) };
        Assert.True(ActorMarkerProjection.TryGetScreenPosition(Entry(offScreen, onScreen), false,
            Project, out var screen));
        Assert.Equal(new Vector2(750, 500), screen);
        Assert.False(ActorMarkerProjection.TryGetScreenPosition(Entry(offScreen), false,
            Project, out var hidden));
        Assert.Equal(default, hidden);

        static bool Project(ActorSnapshot candidate, out Vector2 position)
            => ActorMarkerProjection.TryProject(candidate.Position, Matrix4x4.Identity, Vector2.Zero,
                new Vector2(1000, 1000), out position);
    }

    [Fact]
    public void CurrentSceneCameraMovesMarkerAndHidesItWhenHeadLeavesScreen()
    {
        var world = new Vector3(0, 0.5f, 0);
        var origin = new Vector2(100, 50);
        var size = new Vector2(800, 600);
        Assert.True(ActorMarkerProjection.TryProject(world, Matrix4x4.Identity, origin, size, out var first));
        Assert.Equal(new Vector2(500, 200), first);
        Assert.True(ActorMarkerProjection.TryProject(world, Matrix4x4.CreateTranslation(0.5f, 0, 0),
            origin, size, out var moved));
        Assert.Equal(new Vector2(700, 200), moved);
        Assert.False(ActorMarkerProjection.TryProject(world, Matrix4x4.CreateTranslation(2, 0, 0),
            origin, size, out var hidden));
        Assert.Equal(default, hidden);
        var behind = Matrix4x4.Identity;
        behind.M44 = -1;
        Assert.False(ActorMarkerProjection.TryProject(world, behind, origin, size, out _));
    }

    [Fact]
    public void RecordedCutsceneHeadIsOutsideActualCameraButInsideStaleControlCamera()
    {
        var head = new Vector3(55.5594f, 5.9168959f, 61.495594f);
        var field = new Matrix4x4(-0.8557942f, 0.63845867f, 0, 0.47425792f,
            1.7343345e-8f, 2.1285887f, 0, -0.484183f,
            0.551985f, 0.9898623f, 0, 0.7352865f,
            13.146376f, -114.53785f, 0.1f, -52.567123f);
        var scene = new Matrix4x4(0.40320936f, 0, 0, 0.91827756f,
            0, 2.432765f, 0, 0,
            0.9351434f, 0, 0, -0.39593726f,
            -84.00803f, -12.469494f, 0, -24.105293f);
        Assert.True(ActorMarkerProjection.TryProject(head, field, Vector2.Zero, new Vector2(1920, 1080), out _));
        Assert.False(ActorMarkerProjection.TryProject(head, scene, Vector2.Zero, new Vector2(1920, 1080), out _));
    }

    [Fact]
    public void AngledCameraUsesScreenUpperEdgeInsteadOfWorldUpperCenter()
    {
        var view = Matrix4x4.Identity;
        view.M32 = 0.5f;
        var min = new Vector3(-0.1f, 0, -0.2f);
        var max = new Vector3(0.1f, 0.5f, 0.2f);
        var size = new Vector2(1000, 1000);
        Assert.True(ActorMarkerProjection.TryProject(new Vector3(0, max.Y, 0), view,
            Vector2.Zero, size, out var center));
        Assert.True(ActorMarkerProjection.TryProjectModelTop(min, max, view,
            Vector2.Zero, size, out var upper));
        Assert.Equal(250, center.Y);
        Assert.Equal(200, upper.Y, 3);
        Assert.Equal(center.X, upper.X);
        max.Y = 0.75f;
        Assert.True(ActorMarkerProjection.TryProjectModelTop(min, max, view,
            Vector2.Zero, size, out var taller));
        Assert.Equal(75, taller.Y, 3);
    }

    [Fact]
    public void ModelUpperEdgeOutsideScreenDoesNotLeaveMarkerOverVisibleLowerHead()
    {
        var view = Matrix4x4.Identity;
        view.M32 = 1;
        var min = new Vector3(-0.1f, 0, -0.3f);
        var max = new Vector3(0.1f, 0.8f, 0.3f);
        Assert.True(ActorMarkerProjection.TryProject(new Vector3(0, max.Y, 0), view,
            Vector2.Zero, new Vector2(1000, 1000), out _));
        Assert.False(ActorMarkerProjection.TryProjectModelTop(min, max, view,
            Vector2.Zero, new Vector2(1000, 1000), out var hidden));
        Assert.Equal(default, hidden);
    }

    [Fact]
    public void RecordedPlayerModelUpperEdgeIsAboveFormerAnchor()
    {
        var position = new Vector3(53.477966f, 3.9997265f, 62.700493f);
        var min = position + new Vector3(-0.7133362f, -0.19348997f, -0.23477566f);
        var max = position + new Vector3(0.7670257f, 2.1283894f, 0.2226269f);
        var view = new Matrix4x4(-0.986493f, -0.02215441f, 0, -0.24806328f,
            0, 2.4311275f, 0, -0.036686398f,
            -0.2527896f, 0.08645597f, 0, 0.96804893f,
            68.60567f, -19.152132f, 0, -41.59253f);
        var center = (min + max) * 0.5f;
        Assert.True(ActorMarkerProjection.TryProject(new Vector3(center.X, max.Y, center.Z), view,
            Vector2.Zero, new Vector2(3440, 1440), out var former));
        Assert.True(ActorMarkerProjection.TryProjectModelTop(min, max, view,
            Vector2.Zero, new Vector2(3440, 1440), out var upper));
        Assert.InRange(former.Y - upper.Y, 4.4f, 4.6f);
    }

    private static ActorEntry Entry(params ActorSnapshot[] representations)
        => new(representations[0].LogicalKey, "Actor", ObjectKind.EventNpc, false, representations);

    private static ActorSnapshot Snapshot(ushort index, bool isGPose)
        => new(new LogicalActorKey(1, 100, 10, 1025176, ObjectKind.EventNpc, 144),
            new ActorRepresentationKey(index, (ulong)(100 + index), (uint)(10 + index), isGPose, 144),
            "Actor", ObjectKind.EventNpc, 1025176, 0, 1, 0, 1, 0, 1, false);
}
