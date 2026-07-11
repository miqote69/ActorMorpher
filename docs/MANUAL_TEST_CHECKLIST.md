# Manual Test Checklist

Do not enable appearance-write buttons until the standalone memory implementation has been reviewed for the current FFXIVClientStructs version.

## Available now

- [ ] Plugin loads without Penumbra installed
- [ ] Plugin loads without Glamourer installed
- [ ] Local player is first in Actors
- [ ] Actors only contains PC, EventNpc, and BattleNpc entries
- [ ] Actor name/type/race/gender filters combine correctly
- [ ] Actor selection clears after index reuse by another actor
- [ ] Model Search returns Human, Demihuman, and Monster rows
- [ ] Model ID and name filters combine correctly
- [ ] Young-only search does not include old/adult NPCs
- [ ] Model detail shows completeness and disabled reason
- [ ] GPose entry does not freeze or crash
- [ ] Ambiguous same-name NPC copies are skipped
- [ ] Bulk Outfit preview excludes the local player by default
- [ ] Bulk Outfit preview counts non-Human actors as skipped
- [ ] Territory change, logout, and plugin disable do not crash

## Blocked until safe memory writes exist

- [ ] Human Apply and Restore, including Young NPC bones
- [ ] Monster Apply and Restore
- [ ] complete Demihuman Apply and Restore
- [ ] GPose override propagation and exit synchronization
- [ ] ten-slot Bulk Outfit Apply and Restore
- [ ] Stain 1 and Stain 2 visual verification
- [ ] Facewear Apply and removal
- [ ] Unequip All while preserving both weapons, job, customize, and ModelChara ID
- [ ] repeated Apply/Restore and cancellation stress tests

Any native crash, T-pose, missing skeleton, or incorrect equipment result is a release blocker. Attach the Dalamud log and crash pack before changing offsets or structures.
