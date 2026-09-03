# Implementation Notes

`APPEARANCE_APPLICATION_SPEC.md` is normative for Model Search Apply, local-player representation transfer, and Model Search Restore. This file records implementation structure only.

## Safety status

This revision connects the existing actor, state, GPose, and redraw architecture to current FFXIVClientStructs appearance and outfit operations. Game-side verification remains pending.

| Area | Status |
| --- | --- |
| Penumbra/Glamourer IPC | Removed |
| Actor list and filters | Implemented |
| Logical/representation identity | Implemented |
| Bulk Outfit `Original`/`Desired` store | Implemented and tested; unrelated to Model Search A/B/C ownership |
| Standalone redraw coordinator | Implemented and fake-tested |
| Human/Monster/Demihuman writes | Implemented; game test pending |
| GPose representation mapping | Implemented conservatively; game test pending |
| Bulk Outfit preview and filters | Implemented |
| Bulk Outfit/Unequip/Restore writes | Implemented; game test pending |
| Facewear | Implemented with `SetGlasses`; game test pending |
| Demihuman/Monster static 3D preview | Implemented with bounded managed software projection |
| Human 3D preview | Blocked by CharaView slot/texture ownership |

## Actor identity

`LogicalActorKey` contains original object index, GameObject ID, Entity ID, Base ID, ObjectKind, and Territory ID. `ActorRepresentationKey` contains the current object index and IDs plus a GPose marker. No address, pointer, name-only key, or index-only key is persisted.

The registry refreshes on Framework Update. A selected actor is re-resolved from its logical key every UI frame. Native operations resolve the current object again and compare object index, GameObject ID, and Entity ID immediately before use.

## Appearance state

Bulk Outfit keeps its own explicit override and pin state. Model Search does not keep an appearance override store and does not capture a pre-apply snapshot. It submits the selected payload once; a failed operation reports failure without automatically presenting another appearance.

Model Search registers only rows that can construct a typed payload used by the normal Apply path. Human classification comes from ModelChara `Type == 1`, including Young NPC body types, and preserves complete Customize plus explicit equipment values. Demihuman preserves each available structural component independently and is not rejected by Human completeness rules. Monster uses the referenced ModelChara member and may be model-only. Source rows preserve their Scale value unchanged; ModelChara-only Monster entries have no inferred Scale.

## Redraw

`RedrawCoordinator` advances on Framework Update through Pending, model hide, and one model show. The selected payload is passed only to that single `EnableDraw` call, and the operation completes when the call returns. There is no generated-body gate, rendered-state equality gate, timeout, retry, second redraw, automatic visibility redraw, or automatic rollback. Every native access follows target-Actor identity validation and no pointer is retained between frames.

During Actor Morpher's explicit target-Actor synchronous `EnableDraw` call, `NativeDrawObjectInjector` temporarily substitutes and supplies every selected ModelChara, Customize, Equipment, Human weapon, visor, and Scale value that is present. It does not use ModelChara equality, BodyType, pointer identity, completeness, normalization, generated appearance, or readback as a reason to skip or reject the payload. Demihuman Customize and Equipment are independent components. The temporary backing is restored in `finally` as call cleanup; it is not retained as a restore snapshot. No post-create repair, rendered-state verification, retry, rollback, or fallback is performed.

After a successful local-player Apply, `LocalPlayerAppearancePersistence` owns one latest-state transition payload C, the current Representation, and transferred Representation keys. It does not retain Model Search source B or pre-Apply A. There is no continuous capture loop. Immediately before a new-Representation transfer, one passive capture refreshes C if the current generated Representation still exists; a successful Bulk Outfit operation updates only C's equipment. Representation identity includes territory so reused local-player IDs after a field move do not suppress the transfer. `NativeCutsceneActorTracker` links only a cutscene ObjectIndex from 200 through 439 whose explicit `CopyFromCharacter` source ObjectIndex is 0. A linked new Representation receives C only on its first intercepted creation; later creation calls for the same Representation run without Actor Morpher injection. Restore/logout clears the transition state.

The Ryne plus local Bulk Outfit path was visually accepted in game on 2026-08-30: Ryne's face and the latest player-selected outfit remained correct inside the cutscene and after returning to the field. Diagnostics recorded `ModelCharaId=2720`, Customize signature `46FC138471BC8419`, the then-observed private `CharacterBase.ModelScale=0.84`, and successful local-player Bulk Outfit application before entry. Later scale diagnosis showed that this private value was the wrong actor-wide Scale target, so it remains historical evidence only and is not a current scale acceptance predicate. The diagnostic file reached its configured 64 KB limit before the later cutscene events, so the final cutscene and return verdict is User visual acceptance rather than a complete diagnostic trace.

