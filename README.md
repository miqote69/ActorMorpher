# Actor Morpher

Dalamud plugin for browsing visible actors and searchable Human, Demihuman, and Monster model data.

## Install

Add this URL to Dalamud's custom plugin repositories:

```text
https://raw.githubusercontent.com/miqote69/ActorMorpher/main/repo.json
```

Commands: `/actormorpher` or `/amorph`

## Current status

The plugin now has standalone actor identity, state stores, staged redraw coordination, conservative GPose mapping, model-data completeness checks, and Bulk Outfit target previews. It does not require Glamourer or Penumbra IPC.

Appearance and outfit memory writes remain disabled. Safe standalone writes for Human, Monster, Demihuman, equipment, and Facewear have not yet been verified against the current game client. Buttons that would perform those writes are disabled instead of using guessed offsets or partial model-ID writes.

See [implementation notes](docs/IMPLEMENTATION_NOTES.md), [diagnostics](docs/DIAGNOSTICS.md), [testing](docs/TESTING.md), and the [manual checklist](docs/MANUAL_TEST_CHECKLIST.md).

This is a cosmetic client-side plugin. It does not automate gameplay, send network requests, or collect player data.
