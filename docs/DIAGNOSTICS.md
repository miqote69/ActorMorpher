# Actor Morpher Diagnostics

## Purpose

Actor Morpher diagnostics record local troubleshooting events as UTF-8 JSON Lines. Each line is an independent JSON object, so a partial log remains readable and related events can be found by Operation ID.

File diagnostics never call Penumbra or Glamourer IPC. A file error is reported once to the standard Dalamud log and does not fail Actor Morpher operations.

## Modes

| Mode | Behavior |
| --- | --- |
| Off | Creates no diagnostic directory, file, Channel, writer, ring buffer, rotation, retention, or snapshot automation. |
| Errors Only | Stores session start, errors, fatal events, failed operations, rollback failures, unexpected exceptions, and file logging failures. |
| Full Troubleshooting | Stores lifecycle, user actions, operation phases, Actor identity, redraw, GPose, target summaries, failures, performance markers, and snapshots. |

The initial mode is `Full` for a Dalamud Dev Plugin and `Off` for a repository release. A saved user setting is preserved across updates.

## Locations

Standard location:

```text
<Dalamud plugin config>/ActorMorpher/diagnostics/
    logs/
        actormorpher-<utc timestamp>-<session>.jsonl
        actormorpher-<utc timestamp>-<session>-part02.jsonl
        latest.jsonl
    snapshots/
    latest-session.txt
```

When the Dev Mirror option is enabled:

```text
<plugin assembly directory>/ActorMorpherDiagnostics/
    latest.jsonl
    latest-session.txt
```

Codex should read `ActorMorpherDiagnostics/latest.jsonl` first when it exists. Otherwise read `<ConfigDirectory>/diagnostics/logs/latest.jsonl`.

## Operations

Operations use stable prefixes such as:

```text
morph-000001
redraw-000002
gpose-000003
bulk-000004
restore-000005
snapshot-000006
```

Operation entries include phase, outcome, elapsed time, Actor key, and parent Operation ID when applicable. An operation disposed without explicit completion or failure is recorded as `Abandoned` in Full mode.

## Privacy

The default configuration does not save Actor names or raw memory addresses. Actor identity is represented by a session-salted short hash such as `Pc#5C912A`. The same Actor is stable within one session and changes hash in a later session.

The standard log may contain object index, GameObject ID, Entity ID, Base ID, ObjectKind, Territory ID, ModelChara ID, race, gender, body type, and GPose representation state. It does not collect chat, Content ID, authentication data, or external service data.

Windows profile paths written as event properties are masked with `%USERPROFILE%`. Diagnostic Snapshot environment data can contain the active diagnostic path.

## Marker

Use `Add Diagnostic Marker` immediately before and after reproducing a problem. Notes are optional, limited to 200 characters, and control characters are removed.

## Troubleshooting Capture

`Begin Troubleshooting Capture` remembers the persistent mode and temporarily starts Full logging without saving Full to the configuration. `End Troubleshooting Capture` writes the end event, creates an expanded snapshot, then restores the previous mode.

## Snapshot

An expanded snapshot contains:

```text
diagnostics-<utc timestamp>/
    latest.jsonl
    recent-context.jsonl
    environment.json
    configuration.redacted.json
    summary.txt
```

If a snapshot exists, Codex should read `summary.txt`, `latest.jsonl`, and `recent-context.jsonl` in that order.

## Retention

Defaults:

* maximum file part: 10 MB
* retained sessions: 10
* retention period: 14 days
* maximum diagnostic log size: 100 MB
* writer queue: 4096 events
* recent context ring: 500 events in Full mode

The current session is never removed by retention. When the queue is full, the game thread does not wait; new diagnostic entries are dropped and counted.

## Reading Logs

Errors:

```powershell
Get-Content .\ActorMorpherDiagnostics\latest.jsonl |
    Select-String '"level":"Error"'
```

Operation:

```powershell
Get-Content .\ActorMorpherDiagnostics\latest.jsonl |
    Select-String 'redraw-000017'
```

## Known Limitations

Appearance and outfit writes are currently disabled, so successful Morph, Restore, Bulk Outfit, and Unequip events cannot yet be produced. Their rejection and safety paths are logged. Game-side timing, file permission failures, Dev Mirror behavior, and plugin unload flushing still require FF14 validation.