Restore is always callable. If a per-actor Bulk `Original` exists, transition carry, hook state, pin state, and local outfit carry remain untouched until that asynchronous single-actor restore reports success. Actor Morpher then clears transition carry, the hook, the pin, and local outfit carry before invoking one payload-free `DisableDraw -> EnableDraw`. A redraw failure does not restore or retain those Actor Morpher carry states. Since Apply already restored its temporary B backing in `finally`, this regenerates from game-owned backing without a saved Model Search appearance. A redraw while C is active, a redraw before Bulk `Original`, or a saved A snapshot is not a substitute.

Across territory, cutscene, and GPose representation changes, the transition owner compares the resolved RepresentationKey with its current key. The same key is never written. A new key receives latest C once through the creation path. There is no automatic retry, source-B lookup, or pre-Apply snapshot fallback.

## GPose

`IClientState.IsGPosing` is monitored on Framework Update. Entry waits for representations before mapping. Mapping does not use a fixed GPose index range or name-only matching. It tries unique GameObject ID, network Entity ID, Base ID plus ObjectKind, a strict appearance composite, then a stable composite of name, ObjectKind, race, and gender that excludes volatile ModelChara and Body Type values. Every stage requires exactly one candidate; ambiguous copies are skipped.

The local player additionally accepts the current client-defined GPose player slot at Object Table index 201, but only when the source is the local player and the candidate is a PC. Other actors continue to use identity matching. While GPose is active, resolver failure never falls back to a normal-world representation; the operation is rejected instead of redrawing a hidden field actor.

The GPose local-player slot is acquired directly from `IObjectTable[201]` rather than requiring it to survive the general registry enumeration filters. Appearance, outfit, and redraw operations subsequently resolve the same table slot directly and revalidate GameObject ID and Entity ID before every native access.

GPose Bulk Outfit operations are unavailable until mapping reaches Ready. Target preview includes only logical actors with a mapped GPose representation or the validated direct local-player representation. Normal-world actors that remain in the Object Table during GPose are excluded instead of being counted and later skipped during capture.

## Bulk Outfit

Outfit data contains exactly ten armor/accessory slots: Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, Right Ring, and Left Ring. Each slot stores Set, Variant, Stain 1, and Stain 2. Facewear, hat visibility, and visor state are separate. Weapons, job, level, customize, ModelChara ID, and weapon visibility are not part of the outfit model.

Unequip planning requires verified slot-specific Nothing data and a verified no-Facewear value. Missing one value rejects the entire plan; no shared zero block is fabricated.

Bulk writes use `LoadEquipment` for exactly ten slots, `SetGlasses` for Facewear, `HideHeadgear` for hat visibility, and `SetVisor` for visor state. The outfit type contains no weapons, class/job, customize, or ModelChara fields. Batches process one logical actor per Framework Update. A failed actor write is reported and the batch advances without an automatic appearance write or rollback.

The source outfit table resolves armor and accessories against the localized Item sheet by OutfitSlot plus the packed Set and Variant model key. It displays the representative game icon and up to three distinct item names when several items share one appearance. Unresolved nonzero models are shown as unavailable rather than unequipped.

Source Outfit, Human Model Search, and Actor Details use the same equipment table. Armor Set IDs use `e####`, accessory IDs use `a####`, and variants use plain decimal numbers. Stain 1 and Stain 2 are shown as game-color swatches from the localized Stain sheet. The sheet's packed `0xRRGGBB` value is decoded in channel order; hover text shows the localized stain name, decimal RGB channels, and hexadecimal color.

The initial Source Outfit remains empty until Refresh is explicitly pressed. Apply always uses the state currently represented by that table: an unrefreshed empty source builds the same verified per-actor empty-equipment plan as Unequip All, while a refreshed source applies the captured local-player outfit. Apply never refreshes the source implicitly.

Actor Details separates the Bulk `Original` captured immediately before the first Bulk change from Bulk `Desired`. Modified actor names are green only when a Bulk `Original` exists and the current rendered outfit differs from it; no baseline is invented for Model Search or game-only changes. A pin has a separate color and stores stable actor identity plus desired outfit in configuration. Visible matching Human actors are checked at a bounded interval and a mismatched pinned outfit is reapplied through the normal one-actor Bulk transaction. Explicit restore removes the pin so it cannot immediately overwrite the restored outfit.

