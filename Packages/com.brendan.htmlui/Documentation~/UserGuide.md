# HTML UI — user guide

Build Unity UI for the web out of HTML and CSS, and get the things the web already does well: screen readers,
find-in-page, text selection, IME, native form controls, CSS Grid, web fonts.

This guide is task-oriented. For the mechanism, see [Runtime.md](Runtime.md); for the Editor preview, see
[EditorPreview.md](EditorPreview.md).

## Contents

- [Is this the right tool?](#is-this-the-right-tool)
- [Setup](#setup)
- [Your first HUD](#your-first-hud)
- [Writing the HTML and CSS](#writing-the-html-and-css)
- [Reacting to the UI](#reacting-to-the-ui)
- [Updating the UI](#updating-the-ui)
- [Panels in the 3D scene](#panels-in-the-3d-scene)
- [Input and click-through](#input-and-click-through)
- [Sizing and sharpness](#sizing-and-sharpness)
- [Accessibility](#accessibility)
- [Working in the Editor](#working-in-the-editor)
- [Performance](#performance)
- [Troubleshooting](#troubleshooting)
- [Limitations](#limitations)

## Is this the right tool?

**Use it when** you are shipping to the web and the UI needs to be accessible, searchable, selectable, or
typed into with an IME; when you want to author with CSS Grid, web fonts and media queries; or when you want the
UI to be inspectable in DevTools and testable with web tooling.

**Do not use it when** you ship to platforms other than the web — there is no bridge on desktop, console or
mobile; when the UI has to run in every browser today, since compositing needs Chrome with a flag or an Origin
Trial token (other browsers get a working overlay, but the UI cannot then sit inside the 3D scene); or when the
UI is a small number of simple elements, where UI Toolkit is less machinery.

It composes fine with UI Toolkit and uGUI — they can coexist in the same project and even the same canvas.

## Setup

**Unity** 6000.0 or newer with the Web platform module. WebGL2 and WebGPU are both supported.

**Chrome** 148 or newer, with one of:

* `chrome://flags/#canvas-draw-element` enabled — for local development, or
* an [Origin Trial token](https://developer.chrome.com/origintrials/#/view_trial/3478467762190286849) for your
  origin — for a deployed build.

Other browsers, and Chrome without either, fall back to overlay mode automatically. The UI still works.

**WebGL template.** Set **Project Settings ▸ Player ▸ Resolution and Presentation ▸ WebGL Template** to `HtmlUI`.
It carries a full-window canvas and a placeholder `<meta http-equiv="origin-trial">` tag — paste your token
there. Skipping this is fine for flag-based local testing; the bridge sets the canvas attributes it needs at
runtime either way.

**The sample.** Import **Full UI Sample** from the Package Manager. It is a complete game UI — menu, settings
form with ARIA tabs, inventory listbox, HUD, `<dialog>` modals, toasts, themes, and an interactive console on a
3D quad — and it is the fastest way to see what the package expects of you.

## Your first HUD

Three pieces: a `TextAsset` of HTML, a `TextAsset` of CSS, and a GameObject carrying `HtmlDocument` plus a
surface.

**`Hud.html`** — a body fragment, not a whole document. No `<html>`, `<head>` or `<body>`.

```html
<div class="hud">
  <span class="label">Score</span>
  <output id="score">0</output>
  <button data-action="pause">Pause</button>
</div>
```

**`Hud.css`** — imported as a `TextAsset` by the package's `.css` importer.

```css
.hud {
  position: absolute; inset: 16px auto auto 16px;
  display: flex; gap: 12px; align-items: center;
  font: 600 16px/1 system-ui, sans-serif; color: #e8ecf3;
}
button { font: inherit; padding: 6px 14px; border-radius: 8px; }
```

**In the scene.** Add a UI ▸ Raw Image under a Canvas, then add `HtmlDocument` and `HtmlScreenSurface` to it.
Assign the two TextAssets to the document's **Html** and **Style Sheets** fields.

**In C#:**

```csharp
using HtmlUI;

public class Hud : MonoBehaviour
{
    [SerializeField] HtmlDocument doc;
    int score;

    void OnEnable()
    {
        doc.OnAction("pause", e => Time.timeScale = 0f);
    }

    public void AddScore(int amount)
    {
        score += amount;
        doc.Q("#score").Text = score.ToString();
    }
}
```

Build for Web and open it in Chrome. Press **Tab** — the focus ring appears inside the Unity frame. Press
**Ctrl+F** and search for "Score" — the browser finds it.

To build the same thing from code, deactivate the GameObject while configuring so `OnEnable` sees the finished
setup:

```csharp
var go = new GameObject("HUD", typeof(RectTransform), typeof(RawImage));
go.SetActive(false);
go.transform.SetParent(canvas.transform, false);
var doc = go.AddComponent<HtmlDocument>();
doc.Html = hudHtml;
doc.StyleSheets = new[] { hudCss };
go.AddComponent<HtmlScreenSurface>();
go.SetActive(true);
```

## Writing the HTML and CSS

Your fragment is placed inside this structure, which the package builds:

```html
<div class="hui-panel">          <!-- the panel; sized in CSS pixels -->
  <style>…your CSS…</style>
  <div class="hui-content">      <!-- your fragment goes here -->
  </div>
</div>
```

So `.hui-content` is your root. It is already `width: 100%; height: 100%`, so position things against it
directly.

**What works:** everything Chrome supports. Grid, flexbox, custom properties, container queries, web fonts,
`@media (prefers-reduced-motion)`, `@media (forced-colors)`, `<dialog>`, `<details>`, `<input type=range>`,
emoji, transitions.

**What does not:**

* **`<script>` tags never run.** Content is assigned through `innerHTML`. Use `HtmlDocument.Eval(js)` when you
  genuinely need script — it runs with `panel`, `root` and `HUI` in scope.
* **Cross-origin `<iframe>` content is not drawn.**
* **External resources** load normally, but a slow web font means a frame or two of fallback text. Prefer
  bundling fonts with the build.

**Backgrounds.** The panel is transparent by default, so the scene shows through. Give `.hui-content` or your own
root a background if you want the UI opaque.

**Multiple stylesheets** are concatenated in array order, then `Extra Css` from the inspector is appended last —
handy for per-instance overrides such as a theme variable block.

## Reacting to the UI

Three ways to hook an event, in increasing specificity.

**`data-action`** — the pattern the sample uses throughout, and the one to reach for first. Put the attribute on
a button and route by name; it matches the closest ancestor carrying the attribute, so an icon inside the button
still works.

```html
<button data-action="show" data-screen="settings">Settings</button>
```

```csharp
doc.OnAction("show", e => ShowScreen(e.GetData("screen")));
```

**By element id and type:**

```csharp
doc.On("volume", "input", e => audio.volume = e.ValueAsFloat);
doc.On("player-name", "change", e => profile.Name = e.value);
```

**By type, anywhere in the document:**

```csharp
doc.On("keydown", e => { if (e.IsKey("Escape")) CloseMenu(); });
```

`HtmlEvent` carries `type`, `id`, `tag`, `name`, `action`, `value` (plus `ValueAsFloat` / `ValueAsInt`),
`isChecked`, `key`, `code`, pointer `x`/`y` in panel pixels, `ctrl`/`shift`/`alt`, the ancestor `path`, and
`data-*` attributes via `GetData(name)`. Set `e.Handled = true` to stop further C# dispatch.

Handlers run in order: `EventReceived`, then target-id handlers, then ancestor-id handlers nearest-first, then
`data-action`, then type handlers.

Only `click`, `dblclick`, `input`, `change`, `submit`, `keydown`, `focusin` and `focusout` are forwarded by
default. `On` enables whatever type you ask for; call `doc.Listen("pointerover")` explicitly if you subscribe
through `EventReceived` instead.

## Updating the UI

`Q` and `QAll` take CSS selectors and return `HtmlElement`, which is chainable. A selector that matches nothing
returns a safe no-op object, so null checks are unnecessary.

```csharp
doc.Q("#score").Text = "1370";
doc.Q("#health").SetValue(72).Text = "72%";
doc.Q("#panel").AddClass("visible").RemoveClass("dimmed");
doc.Q("#submit").Disabled = !form.IsValid;
doc.Q("#screen-menu").Hidden = true;
doc.Q("#confirm-dialog").ShowModal();          // <dialog>, with real focus trapping
doc.Q("#toasts").Append("<div class='toast'>Saved</div>");
doc.Q("#old-toast").Remove();

string typed = doc.Q("#search").Value;
bool on     = doc.Q("#bloom").Checked;
Rect where  = doc.Q("#target").Bounds;         // panel CSS pixels
```

Swap the whole document at runtime with `doc.Html = otherTextAsset`, or `doc.SetHtml(string)` /
`doc.SetCss(string)` for generated content. `doc.Reload()` re-applies the serialized assets.

**Dispose handles you create in a loop.** `HtmlElement` is `IDisposable` and each one holds a browser-side slot:

```csharp
foreach (var btn in doc.QAll(".nav-btn"))
{
    btn.SetAttribute("aria-current", btn.GetAttribute("data-screen") == screen);
    btn.Dispose();
}
```

A single `doc.Q("#x").Text = "…"` in an update loop is fine; a hundred undisposed handles per frame is not.

## Panels in the 3D scene

Use `HtmlWorldSurface` instead of `HtmlScreenSurface`: put `HtmlDocument` + `HtmlWorldSurface` on a Quad. The
surface derives the document's screen transform from the camera every frame, so the browser hit-tests the
*projected* DOM — a form on a tilted panel is clickable where you see it, and a screen reader reports the right
bounds.

```csharp
var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
var doc  = quad.AddComponent<HtmlDocument>();
doc.Html = consoleHtml;
doc.ResolutionScale = 2f;                       // supersample; it will be viewed at an angle
var surface = quad.AddComponent<HtmlWorldSurface>();
surface.TargetCamera = Camera.main;             // defaults to Camera.main
```

Set **Pixels Per Unit** above zero on the surface to derive the document size from the mesh bounds instead of
setting it by hand. Leave **Mipmaps** on — world panels are minified and will shimmer without them.

## Input and click-through

**Pointer Mode** decides what the panel captures:

| Mode | Behaviour |
| --- | --- |
| `ChildrenOnly` *(default)* | Only the direct children of your content capture clicks. Clicking empty space reaches Unity — what a HUD wants. |
| `Panel` | The whole rectangle captures input. For a full-screen menu that should block the game. |
| `None` | Display only. |

**Block Unity Input** (on by default) stops events the UI handled from also reaching Unity's input system. Leave
it on unless you specifically want both to see the same click. It is also what makes typing in an `<input>` work
— without it Unity's key handling swallows keystrokes.

**Prevent Form Submit** (on by default) calls `preventDefault()` on `submit` so a form never navigates the page
away from your game. Submit events still reach your handlers.

## Sizing and sharpness

Document **Size** is in **CSS pixels**, not device pixels. The texture is
`size × devicePixelRatio × resolutionScale`.

* `HtmlScreenSurface` with **Size Document To Rect** on (default) resizes the document to the RectTransform every
  frame, so a responsive HUD just works — and CSS media/container queries respond to it.
* `ResolutionScale` supersamples. Leave it at 1 for screen-space UI; use 2 for world panels or anything viewed
  minified or at an angle.
* `Mipmaps` on means trilinear + anisotropic sampling. Keep it for world panels; you can turn it off for a
  pixel-exact full-screen overlay.

## Accessibility

This is the reason the package exists, and most of it is just writing decent HTML. Things worth being deliberate
about:

* **Use real elements.** `<button>`, `<input>`, `<dialog>`, `<output>`, `<fieldset>`. A `<div>` with a click
  handler is invisible to a screen reader and unreachable by keyboard.
* **Label everything.** `<label for>`, or `aria-label` where no visible text exists.
* **Announce state changes** that are not otherwise visible to assistive technology:

  ```csharp
  doc.Announce("Mission complete");                  // polite
  doc.Announce("Shield critical", assertive: true);  // interrupts
  ```

* **Manage focus on navigation.** When you swap screens, move focus to the new heading, as the sample does:

  ```csharp
  doc.Q("#screen-settings h2").SetAttribute("tabindex", "-1").Focus();
  ```

* **`<dialog>` + `ShowModal()`** gives you focus trapping, `Esc` to close and background inertness for free.
  Do not hand-roll a modal.
* **Respect user preferences** in CSS: `@media (prefers-reduced-motion: reduce)` and
  `@media (forced-colors: active)`.

Test with a real screen reader in a real build — NVDA, VoiceOver or ChromeVox. The Editor preview cannot check
any of this.

## Working in the Editor

Play mode renders documents through a real Chrome, so you can iterate without building. Layout, styling, script
behaviour, mouse input and events are all genuine. Toggles live under **Window ▸ HTML UI**.

It cannot show you accessibility, IME, or HTML-in-Canvas compositing, and keyboard input is not yet wired up. It
is a fast iteration loop, not a substitute for testing a build. See [EditorPreview.md](EditorPreview.md).

## Performance

* **Textures update only when the DOM changes.** A static HUD costs nothing per frame. Avoid CSS animations on
  large panels — every frame of the animation is a fresh snapshot and upload.
* **Panel area is what costs.** A full-screen 4K panel at `resolutionScale = 2` is a large upload every time it
  changes. Prefer several small panels over one full-screen one where the layout allows.
* **Batch DOM work.** Setting `InnerHtml` once beats twenty individual mutations.
* **`contain` is already applied** to panels, so a change in one does not re-layout another.
* **Reads are cheaper than writes** in a build (both are direct calls), but in the Editor preview reads are a
  round trip — keep `Value` and `GetAttribute` out of per-frame code and you will be fast in both.

## Troubleshooting

**The UI renders as a DOM overlay instead of inside the 3D scene.** Overlay mode. Check the Chrome flag or your
Origin Trial token, and log `HtmlRuntime.Instance.Features` from the build to see what was detected.

**Nothing renders at all in the build.** Look for `[HtmlUI]` warnings in the browser console. On WebGPU, a
message about resolving the device or texture means the panel cannot be composited — switch to WebGL2, or set
`HtmlRuntime.ForceOverlay = true` before creating documents.

**The panel is visible but clicks do nothing.** Geometry registration failed and the panel fell back to a plain
CSS transform. Check the console for a `getElementTransform` or `updateElementGeometry` warning.

**Typing in a text field does nothing.** Make sure **Block Unity Input** is on.

**Clicks pass through my full-screen menu into the game.** Set **Pointer Mode** to `Panel`.

**The texture only updates sometimes.** Set `HtmlRuntime.UpdateMode = HtmlUpdateMode.EveryFrame`. If that fixes
it, paint events are not firing for that panel — worth reporting, with your Chrome version.

**Colours look wrong or edges are haloed.** `PremultipliedAlpha` and the surface material must agree. Leave the
setting at its default unless you know why you are changing it.

**Everything is blank for the first frame or two.** Normal — there is no snapshot yet. It retries.

Turn on `HtmlRuntime.DebugLogging = true` before creating your first document for a verbose trace.

## Limitations

* **Web builds only.** No bridge exists on desktop, mobile or console.
* **Compositing needs Chrome 148+** with the flag or a token. The API is in origin trial and its signatures have
  changed between versions; the bridge feature-detects each variant it knows about.
* **No `<script>`** in your HTML; use `Eval`.
* **Cross-origin iframes** are not drawn.
* **Overlay mode cannot be occluded** by scene geometry or placed on a mesh — it is drawn above the canvas.
