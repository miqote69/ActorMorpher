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

`RedrawCoordinator` advances on Framework Update through pre-apply, model hide, hidden reapply, model show, recreation wait, verify, and symmetric rollback stages. Normal actors use the current FFXIVClientStructs `VisibilityFlags.Model` path; GPose representations additionally use generated `DisableDraw` and `EnableDraw` member functions. Every native access follows identity validation and no pointer is retained between frames.

During Actor Morpher's synchronous `EnableDraw` call, `NativeDrawObjectInjector` hooks the current FFXIVClientStructs `CharacterBase.Create` member-function address and substitutes the operation's ModelChara ID, Customize, and ten equipment slots. The injection context is managed data scoped to that call; native pointers are stack-local and are never retained. This avoids ObjectKind mutation while allowing NPC body types and non-Human draw objects to be created with their complete source data.

The production memory layer captures ModelChara ID, the generated Customize array, and the generated equipment array. Writes are staged between current FFXIVClientStructs `DisableDraw` and `EnableDraw` member calls and verified afterward. Actor identity is re-resolved before every native access; no pointer is retained between frames. The new path has not yet been exercised in FF14 by this revision.

Human-to-Human restoration performs two complete redraws with the original BaseData. The first redraw invalidates a CharacterBase left by an adult, old, or Young NPC appearance; the second creates the visible Human from the same original Customize and Equipment payload. The override store is removed only after the second redraw succeeds.

The local player's successfully applied Desired appearance is retained separately across territory changes. The old logical key, native object, and restore Base are not retained. Once the new territory's local player is resolvable, Actor Morpher captures that actor's current appearance as a fresh Base and submits the retained Desired data through `AppearanceApplyService`. Failed starts and redraws remain pending for a bounded retry. Restore and logout clear the retained Desired state.

## GPose

`IClientState.IsGPosing` is monitored on Framework Update. Entry waits for representations before mapping. Mapping does not use a fixed GPose index range or name-only matching. It tries unique GameObject ID, network Entity ID, Base ID plus ObjectKind, then a strict composite match. Ambiguous copies are skipped.

The local player additionally accepts the current client-defined GPose player slot at Object Table index 201, but only when the source is the local player and the candidate is a PC. Other actors continue to use identity matching. While GPose is active, resolver failure never falls back to a normal-world representation; the operation is rejected instead of redrawing a hidden field actor.

The GPose local-player slot is acquired directly from `IObjectTable[201]` rather than requiring it to survive the general registry enumeration filters. Appearance, outfit, and redraw operations subsequently resolve the same table slot directly and revalidate GameObject ID and Entity ID before every native access.

## Bulk Outfit

Outfit data contains exactly ten armor/accessory slots: Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, Right Ring, and Left Ring. Each slot stores Set, Variant, Stain 1, and Stain 2. Facewear, hat visibility, and visor state are separate. Weapons, job, level, customize, ModelChara ID, and weapon visibility are not part of the outfit model.

Unequip planning requires verified slot-specific Nothing data and a verified no-Facewear value. Missing one value rejects the entire plan; no shared zero block is fabricated.

Bulk writes use `LoadEquipment` for exactly ten slots, `SetGlasses` for Facewear, `HideHeadgear` for hat visibility, and `SetVisor` for visor state. The outfit type contains no weapons, class/job, customize, or ModelChara fields. Batches process one logical actor per Framework Update and continue after an actor-local rollback.

The source outfit table resolves armor and accessories against the localized Item sheet by OutfitSlot plus the packed Set and Variant model key. It displays the representative game icon and up to three distinct item names when several items share one appearance. Unresolved nonzero models are shown as unavailable rather than unequipped.

Apply captures the local player's current Human outfit again when the batch starts, so the operation never relies on a stale preview. Managed failures are isolated per actor: the pre-operation outfit and override-store state are restored, the failure is logged, and the batch advances to the next logical actor. Restore enumerates the store rather than the current UI filter.

## Reference audit and licenses

Glamourer state, GPose, NPC-data, and apply designs were reviewed as behavioral references. Its repository contains an Apache-2.0 license. Penumbra redraw behavior was reviewed as a behavioral reference; the checked-out top-level repository did not contain a license file. No Glamourer or Penumbra source file, class, volatile offset, signature, VTable index, or IPC implementation was copied into Actor Morpher.

Actor Morpher remains MIT licensed. Since no third-party implementation code was incorporated, no third-party source notice was added to the distributed binary.

## Model preview lifetime

`ModelPreviewController` is the sole owner of preview backend selection and release. It debounces selection for 200 ms, distinguishes entries by row/category/source/source-row, and ignores superseded generations. Backend resources are released when Model Search becomes inactive, its UI heartbeat expires, logout or territory change occurs, or the plugin is disposed. All backend lifecycle exceptions are contained and diagnosed before a future native renderer is enabled.

Human preview preparation clones 26 Customize bytes, ten equipment models, main/off-hand plus an empty third weapon slot, two empty glasses slots, and visibility/visor flags into immutable managed data. It rejects mismatched ModelChara ID, source row, payload lengths, Customize, or equipment. The current safe capability profile deliberately reports both exclusive CharaView slot ownership and native texture ownership as unavailable.

Human Model Search exposes the Customize Tribe value as a localized subcategory. Race IDs 1 through 8 map to their two sequential Tribe rows, and changing Race clears an incompatible Tribe selection. Human data validation rejects a Tribe that does not belong to its Race.

Bulk Outfit targeting applies inclusion first and exclusion second. The exclusion filter has its own actor type, race, gender, and name conditions. An actor must match every enabled exclusion condition to be removed; exclusion always wins when inclusion and exclusion both match. The resolved matching, excluded, eligible, non-Human, and unavailable counts are recorded when an operation is requested.

Monster and Demihuman preview geometry uses Lumina 7.5.0's public High LOD `Model`, `Mesh.Vertices`, and `Mesh.Indices` API. Main meshes are converted to bounded managed CPU arrays containing position, normal, UV, color, material path, and copied triangle indices. Missing normals are generated from triangle faces. Non-finite positions, malformed triangle lists, and out-of-range indices reject only their source mesh; a fully invalid file remains isolated to its model part. Bounds are calculated from decoded positions, combined across parts, and converted to a square Auto Frame camera distance. No raw graphics resource or game pointer is retained.