The pin control follows Minion Scaler's fixed-width icon pattern: an unpinned outfit shows the pin icon, while a pinned outfit shows the red delete action used to remove that pin. Actor filtering treats Unapplied, Outfit Modified, and Pinned as exclusive states. Bulk Restore resolves its target list immediately before starting and excludes pinned actors; individual Actor restore remains an explicit unpin-and-restore operation.

Model Search and Bulk Outfit state are independent. Queueing, rejection, failure, or success of Model Search does not remove a pin or erase Bulk state. A later explicit Bulk operation changes equipment only and updates that equipment in latest C.

Successful local-player Bulk operations publish their actual per-actor Desired result. This is important for Unequip All because its Desired outfit is built from the captured actor at processing time. When a Model Search transition session exists, its latest C is the sole representation-transfer owner and receives the successful Bulk equipment. A separate Bulk persistence path must not race it or reapply equipment to the same Representation. Bulk pinning remains its own explicit feature.

Apply uses the Source Outfit state currently shown in the UI; only the explicit Refresh command captures the local player's current Human outfit. Managed failures are isolated per actor: the override-store state is restored without writing a captured outfit back to the actor, the failure is logged, and the batch advances to the next logical actor. Restore resolves the store while excluding pinned actors rather than using the current UI filter. The explicit first reapply after a territory change remains, but a failed reapply does not schedule another attempt. Pin monitoring remains the separately requested persistent feature.

## Reference audit and licenses

Glamourer state, GPose, NPC-data, and apply designs were reviewed as behavioral references. Its repository contains an Apache-2.0 license. Penumbra redraw and cutscene-parent behavior was reviewed as a behavioral reference; the checked-out top-level repositories did not contain a license file. Actor Morpher uses the corresponding current FFXIVClientStructs member-function and runtime virtual-function pointers directly. No Glamourer or Penumbra source file, class, volatile signature, or IPC implementation was copied into Actor Morpher.

Actor Morpher remains MIT licensed. Since no third-party implementation code was incorporated, no third-party source notice was added to the distributed binary.

## Model preview lifetime

`ModelPreviewController` is the sole owner of preview backend selection and release. It debounces selection for 200 ms, distinguishes entries by row/category/source/source-row, and ignores superseded generations. Backend resources are released when Model Search becomes inactive, its UI heartbeat expires, logout or territory change occurs, or the plugin is disposed. All backend lifecycle exceptions are contained and diagnosed before a future native renderer is enabled.

Human preview preparation clones 26 Customize bytes, ten equipment models, main/off-hand plus an empty third weapon slot, two empty glasses slots, and visibility/visor flags into immutable managed data. It rejects mismatched ModelChara ID, source row, payload lengths, Customize, or equipment. The current safe capability profile deliberately reports both exclusive CharaView slot ownership and native texture ownership as unavailable.

Human Model Search exposes the Customize Tribe value as a localized subcategory. Race IDs 1 through 8 map to their two sequential Tribe rows, and changing Race clears an incompatible Tribe selection. Human data validation rejects a Tribe that does not belong to its Race.

Bulk Outfit targeting applies inclusion first and exclusion second. The exclusion filter has its own actor type, race, gender, and name conditions. An actor must match every enabled exclusion condition to be removed; exclusion always wins when inclusion and exclusion both match. The resolved matching, excluded, eligible, non-Human, and unavailable counts are recorded when an operation is requested.

Monster and Demihuman preview geometry uses Lumina 7.5.0's public High LOD `Model`, `Mesh.Vertices`, and `Mesh.Indices` API. Main meshes are converted to bounded managed CPU arrays containing position, normal, UV, color, material path, and copied triangle indices. Missing normals are generated from triangle faces. Non-finite positions, malformed triangle lists, and out-of-range indices reject only their source mesh; a fully invalid file remains isolated to its model part. Bounds are calculated from decoded positions, combined across parts, and converted to a square Auto Frame camera distance. No raw graphics resource or game pointer is retained.

The software preview scene samples at most 6,000 non-degenerate triangles across all valid parts. Camera yaw, pitch, and zoom are finite and clamped. Every frame is projected into the existing ImGui canvas with deterministic material colors, flat lighting, and painter-order depth sorting. Selection change, tab suspension, territory change, logout, and plugin disposal release the managed scene through `ModelPreviewController`; no game object, CharaView slot, native texture, render target, or unmanaged GPU resource is created.
