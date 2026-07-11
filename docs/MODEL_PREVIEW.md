# Model Preview

Model Search includes a stable square preview surface and preview state reporting. The backend is intentionally disabled in this build because the current APIs do not provide safe CharaView slot and texture ownership.

The UI and backend are separated through `IModelPreviewBackend`; `MainWindow` never accesses game memory. A future safe backend can replace `UnavailableModelPreviewBackend` without changing model search or actor mutation services.

Model Details now reports preview preparation independently of the rendering backend. Human entries show whether complete in-memory appearance data is available. Monster and Demihuman entries list their resolved game asset paths and whether each file exists. This lets the next backend consume validated inputs instead of constructing paths during rendering.
