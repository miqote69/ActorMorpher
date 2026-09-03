# Appearance Application Specification

## Authority

This document is the sole normative specification for Model Search Apply, local-player representation transitions, Model Search Restore, and current-appearance display in the Actor list and Actor detail. `TEST_PLAN.md`, `TEST_SPECIFICATION.md`, `IMPLEMENTATION_NOTES.md`, `MANUAL_TEST_CHECKLIST.md`, `PENUMBRA_COMPATIBILITY.md`, `TESTING.md`, and `DIAGNOSTICS.md` are subordinate to it. Historical incident descriptions are evidence only and cannot redefine current behavior.

## Terms

- **A: game original（ゲームが管理する通常のプレイヤー外見）** - Actor MorpherでModel Search Applyを行う前からゲーム側が所有している、プレイヤー本来の通常外見。装備、種族・キャラクター設定などに基づいてゲームが生成・再生成する。Actor MorpherはAをModel Search用の復元スナップショットとして保存しない。
- **B: selected source（Model Searchで選択した適用元NPCの外見データ）** - Model Searchでユーザーが選択し、Applyによって対象Actorへ適用するHuman、Demihuman、またはMonsterの完全な型付きpayload。BはApply時の適用元であり、Apply後も表示状態を支配し続ける別の正本ではない。
- **C: latest player state（現在画面に表示されている最新のプレイヤー外見）** - Apply成功後、そのRepresentationで現在実際に表示されている外見。Apply直後は選択したBの完全な内容から始まり、その後にユーザー操作、ゲーム、またはBulk Outfitによって装備、帽子表示、Visorなどが変化した場合は、その最新の変化を含む。Actor一覧、Actor詳細、およびRepresentation間の引継ぎが参照する「現在の外見」はCを意味する。
- **Representation** - one concrete field, cutscene, or GPose actor instance identified by `ActorRepresentationKey`.

### Relationship between A, B, and C

The letters A, B, and C are local shorthand used only by this specification. They mean the following sequence; a letter alone must not be treated as a separate product feature or an additional stored copy.

1. Before Apply, the game owns and displays the player's ordinary appearance **A**.
2. The user selects NPC appearance data **B** in Model Search and presses Apply.
3. A successful Apply applies the complete **B** to the exact target Representation. That applied appearance becomes the initial current displayed appearance **C**.
4. Later changes to what that Representation actually displays update **C**. Therefore, C means the latest displayed state and is not permanently frozen to the original B.
5. **A** remains game-owned. Restore does not load a saved copy of A; it requests normal game regeneration of A.

In user-facing explanations, write the Japanese meaning first—for example, 「現在画面に表示されている最新の外見（C）」—and do not use only 「外見A」「外見B」「外見C」 without defining the letter in the same context.

## Current actor data and shared snapshot

The existing `ActorRegistry` / `ActorSnapshot` lane is the owner and publisher of the shared **Current User Appearance/Equipment snapshot**; no new component is created. A successful Model Search Apply promotes the complete selected-NPC payload to current C for the exact target Representation. Each later Framework update must preserve that managed C instead of replacing its NPC-owned values with the target Actor's restored backing A, while incorporating current rendered values from that same Representation. The normal publication order is:

1. `ActorRegistry.CreateSnapshot` obtains current identity, `ModelCharaId`, category, and `Customize`.
2. Existing `NativeOutfitMemory.TryCaptureRendered` data supplies current armor/accessories, Facewear, hat visibility, and Visor.
3. An existing game-read path supplies current mainhand and offhand.
4. An existing rendered-Scale read supplies current Scale.

The published category contract contains the current `ModelCharaId` and category, `Customize`, armor/accessories, mainhand, offhand, Facewear, hat visibility, Visor, and Scale. Every value belongs to that same current Actor/Representation. For a managed Model Search appearance, `ModelCharaId` and hat visibility remain the selected NPC's values when the target Actor backing is restored after creation; the other fields use the selected NPC payload at Apply success and later same-Representation rendered or explicit Actor Morpher updates. A category-specific `N/A` means only that the current category does not define that field; it must not receive a past value from another category. `N/A` is not a capture-failure result and does not prescribe new behavior when a current read cannot be obtained.

### Existing publisher completeness predicate

`ARM_AFTER_COMPLETE_C` uses only this existing publisher contract. `ModelCharaId` and category are always present values; `ModelCharaId=0` is valid. `ModelScale` must be present (`not null`) but is not normalized or range-checked; `SourceRowId` is not an arm-completeness input. The exact presence and length checks below are the entire predicate: `0` and `false` are present valid values, and no fill-in value, compatibility condition, or second C authority is allowed.

