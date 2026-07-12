# Model Preview

Model Search includes a stable square preview surface and preview state reporting. The backend is intentionally disabled in this build because the current APIs do not provide safe CharaView slot and texture ownership.

The UI and backend are separated through `IModelPreviewBackend`; `MainWindow` never accesses game memory. `ModelPreviewController` now owns selection and lifetime management around that backend. A future safe renderer can replace `UnavailableModelPreviewBackend` without changing model search or actor mutation services.

Selections are identified by ModelChara row, category, source, and source row rather than Model ID alone. A 200 ms debounce prevents rapidly scrolling the result list from creating every intermediate preview. Each selection has a generation, and superseded generations are released before the newest model is dispatched.

The controller releases its backend when Model Search is not visible, the window stops drawing, the client logs out, the territory changes, or the plugin is disposed. The latest selection is rescheduled after the UI and game session become active again. Backend selection, release, camera reset, and disposal exceptions are contained and written to diagnostics as `AM7102`/`AM7103` events.

Model Details now reports preview preparation independently of the rendering backend. Human entries show whether complete in-memory appearance data is available. Monster and Demihuman entries list their resolved game asset paths and whether each file exists. This lets the next backend consume validated inputs instead of constructing paths during rendering.
