# Test Plan

## Objective and scope

Verify that Model Search carries the source NPC model scale through apply, Human-to-Human transitions, game-triggered local-player and cutscene-copy CharacterBase recreation, rollback, territory reapply, and restore without changing unrelated game-object or draw-object scale layers. A cutscene copy and the returning local player must preserve the active morph's model, Customize, and model scale while using the latest successfully applied local-player outfit rather than stale equipment retained from Model Search.

The protected baseline is `v0.0.0.46` (`cfc4195`). This change covers `AppearanceData`, Model Search source construction, redraw finalization, application verification, override storage, and local-player persistence.

Excluded from this change are 3D preview scale, manual scale controls, Bulk Outfit behavior, collision height, animation scale, and release packaging.

## Test levels and techniques

- Unit: value propagation, first-snapshot preservation, clean Human transition ordering, rollback, restore, and persistence.
- Build/static: Debug and Release compilation, full automated suite, `git diff --check`, review of native writes, and confirmation that the local-player hook uses the actual actor vtable entry rather than the static `GameObject` base vtable.
- Runtime: apply `BNpcBase#10072` (Ryne), change the visible player outfit, enter a cutscene that creates a separate actor copied from the local player, inspect diagnostic model/customize/equipment/scale values, then restore the original player.
- Visual/User acceptance: compare the resulting Ryne height with the original NPC. This remains separate from runtime numeric verification.

Test doubles are permitted only for managed application, restore, and persistence tests. They cannot establish native memory compatibility or visual correctness.

## Environment and resources

- Current FFXIV client and Dalamud API 15 development environment.
- Actor Morpher Debug DLL loaded through Dalamud Dev Plugin Locations with automatic reload.
- Full diagnostics plus the temporary `AM3010` standard-log scale record.
- Current game data for `BNpcBase#10072`: `ModelCharaId=2720`, `Scale=0.84`, `Height=255`.

## Entry, suspension, resumption, and exit

Entry requires a clean buildable `v0.0.0.46` baseline plus the read-only scale diagnosis. Suspend immediately on a crash, T-pose, unavailable local actor, redraw timeout, non-finite scale, or failed restore. Resume only after the failing stage and actor state are known and the character has returned to a normal field state.

Exit requires:

- all automated tests pass;
- Debug and Release builds have zero warnings and errors;
- runtime apply records final `CharacterBaseModelScale=0.84` for Ryne;
- a game-triggered local-player `EnableDraw` receives the retained Ryne appearance once during CharacterBase creation, without queuing another redraw or retry;
- a cutscene actor explicitly copied from ObjectIndex 0 retains the active Ryne model, Customize, and model scale while receiving the latest successful local-player Bulk Outfit equipment;
- equipment changed after the Ryne apply remains present in the cutscene, and stale Model Search equipment is not injected;
- cutscene linking does not depend on a source draw object that the game may already have removed before `CopyFromCharacter`;
- when the field local player is recreated after the cutscene, the same latest outfit remains applied instead of reverting to Ryne's Model Search equipment;
- direct `CharacterSetupContainer.CopyFromCharacter` creation is recognized without actor-name matching;
- an unmapped or non-local cutscene actor passes through unchanged, and a destroyed cutscene slot cannot retain a stale local-player link;
- a game-triggered `EnableDraw` for any non-local actor passes through unchanged;
- runtime restore records the pre-apply player model scale and restores the original visible height;
- restore and logout stop the retained local-player creation injection;
- no unrelated scale layer is written by the implementation.

The automated suite and build are decided by their command results. Native runtime evidence is decided by the recorded values. Final visual acceptance belongs to the user.

## Regression, risks, and evidence

Run the complete existing suite because `AppearanceData` is shared by Human, Demihuman, Monster, rollback, and persistence paths. The main risks are losing the original scale, resetting the desired scale during redraw, treating an invalid source scale as valid, declaring success before the recreated draw object has the requested scale, injecting retained data into an unrelated cutscene actor through a stale or guessed association, failing to update retained equipment after a successful local-player Bulk Outfit operation, replacing the player's current outfit with stale Model Search equipment, replacing the active morph Customize with normalized player backing data, requiring a draw object that is unavailable during the copy event, or invoking the static `GameObject` base `EnableDraw` implementation instead of the local actor's actual virtual implementation. The latter failure left the player hidden and must not recur. Cutscene persistence must remain generation-event driven; frame polling, name matching, redraw retries, automatic rollback, unrelated actor or saved-state lookup during cutscene creation, and unverified indirect `CopyPlayerCustomize` handling are excluded.

Retain the test command summaries, relevant `AM3010` lines, game-data source row values, and final Git diff. Runtime failure must leave the change unpublished.
