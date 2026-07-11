# Actor Morpher

Dalamud plugin for browsing visible actors and searchable Human, Demihuman, and Monster model data.

## Install

Add this URL to Dalamud's custom plugin repositories:

```text
https://raw.githubusercontent.com/miqote69/ActorMorpher/main/repo.json
```

Commands: `/actormorpher` or `/amorph`

## Current status

The plugin provides standalone actor identity, Human/Demihuman/Monster morph application and restore, staged redraw coordination, conservative GPose synchronization, and Bulk Outfit apply/unequip/restore. It does not require Glamourer or Penumbra IPC.

Native appearance features use the current Dalamud and FFXIVClientStructs APIs without guessed offsets, signatures, VTable indexes, or ObjectKind writes. These new write paths still require FF14 testing against the current client; enable diagnostics before testing and report any crash pack with the session log.

See [implementation notes](docs/IMPLEMENTATION_NOTES.md), [diagnostics](docs/DIAGNOSTICS.md), [testing](docs/TESTING.md), and the [manual checklist](docs/MANUAL_TEST_CHECKLIST.md).

This is a cosmetic client-side plugin. It does not automate gameplay, send network requests, or collect player data.
