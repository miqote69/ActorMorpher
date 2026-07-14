# Actor Morpher

Actor Morpher is a standalone Dalamud plugin for changing the appearance of visible FFXIV actors.

It can search and apply Human, Demihuman, and Monster models, restore captured appearances, and apply outfits to multiple actors. It does not require Glamourer or Penumbra IPC.

Its primary purpose is to support mod development by making it easier to verify mod behavior across different character types.

> [!CAUTION]
> Actor Morpher is under active development. Appearance application or restoration may fail, leave an actor in an incorrect visual state, or crash the game. Enable diagnostics before testing and use the plugin at your own risk.

## Install

Add this URL to Dalamud's custom plugin repositories:

```text
https://raw.githubusercontent.com/miqote69/ActorMorpher/main/repo.json
```

Open the plugin with either command:

```text
/actormorpher
/amorph
```

See the [latest release](https://github.com/miqote69/ActorMorpher/releases/latest) for the current version.

## Features

### Actors

- Lists visible player characters and supported NPC actors.
- Keeps the local player at the top of the list.
- Filters by name, actor type, race, gender, and modification state.
- Shows original and applied equipment.
- Restores the appearance captured before the first change.

### Model Search

- Searches Human, Demihuman, and Monster models by localized name or Model ID.
- Filters Human results by race, tribe, gender, Adult, and Young NPC.
- Applies a selected model to yourself or another visible actor.
- Shows Human equipment details and preview asset status.
- Provides an optional software-rendered 3D preview with rotation and zoom.
- Keeps a successfully applied local-player model across territory changes until restored.

### Bulk Outfit

- Copies the refreshed local-player outfit to matching Human actors.
- Unequips supported armor and accessory slots.
- Uses independent target and exclusion filters; exclusion takes priority.
- Shows affected actors before applying.
- Restores captured outfits while excluding pinned actors.
- Persists pinned outfits across restarts, territory changes, and plugin updates when the actor can be matched again.

Bulk Outfit supports Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, Right Ring, Left Ring, Facewear, headgear visibility, and visor state. It does not change weapons, class or job, level, character customization, or ModelChara ID.

### GPose

- Maps visible actors to their GPose representations before applying changes.
- Skips ambiguous or unavailable mappings instead of targeting hidden field actors.
- Supports model application, restoration, and Bulk Outfit operations for validated mappings.

### Languages

- Supports English, Japanese, German, and French UI text.
- Model names, NPC names, race names, and item names follow the selected FFXIV data language.
- The plugin language can follow the game automatically or be selected in Settings.
- Korean and Chinese FFXIV client versions have not been tested because no compatible test environment is available; whether the plugin works correctly on those clients is unknown.

### Diagnostics

- Records local operation stages, validation failures, redraws, GPose mapping, and rollback attempts.
- Supports troubleshooting captures and diagnostic snapshots.
- Does not upload logs automatically.

## Current Limitations

- Only actors currently available to the game client can be targeted.
- Some searchable models cannot be applied or fully previewed when required game data is incomplete.
- Restoration cannot be guaranteed after actor despawns, game updates, or incompatible external changes.
- GPose operations require an unambiguous mapped representation.
- The 3D preview is a managed software preview, not a native game render, and can be disabled in Settings.
- FFXIV, Dalamud, or FFXIVClientStructs updates may temporarily break native appearance operations.

## Documentation

- [Model preview architecture](docs/MODEL_PREVIEW.md)
- [Penumbra compatibility](docs/PENUMBRA_COMPATIBILITY.md)
- [Diagnostics](docs/DIAGNOSTICS.md)
- [Testing](docs/TESTING.md)
- [Manual test checklist](docs/MANUAL_TEST_CHECKLIST.md)
- [Implementation notes](docs/IMPLEMENTATION_NOTES.md)

## Privacy and Scope

Actor Morpher is a cosmetic, client-side plugin. It does not automate gameplay, send gameplay commands, upload actor information, or collect player data.

## License

Actor Morpher is licensed under the [MIT License](LICENSE).
