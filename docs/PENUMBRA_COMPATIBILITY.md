# Penumbra Compatibility

## 2026-07-12 crash analysis

The crash at 02:50:52 JST was a native access violation in the game's EQDP lookup. The stack entered through Penumbra's `EqdpEquipHook` while creating a Human draw object and included Actor Morpher's globally installed `CreateCharacterBase` detour. Immediately before the crash, resources for special Young NPC race code `c9201` and equipment set `e9044` were requested. The Penumbra collection changed the priority of `Nude Raya-O-Senna (Gen3)` from 0 to 1 immediately before the automatic redraw.

Actor Morpher previously kept the morphed Body Type and equipment in the game object's backing `DrawData`. A later Penumbra Mod toggle could therefore begin its independent redraw with the unsupported `c9201` and adult-equipment combination.

The mitigation has two parts:

1. Actor Morpher's `CreateCharacterBase` hook is enabled only around Actor Morpher's own synchronous draw creation and disabled immediately afterward.
2. After a non-standard Human Body Type is drawn and verified, the visible draw object is retained while the game object's backing appearance is normalized to the original snapshot. Later external redraws therefore start from a valid player appearance instead of stale special-body data.

Human-to-Human changes also pass through the original snapshot before the final NPC appearance is created. This invalidates stale CharacterBase resources when switching directly between special Human NPCs.
