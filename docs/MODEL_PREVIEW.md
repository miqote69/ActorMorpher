# Model Preview

Model Search includes a stable square preview surface and preview state reporting. The backend is intentionally disabled in this build because the current APIs do not provide safe CharaView slot and texture ownership.

The UI and backend are separated through `IModelPreviewBackend`; `MainWindow` never accesses game memory. A future safe backend can replace `UnavailableModelPreviewBackend` without changing model search or actor mutation services.