| Current category / completeness | Complete C predicate | Category `N/A` (not required for arm) |
| --- | --- | --- |
| Human | `Completeness=Complete`; `Customize.Length=26`; `Equipment.Length=10`; `ModelScale` is present; `Mainhand`, `Offhand`, `VisorToggled`, `FacewearModelId`, and `HatVisible` are each present | None of the listed Human fields; zero equipment and `0`/`false` field values remain valid |
| Demihuman | `Completeness=Complete`; `Equipment.Length=10`; `ModelScale` is present | `Customize=[]`, `Mainhand=null`, `Offhand=null`, `VisorToggled=null`, `FacewearModelId=null`, and `HatVisible=null`; zero equipment remains valid |
| Monster | `Completeness=ModelOnly`; `ModelScale` is present | `Customize=[]`, `Equipment=[]`, `Mainhand=null`, `Offhand=null`, `VisorToggled=null`, `FacewearModelId=null`, and `HatVisible=null`; `ModelCharaId=0` remains a present valid value |
| Other, or any `Completeness=Unsupported` snapshot | Not complete C; it does not arm | No missing field is supplemented or remapped |

The Actor list, Actor detail, and representation transition are consumers of that one published snapshot. The Actor detail includes mainhand, offhand, and Facewear as equipment values. Neither the UI nor the transition creates another appearance-value copy. Existing `LocalPlayerAppearancePersistence` may retain the same immutable `AppearanceData` instance and its Representation key so a newly created field, cutscene, or GPose Representation can publish and consume that same C; it is not a separately assembled authority. Current `ModelCharaId` and displayed equipment are obtainable after Apply; the defect addressed here is incomplete sharing of current actor data between Apply, the UI, and representation transition.

## Model Search registration

Every displayed Model Search result must contain a typed payload that the normal Apply path can use. A row that cannot construct its category payload is not registered and is not shown as a disabled result.

The User's 2026-09-03 search-filter request additionally excludes Human source rows without usable race/sex/tribe identity: Customize must contain 26 bytes, race must be 1-8, sex 0-1, and tribe must be nonzero and belong to that race. This is search registration only; it does not add an Apply admission check. The reported `BattleNpc#18950` has race/tribe 0 and is excluded, while the same-named `#18981` has race 1/sex 0/tribe 2 and remains eligible. Do not exclude by name, ModelChara ID 0, equipment zeros/sentinels, Body Type alone, or preview readiness. Demihuman and Monster registration rules remain unchanged.

- Human payloads contain ModelChara, all 26 Customize bytes, all ten equipment slots including explicit zero values, weapons, Facewear, hat visibility, visor state, and the exact NPC source Scale. Facewear is the first NPC `NpcEquip.Glasses` row ID, with `0` representing no Facewear. Hat visibility is `true` when the effective NPC head equipment model is nonzero and `false` when it is zero. These values always come from the selected NPC; the target Actor's Facewear, hat visibility, and Scale are never inherited or used as fill-in values.
- Demihuman payloads contain the model and structural equipment components supplied by that source. Demihuman eligibility is not judged by Human equipment completeness rules.
- Monster payloads are model-only when that is the complete game representation for the source.

## Apply transaction

Apply performs one bounded transaction against the selected target.

