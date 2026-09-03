using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace WebUI.Html.Editor.Cdp
{
    /// <summary>
    /// An <see cref="IHtmlBackend"/> that renders documents in a real Chrome driven over the DevTools Protocol,
    /// so HTML UIs show up and respond in the Editor's Game view.
    /// </summary>
    /// <remarks>
    /// One browser target per document: the target's viewport is the document, DevTools screencasts it, and each
    /// frame is decoded into a <see cref="RenderTexture"/> that matches what <c>HtmlUI.jslib</c> would have produced
    /// (premultiplied, first row at the top of the page). Pointer input is projected back through the document's
    /// pixel-to-clip matrix, so both screen and world surfaces are clickable.
    /// <para>
    /// This is a preview, not the real thing: accessibility, IME and HTML-in-Canvas compositing only exist in a
    /// web build. Layout, styling, script behaviour and event payloads are genuine, because it is genuinely Chrome.
    /// </para>
    /// </remarks>
    internal sealed class CdpHtmlBackend : IHtmlBackend
    {
        private const string BlitShaderName = "Hidden/HtmlUI/EditorPremultiply";
        private const double CaptureFallbackDelay = 2.5;   // seconds without a screencast frame before polling instead
        private const double CapturePollInterval = 0.05;

        private sealed class Panel
        {
            public int Id;
            public string TargetId;
            public string SessionId;

            public int Width = 1, Height = 1;      // CSS pixels
            public float Scale = 1f;               // extra device pixel ratio
            public bool Visible = true;
            public readonly HashSet<string> Listened = new HashSet<string>();
            public string Html = string.Empty;
            public string Css = string.Empty;

            public bool Ready;                     // bridge injected; commands can be sent directly
            public double ReadyTime;
            public bool Screencasting;

            public byte[] PendingFrame;            // most recent encoded frame, consumed on the main thread
            public Texture2D Staging;
            public RenderTexture Target;
            public int TextureWidth, TextureHeight;

            public Matrix4x4 PixelToClip = Matrix4x4.identity;
            public bool HasGeometry;
            public bool PointerDown;
            public bool PointerInside;
            public Vector2 LastPointer;

            public bool UseCapturePolling;
            public bool CaptureInFlight;
            public double LastCaptureRequest;
            public double LastFrameTime;
        }

        private readonly Dictionary<int, Panel> _panels = new Dictionary<int, Panel>();
        private readonly Dictionary<string, Panel> _bySession = new Dictionary<string, Panel>();
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly List<Panel> _iteration = new List<Panel>();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

        private ChromeLauncher _chrome;
        private CdpClient _client;
        private Material _blit;
        private int _nextPanelId = 1;
        private bool _starting;
        private bool _failed;
        private readonly bool _headless;
        private readonly bool _debug;

        /// <summary>True once Chrome is up and the protocol connection is live.</summary>
        public bool Connected => _client != null && _client.IsOpen;
        public bool Failed => _failed;
        public string Status { get; private set; } = "not started";

        public CdpHtmlBackend(bool headless, bool debugLogging)
        {
            _headless = headless;
            _debug = debugLogging;
        }

        // ------------------------------------------------------------------ lifecycle

        public int Init(int backend, int linear, int forceOverlay, int debug)
        {
            StartBrowser();
            // Documents behave as if HTML-in-Canvas were available: they get a texture to sample.
            return (int)HtmlRenderMode.Texture;
        }

        private void StartBrowser()
        {
            if (_starting || _failed) return;
            _starting = true;
            Status = "starting Chrome";
            _ = StartBrowserAsync();
        }

        private async Task StartBrowserAsync()
        {
            try
            {
                var chrome = new ChromeLauncher();
                await chrome.LaunchAsync(_headless, _debug, _cancel.Token).ConfigureAwait(false);
                var client = await CdpClient.ConnectAsync(chrome.BrowserWebSocketUrl, _cancel.Token).ConfigureAwait(false);

                Post(() =>
                {
                    if (_cancel.IsCancellationRequested) { client.Dispose(); chrome.Dispose(); return; }
                    _chrome = chrome;
                    _client = client;
                    Status = "connected (" + System.IO.Path.GetFileName(chrome.ExecutablePath) + ")";
                    Debug.Log($"[HtmlUI] Editor preview connected to {chrome.ExecutablePath}");
                    foreach (var panel in _panels.Values) BeginPanelSetup(panel);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Post(() =>
                {
                    _failed = true;
                    Status = "failed: " + e.Message;
                    Debug.LogWarning($"[HtmlUI] Editor preview unavailable: {e.Message}");
                });
            }
        }

        public void Shutdown()
        {
            try { _cancel.Cancel(); } catch { }

            foreach (var panel in _panels.Values) ReleaseTextures(panel);
            _panels.Clear();
            _bySession.Clear();
            _pendingOps.Clear();
            _handles.Clear();
            _handleAge.Clear();

            if (_blit != null) { UnityEngine.Object.DestroyImmediate(_blit); _blit = null; }
            _client?.Dispose();
            _client = null;
            _chrome?.Dispose();
            _chrome = null;
            Status = "stopped";
            // The token source is deliberately not disposed: setup tasks may still be holding its token.
        }

        private void Post(Action action) => _mainThread.Enqueue(action);

        // ------------------------------------------------------------------ panels

        public int PanelCreate(int width, int height)
        {
            var panel = new Panel
            {
                Id = _nextPanelId++,
                Width = Mathf.Max(1, width),
                Height = Mathf.Max(1, height),
            };
            _panels[panel.Id] = panel;
            if (Connected) BeginPanelSetup(panel);
            return panel.Id;
        }

        public void PanelDestroy(int id)
        {
            if (!_panels.TryGetValue(id, out var panel)) return;
            _panels.Remove(id);
            _pendingOps.Remove(id);
            if (panel.SessionId != null) _bySession.Remove(panel.SessionId);
            ReleaseTextures(panel);

            if (Connected && panel.TargetId != null)
                _client.Send("Target.closeTarget", "{\"targetId\":" + Json.Quote(panel.TargetId) + "}");
        }

        /// <summary>Creates the browser target for a panel and injects the bridge. Runs off the main thread.</summary>
        private void BeginPanelSetup(Panel panel)
        {
            if (panel.TargetId != null || !Connected) return;
            _ = SetUpPanelAsync(panel);
        }

        private async Task SetUpPanelAsync(Panel panel)
        {
            var client = _client;
            try
            {
                // No width/height here: Chrome rejects those unless a new window is being opened, and the
                // viewport is set properly by Emulation.setDeviceMetricsOverride in FlushPanel anyway.
                var created = await client.SendAsync("Target.createTarget", "{\"url\":\"about:blank\"}").ConfigureAwait(false);
                var targetId = Json.Str(created, "targetId");
                if (string.IsNullOrEmpty(targetId)) throw new CdpException("Target.createTarget returned no targetId");

                var attached = await client.SendAsync("Target.attachToTarget",
                    "{\"targetId\":" + Json.Quote(targetId) + ",\"flatten\":true}").ConfigureAwait(false);
                var sessionId = Json.Str(attached, "sessionId");
                if (string.IsNullOrEmpty(sessionId)) throw new CdpException("Target.attachToTarget returned no sessionId");

                await client.SendAsync("Page.enable", null, sessionId).ConfigureAwait(false);
                await client.SendAsync("Runtime.enable", null, sessionId).ConfigureAwait(false);
                await client.SendAsync("Runtime.addBinding",
                    "{\"name\":" + Json.Quote(CdpBridgeJs.EventBinding) + "}", sessionId).ConfigureAwait(false);
                // A transparent page lets the document composite over the Unity scene like the real thing.
                await client.SendAsync("Emulation.setDefaultBackgroundColorOverride",
                    "{\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0}}", sessionId).ConfigureAwait(false);
                // Re-inject after any navigation the document's own content might trigger.
                await client.SendAsync("Page.addScriptToEvaluateOnNewDocument",
                    "{\"source\":" + Json.Quote(CdpBridgeJs.Source) + "}", sessionId).ConfigureAwait(false);

                await EvaluateAsync(client, sessionId, CdpBridgeJs.Source).ConfigureAwait(false);

                Post(() =>
                {
                    if (!_panels.ContainsKey(panel.Id)) return;   // destroyed while we were setting it up
                    panel.TargetId = targetId;
                    panel.SessionId = sessionId;
                    panel.Ready = true;
                    panel.ReadyTime = NowSeconds();
                    _bySession[sessionId] = panel;
                    FlushPanel(panel);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Post(() => Debug.LogWarning($"[HtmlUI] Editor preview could not create a page for document {panel.Id}: {e.Message}"));
            }
        }

        /// <summary>Pushes the panel's whole authoritative state to the browser. Main thread.</summary>
        private void FlushPanel(Panel panel)
        {
            if (!panel.Ready || !Connected) return;

            ApplyMetrics(panel);
            Eval(panel, "window.__HUI.init(" + panel.Id + ")");
            Eval(panel, "window.__HUI.setCss(" + Json.Quote(panel.Css) + ")");
            Eval(panel, "window.__HUI.setHtml(" + Json.Quote(panel.Html) + ")");
            Eval(panel, "window.__HUI.setVisible(" + (panel.Visible ? "true" : "false") + ")");
            foreach (var type in panel.Listened)
                Eval(panel, "window.__HUI.listen(" + Json.Quote(type) + ",true)");

            StartScreencast(panel);
        }

        private void ApplyMetrics(Panel panel)
        {
            if (!panel.Ready || !Connected) return;
            _client.Send("Emulation.setDeviceMetricsOverride",
                "{\"width\":" + panel.Width +
                ",\"height\":" + panel.Height +
                ",\"deviceScaleFactor\":" + Json.Number(panel.Scale) +
                ",\"mobile\":false}", panel.SessionId);
        }

        private void StartScreencast(Panel panel)
        {
            if (!panel.Ready || !Connected) return;
            panel.Screencasting = true;
            _client.Send("Page.startScreencast",
                "{\"format\":\"png\",\"maxWidth\":" + PixelWidth(panel) +
                ",\"maxHeight\":" + PixelHeight(panel) + ",\"everyNthFrame\":1}", panel.SessionId);
        }

        private int PixelWidth(Panel p) => Mathf.Max(1, Mathf.RoundToInt(p.Width * p.Scale));
        private int PixelHeight(Panel p) => Mathf.Max(1, Mathf.RoundToInt(p.Height * p.Scale));

        private void Eval(Panel panel, string expression)
        {
            if (!panel.Ready || !Connected) return;
            _client.Send("Runtime.evaluate",
                "{\"expression\":" + Json.Quote(expression) + ",\"returnByValue\":true,\"awaitPromise\":false}",
                panel.SessionId);
        }

        private static Task<Dictionary<string, object>> EvaluateAsync(CdpClient client, string sessionId, string expression)
            => client.SendAsync("Runtime.evaluate",
                "{\"expression\":" + Json.Quote(expression) + ",\"returnByValue\":true,\"awaitPromise\":false}", sessionId);

        // ------------------------------------------------------------------ IHtmlBackend content API

        public void PanelSetHtml(int id, string html)
        {
            if (!_panels.TryGetValue(id, out var p)) return;
            p.Html = html ?? string.Empty;
            if (p.Ready) Eval(p, "window.__HUI.setHtml(" + Json.Quote(p.Html) + ")");
        }

        public void PanelSetCss(int id, string css)
        {
            if (!_panels.TryGetValue(id, out var p)) return;
            p.Css = css ?? string.Empty;
            if (p.Ready) Eval(p, "window.__HUI.setCss(" + Json.Quote(p.Css) + ")");
        }

        public void PanelSetSize(int id, int width, int height)
        {
            if (!_panels.TryGetValue(id, out var p)) return;
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (p.Width == width && p.Height == height) return;
            p.Width = width;
            p.Height = height;
            ApplyMetrics(p);
            if (p.Screencasting) StartScreencast(p);   // refresh the cap so the frame is not downscaled
        }

        public void PanelSetVisible(int id, bool visible)
        {
            if (!_panels.TryGetValue(id, out var p)) return;
            p.Visible = visible;
            if (p.Ready) Eval(p, "window.__HUI.setVisible(" + (visible ? "true" : "false") + ")");
        }

        public void PanelSetResolutionScale(int id, float scale)
        {
            if (!_panels.TryGetValue(id, out var p)) return;
            scale = Mathf.Clamp(scale, 0.25f, 4f);
            if (Mathf.Approximately(p.Scale, scale)) return;
            p.Scale = scale;
            ApplyMetrics(p);
            if (p.Screencasting) StartScreencast(p);
        }

        public void PanelListen(int id, string eventType, bool enabled)
        {
            if (!_panels.TryGetValue(id, out var p) || string.IsNullOrEmpty(eventType)) return;
            if (enabled ? !p.Listened.Add(eventType) : !p.Listened.Remove(eventType)) return;
            if (p.Ready) Eval(p, "window.__HUI.listen(" + Json.Quote(eventType) + "," + (enabled ? "true" : "false") + ")");
        }

        public void PanelInvalidate(int id) { /* the browser repaints on its own; nothing to force */ }

        public void PanelAnnounce(int id, string text, bool assertive)
        {
            if (!_panels.TryGetValue(id, out var p) || !p.Ready) return;
            Eval(p, "window.__HUI.announce(" + Json.Quote(text ?? string.Empty) + "," + (assertive ? "true" : "false") + ")");
        }

        public string PanelEval(int id, string javascript)
        {
            if (!_panels.TryGetValue(id, out var p) || !p.Ready || !Connected) return string.Empty;

            // Give the caller the same synchronous contract the jslib has, at the cost of a short block.
            var wrapped = "(function(){var panel=window.__HUI.panel,root=window.__HUI.content,HUI=window.__HUI;return ("
                          + javascript + ");})()";
            var task = EvaluateAsync(_client, p.SessionId, wrapped);
            if (!task.Wait(250)) return string.Empty;

            var result = Json.Dict(task.Result, "result");
            if (result == null) return string.Empty;
            if (result.TryGetValue("value", out var value) && value != null)
                return value as string ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return Json.Str(result, "description", string.Empty);
        }

        public void PanelSetGeometry(int id, float[] columnMajor)
        {
            if (!_panels.TryGetValue(id, out var p) || columnMajor == null || columnMajor.Length < 16) return;
            var m = new Matrix4x4();
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    m[r, c] = columnMajor[c * 4 + r];
            p.PixelToClip = m;
            p.HasGeometry = true;
        }

        public Texture PanelGetTexture(int id) => _panels.TryGetValue(id, out var p) ? p.Target : null;

        public void PanelGetTextureSize(int id, int[] outWidthHeight)
        {
            if (outWidthHeight == null || outWidthHeight.Length < 2) return;
            if (!_panels.TryGetValue(id, out var p))
            {
                outWidthHeight[0] = outWidthHeight[1] = 0;
                return;
            }
            // Before the first frame lands, report the size the browser was asked for.
            outWidthHeight[0] = p.TextureWidth > 0 ? p.TextureWidth : PixelWidth(p);
            outWidthHeight[1] = p.TextureHeight > 0 ? p.TextureHeight : PixelHeight(p);
        }

        // ------------------------------------------------------------------ elements

        /// <summary>
        /// What an element handle actually is: a recipe for finding the element again, not a reference to it.
        /// The browser resolves it per operation, which avoids a round trip on every <c>Q()</c> and keeps the
        /// page from holding references to elements Unity has forgotten about.
        /// </summary>
        private sealed class ElementSpec
        {
            public int PanelId;
            public string Selector;
            public int Index = -1;
            public bool Parent;
            public ElementSpec Of;
        }

        // Handles are never reused by the runtime API, and HtmlElement.Dispose is optional, so the table is
        // bounded and the oldest entries are evicted. Handles are used within a frame of being created.
        private const int MaxHandles = 8192;
        private readonly Dictionary<int, ElementSpec> _handles = new Dictionary<int, ElementSpec>();
        private readonly Queue<int> _handleAge = new Queue<int>();
        private readonly Dictionary<int, StringBuilder> _pendingOps = new Dictionary<int, StringBuilder>();
        private int _nextHandle = 1;

        private int AddHandle(ElementSpec spec)
        {
            int handle = _nextHandle++;
            _handles[handle] = spec;
            _handleAge.Enqueue(handle);
            while (_handleAge.Count > MaxHandles) _handles.Remove(_handleAge.Dequeue());
            return handle;
        }

        private ElementSpec Spec(int handle) => handle != 0 && _handles.TryGetValue(handle, out var s) ? s : null;

        public int Query(int panel, string selector)
        {
            if (!_panels.ContainsKey(panel) || string.IsNullOrEmpty(selector)) return 0;
            return AddHandle(new ElementSpec { PanelId = panel, Selector = selector });
        }

        public string QueryAll(int panel, string selector)
        {
            if (!_panels.ContainsKey(panel) || string.IsNullOrEmpty(selector)) return string.Empty;

            int count = (int)ReadNumber(new ElementSpec { PanelId = panel, Selector = selector }, "count", null, 0);
            if (count <= 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(AddHandle(new ElementSpec { PanelId = panel, Selector = selector, Index = i }));
            }
            return sb.ToString();
        }

        public int ElemQuery(int handle, string selector)
        {
            var spec = Spec(handle);
            if (spec == null || string.IsNullOrEmpty(selector)) return 0;
            return AddHandle(new ElementSpec { PanelId = spec.PanelId, Selector = selector, Of = spec });
        }

        public int ElemParent(int handle)
        {
            var spec = Spec(handle);
            if (spec == null) return 0;
            return AddHandle(new ElementSpec { PanelId = spec.PanelId, Parent = true, Of = spec });
        }

        public void ElemRelease(int handle)
        {
            if (handle != 0) _handles.Remove(handle);
        }

        // ---- writes: queued and flushed once per frame

        public void ElemSetText(int handle, string value) => Write(handle, "text", value);
        public void ElemSetHtml(int handle, string value) => Write(handle, "html", value);
        public void ElemInsertHtml(int handle, string where, string html) => Write(handle, "insert", where, html);
        public void ElemSetAttr(int handle, string name, string value) => Write(handle, "attr", name, value);
        public void ElemRemoveAttr(int handle, string name) => Write(handle, "rmattr", name);
        public void ElemSetProp(int handle, string name, string value) => Write(handle, "prop", name, value);
        public void ElemSetBoolProp(int handle, string name, bool value) => WriteBool(handle, "boolprop", name, value);
        public void ElemSetStyle(int handle, string name, string value) => Write(handle, "style", name, value);
        public void ElemAddClass(int handle, string className) => Write(handle, "addcls", className);
        public void ElemRemoveClass(int handle, string className) => Write(handle, "rmcls", className);
        public void ElemFocus(int handle) => Write(handle, "focus");
        public void ElemBlur(int handle) => Write(handle, "blur");
        public void ElemClick(int handle) => Write(handle, "click");
        public void ElemScrollIntoView(int handle) => Write(handle, "scroll");

        public void ElemToggleClass(int handle, string className, int force)
        {
            var spec = Spec(handle);
            if (spec == null) return;
            var sb = BeginOp(spec, "tglcls");
            if (sb == null) return;
            sb.Append(",\"a\":");
            Json.Quote(className, sb);
            sb.Append(",\"b\":").Append(force).Append('}');
        }

        public void ElemRemove(int handle)
        {
            Write(handle, "remove");
            ElemRelease(handle);
        }

        public void ElemShowModal(int handle, bool show)
        {
            var spec = Spec(handle);
            if (spec == null) return;
            var sb = BeginOp(spec, "modal");
            if (sb == null) return;
            sb.Append(",\"a\":").Append(show ? "true" : "false").Append('}');
        }

        // ---- reads: a blocking round trip, so only where the API cannot avoid one

        public string ElemEnsureId(int handle) => ReadString(Spec(handle), "ensureid", null);
        public string ElemGetText(int handle) => ReadString(Spec(handle), "text", null);
        public string ElemGetHtml(int handle) => ReadString(Spec(handle), "html", null);
        public string ElemGetAttr(int handle, string name) => ReadString(Spec(handle), "attr", name);
        public bool ElemHasAttr(int handle, string name) => ReadBool(Spec(handle), "hasattr", name);
        public string ElemGetProp(int handle, string name) => ReadString(Spec(handle), "prop", name);
        public bool ElemGetBoolProp(int handle, string name) => ReadBool(Spec(handle), "boolprop", name);
        public string ElemGetStyle(int handle, string name) => ReadString(Spec(handle), "style", name);
        public bool ElemHasClass(int handle, string className) => ReadBool(Spec(handle), "hascls", className);
        public bool ElemMatches(int handle, string selector) => ReadBool(Spec(handle), "matches", selector);

        public void ElemGetBounds(int handle, float[] outXYWH)
        {
            if (outXYWH == null || outXYWH.Length < 4) return;
            outXYWH[0] = outXYWH[1] = outXYWH[2] = outXYWH[3] = 0f;

            var text = ReadString(Spec(handle), "bounds", null);
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split(',');
            for (int i = 0; i < 4 && i < parts.Length; i++)
                float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out outXYWH[i]);
        }

        // ---- op plumbing

        private void Write(int handle, string op, string a = null, string b = null)
        {
            var spec = Spec(handle);
            if (spec == null) return;
            var sb = BeginOp(spec, op);
            if (sb == null) return;
            if (a != null)
            {
                sb.Append(",\"a\":");
                Json.Quote(a, sb);
            }
            if (b != null)
            {
                sb.Append(",\"b\":");
                Json.Quote(b, sb);
            }
            sb.Append('}');
        }

        private void WriteBool(int handle, string op, string a, bool b)
        {
            var spec = Spec(handle);
            if (spec == null) return;
            var sb = BeginOp(spec, op);
            if (sb == null) return;
            sb.Append(",\"a\":");
            Json.Quote(a, sb);
            sb.Append(",\"b\":").Append(b ? "true" : "false").Append('}');
        }

        /// <summary>Opens an op object in the panel's pending batch, or returns null if the panel is gone.</summary>
        private StringBuilder BeginOp(ElementSpec spec, string op)
        {
            if (!_panels.TryGetValue(spec.PanelId, out var panel) || !panel.Ready) return null;

            if (!_pendingOps.TryGetValue(spec.PanelId, out var sb))
                _pendingOps[spec.PanelId] = sb = new StringBuilder("[");
            if (sb.Length > 1) sb.Append(',');

            sb.Append("{\"o\":");
            Json.Quote(op, sb);
            sb.Append(",\"h\":");
            AppendSpec(sb, spec);
            return sb;
        }

        private static void AppendSpec(StringBuilder sb, ElementSpec spec)
        {
            sb.Append('{');
            bool first = true;
            if (spec.Selector != null)
            {
                sb.Append("\"s\":");
                Json.Quote(spec.Selector, sb);
                first = false;
            }
            if (spec.Index >= 0)
            {
                if (!first) sb.Append(',');
                sb.Append("\"i\":").Append(spec.Index);
                first = false;
            }
            if (spec.Parent)
            {
                if (!first) sb.Append(',');
                sb.Append("\"up\":true");
                first = false;
            }
            if (spec.Of != null)
            {
                if (!first) sb.Append(',');
                sb.Append("\"p\":");
                AppendSpec(sb, spec.Of);
            }
            sb.Append('}');
        }

        /// <summary>Sends every queued write for a panel. Reads call this first so they see their own writes.</summary>
        private void FlushOps(int panelId)
        {
            if (!_pendingOps.TryGetValue(panelId, out var sb) || sb.Length <= 1) return;
            sb.Append(']');
            var ops = sb.ToString();
            sb.Clear();
            sb.Append('[');

            if (_panels.TryGetValue(panelId, out var panel) && panel.Ready && Connected)
                Eval(panel, "window.__HUI.apply(" + ops + ")");
        }

        private void FlushAllOps()
        {
            foreach (var panelId in _pendingOps.Keys) FlushOps(panelId);
        }

        private Dictionary<string, object> ReadRaw(ElementSpec spec, string op, string argument)
        {
            if (spec == null) return null;
            if (!_panels.TryGetValue(spec.PanelId, out var panel) || !panel.Ready || !Connected) return null;
            FlushOps(spec.PanelId);

            var expression = new StringBuilder("window.__HUI.read(");
            AppendSpec(expression, spec);
            expression.Append(',');
            Json.Quote(op, expression);
            expression.Append(',');
            if (argument == null) expression.Append("null"); else Json.Quote(argument, expression);
            expression.Append(')');

            var task = EvaluateAsync(_client, panel.SessionId, expression.ToString());
            // A short block: the browser is local, so this is a fraction of a millisecond in practice.
            if (!task.Wait(100) || task.IsFaulted) return null;
            return Json.Dict(task.Result, "result");
        }

        private string ReadString(ElementSpec spec, string op, string argument)
        {
            var result = ReadRaw(spec, op, argument);
            if (result == null || !result.TryGetValue("value", out var value) || value == null) return string.Empty;
            return value as string ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private bool ReadBool(ElementSpec spec, string op, string argument)
        {
            var result = ReadRaw(spec, op, argument);
            return result != null && result.TryGetValue("value", out var value) && value is bool b && b;
        }

        private double ReadNumber(ElementSpec spec, string op, string argument, double fallback)
        {
            var result = ReadRaw(spec, op, argument);
            return result != null && result.TryGetValue("value", out var value) && value is double d ? d : fallback;
        }

        public void GetCanvasInfo(float[] outInfo)
        {
            if (outInfo == null || outInfo.Length < 5) return;
            outInfo[0] = Screen.width;
            outInfo[1] = Screen.height;
            outInfo[2] = 1f;
            outInfo[3] = Screen.width;
            outInfo[4] = Screen.height;
        }

        // ------------------------------------------------------------------ per-frame

        public void Update()
        {
            while (_mainThread.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            if (_client == null) return;
            if (_client.Fault != null && !_failed)
            {
                _failed = true;
                Status = "disconnected: " + _client.Fault.Message;
                Debug.LogWarning("[HtmlUI] Editor preview lost its connection to Chrome: " + _client.Fault.Message);
            }

            // Dispatching an event can run user code that destroys a document, so snapshot before iterating.
            while (_client.TryDequeueEvent(out var evt)) HandleEvent(evt);

            // Element writes queued by game code this frame go out as one batch per document.
            FlushAllOps();

            double now = NowSeconds();
            _iteration.Clear();
            _iteration.AddRange(_panels.Values);
            foreach (var panel in _iteration)
            {
                ApplyPendingFrame(panel);
                PumpCaptureFallback(panel, now);
                PumpPointer(panel);
            }
        }

        private void HandleEvent(Dictionary<string, object> evt)
        {
            var method = Json.Str(evt, "method");
            var sessionId = Json.Str(evt, "sessionId");
            var parameters = Json.Dict(evt, "params");

            switch (method)
            {
                case "Page.screencastFrame":
                {
                    if (sessionId == null || !_bySession.TryGetValue(sessionId, out var panel)) return;
                    var data = Json.Str(parameters, "data");
                    if (!string.IsNullOrEmpty(data))
                    {
                        try { panel.PendingFrame = Convert.FromBase64String(data); }
                        catch (FormatException) { /* skip a malformed frame rather than tearing down */ }
                    }
                    // Chrome stalls the screencast until each frame is acknowledged.
                    int ack = (int)Json.Num(parameters, "sessionId");
                    _client.Send("Page.screencastFrameAck", "{\"sessionId\":" + ack + "}", sessionId);
                    break;
                }

                case "Runtime.bindingCalled":
                {
                    if (sessionId == null || !_bySession.TryGetValue(sessionId, out var panel)) return;
                    if (Json.Str(parameters, "name") != CdpBridgeJs.EventBinding) return;
                    var payload = Json.Str(parameters, "payload");
                    if (!string.IsNullOrEmpty(payload)) HtmlBackend.DispatchEvent(panel.Id, payload);
                    break;
                }

                case "Runtime.consoleAPICalled":
                {
                    if (!_debug) return;
                    Debug.Log("[HtmlUI/page] " + Json.Str(parameters, "type") + ": " + DescribeArgs(parameters));
                    break;
                }

                case "Target.detachedFromTarget":
                {
                    var gone = Json.Str(parameters, "sessionId");
                    if (gone != null && _bySession.TryGetValue(gone, out var panel))
                    {
                        panel.Ready = false;
                        panel.Screencasting = false;
                        _bySession.Remove(gone);
                    }
                    break;
                }
            }
        }

        private static string DescribeArgs(Dictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.TryGetValue("args", out var raw) || !(raw is List<object> args))
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var arg in args)
            {
                var a = arg as Dictionary<string, object>;
                if (a == null) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(a.TryGetValue("value", out var v) && v != null ? v.ToString() : Json.Str(a, "description", ""));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ frames -> textures

        private void ApplyPendingFrame(Panel panel)
        {
            var bytes = panel.PendingFrame;
            if (bytes == null) return;
            panel.PendingFrame = null;
            panel.LastFrameTime = NowSeconds();

            if (panel.Staging == null)
            {
                // linear:true keeps LoadImage from tagging the texture sRGB, so the browser's encoded
                // bytes reach the blit untouched and premultiplication happens in the same space the
                // browser would have used.
                panel.Staging = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                {
                    name = "HtmlUI staging",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            if (!panel.Staging.LoadImage(bytes, false)) return;

            EnsureTarget(panel, panel.Staging.width, panel.Staging.height);
            if (panel.Target == null) return;

            var material = BlitMaterialForThisFrame();
            bool previousSrgbWrite = GL.sRGBWrite;
            GL.sRGBWrite = false;   // write the premultiplied encoded values through unchanged
            if (material != null) Graphics.Blit(panel.Staging, panel.Target, material);
            else Graphics.Blit(panel.Staging, panel.Target);
            GL.sRGBWrite = previousSrgbWrite;

            if (panel.Target.useMipMap) panel.Target.GenerateMips();
        }

        private void EnsureTarget(Panel panel, int width, int height)
        {
            if (panel.Target != null && panel.TextureWidth == width && panel.TextureHeight == height) return;

            if (panel.Target != null)
            {
                panel.Target.Release();
                UnityEngine.Object.DestroyImmediate(panel.Target);
            }

            panel.TextureWidth = width;
            panel.TextureHeight = height;
            panel.Target = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_SRGB)
            {
                name = "HtmlUI preview " + panel.Id,
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 16,
                wrapMode = TextureWrapMode.Clamp,
            };
            panel.Target.Create();
        }

        private Material EnsureBlitMaterial()
        {
            if (_blit != null) return _blit;
            var shader = Shader.Find(BlitShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[HtmlUI] {BlitShaderName} is missing; preview colours will not be premultiplied.");
                return null;
            }
            _blit = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return _blit;
        }

        private static readonly int s_FlipY = Shader.PropertyToID("_FlipY");

        private Material BlitMaterialForThisFrame()
        {
            var material = EnsureBlitMaterial();
            // Which way is up depends on the graphics API's texture origin, so it stays user-switchable.
            material?.SetFloat(s_FlipY, HtmlEditorPreview.FlipY ? 1f : 0f);
            return material;
        }

        private static void ReleaseTextures(Panel panel)
        {
            if (panel.Target != null)
            {
                panel.Target.Release();
                UnityEngine.Object.DestroyImmediate(panel.Target);
                panel.Target = null;
            }
            if (panel.Staging != null)
            {
                UnityEngine.Object.DestroyImmediate(panel.Staging);
                panel.Staging = null;
            }
            panel.TextureWidth = panel.TextureHeight = 0;
        }

        /// <summary>
        /// Some Chrome configurations never emit screencast frames (notably when the page is fully occluded).
        /// If nothing has arrived shortly after start-up, fall back to polling Page.captureScreenshot.
        /// </summary>
        private void PumpCaptureFallback(Panel panel, double now)
        {
            if (!panel.Ready || !Connected) return;

            if (!panel.UseCapturePolling)
            {
                if (panel.LastFrameTime > 0 || now - panel.ReadyTime < CaptureFallbackDelay) return;
                panel.UseCapturePolling = true;
                Debug.Log("[HtmlUI] Editor preview: no screencast frames, falling back to screenshot polling.");
                _client.Send("Page.stopScreencast", null, panel.SessionId);
                panel.Screencasting = false;
            }

            if (panel.CaptureInFlight || now - panel.LastCaptureRequest < CapturePollInterval) return;
            panel.CaptureInFlight = true;
            panel.LastCaptureRequest = now;

            var task = _client.SendAsync("Page.captureScreenshot",
                "{\"format\":\"png\",\"captureBeyondViewport\":false,\"fromSurface\":true}", panel.SessionId);
            task.ContinueWith(t =>
            {
                Post(() =>
                {
                    panel.CaptureInFlight = false;
                    if (t.IsFaulted || t.Result == null) return;
                    var data = Json.Str(t.Result, "data");
                    if (string.IsNullOrEmpty(data)) return;
                    try { panel.PendingFrame = Convert.FromBase64String(data); }
                    catch (FormatException) { }
                });
            }, TaskScheduler.Default);
        }

        // ------------------------------------------------------------------ pointer input

        private void PumpPointer(Panel panel)
        {
            if (!panel.Ready || !Connected || !panel.HasGeometry || !panel.Visible) return;
            if (!EditorPointer.TryGetMouse(out var screenPosition, out bool buttonDown)) return;

            bool inside = TryProjectToPanel(panel, screenPosition, out var documentPoint);
            documentPoint.x = Mathf.Clamp(documentPoint.x, 0, panel.Width);
            documentPoint.y = Mathf.Clamp(documentPoint.y, 0, panel.Height);

            // Track the pointer while it is over the panel, and while a press started there.
            if (!inside && !panel.PointerDown)
            {
                if (panel.PointerInside)
                {
                    panel.PointerInside = false;
                    DispatchMouse(panel, "mouseMoved", documentPoint, 0, false);
                }
                return;
            }
            panel.PointerInside = inside;

            if (documentPoint != panel.LastPointer)
            {
                panel.LastPointer = documentPoint;
                DispatchMouse(panel, "mouseMoved", documentPoint, panel.PointerDown ? 1 : 0, panel.PointerDown);
            }

            if (buttonDown && !panel.PointerDown)
            {
                panel.PointerDown = true;
                DispatchMouse(panel, "mousePressed", documentPoint, 1, true);
            }
            else if (!buttonDown && panel.PointerDown)
            {
                panel.PointerDown = false;
                DispatchMouse(panel, "mouseReleased", documentPoint, 0, true);
            }
        }

        private void DispatchMouse(Panel panel, string type, Vector2 point, int buttons, bool leftButton)
        {
            _client.Send("Input.dispatchMouseEvent",
                "{\"type\":" + Json.Quote(type) +
                ",\"x\":" + Json.Number(point.x) +
                ",\"y\":" + Json.Number(point.y) +
                ",\"button\":" + (leftButton ? "\"left\"" : "\"none\"") +
                ",\"buttons\":" + buttons +
                ",\"clickCount\":" + (type == "mousePressed" || type == "mouseReleased" ? 1 : 0) +
                "}", panel.SessionId);
        }

        /// <summary>
        /// Inverts the document's pixel-to-clip matrix for a single screen point. Solving the two projected
        /// equations directly (rather than inverting the matrix) keeps this correct for the perspective
        /// matrices <see cref="HtmlWorldSurface"/> produces.
        /// </summary>
        private static bool TryProjectToPanel(Panel panel, Vector2 screenPosition, out Vector2 documentPoint)
        {
            documentPoint = Vector2.zero;
            float screenWidth = Mathf.Max(1, Screen.width), screenHeight = Mathf.Max(1, Screen.height);
            float ndcX = screenPosition.x / screenWidth * 2f - 1f;
            float ndcY = screenPosition.y / screenHeight * 2f - 1f;

            var m = panel.PixelToClip;
            Vector4 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c3 = m.GetColumn(3);

            // clip = px*c0 + py*c1 + c3, and ndc = clip.xy / clip.w.
            float a11 = c0.x - ndcX * c0.w, a12 = c1.x - ndcX * c1.w, b1 = ndcX * c3.w - c3.x;
            float a21 = c0.y - ndcY * c0.w, a22 = c1.y - ndcY * c1.w, b2 = ndcY * c3.w - c3.y;

            float det = a11 * a22 - a12 * a21;
            if (Mathf.Abs(det) < 1e-12f) return false;

            float px = (b1 * a22 - a12 * b2) / det;
            float py = (a11 * b2 - b1 * a21) / det;
            documentPoint = new Vector2(px, py);
            return px >= 0f && py >= 0f && px <= panel.Width && py <= panel.Height;
        }

        private static double NowSeconds() => UnityEditor.EditorApplication.timeSinceStartup;
    }
}
