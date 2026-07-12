# Model Preview Research

## Investigated APIs

- Current FFXIVClientStructs exposes `Client::UI::Misc::CharaView`, camera controls, `SetModelData`, and `Render`.
- `RenderTargetManager.GetCharaViewTexture(uint)` exposes a native render texture pointer.
- Documented client object indices are shared by game UI features. Indices 0-7 can all be occupied, including party portrait views.

## Asset resolver status

Asset resolution is implemented without allocating native preview objects. Human entries validate their in-memory Customize and Equipment payload, then resolve face, hair, body, equipment, accessory, and skeleton candidates with body-type and race/gender fallbacks. Monster entries resolve IMC, body MDL, and base skeleton paths. Demihuman entries resolve IMC, Head, Body, Hands, Legs, Feet, and base skeleton paths. Every candidate is checked through `IDataManager.FileExists`; lookup failures are contained as missing assets.

The resolver result is shown in Model Details and logged as `AM7101`. It remains independent from whether an appearance can be applied. Monster and Demihuman base skeletons use `b0001`. Demihuman equipment parts are optional individually; a missing part is reported as not used rather than as a required failure. Animation paths are not guessed because no verified general path rule is available for every skeleton family.

## Safety result

The current public Dalamud surface does not provide Actor Morpher with an exclusive CharaView client-object slot or a verified ownership contract for wrapping the native render texture as an ImGui texture. Creating a CharaView anyway could overwrite game UI state; wrapping the pointer with guessed lifetime semantics could use a released texture.

The PoC therefore stops before native allocation, as required by the implementation brief. No screen actor, game UI CharaView, native texture, or render target is modified by the current preview backend.

## Dalamud 15.0.2.2 API audit

The installed `Dalamud.dll` (`15.0.2.2+4a6abae`) was inspected directly after the lifecycle-controller phase. `ITextureProvider` can create owned textures and copy from another `IDalamudTextureWrap`, but it does not expose an API that accepts the game's native `Texture*` or reserves ownership of a CharaView render target. `ConvertToKernelTexture` converts in the opposite direction and does not establish ownership of an existing game texture.

Current FFXIVClientStructs still documents CharaView client object indices 0 through 7 as shared by Character, Inspect, CharaCard, Fashion, RetainerStatus, TryOn, GearSetPreview, Colorant, Banner, and FittingShop UI. BannerParty may use all eight. No reservation or conflict-free allocator is exposed. Glamourer and Ktisis were searched again and do not contain a standalone CharaView renderer that establishes either missing ownership contract.

Actor Morpher therefore continues to reject native Human preview allocation. A crash log cannot make an ownership race recoverable because a collision can corrupt another game UI object's lifetime before managed diagnostics run. Human now uses the same managed static geometry renderer as Demihuman and Monster instead.

## Prepared Human input

`HumanPreviewDataBuilder` now validates and snapshots the complete managed input needed by a future CharaView backend: 26 Customize bytes, ten equipment models, three weapon slots, two glasses slots, visibility flags, and visor state. ModelChara ID, source row, Customize, and equipment must agree with the Model Search backing `AppearanceData`. Young NPC Body Type is preserved without normalization.

`ModelPreviewSupportResolver` separates input completeness from backend availability. Human, Demihuman, and Monster data report missing models precisely and permit static software preview whenever at least one valid MDL part is available; a skeleton is not required because this backend does not animate.

## Static geometry extraction

The current game MDL files use version `0x01000006`. The Lumina parser bundled with the tested Dalamud build throws while reading later V6 runtime sections even when the file path and geometry buffers are valid. This caused most Demihuman and Monster entries to report no geometry.

