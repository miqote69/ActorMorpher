# Actor Morpher

> [!CAUTION]
> ## Development build — use entirely at your own risk
>
> Actor Morpher is still under active development and may behave unpredictably.
>
> It may fail to apply or restore an actor correctly, stop working after an FFXIV or Dalamud update, leave an actor in an unexpected visual state, or cause the game to crash.
>
> **No warranty or guarantee of any kind is provided.**
>
> The author does not guarantee stability, compatibility, successful restoration, data integrity, safety, or any result produced by this plugin. The author assumes no responsibility for crashes, lost states, unexpected behavior, damage, or any other consequence caused directly or indirectly by using Actor Morpher.
>
> Use this plugin only if you understand and accept these risks.

Actor Morpher is a standalone Dalamud plugin for browsing visible actors, searching Human, Demihuman, and Monster model data, changing actor appearances, and applying outfit operations to multiple actors.

Actor Morpher does not require Glamourer or Penumbra IPC.

## Features

### Actor browser

- Lists actors currently visible to the game client.
- Provides filters for finding specific actors.
- Displays actor and model information.
- Tracks logical actors separately from their current in-game representations.
- Re-resolves and validates actors before native operations to reduce the risk of applying changes to the wrong object.

### Model search

Searchable model data includes:

- Human
- Demihuman
- Monster

Model entries are classified according to whether enough data is available for safe application.

Some entries may be searchable but not applicable when the required appearance or equipment data is incomplete.

### Morph application and restore

- Applies supported Human, Demihuman, and Monster model data to a selected actor.
- Captures the actor's original state before the first Actor Morpher change.
- Restores the actor to the state captured before the morph was applied.
- Uses a staged redraw process for application and restoration.
- Attempts to roll back an operation when a failure is detected.
- Recreates the original Human twice when restoring from another Human or Young NPC to avoid reusing the previous NPC draw object.

Restoration is not guaranteed. FFXIV object recreation, territory changes, actor despawning, game updates, or plugin errors may prevent a complete restore.

### GPose support

- Detects when GPose is active.
- Attempts to match normal-world actors with their GPose representations.
- Uses multiple identity values instead of relying only on actor names or fixed object indexes.
- Skips ambiguous matches rather than applying changes to an uncertain target.
- Rejects GPose operations when no mapped GPose representation exists instead of falling back to the hidden field actor.

GPose support is intentionally conservative and remains under active testing.

### Bulk Outfit

Bulk Outfit operations can target multiple matching actors.

Supported operations include:

- Apply the local player's current Human outfit.
- Unequip supported outfit slots.
- Restore each actor's previously captured outfit.
- Preview actors affected by the current filters.
- Continue processing after an actor-specific failure.
- Attempt to restore an actor's previous state when its operation fails.
- Show original and applied equipment separately for modified actors.
- Pin an applied outfit so it is restored when the matching actor reappears after restart or update.
- Filter actors by unapplied, outfit-modified, or pinned state.
- Keep pinned actors out of the Bulk Restore batch while allowing explicit per-actor restore.

Bulk Outfit includes these ten armor and accessory slots:

- Head
- Body
- Hands
- Legs
- Feet
- Ears
- Neck
- Wrists
- Right Ring
- Left Ring

Each supported slot includes:

- Set
- Variant
- Stain 1
- Stain 2

The following are handled separately:

- Facewear
- Headgear visibility
- Visor state

Bulk Outfit does not include:

- Weapons
- Weapon visibility
- Class or job
- Level
- Character customization
- ModelChara ID

Unequip operations require verified slot-specific empty equipment data. If the required values cannot be verified, the operation is rejected rather than using fabricated placeholder values.

### Diagnostics

Actor Morpher includes local diagnostic and troubleshooting tools.

Diagnostics may record:

- Operation stages
- Actor identity checks
- Morph and restore results
- Bulk operation progress
- Actor-specific failures
- Rollback attempts
- Redraw coordination
- GPose mapping results

Enable diagnostics before testing new or unstable operations.

If a crash or incorrect result occurs, keep the session log and any crash information available when reporting the problem.

## Install

Add this URL to Dalamud's custom plugin repositories:

```text
https://raw.githubusercontent.com/miqote69/ActorMorpher/main/repo.json
```

Open Actor Morpher with either command:

```text
/actormorpher
/amorph
```

## Current development status

Actor Morpher currently provides:

Model and NPC names, race names, item names, searching, and sorting follow the active FFXIV client language. The plugin UI can follow the client automatically or be selected independently from English, Japanese, German, and French in Settings. Existing diagnostic configuration is preserved when upgrading.

Human Model Search details include equipment model numbers and localized matching item names. Actor restore now coordinates appearance and Bulk Outfit snapshots so a pre-morph body is not combined with an NPC outfit. Bulk writes skip equipment slots that already match, reducing unnecessary resource reloads and interference with externally modded unequipped appearances.

