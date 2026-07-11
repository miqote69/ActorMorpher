# Model Preview Research

## Investigated APIs

- Current FFXIVClientStructs exposes `Client::UI::Misc::CharaView`, camera controls, `SetModelData`, and `Render`.
- `RenderTargetManager.GetCharaViewTexture(uint)` exposes a native render texture pointer.
- Documented client object indices are shared by game UI features. Indices 0-7 can all be occupied, including party portrait views.

## Safety result

The current public Dalamud surface does not provide Actor Morpher with an exclusive CharaView client-object slot or a verified ownership contract for wrapping the native render texture as an ImGui texture. Creating a CharaView anyway could overwrite game UI state; wrapping the pointer with guessed lifetime semantics could use a released texture.

The PoC therefore stops before native allocation, as required by the implementation brief. No screen actor, game UI CharaView, native texture, or render target is modified by the current preview backend.

## Conditions to continue

1. A Dalamud API or documented allocator must reserve and release a CharaView client-object slot for a plugin.
2. A Dalamud texture API must explicitly support a CharaView render texture with defined frame-thread and lifetime rules.
3. The backend must prove cleanup on selection changes, window close, logout, territory change, plugin unload, and exceptions.
4. Human preview should pass first before Demihuman or Monster asset-path work begins.
