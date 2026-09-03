# Penumbra Compatibility

`APPEARANCE_APPLICATION_SPEC.md` is normative. The incident below explains why the current path limits backing substitution and creation injection to Actor Morpher's synchronous Apply or one qualifying new-Representation transfer.

## 2026-07-12 crash analysis

The crash at 02:50:52 JST was a native access violation in the game's EQDP lookup. The stack entered through Penumbra's `EqdpEquipHook` while creating a Human draw object and included Actor Morpher's globally installed `CreateCharacterBase` detour. Immediately before the crash, resources for special Young NPC race code `c9201` and equipment set `e9044` were requested. The Penumbra collection changed the priority of `Nude Raya-O-Senna (Gen3)` from 0 to 1 immediately before the automatic redraw.

Actor Morpher previously kept the morphed Body Type and equipment in the game object's backing `DrawData`. A later Penumbra Mod toggle could therefore begin its independent redraw with the unsupported `c9201` and adult-equipment combination.

The current mitigation is narrower:

1. Apply temporarily substitutes the complete selected payload only around Actor Morpher's synchronous `EnableDraw` creation and restores those bytes in `finally`.
2. There is no post-create Verify, normalization snapshot, retry, rollback, or Human-to-Human intermediate appearance.
3. A later Penumbra redraw does not receive the old Model Search source. Only a genuinely new local-player Representation may receive latest player state C once through the transition bridge.
