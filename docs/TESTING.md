# Testing

## Automated

Run:

```powershell
dotnet test tests\ActorMorpher.Tests\ActorMorpher.Tests.csproj
dotnet build ActorMorpher.csproj -c Release
```

The tests cover:

* first-write-wins appearance and outfit state
* revision increments and restore removal
* identity changes across index, Entity ID, and territory
* unique and ambiguous GPose mapping
* Human-only Bulk Outfit target selection
* batch cancellation state
* slot-specific Unequip planning and fail-closed behavior
* redraw success, actor disappearance, and rollback with fake backends
* diagnostic defaults and configuration bounds
* Off mode zero-file behavior and runtime mode switching
* Errors Only filtering and Full JSONL output
* operation completion, failure, abandonment, and elapsed context
* session-salted Actor redaction
* ring buffer ordering, warning suppression, and expanded snapshots

Automated tests do not prove native FF14 memory compatibility or visual correctness.

## Static safety checks

Before release, search for prohibited dependencies and writes:

```powershell
rg -n "Glamourer|Penumbra|IpcSubscriber|ModelCharaId\s*=|ObjectKind\s*=" --glob "*.cs" --glob "*.csproj"
rg -n "Task\.Delay|Address\s*\{|Character\*.*;|GameObject\*.*;" --glob "*.cs"
```

Expected exceptions are documentation text and immediate local pointer casts used for read-only snapshots or validated redraw calls. No pointer may be stored in a field or state record.
