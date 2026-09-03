using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using NativeCharacterBase = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase;
using NativeHuman = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Human;
using Xunit;

namespace ActorMorpher.Tests;

public sealed unsafe class NativeDrawObjectInjectorTests
{
    [Fact]
    public void SwitchingNpcEquipmentDoesNotBecomeGameOwnedArmorOrRetainPreviousOutfit()
    {
        var character = (Character*)NativeMemory.AllocZeroed((nuint)sizeof(Character));
        try
        {
            ulong[] backing = [100, 200, 300, 400, 500, 600, 700, 800, 900, 1000];
            ulong[] alisaie = [0, 74650, 75439, 74650, 74650, 0, 0, 0, 0, 0];
            ulong[] other = [0xFFFFFFFF, 131123, 0, 131123, 655361, 0, 0, 0, 0, 0];
            ulong[] ryne = [0, 74696, 75439, 74696, 74696, 0, 0, 0, 0, 0];
            var previous = backing.ToArray();
            var characterAddress = (nint)character;
            var customizeBuffer = stackalloc byte[26];
            var equipmentBuffer = stackalloc ulong[10];
            var calls = 0;
            foreach (var selected in new[] { alisaie, other, ryne, alisaie })
            {
                for (var slot = 0; slot < 10; ++slot)
                    character->DrawData.EquipmentModelIds[slot].Value = backing[slot];
                var desired = Human(equipment: selected);
                NativeDrawObjectInjector.SubstituteBacking(character, desired);
                new Span<byte>(customizeBuffer, 26).Clear();
                for (var slot = 0; slot < 10; ++slot)
                    equipmentBuffer[slot] = character->DrawData.EquipmentModelIds[slot].Value;
                var originalEquipment = ReadEquipment((nint)equipmentBuffer);
                var result = NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(
                    desired, (nint)customizeBuffer, (nint)equipmentBuffer, (_, equipment) =>
                    {
                        ++calls;
                        var setupInput = ReadEquipment(equipment);
                        Assert.Equal(selected, setupInput);
                        var actor = (Character*)characterAddress;
                        // TEST_ONLY slot consumer: stateful listeners can use retained armor
                        // when selected input is indistinguishable from game-owned backing.
                        for (var slot = 0; slot < 10; ++slot)
                            if (setupInput[slot] == actor->DrawData.EquipmentModelIds[slot].Value)
                                setupInput[slot] = previous[slot];
                        Assert.Equal(selected, setupInput);
                        Assert.Equal(backing, actor->DrawData.EquipmentModelIds.ToArray().Select(model => model.Value));
                        previous = selected.ToArray();
                        return (nint)1234;
                    });
                Assert.Equal((nint)1234, result);
                Assert.Equal(originalEquipment, ReadEquipment((nint)equipmentBuffer));
                Assert.Equal(backing, character->DrawData.EquipmentModelIds.ToArray().Select(model => model.Value));
            }
            Assert.Equal(4, calls);
        }
        finally
        {
            NativeMemory.Free(character);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsecutiveCreateInputsDoNotBecomeGameOwnedCustomize(bool distinctSecondPayload)
    {
        var character = (Character*)NativeMemory.AllocZeroed((nuint)sizeof(Character));
        try
        {
            byte[] backing = [5, 1, 1, 100, 9, 2, 186, 0, 240, 81, 92, 174, 18, 0, 1, 81, 1, 5, 2, 131, 45, 100, 0, 100, 1, 135];
            byte[] selected = [2, 1, 4, 255, 3, 201, 201, 1, 0, 146, 0, 0, 0, 0, 0, 146, 0, 0, 0, 0, 0, 50, 1, 50, 0, 0];
            var first = Human(customize: selected, equipment: [0, 74650, 75439, 74650, 74650, 0, 0, 0, 0, 0]);
            var secondCustomize = selected.ToArray();
            if (distinctSecondPayload)
                secondCustomize[6] = 202;
            var second = Human(customize: secondCustomize, equipment: first.Equipment);
            var characterAddress = (nint)character;
            var argumentCustomize = stackalloc byte[26];
            var argumentEquipment = stackalloc ulong[10];
            var customizeAddress = (nint)argumentCustomize;
            var equipmentAddress = (nint)argumentEquipment;
            var observedBase = backing.ToArray();
            var calls = 0;
            foreach (var desired in new[] { first, second })
            {
                backing.CopyTo(character->DrawData.CustomizeData.Data);
                NativeDrawObjectInjector.SubstituteBacking(character, desired);
                // The normal Character Create caller uses separate stack input buffers.
                character->DrawData.CustomizeData.Data.CopyTo(new Span<byte>(argumentCustomize, 26));
                new Span<ulong>(argumentEquipment, 10).Fill(777);
                var originalArguments = ReadCustomize(customizeAddress);
                var result = NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(
                    desired, customizeAddress, equipmentAddress, (customize, equipment) =>
                    {
                        ++calls;
                        var input = new Span<byte>((void*)customize, 26);
                        var gameOwned = ((Character*)characterAddress)->DrawData.CustomizeData.Data;
                        // TEST_ONLY stateful observer: equal backing/input can be cached as a
                        // base change and later replaced with retained ordinary customization.
                        if (input.SequenceEqual(gameOwned))
                        {
                            if (input.SequenceEqual(observedBase))
                                backing.CopyTo(input);
                            else
                                observedBase = input.ToArray();
                        }
                        Assert.Equal(backing, gameOwned.ToArray());
                        Assert.Equal(desired.Customize.ToArray(), input.ToArray());
                        Assert.Equal(desired.Equipment.ToArray(), ReadEquipment(equipment));
                        Assert.Equal(customizeAddress, customize);
                        Assert.Equal(equipmentAddress, equipment);
                        return (nint)1234;
                    });
                Assert.Equal((nint)1234, result);
                Assert.Equal(originalArguments, ReadCustomize(customizeAddress));
                Assert.All(ReadEquipment(equipmentAddress), value => Assert.Equal(777UL, value));
                Assert.Equal(backing, character->DrawData.CustomizeData.Data.ToArray());
            }
            Assert.Equal(2, calls);
        }
        finally
        {
            NativeMemory.Free(character);
        }
    }

    [Theory]
    [InlineData("NonNull")]
    [InlineData("Null")]
    [InlineData("NotCalled")]
    public void CreateBoundaryEntryUsesObservedCallResultWithoutApplyState(string callResult)
    {
        var properties = new Dictionary<string, object?> { ["callResult"] = callResult };
        var entry = NativeDrawObjectInjector.CreateHumanObservationEntry(
            "OriginalCreateReturnedBeforeBufferRestore", "observation", properties);
        Assert.Equal(callResult, entry.Outcome);
        Assert.Equal(entry.Properties["callResult"], entry.Outcome);
        Assert.Same(properties, entry.Properties);
    }

    [Fact]
    public void NativeEquipmentInputObservationDoesNotReplaceLoadedEquipment()
    {
        NativeHuman human = default;
        var pending = stackalloc byte[10 * 32];
        new Span<byte>(pending, 10 * 32).Clear();
        var table = stackalloc nint[72];
        new Span<nint>(table, 72).Clear();
        table[71] = (nint)(delegate* unmanaged<NativeCharacterBase*, EquipmentModelId*, uint, void>)&ReadTestPendingEquipment;
        *(nint**)&human = table;
        human.ChangedEquipData = pending;
        var expected = Enumerable.Range(0, 10).Select(i => (ulong)(i * 100)).ToArray();
        for (var slot = 0; slot < 10; ++slot)
            *(ulong*)(pending + slot * 32) = expected[slot];

        var input = NativeDrawObjectInjector.CaptureNativeEquipmentInput((NativeCharacterBase*)&human);
        Assert.Equal(expected, input);
        Assert.All(human.EquipmentModels.ToArray(), model => Assert.Equal(0UL, model.Value));
        var desired = Human(equipment: expected);
        var properties = NativeDrawObjectInjector.BuildHumanDiagnosticProperties(desired,
            Observation(desired) with { Equipment = new ulong[10], EquipmentInput = input, SlotNeedsUpdateBitfield = 0x3FF });
        Assert.Equal(0UL, properties["observedBody"]);
        Assert.Equal(100UL, properties["inputBody"]);
        Assert.Equal(0UL, properties["inputHead"]);
        Assert.Equal("Mismatch", properties["payloadComparison"]);
        Assert.Equal(NativeDrawObjectInjector.Signature(expected), properties["equipmentInputSignature"]);
        Assert.True(NativeDrawObjectInjector.IsRequestedApplySuccessful(true, true, false));
    }

    // TEST_ONLY native getter stand-in: actual FF14 routing/loading is not exercised here.
    [UnmanagedCallersOnly]
    private static void ReadTestPendingEquipment(NativeCharacterBase* characterBase, EquipmentModelId* output, uint slot)
        => output->Value = *(ulong*)(((NativeHuman*)characterBase)->ChangedEquipData + slot * 32);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateArgumentObservationPreservesCallOutcomeAndBufferRestoration(bool throws)
    {
        var customize = stackalloc byte[26];
        var equipment = stackalloc ulong[10];
        new Span<byte>(customize, 26).Fill(99);
        new Span<ulong>(equipment, 10).Fill(999);
        var customizeAddress = (nint)customize;
        var equipmentAddress = (nint)equipment;
        var desired = Human(equipment: Enumerable.Range(0, 10).Select(i => (ulong)i));
        var calls = 0;
        var failure = new InvalidOperationException("native test failure");
        nint Invoke() => NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(
            desired, customizeAddress, equipmentAddress, (actualCustomize, actualEquipment) =>
            {
                ++calls;
                Assert.Equal(customizeAddress, actualCustomize);
                Assert.Equal(equipmentAddress, actualEquipment);
                var before = NativeDrawObjectInjector.CaptureCreateArguments(desired, actualCustomize, actualEquipment);
                Assert.Equal(desired.Equipment.ToArray(), Assert.IsType<ulong[]>(before["argumentEquipment"]));
                ((ulong*)actualEquipment)[1] = 4321;
                var after = NativeDrawObjectInjector.CaptureCreateArguments(desired, actualCustomize, actualEquipment);
                Assert.Equal(4321UL, Assert.IsType<ulong[]>(after["argumentEquipment"])[1]);
                if (throws)
                    throw failure;
                return (nint)1234;
            });

        if (throws)
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Invoke()));
        else
            Assert.Equal((nint)1234, Invoke());
        Assert.Equal(1, calls);
        Assert.All(ReadCustomize(customizeAddress), value => Assert.Equal((byte)99, value));
        Assert.All(ReadEquipment(equipmentAddress), value => Assert.Equal(999UL, value));
    }

