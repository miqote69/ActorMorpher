# Model Preview Research

## Investigated APIs

- Current FFXIVClientStructs exposes `Client::UI::Misc::CharaView`, camera controls, `SetModelData`, and `Render`.
- `RenderTargetManager.GetCharaViewTexture(uint)` exposes a native render texture pointer.
- Documented client object indices are shared by game UI features. Indices 0-7 can all be occupied, including party portrait views.

## Asset resolver status

Phase 1 asset resolution is implemented without allocating native preview objects. Human entries validate their in-memory Customize and Equipment payload. Monster entries resolve IMC, body MDL, and base skeleton paths. Demihuman entries resolve IMC, Head, Body, Hands, Legs, Feet, and base skeleton paths. Every candidate is checked through `IDataManager.FileExists`; lookup failures are contained as missing assets.

The resolver result is shown in Model Details and logged as `AM7101`. It remains independent from whether an appearance can be applied. Monster and Demihuman base skeletons use `b0001`. Demihuman equipment parts are optional individually; a missing part is reported as not used rather than as a required failure. Animation paths are not guessed because no verified general path rule is available for every skeleton family.

## Safety result

The current public Dalamud surface does not provide Actor Morpher with an exclusive CharaView client-object slot or a verified ownership contract for wrapping the native render texture as an ImGui texture. Creating a CharaView anyway could overwrite game UI state; wrapping the pointer with guessed lifetime semantics could use a released texture.

The PoC therefore stops before native allocation, as required by the implementation brief. No screen actor, game UI CharaView, native texture, or render target is modified by the current preview backend.

## Dalamud 15.0.2.2 API audit

The installed `Dalamud.dll` (`15.0.2.2+4a6abae`) was inspected directly after the lifecycle-controller phase. `ITextureProvider` can create owned textures and copy from another `IDalamudTextureWrap`, but it does not expose an API that accepts the game's native `Texture*` or reserves ownership of a CharaView render target. `ConvertToKernelTexture` converts in the opposite direction and does not establish ownership of an existing game texture.

Current FFXIVClientStructs still documents CharaView client object indices 0 through 7 as shared by Character, Inspect, CharaCard, Fashion, RetainerStatus, TryOn, GearSetPreview, Colorant, Banner, and FittingShop UI. BannerParty may use all eight. No reservation or conflict-free allocator is exposed. Glamourer and Ktisis were searched again and do not contain a standalone CharaView renderer that establishes either missing ownership contract.

Actor Morpher therefore continues to reject native preview allocation. A crash log cannot make an ownership race recoverable because a collision can corrupt another game UI object's lifetime before managed diagnostics run.

## Prepared Human input

`HumanPreviewDataBuilder` now validates and snapshots the complete managed input needed by a future CharaView backend: 26 Customize bytes, ten equipment models, three weapon slots, two glasses slots, visibility flags, and visor state. ModelChara ID, source row, Customize, and equipment must agree with the Model Search backing `AppearanceData`. Young NPC Body Type is preserved without normalization.

`ModelPreviewSupportResolver` separates input completeness from backend availability. Human data can report `StaticReady` while remaining blocked by missing CharaView slot/texture ownership. Demihuman and Monster data separately report missing model, missing skeleton, or a ready static asset set blocked only by the unimplemented standalone renderer.

## Conditions to continue

1. A Dalamud API or documented allocator must reserve and release a CharaView client-object slot for a plugin.
2. A Dalamud texture API must explicitly support a CharaView render texture with defined frame-thread and lifetime rules.
3. The backend must prove cleanup on selection changes, window close, logout, territory change, plugin unload, and exceptions.
4. Human preview should pass first before Demihuman or Monster asset-path work begins.