1. Resolve the target and validate only current target identity and login/territory availability. Model Search registration has already constructed the selected typed payload; Apply does not re-admit it.
2. If another Apply for the same actor is already pending, reject the new request. The existing request continues and is not cancelled or replaced.
3. Do not clear or change pin state or Bulk Outfit state because a request was queued, rejected, failed, or succeeded. Terminal success is determined by the requested call alone; it does not run a post-Apply readback or Verify gate.
4. The queued operation retains the target Representation resolved when Apply is accepted. Before `DisableDraw` and before `EnableDraw`, the current target must still be that exact Representation; a changed Representation is a terminal target-identity failure and receives neither the native Apply nor C. Otherwise perform one `DisableDraw`, then one synchronous `EnableDraw` for B.
5. During that explicit target-Actor Apply call, open one removable internal consumer-input transaction identified by the operation, target Representation, and runtime object index. It exists only from immediately before the target `EnableDraw` call until that call returns. The creation and normal draw-data consumer hooks may substitute B only while that exact transaction is active: `CharacterBase.Create` receives ModelChara and Customize, the ten owner-associated `DrawDataContainer.LoadEquipment` consumers receive all ten Equipment values, the mainhand/offhand consumers receive both weapons, the bonus/glasses consumer receives Facewear, and the hat and Visor consumers receive their selected values. Every intercepted normal game consumer is invoked exactly once; the transaction performs no second consumer call of its own. There is no ModelChara, BodyType, native-pointer equality, normalization, or rendered-readback admission gate. Explicit zero values remain active values, and category components are supplied independently when present.
6. Invoke source Scale once when it is present. Actor Morpher does not normalize the source value or compare the game readback with the request.
7. At the end of `EnableDraw`, destroy the consumer-input transaction. Consumer arrival and completion are passive diagnostic observations only: their values cannot determine the Apply result, C publication, backing cleanup, reapplication, or any recovery behavior. In `finally`, also restore the short-lived backing bytes that were substituted for the call. That restoration is unconditional call-scope cleanup, not a response to a missing consumer and not a rollback or automatic restoration of A. Neither the transaction nor these bytes are retained as A or continuing B state, and neither is used by Restore. A later Apply always begins with a fresh transaction containing only its newly selected B.
8. Report terminal success only when the same bounded operation observed a non-null requested Create. Consumer arrival or completion may be recorded passively, but a missing consumer neither turns `TryEnable` false nor changes terminal success/failure, C publication, backing cleanup, or later Apply behavior. This is not a post-Apply appearance readback or Verify gate. There is no post-Create equipment write, second Create, extra native consumer call, generated-body validation, rendered-equality gate, timeout, retry, automatic redraw, automatic rollback, fallback appearance, or automatic reapplication.

On terminal Apply success, the complete selected-NPC payload becomes C for that exact target Representation and is published through the existing `ActorRegistry` / `ActorSnapshot` state. It is no longer a selectable source B or an Apply request: it is the current applied value. The next normal Framework refresh must not replace NPC-owned `ModelCharaId`, Facewear, hat visibility, or Scale with the target Actor's restored backing A. No target-Actor value supplements a missing NPC field. A failed, rejected, or merely queued Apply does not publish C. Later same-Representation rendered changes and successful Actor Morpher outfit changes update that same published C and cannot create a second authority, retry, rollback, or redraw.

## Latest-state representation transfer

The local player's C must survive field, cutscene, and GPose representation replacement. This is a transition bridge, not source-NPC persistence.

- A transition owner may retain current/transferred Representation keys, one-shot transfer ownership, and the same immutable current-C reference already published by `ActorRegistry`, including that a key was already created or observed while unarmed. Such an early key is not re-targeted after a later arm. It must not assemble another appearance, retain pre-Apply A, or look up B as a source row. Territory is part of Representation identity so a field recreation with reused object IDs is still a new Representation.
- The normal per-Framework `ActorRegistry` publication is the current-data source. The transition does not independently poll, refresh, merge, or infer appearance values. Passive capture does not write to the actor. A successful Bulk Outfit operation changes only outfit-owned fields in that same C and is reflected by the next published snapshot.
- The first normal same-source publication of complete C1 under the existing publisher completeness predicate arms immediately; it does not wait for C2. The next different local-player Representation created in field, cutscene, or GPose reads that same latest published C1 and receives it exactly once during its creation.
- If a later normal publication for that same source Representation has `CurrentAppearance=null`, is partial under the predicate, or is `Completeness=Unsupported`, it is not armed. The next destination does not reach `InjectionContext` and must not use or retain C1. A later complete normal C2 publication arms again, and only the next new different Representation receives latest C2 exactly once. The transition refers to the publisher's latest snapshot each time and never keeps a complete publication as an appearance-value copy.
- The same Representation is never reapplied. A transferred key is not written again.
- The transfer path must not use selectable source B, backing A, an earlier snapshot/C, a partial assembly, inference, reverse mapping, or a fallback as substitute data for latest C. It uses only the complete applied-C instance published for the source Representation; it does not reconstruct C, retry, poll for equality, or roll back.
- Unrelated actors and cutscene copies without an explicit local-player source link are never injected.
- This shared-data correction reuses existing game-read paths. It adds no new native hook, product component, native-pointer cache, or creation-identity observer.
- `ARM_AFTER_COMPLETE_C — User approved (2026-09-01)` is the sole new safety measure. A transition arms only after the normal publisher has published a non-null, complete latest C from the same source Representation under the existing publisher completeness predicate. `CurrentAppearance` being null, partial, or `Completeness=Unsupported` does not arm. A category-specific `N/A` is not missing required data. This is current-snapshot evaluation, not a new clear, terminal drop, or cancellation; the completeness decision adds no fill-in value, compatibility condition, or second C authority.
- Before that arm, a different Representation may be created and temporarily display the game's native appearance. This is the approved defer/hold behavior. A later complete publication arms only a subsequent different Representation; the earlier Representation is not injected or reinjected, and the next different Representation receives the latest C exactly once.
- `ARM_AFTER_COMPLETE_C` is defer/hold only. It adds no automatic re-Apply, same-Representation injection or reinjection, timeout, retry count, old-C retention, fallback, terminal drop, clear, reject, or rollback.
- Existing Restore, logout/context clear, target identity, and single-operation boundaries remain unchanged. Other than `ARM_AFTER_COMPLETE_C`, this correction adds no skip, reject, retry, rollback, automatic reapply, `NO_INJECTION_AND_CLEAR`, or capture-failure behavior.