    [Fact]
    public void ArmorObservationSeparatesArrivalTimingOwnersAndMissingOtherConsumers()
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var id = Guid.NewGuid();
        var representation = Field(7);
        var transaction = component.Begin(id, representation, 7, (nint)0x1000, Human());
        var value = 123UL;
        var calls = 0;
        OneShotAppearanceConsumerTransaction.ArmorForwarder forward = (_, _, _, _) => ++calls;
        Assert.True(transaction.TryBeginCreate());
        component.DispatchArmor(0, 0x1000, 7, 1, (nint)(&value), 0, forward);
        component.DispatchArmor(0, 0x2000, 8, 2, (nint)(&value), 0, forward);
        transaction.CompleteCreate(1234);
        component.DispatchArmor(0, 0x1000, 7, 4, (nint)(&value), 0, forward);
        Assert.False(component.End(transaction, id, representation, 7));
        var observed = transaction.CaptureObservation();
        Assert.Equal((ushort)0x12, observed["observedEquipmentMask"]);
        Assert.Equal(2, observed["armorCallsDuringCreate"]);
        Assert.Equal(1, observed["armorCallsOutsideCreate"]);
        Assert.Equal(1, observed["armorCallsWithOtherOwner"]);
        Assert.Equal(false, observed["mainhandObserved"]);
        Assert.True(NativeDrawObjectInjector.IsRequestedApplySuccessful(true, true, false));
        component.DispatchArmor(0, 0x1000, 7, 5, (nint)(&value), 0, forward);
        Assert.Equal(4, calls);
        Assert.Equal(observed, transaction.CaptureObservation());
    }

    public static IEnumerable<object?[]> IncompleteCurrentAppearances()
    {
        yield return [null];
        yield return [Human(customize: Enumerable.Repeat((byte)0, 25))];
        yield return [Demihuman(equipment: Enumerable.Repeat(0UL, 9))];
        yield return [Monster(completeness: AppearanceCompleteness.Complete)];
        yield return [AppearanceData.Create(
            0,
            ModelCategory.Other,
            0,
            AppearanceCompleteness.Unsupported,
            [],
            [],
            0)];
    }

    public static IEnumerable<object[]> CompletePublishedAppearances()
    {
        yield return [Human(modelCharaId: 101)];
        yield return [Demihuman(modelCharaId: 102)];
        yield return [Monster(modelCharaId: 0)];
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void RequestedCreateOutcomeRequiresObservedNonNullNativeResult(
        bool createCallObserved,
        bool createCallSucceeded,
        bool expected)
        => Assert.Equal(
            expected,
            NativeDrawObjectInjector.IsRequestedCreateSuccessful(createCallObserved, createCallSucceeded));

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public void ConsumerCompletionObservationDoesNotChangeRequestedApplyOutcome(
        bool createCallObserved,
        bool createCallSucceeded,
        bool consumerCompletionObserved,
        bool expected)
        => Assert.Equal(
            expected,
            NativeDrawObjectInjector.IsRequestedApplySuccessful(
                createCallObserved,
                createCallSucceeded,
                consumerCompletionObserved));

    [Fact]
    public void CompleteHumanTransactionSubstitutesEveryRequiredConsumer()
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(7);
        var targetAddress = (nint)0x1000;
        var appearance = DiagnosticHuman();
        var transaction = component.Begin(
            operationId,
            representation,
            representation.ObjectIndex,
            targetAddress,
            appearance);

        Assert.True(transaction.TryBeginCreate());
        for (uint slot = 0; slot < 10; ++slot)
        {
            var value = 0xFFFFUL;
            Assert.True(transaction.TrySubstituteArmor(
                targetAddress,
                representation.ObjectIndex,
                slot,
                ref value));
            Assert.Equal(appearance.Equipment[(int)slot], value);
        }
        transaction.CompleteCreate((nint)0x2000);

        var mainhand = 0xFFFFUL;
        var offhand = 0xFFFFUL;
        var facewear = ushort.MaxValue;
        var hidden = (byte)1;
        var visor = (byte)0;
        Assert.True(transaction.TrySubstituteWeapon(targetAddress, representation.ObjectIndex, 0, ref mainhand));
        Assert.True(transaction.TrySubstituteWeapon(targetAddress, representation.ObjectIndex, 1, ref offhand));
        Assert.True(transaction.TrySubstituteFacewear(targetAddress, representation.ObjectIndex, 0, ref facewear));
        Assert.True(transaction.TrySubstituteHat(targetAddress, representation.ObjectIndex, 0, ref hidden));
        Assert.True(transaction.TrySubstituteVisor(targetAddress, representation.ObjectIndex, ref visor));

        Assert.Equal(appearance.Mainhand, mainhand);
        Assert.Equal(appearance.Offhand, offhand);
        Assert.Equal(appearance.FacewearModelId, facewear);
        Assert.Equal(appearance.HatVisible is true ? 0 : 1, hidden);
        Assert.Equal(appearance.VisorToggled is true ? 1 : 0, visor);
        Assert.True(component.End(transaction, operationId, representation, representation.ObjectIndex));
    }

    public static IEnumerable<object[]> DemihumanComponentSets()
    {
        yield return [Enumerable.Range(1, 26).Select(static value => (byte)value), Array.Empty<ulong>()];
        yield return [Array.Empty<byte>(), Enumerable.Range(1, 10).Select(static value => (ulong)value)];
        yield return
        [
            Enumerable.Range(31, 26).Select(static value => (byte)value),
            Enumerable.Range(11, 10).Select(static value => (ulong)value),
        ];
    }

    [Theory]
    [MemberData(nameof(DemihumanComponentSets))]
    public void DemihumanRequiresOnlyItsIndependentlyPresentComponents(
        IEnumerable<byte> customize,
        IEnumerable<ulong> equipment)
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(8);
        var appearance = AppearanceData.Create(
            44,
            ModelCategory.Demihuman,
            1,
            AppearanceCompleteness.Complete,
            customize,
            equipment,
            0.9f);
        var transaction = component.Begin(operationId, representation, 8, (nint)0x3000, appearance);

        Assert.True(transaction.TryBeginCreate());
        if (!appearance.Equipment.IsDefaultOrEmpty)
        {
            for (uint slot = 0; slot < 10; ++slot)
            {
                var value = 0UL;
                Assert.True(transaction.TrySubstituteArmor(
                    (nint)0x3000,
                    representation.ObjectIndex,
                    slot,
                    ref value));
                Assert.Equal(appearance.Equipment[(int)slot], value);
            }
        }
        transaction.CompleteCreate((nint)0x4000);

        Assert.True(component.End(transaction, operationId, representation, 8));
    }

    [Fact]
    public void ModelOnlyMonsterRequiresOnlyItsCreate()
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(9);
        var transaction = component.Begin(operationId, representation, 9, (nint)0x5000, Monster(55));

        Assert.True(transaction.TryBeginCreate());
        transaction.CompleteCreate((nint)0x6000);

        Assert.True(component.End(transaction, operationId, representation, 9));
    }

    public static IEnumerable<object[]> HumanRequiredConsumers()
    {
        for (var slot = 0; slot < 10; ++slot)
            yield return [$"Armor{slot}"];
        yield return ["Mainhand"];
        yield return ["Offhand"];
        yield return ["Facewear"];
        yield return ["Hat"];
        yield return ["Visor"];
    }

    [Theory]
    [MemberData(nameof(HumanRequiredConsumers))]
    public void MissingHumanConsumerIsObservedAsIncomplete(string missing)
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(10);
        var appearance = DiagnosticHuman();
        var transaction = component.Begin(operationId, representation, 10, (nint)0x7000, appearance);

        CompleteHumanConsumers(transaction, appearance, (nint)0x7000, 10, (nint)0x8000, missing);

        Assert.False(component.End(transaction, operationId, representation, 10));
    }

    [Theory]
    [InlineData("Operation")]
    [InlineData("Representation")]
    [InlineData("ObjectIndex")]
    public void EveryTransactionIdentityElementMustMatch(string mismatched)
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(11);
        var appearance = Monster(66);
        var transaction = component.Begin(operationId, representation, 11, (nint)0x9000, appearance);
        Assert.True(transaction.TryBeginCreate());
        transaction.CompleteCreate((nint)0xA000);

        var actualOperationId = mismatched == "Operation" ? Guid.NewGuid() : operationId;
        var actualRepresentation = mismatched == "Representation" ? Field(12) : representation;
        var actualObjectIndex = mismatched == "ObjectIndex" ? (ushort)12 : (ushort)11;

        Assert.False(component.End(transaction, actualOperationId, actualRepresentation, actualObjectIndex));
        var next = component.Begin(Guid.NewGuid(), Field(13), 13, (nint)0xB000, Monster(67));
        component.Abort(next);
    }

    [Fact]
    public void NonMatchingOwnerPassesThroughAndCannotComplete()
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(14);
        var appearance = DiagnosticHuman();
        var transaction = component.Begin(operationId, representation, 14, (nint)0xC000, appearance);
        CompleteHumanConsumers(transaction, appearance, (nint)0xC000, 14, (nint)0xD000, "Mainhand");
        var original = 0xABCDUL;

        Assert.False(transaction.TrySubstituteWeapon((nint)0xC001, 14, 0, ref original));
        Assert.Equal(0xABCDUL, original);
        Assert.False(component.End(transaction, operationId, representation, 14));
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(42U)]
    public void HatConsumerAcceptsAnyGameSuppliedId(uint id)
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(16);
        var transaction = component.Begin(
            operationId,
            representation,
            representation.ObjectIndex,
            (nint)0x11000,
            DiagnosticHuman());
        var hidden = byte.MaxValue;

        Assert.True(transaction.TrySubstituteHat(
            (nint)0x11000,
            representation.ObjectIndex,
            id,
            ref hidden));
        Assert.Equal(0, hidden);
        component.Abort(transaction);
    }

    [Fact]
    public void NativeConsumerDispatchesForwardEachOriginalExactlyOnceWithSubstitutedArguments()
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(17);
        var appearance = DiagnosticHuman();
        var targetAddress = (nint)0x12000;
        var drawData = (nint)0x13000;
        var transaction = component.Begin(
            operationId,
            representation,
            representation.ObjectIndex,
            targetAddress,
            appearance);

        var armor = ulong.MaxValue;
        var armorCalls = 0;
        ulong forwardedArmor = 0;
        component.DispatchArmor(
            drawData,
            targetAddress,
            representation.ObjectIndex,
            0,
            (nint)(&armor),
            1,
            (_, _, forwarded, _) =>
            {
                armorCalls++;
                forwardedArmor = *(ulong*)forwarded;
            });

        var weaponCalls = 0;
        ulong forwardedWeapon = 0;
        component.DispatchWeapon(
            drawData,
            targetAddress,
            representation.ObjectIndex,
            0,
            ulong.MaxValue,
            1,
            2,
            3,
            4,
            5,
            (_, _, forwarded, _, _, _, _, _) =>
            {
                weaponCalls++;
                forwardedWeapon = forwarded;
            });

        var facewearCalls = 0;
        ushort forwardedFacewear = 0;
        component.DispatchFacewear(
            drawData,
            targetAddress,
            representation.ObjectIndex,
            0,
            ushort.MaxValue,
            (_, _, forwarded) =>
        {
            facewearCalls++;
            forwardedFacewear = forwarded;
        });

        var hatCalls = 0;
        byte forwardedHidden = byte.MaxValue;
        component.DispatchHat(
            drawData,
            targetAddress,
            representation.ObjectIndex,
            42,
            byte.MaxValue,
            (_, _, forwarded) =>
        {
            hatCalls++;
            forwardedHidden = forwarded;
        });

        var visorCalls = 0;
        byte forwardedVisor = byte.MaxValue;
        component.DispatchVisor(
            drawData,
            targetAddress,
            representation.ObjectIndex,
            byte.MaxValue,
            (_, forwarded) =>
        {
            visorCalls++;
            forwardedVisor = forwarded;
        });

        Assert.Equal(1, armorCalls);
        Assert.Equal(appearance.Equipment[0], forwardedArmor);
        Assert.Equal(1, weaponCalls);
        Assert.Equal(appearance.Mainhand, forwardedWeapon);
        Assert.Equal(1, facewearCalls);
        Assert.Equal(appearance.FacewearModelId, forwardedFacewear);
        Assert.Equal(1, hatCalls);
        Assert.Equal(0, forwardedHidden);
        Assert.Equal(1, visorCalls);
        Assert.Equal(1, forwardedVisor);
        component.Abort(transaction);
    }

    [Fact]
    public void NonMatchingNativeConsumerDispatchesForwardEachOriginalArgumentExactlyOnce()
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var operationId = Guid.NewGuid();
        var representation = Field(18);
        var targetAddress = (nint)0x14000;
        var wrongAddress = (nint)0x14001;
        var drawData = (nint)0x15000;
        var transaction = component.Begin(
            operationId,
            representation,
            representation.ObjectIndex,
            targetAddress,
            DiagnosticHuman());
        Assert.True(transaction.TryBeginCreate());
        transaction.CompleteCreate((nint)0x16000);

        var armor = 101UL;
        var armorAddress = (nint)(&armor);
        var armorCalls = 0;
        component.DispatchArmor(
            drawData,
            wrongAddress,
            representation.ObjectIndex,
            3,
            armorAddress,
            7,
            (forwardedDrawData, slot, data, force) =>
            {
                armorCalls++;
                Assert.Equal(drawData, forwardedDrawData);
                Assert.Equal(3U, slot);
                Assert.Equal(armorAddress, data);
                Assert.Equal(7, force);
                Assert.Equal(101UL, *(ulong*)data);
            });

        var weaponCalls = 0;
        component.DispatchWeapon(
            drawData,
            targetAddress,
            checked((ushort)(representation.ObjectIndex + 1)),
            0,
            102,
            1,
            2,
            3,
            4,
            5,
            (forwardedDrawData, slot, weapon, first, second, third, fourth, fifth) =>
            {
                weaponCalls++;
                Assert.Equal(drawData, forwardedDrawData);
                Assert.Equal(0U, slot);
                Assert.Equal(102UL, weapon);
                Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, new[] { first, second, third, fourth, fifth });
            });

        var facewearCalls = 0;
        component.DispatchFacewear(
            drawData,
            wrongAddress,
            representation.ObjectIndex,
            0,
            103,
            (forwardedDrawData, slot, id) =>
            {
                facewearCalls++;
                Assert.Equal(drawData, forwardedDrawData);
                Assert.Equal(0, slot);
                Assert.Equal(103, id);
            });

        var hatCalls = 0;
        component.DispatchHat(
            drawData,
            wrongAddress,
            representation.ObjectIndex,
            42,
            104,
            (forwardedDrawData, id, hidden) =>
            {
                hatCalls++;
                Assert.Equal(drawData, forwardedDrawData);
                Assert.Equal(42U, id);
                Assert.Equal(104, hidden);
            });

        var visorCalls = 0;
        component.DispatchVisor(
            drawData,
            wrongAddress,
            representation.ObjectIndex,
            105,
            (forwardedDrawData, toggled) =>
            {
                visorCalls++;
                Assert.Equal(drawData, forwardedDrawData);
                Assert.Equal(105, toggled);
            });

        Assert.Equal(1, armorCalls);
        Assert.Equal(1, weaponCalls);
        Assert.Equal(1, facewearCalls);
        Assert.Equal(1, hatCalls);
        Assert.Equal(1, visorCalls);
        Assert.False(component.End(transaction, operationId, representation, representation.ObjectIndex));
    }

    [Theory]
    [InlineData("Success")]
    [InlineData("Failure")]
    [InlineData("Exception")]
    public void LaterTransactionStartsFreshAfterEveryPriorTermination(string firstTermination)
    {
        using var component = new OneShotAppearanceConsumerTransaction();
        var representation = Field(15);
        var first = DiagnosticHuman();
        var firstOperation = Guid.NewGuid();
        var firstTransaction = component.Begin(firstOperation, representation, 15, (nint)0xE000, first);
        CompleteHumanConsumers(
            firstTransaction,
            first,
            (nint)0xE000,
            15,
            (nint)0xF000,
            firstTermination == "Failure" ? "Visor" : null);
        if (firstTermination == "Exception")
            component.Abort(firstTransaction);
        else
            Assert.Equal(firstTermination == "Success", component.End(firstTransaction, firstOperation, representation, 15));

        var second = DiagnosticHuman() with
        {
            ModelCharaId = 777,
            Equipment = Enumerable.Range(101, 10).Select(static value => (ulong)value).ToImmutableArray(),
            Mainhand = 801,
            Offhand = 802,
            FacewearModelId = 803,
            HatVisible = false,
            VisorToggled = false,
        };
        var secondOperation = Guid.NewGuid();
        var secondTransaction = component.Begin(secondOperation, representation, 15, (nint)0xE000, second);
        CompleteHumanConsumers(secondTransaction, second, (nint)0xE000, 15, (nint)0xF100, null);

        Assert.True(component.End(secondTransaction, secondOperation, representation, 15));
    }

    private static void CompleteHumanConsumers(
        OneShotAppearanceConsumerTransaction.Transaction transaction,
        AppearanceData appearance,
        nint targetAddress,
        ushort objectIndex,
        nint drawObject,
        string? missing)
    {
        Assert.True(transaction.TryBeginCreate());
        for (uint slot = 0; slot < 10; ++slot)
        {
            if (missing == $"Armor{slot}")
                continue;
            var value = ulong.MaxValue;
            Assert.True(transaction.TrySubstituteArmor(targetAddress, objectIndex, slot, ref value));
            Assert.Equal(appearance.Equipment[(int)slot], value);
        }
        transaction.CompleteCreate(drawObject);

        var mainhand = ulong.MaxValue;
        var offhand = ulong.MaxValue;
        var facewear = ushort.MaxValue;
        var hidden = byte.MaxValue;
        var visor = byte.MaxValue;
        if (missing != "Mainhand")
            Assert.True(transaction.TrySubstituteWeapon(targetAddress, objectIndex, 0, ref mainhand));
        if (missing != "Offhand")
            Assert.True(transaction.TrySubstituteWeapon(targetAddress, objectIndex, 1, ref offhand));
        if (missing != "Facewear")
            Assert.True(transaction.TrySubstituteFacewear(targetAddress, objectIndex, 0, ref facewear));
        if (missing != "Hat")
            Assert.True(transaction.TrySubstituteHat(targetAddress, objectIndex, 0, ref hidden));
        if (missing != "Visor")
            Assert.True(transaction.TrySubstituteVisor(targetAddress, objectIndex, ref visor));
    }

    [Fact]
    public void ConsecutivePayloadsUseTheSameOriginalBuffersAndRestoreBetweenCalls()
    {
        var originalCustomize = Enumerable.Range(101, 26).Select(static value => (byte)value).ToArray();
        var originalEquipment = Enumerable.Range(101, 10).Select(static value => (ulong)value).ToArray();
        var b1Customize = Enumerable.Range(1, 26).Select(static value => (byte)value).ToArray();
        var b1Equipment = new ulong[] { 1, 2, 3, 0, 5, 6, 7, 8, 9, 10 };
        var b2Customize = Enumerable.Range(31, 26).Select(static value => (byte)value).ToArray();
        var b2Equipment = new ulong[] { 11, 12, 0, 14, 15, 16, 17, 18, 19, 20 };
        byte* customize = stackalloc byte[26];
        originalCustomize.AsSpan().CopyTo(new Span<byte>(customize, 26));
        ulong* equipment = stackalloc ulong[10];
        originalEquipment.AsSpan().CopyTo(new Span<ulong>(equipment, 10));
        var customizeAddress = (nint)customize;
        var equipmentAddress = (nint)equipment;
        var callCount = 0;

        var first = NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(
            Human(customize: b1Customize, equipment: b1Equipment),
            customizeAddress,
            equipmentAddress,
            (actualCustomizeAddress, actualEquipmentAddress) =>
            {
                ++callCount;
                Assert.Equal(customizeAddress, actualCustomizeAddress);
                Assert.Equal(equipmentAddress, actualEquipmentAddress);
                Assert.Equal(b1Customize, ReadCustomize(actualCustomizeAddress));
                Assert.Equal(b1Equipment, ReadEquipment(actualEquipmentAddress));
                return 0x1001;
            });

        Assert.Equal((nint)0x1001, first);
        Assert.Equal(originalCustomize, ReadCustomize(customizeAddress));
        Assert.Equal(originalEquipment, ReadEquipment(equipmentAddress));

        var second = NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(
            Human(customize: b2Customize, equipment: b2Equipment),
            customizeAddress,
            equipmentAddress,
            (actualCustomizeAddress, actualEquipmentAddress) =>
            {
                ++callCount;
                Assert.Equal(customizeAddress, actualCustomizeAddress);
                Assert.Equal(equipmentAddress, actualEquipmentAddress);
                Assert.Equal(b2Customize, ReadCustomize(actualCustomizeAddress));
                Assert.Equal(b2Equipment, ReadEquipment(actualEquipmentAddress));
                return 0x1002;
            });

        Assert.Equal((nint)0x1002, second);
        Assert.Equal(2, callCount);
        Assert.Equal(originalCustomize, ReadCustomize(customizeAddress));
        Assert.Equal(originalEquipment, ReadEquipment(equipmentAddress));
    }

    [Fact]
    public void CategorySpecificPresentAndAbsentComponentsAreTransactional()
    {
        AssertTemporaryBufferTransaction(Human(
            customize: Enumerable.Range(1, 26).Select(static value => (byte)value),
            equipment: new ulong[] { 1, 0, 3, 4, 5, 6, 7, 8, 9, 10 }));
        AssertTemporaryBufferTransaction(AppearanceData.Create(
            1,
            ModelCategory.Demihuman,
            0,
            AppearanceCompleteness.Complete,
            Enumerable.Range(31, 26).Select(static value => (byte)value),
            [],
            0.84f));
        AssertTemporaryBufferTransaction(Demihuman(
            modelCharaId: 2,
            equipment: new ulong[] { 11, 12, 13, 14, 0, 16, 17, 18, 19, 20 }));
        AssertTemporaryBufferTransaction(Monster(modelCharaId: 3));
    }

    [Fact]
    public void NativeExceptionPropagatesAfterOriginalBuffersAreRestored()
    {
        var originalCustomize = Enumerable.Range(101, 26).Select(static value => (byte)value).ToArray();
        var originalEquipment = Enumerable.Range(101, 10).Select(static value => (ulong)value).ToArray();
        byte* customize = stackalloc byte[26];
        originalCustomize.AsSpan().CopyTo(new Span<byte>(customize, 26));
        ulong* equipment = stackalloc ulong[10];
        originalEquipment.AsSpan().CopyTo(new Span<ulong>(equipment, 10));
        var customizeAddress = (nint)customize;
        var equipmentAddress = (nint)equipment;
        var exceptionObserved = false;

        try
        {
            NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(
                Human(
                    customize: Enumerable.Range(1, 26).Select(static value => (byte)value),
                    equipment: Enumerable.Range(1, 10).Select(static value => (ulong)value)),
                customizeAddress,
                equipmentAddress,
                static (_, _) => throw new InvalidOperationException("native failure"));
        }
        catch (InvalidOperationException exception) when (exception.Message == "native failure")
        {
            exceptionObserved = true;
        }

        Assert.True(exceptionObserved);
        Assert.Equal(originalCustomize, ReadCustomize(customizeAddress));
        Assert.Equal(originalEquipment, ReadEquipment(equipmentAddress));
    }

    [Fact]
    public void PostRestoreSelectionUsesTheReacquiredCurrentHumanWhenCharacterBaseChanged()
    {
        var created = (nint)0x1000;
        var current = (nint)0x2000;

        var selected = NativeDrawObjectInjector.SelectPostRestoreCharacterBaseAddress(
            created,
            current,
            true,
            out var continuity,
            out var unavailableReason);

        Assert.Equal(current, selected);
        Assert.Equal("Changed", continuity);
        Assert.Null(unavailableReason);
        Assert.NotEqual(created, selected);
    }

    [Theory]
    [InlineData(0, 0x2000, true, "CreateReturnedNull")]
    [InlineData(0x1000, 0, true, "CurrentCharacterBaseNull")]
    [InlineData(0x1000, 0x2000, false, "CurrentCharacterBaseNonHuman")]
    public void PostRestoreSelectionNeverReturnsAnUnavailableOrNonHumanAddress(
        long created,
        long current,
        bool currentIsHuman,
        string expectedReason)
    {
        var selected = NativeDrawObjectInjector.SelectPostRestoreCharacterBaseAddress(
            (nint)created,
            (nint)current,
            currentIsHuman,
            out _,
            out var unavailableReason);

        Assert.Equal(nint.Zero, selected);
        Assert.Equal(expectedReason, unavailableReason);
    }

    [Fact]
    public void CompleteComparisonDoesNotTreatBackingOrEffectiveHatAsObserved()
    {
        var desired = DiagnosticHuman();
        var observation = Observation(desired) with
        {
            HatVisibleBacking = desired.HatVisible,
            HatVisibleEffective = desired.HatVisible,
            HatVisibleObserved = null,
            CharacterBaseContinuity = "Changed",
        };

        var properties = NativeDrawObjectInjector.BuildHumanDiagnosticProperties(desired, observation);

        Assert.Equal("NonNull", properties["callResult"]);
        Assert.Equal("Unavailable", properties["payloadComparison"]);
        Assert.Equal("Changed", properties["characterBaseContinuity"]);
        Assert.Equal(desired.HatVisible, properties["hatVisibleBacking"]);
        Assert.Equal(desired.HatVisible, properties["hatVisibleEffective"]);
        Assert.Null(properties["hatVisibleObserved"]);
        Assert.Contains("HatVisible", Assert.IsAssignableFrom<IEnumerable<string>>(properties["unavailableFields"]));
    }

    [Fact]
    public void KnownHumanMismatchTakesPrecedenceOverUnavailableObservedHat()
    {
        var desired = DiagnosticHuman();
        var changedCustomize = desired.Customize.ToArray();
        changedCustomize[2] = 1;
        var observation = Observation(desired) with
        {
            Customize = changedCustomize,
            HatVisibleObserved = null,
        };

        var properties = NativeDrawObjectInjector.BuildHumanDiagnosticProperties(desired, observation);
        var mismatches = Assert.IsAssignableFrom<IEnumerable<string>>(properties["mismatchedFields"]);

        Assert.Equal("Mismatch", properties["payloadComparison"]);
        Assert.Contains("CustomizeSignature", mismatches);
        Assert.Contains("BodyType", mismatches);
        Assert.Contains("HatVisible", Assert.IsAssignableFrom<IEnumerable<string>>(properties["unavailableFields"]));
        Assert.True(NativeDrawObjectInjector.IsRequestedCreateSuccessful(true, true));
    }

    private static LogicalActorKey Key(uint territory)
        => new(0, 100, 200, 300, ObjectKind.Pc, territory);

    private static void AssertTemporaryBufferTransaction(AppearanceData appearance)
    {
        var originalCustomize = Enumerable.Range(101, 26).Select(static value => (byte)value).ToArray();
        var originalEquipment = Enumerable.Range(101, 10).Select(static value => (ulong)value).ToArray();
        var expectedCustomize = appearance.Customize.IsDefaultOrEmpty
            ? originalCustomize
            : appearance.Customize.ToArray();
        var expectedEquipment = appearance.Equipment.IsDefaultOrEmpty
            ? originalEquipment
            : appearance.Equipment.ToArray();
        byte* customize = stackalloc byte[26];
        originalCustomize.AsSpan().CopyTo(new Span<byte>(customize, 26));
        ulong* equipment = stackalloc ulong[10];
        originalEquipment.AsSpan().CopyTo(new Span<ulong>(equipment, 10));
        var customizeAddress = (nint)customize;
        var equipmentAddress = (nint)equipment;

        NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(
            appearance,
            customizeAddress,
            equipmentAddress,
            (actualCustomizeAddress, actualEquipmentAddress) =>
            {
                Assert.Equal(customizeAddress, actualCustomizeAddress);
                Assert.Equal(equipmentAddress, actualEquipmentAddress);
                Assert.Equal(expectedCustomize, ReadCustomize(actualCustomizeAddress));
                Assert.Equal(expectedEquipment, ReadEquipment(actualEquipmentAddress));
                return 0x2001;
            });

        Assert.Equal(originalCustomize, ReadCustomize(customizeAddress));
        Assert.Equal(originalEquipment, ReadEquipment(equipmentAddress));
    }

    private static byte[] ReadCustomize(nint customizeAddress)
        => new Span<byte>((void*)customizeAddress, 26).ToArray();

    private static ulong[] ReadEquipment(nint equipmentAddress)
        => new Span<ulong>((void*)equipmentAddress, 10).ToArray();

    private static ActorRepresentationKey Field(uint value)
        => new((ushort)value, value, value, false, 10);

    private static ActorRepresentationKey Cutscene(uint value)
        => new((ushort)value, value, value, false, 10);

    private static ActorRepresentationKey GPose(uint value)
        => new((ushort)value, value, value, true, 10);

    private static AppearanceData Human(
        uint modelCharaId = 0,
        IEnumerable<byte>? customize = null,
        IEnumerable<ulong>? equipment = null,
        float? modelScale = 0.84f)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Human,
            0,
            AppearanceCompleteness.Complete,
            customize ?? Enumerable.Repeat((byte)0, 26),
            equipment ?? Enumerable.Repeat(0UL, 10),
            modelScale,
            0,
            0,
            false,
            0,
            false);

    private static AppearanceData Demihuman(
        uint modelCharaId = 0,
        IEnumerable<ulong>? equipment = null,
        float? modelScale = 0.84f)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Demihuman,
            0,
            AppearanceCompleteness.Complete,
            [],
            equipment ?? Enumerable.Repeat(0UL, 10),
            modelScale);

    private static AppearanceData Monster(
        uint modelCharaId = 0,
        AppearanceCompleteness completeness = AppearanceCompleteness.ModelOnly,
        float? modelScale = 0.84f)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Monster,
            0,
            completeness,
            [],
            [],
            modelScale);

    private static AppearanceData DiagnosticHuman()
        => AppearanceData.Create(
            604,
            ModelCategory.Human,
            1046813,
            AppearanceCompleteness.Complete,
            Enumerable.Range(1, 26).Select(static value => (byte)value),
            Enumerable.Range(1, 10).Select(static value => (ulong)value),
            0.97f,
            101,
            102,
            true,
            27,
            true);

    private static NativeDrawObjectInjector.HumanDiagnosticObservation Observation(AppearanceData desired)
        => new(
            desired.ModelCharaId,
            "CreateArgument",
            desired.Category,
            desired.Customize,
            desired.Equipment,
            desired.ModelScale,
            desired.Mainhand,
            desired.Offhand,
            desired.FacewearModelId,
            desired.HatVisible,
            null,
            desired.HatVisible,
            desired.VisorToggled,
            "Same",
            "Available",
            null,
            "NonNull");
}
