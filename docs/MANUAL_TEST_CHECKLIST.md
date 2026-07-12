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
- [ ] Human Race and Tribe filters combine correctly
- [ ] Hyur limits Tribe choices to Midlander and Highlander
- [ ] Any Race exposes all 16 localized Tribe names
- [ ] Young-only search does not include old/adult NPCs
- [ ] Model detail shows completeness and disabled reason
- [ ] Rapidly changing Model Search selection settles on only the last preview
- [ ] Leaving Model Search pauses the preview and returning reloads the selection
- [ ] Closing and reopening the plugin window reloads the selected preview without a stale model
- [ ] Territory change and logout release the preview without a crash
- [ ] Monster details show nonzero Geometry counts and finite Bounds when its MDL is present
- [ ] Demihuman Geometry combines all present parts without failing on unused parts
- [ ] Auto Frame distance is finite and positive for ready Geometry
- [ ] Rapid model selection does not visibly parse every intermediate MDL
- [ ] Monster static 3D preview is nonblank and framed inside the square canvas
- [ ] Demihuman static 3D preview combines every valid visible equipment part
- [ ] Adult Human static 3D preview shows face, hair, body, and available equipment
- [ ] Young NPC static 3D preview uses the Young face and hair and remains correctly framed
- [ ] Young NPC previews with shared Human Hands or Feet render every equipped part
- [ ] Demihuman hair stored in Head or Hands uses the source NPC hair color rather than the fallback palette
- [ ] Left-drag rotates the static preview and mouse wheel changes zoom
- [ ] Reset Camera restores the initial static preview framing
- [ ] Missing or malformed optional Demihuman parts do not suppress valid parts
- [ ] Human preview works without allocating or modifying a CharaView object
- [ ] GPose entry does not freeze or crash
- [ ] Ambiguous same-name NPC copies are skipped
- [ ] GPose Bulk Outfit waits until representation mapping is ready
- [ ] GPose Bulk Apply and Unequip affect every mapped matching actor, not hidden field actors
- [ ] Bulk source equipment uses e####/a#### Set IDs and plain-number variants
- [ ] Applying before Source Refresh applies the visibly empty source instead of silently capturing player equipment
- [ ] Applying after Source Refresh applies exactly the equipment shown in the source table
- [ ] Stain columns show color swatches and hover displays the correct RGB and HEX values
- [ ] Selected Human Actor details show the same equipment table as Bulk source
- [ ] Human Model Search details show icons, model IDs, names, and both Stain swatches
- [ ] Bulk-modified Actor names are colored and pinned Actor names use the pinned color
- [ ] Actor details show original and applied Bulk equipment as separate tables
- [ ] A pinned outfit reapplies after territory change, game restart, and plugin update
- [ ] Restore clears the pin and leaves the original outfit restored
- [ ] Unpinned equipment shows the pin icon and pinned equipment shows the red delete icon
- [ ] Actor state filter separates Unapplied, Outfit Modified, and Pinned actors
- [ ] Bulk Restore skips pinned actors while restoring every other modified actor
- [ ] Bulk Outfit preview excludes the local player by default
- [ ] Bulk Outfit preview counts non-Human actors as skipped
- [ ] Disabled exclusion filters do not remove matching actors
- [ ] Enabled exclusion filters remove only actors matching every exclusion condition
- [ ] Identical target and exclusion conditions result in zero eligible actors
- [ ] Include Yourself followed by a Players exclusion removes the local player
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