## Restore

The Actor-list Restore control is always enabled for a selectable actor. For the local player it performs this order:

1. If a Bulk Outfit override exists for that actor, restore that Bulk Outfit `Original` through the existing single-actor Bulk path.
2. If that Bulk `Original` restore fails, retain transition carry, hook, pin, and local outfit carry and do not start regeneration. After it succeeds, or when no Bulk override exists, clear Model Search transition carry, the transition hook, the actor pin, and local outfit carry before regeneration.
3. With no Actor Morpher payload active, invoke one `DisableDraw -> EnableDraw` cycle. Because Apply restored the temporary B backing in `finally`, this asks the game to generate the actor once from its game-owned backing, equivalent in outcome to the regeneration observed when leaving the inn. If this redraw fails, report failure without restoring any cleared Actor Morpher carry.

Restore does not read a saved pre-Apply A, B, or an earlier C. It must clear all Actor Morpher carry before the payload-free redraw, and it must restore any Bulk `Original` first so the game-owned outfit is also authoritative. A redraw while C is still active, before Bulk `Original` is restored, or while a pin/local carry can automatically reapply is not a valid Restore. A saved snapshot or a new speculative native API must not be substituted for this existing path.

## Bulk Outfit interaction

Bulk Outfit changes only outfit fields. It does not change ModelChara, Customize, face, hair, body, scale, job, or weapons. Its successful result becomes the equipment portion of C and is therefore what a later new Representation receives. A failed Bulk write does not trigger an automatic write of a previously captured outfit, and a failed territory-change reapply is not retried automatically.

Model Search Apply and Bulk Outfit state are independent. Apply success does not remove a pin or erase Bulk state. Apply failure or rejection changes neither. Per the User's 2026-09-03 indicator correction, Actor-list green coloring and the modified filter use either the current Representation's successful model-Apply marker or a Bulk Outfit `Original` that differs from the current rendered outfit. Pinned orange retains precedence. No Model Search or game-state baseline is created solely for coloring.

## Failure and UI rules

The User's 2026-09-03 mixed Model Search/Bulk defect report requires preserving the original outfit rather than learning an applied NPC outfit as the restore target. When creating the first Bulk override on an `IsAppearanceManaged` Representation, capture `Original` from the existing game-backing outfit reader before the Bulk write. The existing rendered reader still supplies Refresh Source and the current values used to construct a Bulk unequip operation. If a Bulk override already exists, retain its `Original` across model switches and subsequent Bulk changes. For an unmanaged Representation, retain the existing rendered pre-Bulk `Original` behavior. If the required first original cannot be captured, use the existing capture-unavailable termination before writing; do not substitute NPC C, guess, or add recovery. Explicit Restore consumes and clears that same Bulk Original through the existing path. No Model Search snapshot, additional persistence, or automatic restore is introduced.

- Apply and Restore buttons are not disabled by operation history or the absence of a saved snapshot.
- Queue acceptance is not displayed as Apply success. Success is shown only after the terminal native operation succeeds.
- Visual mismatch is a runtime/User-acceptance failure, but it does not authorize automatic retry, rollback, or another appearance write.
- Native target identity loss or an exception from the requested call is terminal for that request and leaves unrelated plugin state unchanged. Actor Morpher does not turn generated-body or Scale readback differences into failure.

## Explicitly superseded behavior

The following are not current behavior: permanent B writes to game backing, continuous source-row enforcement, same-Representation reapply, timer reapply, transition lookup of B, pre-Apply appearance snapshots, mismatch-driven Verify/timeout, automatic rollback, automatic visibility recovery, second redraw, and cancelling the first pending Apply to run a second.

## Document boundary

Updating this document alone is not implementation, runtime PASS, or product completion.
