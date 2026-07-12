# Manual Test Checklist

Enable Full diagnostics before testing native appearance operations. Stop after any crash, permanent T-pose, missing skeleton, or incorrect restore and attach both the session log and crash pack.

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
- [ ] Rapidly changing Model Search selection settles on only the last preview
- [ ] Leaving Model Search pauses the preview and returning reloads the selection
- [ ] Closing and reopening the plugin window reloads the selected preview without a stale model
- [ ] Territory change and logout release the preview without a crash
- [ ] GPose entry does not freeze or crash
- [ ] Ambiguous same-name NPC copies are skipped
- [ ] Bulk Outfit preview excludes the local player by default
- [ ] Bulk Outfit preview counts non-Human actors as skipped
- [ ] Territory change, logout, and plugin disable do not crash

## Native appearance verification

- [ ] Human Apply and Restore, including Young NPC bones
- [ ] Monster Apply and Restore
- [ ] complete Demihuman Apply and Restore
- [ ] GPose override propagation and exit synchronization
- [ ] ten-slot Bulk Outfit Apply and Restore
- [ ] Stain 1 and Stain 2 visual verification
- [ ] Facewear Apply and removal
- [ ] Unequip All while preserving both weapons, job, customize, and ModelChara ID
- [ ] repeated Apply/Restore and cancellation stress tests

For each Bulk Outfit test, verify that main hand, off hand, weapon dyes, weapon visibility, class job, level, race, gender, customize, and ModelChara ID remain unchanged.

## Diagnostics

- [ ] Dev Plugin starts with Full diagnostics on a new configuration
- [ ] Release Plugin starts with diagnostics Off on a new configuration
- [ ] Off creates no diagnostics directory or file
- [ ] Errors Only excludes successful operations and records errors
- [ ] Full creates a session JSONL file and `latest.jsonl`
- [ ] Dev Mirror creates `ActorMorpherDiagnostics/latest.jsonl`
- [ ] UI log paths match the actual files
- [ ] Actor selection and Model selection are recorded once per action
- [ ] Redraw operations use a shared Operation ID
- [ ] GPose entry, exit, mapping, and timeout are recorded
- [ ] Diagnostic Marker is recorded with normalized text
- [ ] Troubleshooting Capture temporarily changes Off to Full
- [ ] Ending Capture restores the previous persistent mode
- [ ] Snapshot creates all five expanded files
- [ ] Actor names are absent by default
- [ ] Actor names appear only after enabling the privacy option
- [ ] Raw addresses are absent by default
- [ ] A non-writable destination does not crash or disable the plugin
- [ ] A large event burst does not visibly stall Framework Update
- [ ] Plugin disable closes diagnostic files
- [ ] Restart creates a different Session ID
- [ ] Retention removes old sessions but not the current session
- [ ] `latest.jsonl` can be read while the plugin is running

Any native crash, T-pose, missing skeleton, or incorrect equipment result is a release blocker. Attach the Dalamud log and crash pack before changing offsets or structures.
