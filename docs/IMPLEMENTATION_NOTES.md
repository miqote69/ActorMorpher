# Implementation Notes

## Safety status

This revision connects the existing actor, state, GPose, and redraw architecture to current FFXIVClientStructs appearance and outfit operations. Game-side verification remains pending.

| Area | Status |
| --- | --- |
| Penumbra/Glamourer IPC | Removed |
| Actor list and filters | Implemented |
| Logical/representation identity | Implemented |
| Base/Desired and Original/Desired stores | Implemented and tested |
| Standalone redraw coordinator | Implemented and fake-tested |
| Human/Monster/Demihuman writes | Implemented; game test pending |
| GPose representation mapping | Implemented conservatively; game test pending |
| Bulk Outfit preview and filters | Implemented |
| Bulk Outfit/Unequip/Restore writes | Implemented; game test pending |
| Facewear | Implemented with `SetGlasses`; game test pending |

## Actor identity

`LogicalActorKey` contains original object index, GameObject ID, Entity ID, Base ID, ObjectKind, and Territory ID. `ActorRepresentationKey` contains the current object index and IDs plus a GPose marker. No address, pointer, name-only key, or index-only key is persisted.

The registry refreshes on Framework Update. A selected actor is re-resolved from its logical key every UI frame. Native operations resolve the current object again and compare object index, GameObject ID, and Entity ID immediately before use.

## Appearance state

Appearance and outfit stores use first-write-wins snapshots. A later desired state increments the revision without replacing the original state. A successful restore removes the state. Failed operations reapply the per-operation snapshot and restore the previous store state.

Model search data is classified as `Complete`, `ModelOnly`, or `Unsupported`. Demihuman rows are only applicable when source customize and equipment data are complete. Monster rows use the verified ModelChara member with model-only payloads. Human classification comes from ModelChara `Type == 1`, including nonzero Young NPC model IDs.

## Redraw

`RedrawCoordinator` advances on Framework Update through disable, apply, enable, verify, and rollback stages. It has territory, logout, actor-loss, cancellation, timeout, and rollback handling. The backend uses only current FFXIVClientStructs `DisableDraw` and `EnableDraw` member functions after identity validation. It stores no native pointer.

The production memory layer captures ModelChara ID, the generated Customize array, and the generated equipment array. Writes are staged between current FFXIVClientStructs `DisableDraw` and `EnableDraw` member calls and verified afterward. Actor identity is re-resolved before every native access; no pointer is retained between frames. The new path has not yet been exercised in FF14 by this revision.

## GPose

`IClientState.IsGPosing` is monitored on Framework Update. Entry waits for representations before mapping. Mapping does not use a fixed GPose index range or name-only matching. It tries unique GameObject ID, network Entity ID, Base ID plus ObjectKind, then a strict composite match. Ambiguous copies are skipped.

## Bulk Outfit

Outfit data contains exactly ten armor/accessory slots: Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, Right Ring, and Left Ring. Each slot stores Set, Variant, Stain 1, and Stain 2. Facewear, hat visibility, and visor state are separate. Weapons, job, level, customize, ModelChara ID, and weapon visibility are not part of the outfit model.

Unequip planning requires verified slot-specific Nothing data and a verified no-Facewear value. Missing one value rejects the entire plan; no shared zero block is fabricated.

Bulk writes use `LoadEquipment` for exactly ten slots, `SetGlasses` for Facewear, `HideHeadgear` for hat visibility, and `SetVisor` for visor state. The outfit type contains no weapons, class/job, customize, or ModelChara fields. Batches process one logical actor per Framework Update and continue after an actor-local rollback.

## Reference audit and licenses

Glamourer state, GPose, NPC-data, and apply designs were reviewed as behavioral references. Its repository contains an Apache-2.0 license. Penumbra redraw behavior was reviewed as a behavioral reference; the checked-out top-level repository did not contain a license file. No Glamourer or Penumbra source file, class, volatile offset, signature, VTable index, or IPC implementation was copied into Actor Morpher.

Actor Morpher remains MIT licensed. Since no third-party implementation code was incorporated, no third-party source notice was added to the distributed binary.