An appearance successfully applied to the local player is retained across field and territory transitions. Actor Morpher keeps only the desired managed appearance, resolves the newly created local player actor after the transition, captures a fresh restore base, and reapplies through the normal staged redraw path. The retained appearance is cleared on restore or logout. Bulk Outfit's source equipment table resolves each appearance by slot, Set, and Variant and displays localized matching item names with game icons.

Model Search and Bulk Outfit now use explicit last-operation priority. A successful Model Search apply ends the target actor's previous Bulk Outfit override, so the selected model's own equipment is not replaced by an earlier Unequip or copied outfit. A later Bulk Outfit operation can still override those equipment slots. The local player's successful Bulk Outfit Desired state, including Unequip All, is retained across territory changes and reapplied after any retained model appearance has finished.

In GPose, Bulk Outfit waits for representation mapping and targets only actors with a validated GPose representation; hidden normal-world actors are not included in the batch. Mapping includes a unique stable composite of name, ObjectKind, race, and gender when volatile model/body values differ. The source and Actors equipment tables share the same slot display with localized item name and icon, formal `e####` or `a####` Set IDs, plain-number variants, and game Stain color swatches with RGB tooltips. The initial source is empty; Refresh explicitly captures the local player's outfit, and Apply always uses the state currently shown in the source table.

Human Model Search can filter by localized Tribe within Race, including Midlander and Highlander for Hyur. Bulk Outfit has independent target and exclusion filters; exclusion takes precedence whenever an actor matches both.

Model Search resolves and displays preview asset readiness for Human, Demihuman, and Monster entries. All three categories render through a bounded static software 3D preview with drag rotation, wheel zoom, and camera reset. Human resolution prioritizes valid age-specific face and hair models and falls back safely for body and equipment. Native CharaView allocation remains disabled because current APIs do not expose exclusive slot ownership or verified native texture lifetime rules. See [preview research](docs/MODEL_PREVIEW_RESEARCH.md) and [preview architecture](docs/MODEL_PREVIEW.md).

Preview details decode complete High LOD Main meshes through a bounded MDL V5/V6 parser after selection debounce. The parser reads positions, UVs, vertex colors, material references, bone tables, skinning weights, and indices, validates every range, and avoids the later V6 sections that fail in the bundled Lumina parser. Human shared equipment is transformed to the selected race and body type with the game's `human.pbd` bone deformers; missing Hands and Feet models fall back to the verified bare `e0000` parts. The renderer resolves IMC material variants, loads game MTRL and TEX assets, composes character color tables, and builds Human hair color from the selected NPC's `human.cmp` customization palette. Missing normals are generated from triangle geometry, while malformed parts are skipped and reported. No native game object is created. The static 3D preview can be disabled in Settings; disabling it releases the active scene and generated textures and skips geometry inspection.

Special Human Body Types use normalized backing data after their visible draw object is created. This prevents later Penumbra redraws from starting with stale Young NPC skeleton and equipment data. See [Penumbra compatibility](docs/PENUMBRA_COMPATIBILITY.md).

Native appearance features use the current Dalamud and FFXIVClientStructs APIs without guessed offsets, signatures, VTable indexes, or ObjectKind writes. These new write paths still require FF14 testing against the current client; enable diagnostics before testing and report any crash pack with the session log.

- Actor listing and filtering
- Logical actor identity tracking
- Human model search and application
- Demihuman model search and supported application
- Monster model search and supported application
- Original state capture
- Morph restoration
- Staged redraw coordination
- Conservative GPose representation matching
- Bulk Outfit preview and filters
- Bulk Outfit apply
- Bulk Outfit unequip
- Bulk Outfit restore
- Facewear support
- Headgear visibility support
- Visor state support
- Diagnostic logging and troubleshooting tools

The plugin uses current Dalamud and FFXIVClientStructs APIs.

It does not intentionally rely on:

- Glamourer IPC
- Penumbra IPC
- Guessed memory offsets
- Guessed signatures
- Guessed VTable indexes
- ObjectKind modification

Native appearance operations are still being developed and tested against the live FFXIV client.

Features may be incomplete, may change without notice, and may stop working after changes to FFXIV, Dalamud, or FFXIVClientStructs.

## Documentation

- [Implementation notes](docs/IMPLEMENTATION_NOTES.md)
- [Diagnostics](docs/DIAGNOSTICS.md)
- [Testing](docs/TESTING.md)
- [Manual test checklist](docs/MANUAL_TEST_CHECKLIST.md)

## Privacy and scope

Actor Morpher is a cosmetic, client-side plugin.

It does not:

- Automate gameplay
- Send gameplay commands
- Send network requests
- Upload actor information
- Collect player data

## License

Actor Morpher is licensed under the MIT License.
