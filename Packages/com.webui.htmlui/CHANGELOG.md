# Changelog

## Unreleased

### Added
- Editor preview: documents render and respond in the Game view during play mode, backed by a real Chrome driven
  over the DevTools Protocol (`WebUI.Html.Editor.Cdp`). One browser target per document, screencast frames
  premultiplied into a `RenderTexture`, pointer input projected through the document's pixel-to-clip matrix.
- `IHtmlBackend` / `HtmlBackend`: a registration point for bridges other than `HtmlUI.jslib`. The Editor stubs in
  `HtmlNative` forward to the registered backend, so nothing above the bridge changed.
- Element API in the preview: `Q`, `QAll` and every `HtmlElement` operation. Handles are resolved per operation
  from a selector description, writes are batched once per document per frame, reads are a blocking round trip.
- **Window > HTML UI** menu for the preview (enable, headless, console logging, frame orientation, restart).

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
