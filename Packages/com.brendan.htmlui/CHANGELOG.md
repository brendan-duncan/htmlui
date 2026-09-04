# Changelog

## Unreleased

### Added
- Editor preview: documents render and respond in the Game view during play mode, backed by a real Chrome driven
  over the DevTools Protocol (`HtmlUI.Editor.Cdp`). One browser target per document, screencast frames
  premultiplied into a `RenderTexture`, pointer input projected through the document's pixel-to-clip matrix.
- `IHtmlBackend` / `HtmlBackend`: a registration point for bridges other than `HtmlUI.jslib`. The Editor stubs in
  `HtmlNative` forward to the registered backend, so nothing above the bridge changed. `HtmlBackend.SetKeyboardCapture`
  and `DrainKeyPresses` let a backend read the Game view's keys through an IMGUI relay on the runtime object.
- Element API in the preview: `Q`, `QAll` and every `HtmlElement` operation. Handles are resolved per operation
  from a selector description, writes are batched once per document per frame, reads are a blocking round trip.
- **Window > HTML UI** menu for the preview (enable, headless, console logging, frame orientation, restart).
- Editor preview frames are decoded off the main thread: screencast messages are base64-decoded straight from the
  socket bytes, PNGs are decoded by `PngDecoder` on a pool thread with recycled buffers, and the main thread only
  uploads pixels. `Texture2D.LoadImage` remains as the fallback for PNGs outside the RGB/RGBA 8-bit subset.
- Editor preview keyboard input: keys typed in the Game view reach the document the last click landed in, via
  IMGUI events relayed as DevTools key events. Text fields, Enter, Backspace, arrows, Escape and Ctrl shortcuts
  work; IME and composed input do not.

- **Assets ▸ Create ▸ HTML UI ▸ HTML Document** and **Style Sheet**: new `.html` fragments and `.css` style sheets
  from the Project window, with the usual inline rename and a small starter template.
- `.html` and `.css` assets carry their own Project window icons (orange `<>`, blue `{}`). `.html` now goes through
  the package's `HtmlImporter`, selected per asset by an `AssetPostprocessor` because Unity's text importer owns the
  extension by default; the result is still a `TextAsset`.

### Fixed
- Stopping play mode no longer throws `MissingReferenceException` from `HtmlDocument.AfterBridgeUpdate`: a
  document drops its backend-owned texture once the backend that created it has been unregistered.

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
