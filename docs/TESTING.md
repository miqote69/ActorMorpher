# Testing

## Automated

Test selection is governed by `TEST_PLAN.md` and `TEST_SPECIFICATION.md`. Do not run the entire suite or Release build by default. For the current appearance repair, run only the focused cases whose result can change acceptance, followed by one Debug build because the native plugin artifact must compile:

```powershell
dotnet test tests\ActorMorpher.Tests\ActorMorpher.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeDrawObjectInjectorTests|FullyQualifiedName~ApplyServicesTests|FullyQualifiedName~RedrawCoordinatorTests"
dotnet build ActorMorpher.csproj -c Debug --no-restore
```

These checks protect only:

* one synchronous Apply transaction and terminal result
* one Human post-Create call for each equipment slot `0..9`, with no non-Human/failed-Create call or retry
* rejection of a second pending Apply without cancelling the first
* no pin/Bulk mutation on queued, rejected, failed, or succeeded Apply
* failed Apply does not publish current C

Automated tests and a successful build do not prove native FF14 memory compatibility, the payload-free Restore redraw's runtime result, or visual correctness. Those remain `UNEXECUTED` until separately performed in the game.

## Static safety checks

Before release, search for prohibited dependencies and writes:

```powershell
rg -n "Glamourer|Penumbra|IpcSubscriber|ModelCharaId\s*=|ObjectKind\s*=" --glob "*.cs" --glob "*.csproj"
rg -n "Task\.Delay|Address\s*\{|Character\*.*;|GameObject\*.*;" --glob "*.cs"
```

Expected exceptions are documentation text, the validated ModelChara assignment in `NativeAppearanceMemory`, and immediate local pointer casts used for snapshots or current FFXIVClientStructs member calls. No pointer may be stored in a field or state record.
