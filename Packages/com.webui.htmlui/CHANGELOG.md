# Changelog

## [0.1.0] - 2026-09-01

### Added
- `HtmlDocument`, `HtmlElement`, `HtmlEvent`, `HtmlRuntime` runtime API.
- `HtmlScreenSurface` (uGUI RawImage) and `HtmlWorldSurface` (mesh) presenters with geometry sync.
- HTML-in-Canvas bridge (`HtmlUI.jslib`) for WebGL2 (`texElementSubImage2D` / `texElementImage2D`) and WebGPU
  (`drawElementImageToTexture` / `copyElementImageToTexture`), paint-event driven updates, DOM overlay fallback.
- Premultiplied-alpha shaders for uGUI and unlit world surfaces.
- `.css` ScriptedImporter, HtmlDocument inspector, build-time reminders.
- `HtmlUI` WebGL template with Origin Trial placeholder.
- Full UI Sample ("Orbital Salvage").