`MdlPreviewParser` now reads the decompressed bytes returned by `IDataManager.GetFile(path).Data`. It supports MDL V5 and V6 but intentionally parses only the bounded subset needed for a static preview: file header, fixed vertex declarations, model mesh counts, High LOD range, mesh records, position, UV and color streams, material string references, and 16-bit indices. Every count, offset, stride, allocation, and buffer range is checked before use. It never enters bone tables, shapes, or animation data, which avoids the failing V6 section while keeping the parser small.

The parser was exercised directly against current game data for multiple Demihuman equipment parts, a Monster body, adult Human body/face/hair/equipment, and Young NPC face/hair models. The production `ModelPreviewMeshBuilder` produced non-empty meshes with finite bounds for every present test asset. Missing Young body/equipment paths correctly fall back to the adult race/gender model while retaining the Young face and hair paths.

`ModelPreviewGeometryInspector` contains file-local failures, combines actual decoded vertex bounds across available Demihuman parts, and emits aggregate mesh, skipped mesh, vertex, index, triangle, and LOD counts as diagnostic event `AM7104`. Any skipped mesh marks the result partial. `ModelPreviewCameraFraming` calculates a square-preview target, distance, and near/far planes from those combined bounds. Extraction starts only after the 200 ms preview selection debounce settles, so rapidly scrolling the model list does not parse every intermediate MDL.

`SoftwareModelPreviewBackend` turns those CPU meshes into a bounded static preview. It preserves every non-degenerate High LOD triangle up to a fail-closed 200,000-triangle scene limit, combines valid parts, and publishes only immutable managed scene data. The area threshold retains small face and hair triangles instead of mistaking them for degenerate geometry. `SoftwareModelPreviewProjector` applies bounded yaw, pitch, and zoom, directional brightness, depth sorting, and orthographic projection before `MainWindow` submits textured ImGui triangles.

`LuminaModelGeometrySource` resolves each relative material path with the requested variant and its IMC material ID. `MtrlPreviewParser` reads current legacy and Dawntrail color-table layouts plus sampler bindings. Direct diffuse textures are loaded through Dalamud's game texture provider. Character legacy materials compose their base color from the index texture, color-table rows, optional diffuse, and normal-map opacity. Human hair materials compose the selected NPC's main and highlight colors from `human.cmp` with the hair normal and mask textures. Generated BGRA textures are owned by the plugin and disposed whenever the preview selection changes or the plugin unloads.

Human equipment MDLs are frequently stored only for a shared Hyur family and are reshaped by the client for other races. `MdlPreviewParser` therefore also reads V5/V6 bone tables, blend weights, and 8-bit or 16-bit blend indices. `HumanPbdDeformer` reads `chara/xls/boneDeformer/human.pbd`, follows its racial parent tree, composes the target bone matrices, and applies weighted vertex deformation before bounds and normals are generated. Parts already authored for the target Human code are left unchanged. If a required Human part cannot be deformed, the backend rejects the whole preview rather than displaying detached optional parts. Unequipped or unresolved Body, Hands, Legs, and Feet use the verified `chara/equipment/e0000` bare-part fallback.

Settings can disable the 3D preview entirely. The disabled path clears the selected backend scene and generated texture cache, does not project triangles, and skips geometry inspection while leaving model details and appearance actions available.

The production parser was also exercised across every present model candidate in the current ModelChara data: 305 Demihuman MDLs and 2,377 Monster MDLs parsed successfully. The screenshot comparison models retained all 8,962 Hecteyes triangles, all 3,044 2P body-equipment triangles, all 2,142 2P face triangles, and all 941 2P hair triangles. This verification does not replace an in-game visual check because the software preview intentionally does not execute the game's native shader packages or skeletal animation.

## Conditions for native animated preview

1. A Dalamud API or documented allocator must reserve and release a CharaView client-object slot for a plugin.
2. A Dalamud texture API must explicitly support a CharaView render texture with defined frame-thread and lifetime rules.
3. The backend must prove cleanup on selection changes, window close, logout, territory change, plugin unload, and exceptions.
4. Native preview must remain separate from the managed static software renderer.
