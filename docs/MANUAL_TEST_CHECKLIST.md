# Manual Test Checklist

`APPEARANCE_APPLICATION_SPEC.md` is normative. Run only the cases selected by `TEST_PLAN.md`; unchecked items are not implied release requirements.

Enable Full diagnostics before testing native appearance operations. Stop after any crash, permanent T-pose, missing skeleton, or incorrect restore and attach both the session log and crash pack.

## Available now

- [ ] Plugin loads without Penumbra installed
- [ ] Plugin loads without Glamourer installed
- [ ] Local player is first in Actors
- [ ] Actors only contains PC, EventNpc, BattleNpc, and Companion entries
- [ ] The local player's summoned Companion is fixed immediately after the local player when present
- [ ] Actor name/type/race/gender filters combine correctly
- [ ] Actor selection clears after index reuse by another actor
- [ ] Model Search returns Human, Demihuman, and Monster rows
- [ ] Model ID and name filters combine correctly
- [ ] Human Race and Tribe filters combine correctly
- [ ] Hyur limits Tribe choices to Midlander and Highlander
- [ ] Any Race exposes all 16 localized Tribe names
- [ ] Young-only search does not include old/adult NPCs
- [ ] Every displayed Model Search result is applyable and contains its category payload; unconstructable rows are absent
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
- [ ] Demihuman hair stored in Head or Hands uses its MTRL `g_DiffuseColor` rather than the fallback palette
- [ ] Au Ra Young NPC Face values such as 102 resolve the corresponding `f0002` model and render clan skin color
- [ ] Human equipment previews apply Stain 1 and Stain 2 only to MTRL rows assigned to the corresponding dye channel
- [ ] Human Feet and Hands use a shared `c0101` model with PBD deformation when the gender-specific model is absent
- [ ] Lalafell female previews render bare Hands and Feet through the same-race `c1101` fallback
- [ ] Miqo'te female previews skip incompatible `c0701` equipment and retain Body, Legs, Hands, and Feet through valid fallbacks
- [ ] Human face previews show only FacialFeature submeshes selected by the NPC customization bitmask
- [ ] Human iris and facial-feature meshes use the NPC eye and feature colors instead of fallback mesh colors
- [ ] EventNpc 1036911 / 2B and 1031836 / 2P exclude the IMC-disabled `atr_tv_a` body submesh
- [ ] EventNpc 1033925 / 2B keeps body skin behind the bodice while exposed skin remains visible
- [ ] EventNpc 1031836 / 2P keeps lower-body underwear behind the white skirt while stockings remain visible
- [ ] EventNpc 1025107 / Yotsuyu excludes the `atr_tls` tail insert while keeping the robe and valid lower-body geometry
- [ ] BattleNpc 8764 / Yotsuyu cloth shades smoothly without moving triangular facets
- [ ] Two-sided skirt and coat materials remain visible from their intended inner side
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
- [ ] Actor names are green only when current rendered equipment differs from an existing Bulk `Original`; no Bulk `Original` stays white and pinned uses the pinned color
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

- [ ] Applying an exact Ryne search row records its `Source`/`SourceId`; `CharacterData.ModelScale` and `GameObject.Scale` equal that row's source Scale, and the visible size matches that exact NPC
- [x] After Ryne Apply and a local-player Bulk Outfit, the cutscene and returning field player keep Ryne's face and the latest applied outfit (User visual acceptance, 2026-08-30)
- [ ] Entering a cutscene after Ryne Apply keeps Ryne's original NPC height on the cutscene copy
- [ ] Cutscene entry records `AM4006` for parent ObjectIndex 0 and `AM4005` with `isCutsceneCopy=true`
- [ ] Leaving the cutscene or destroying its actor records `AM4007` and does not affect unrelated cutscene actors
- [ ] Restore is always callable; after any Bulk Original succeeds it clears transition carry, hook, pin, and local outfit carry before one original-player regeneration request, reads no saved pre-Apply appearance, and does not reinstate carry after redraw failure
- [ ] Human Apply, including Young NPC bones, uses the selected payload once; later manual changes become latest C
- [ ] Monster Apply and one-time latest-C transfer to each new territory/GPose Representation
- [ ] Demihuman Apply uses its structural parts and one-time latest-C transfer to each new territory/GPose Representation
- [ ] GPose entry/exit uses the latest actor state rather than the original Model Search source
- [ ] ten-slot Bulk Outfit Apply and Restore
- [ ] Stain 1 and Stain 2 visual verification
- [ ] Facewear Apply and removal
- [ ] Unequip All while preserving both weapons, job, customize, and ModelChara ID
- [ ] Model Search Apply queue, rejection, failure, and success leave pin plus Bulk `Original`/`Desired` unchanged
- [ ] A second Apply while one is pending is rejected without cancelling or replacing the first

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
- [ ] GPose entry, exit, mapping, and mapping-readiness terminal failure are recorded; this is not a Model Search Verify timeout
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
