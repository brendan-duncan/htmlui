using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WebUI.Html
{
    /// <summary>
    /// Raw bindings to HtmlUI.jslib. Only WebGL/WebGPU player builds have a real implementation;
    /// in the Editor and on other platforms every call is a harmless no-op so scenes still load.
    /// </summary>
    internal static class HtmlNative
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void EventCallback(int panel, IntPtr json);

        /// <summary>Reads a UTF8 string returned by the bridge and frees it.</summary>
        public static string TakeString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            try
            {
                return ReadUtf8(ptr);
            }
            finally
            {
                HtmlUI_Free(ptr);
            }
        }

        public static string ReadUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool Available => true;

        [DllImport("__Internal")] public static extern int HtmlUI_Init(int backend, int linear, int forceOverlay, int debug, EventCallback cb);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_GetFeatures();
        [DllImport("__Internal")] public static extern void HtmlUI_GetCanvasInfo(float[] outInfo);
        [DllImport("__Internal")] public static extern void HtmlUI_SetUpdateMode(int mode);
        [DllImport("__Internal")] public static extern void HtmlUI_SetGeometryMode(int mode);
        [DllImport("__Internal")] public static extern void HtmlUI_Update();
        [DllImport("__Internal")] public static extern void HtmlUI_Free(IntPtr ptr);

        [DllImport("__Internal")] public static extern int HtmlUI_PanelCreate(int w, int h);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelDestroy(int id);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetHtml(int id, string html);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetCss(int id, string css);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetSize(int id, int w, int h);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetVisible(int id, int visible);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetPointerMode(int id, int mode);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetBlockInput(int id, int block);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetPremultiplied(int id, int v);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetPreventSubmit(int id, int v);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetResolutionScale(int id, float scale);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetMipmaps(int id, int v);
        [DllImport("__Internal")] public static extern int HtmlUI_PanelTakeUpdated(int id);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelListen(int id, string type, int enabled);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelInvalidate(int id);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelSetGeometry(int id, float[] columnMajor16);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelGetTextureSize(int id, int[] outWH);
        [DllImport("__Internal")] public static extern int HtmlUI_PanelCreateGLTexture(int id);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelBindGPUTexture(int id, IntPtr texturePtr);
        [DllImport("__Internal")] public static extern void HtmlUI_PanelAnnounce(int id, string text, int assertive);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_PanelEval(int id, string code);

        [DllImport("__Internal")] public static extern int HtmlUI_Query(int id, string selector);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_QueryAll(int id, string selector);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemRelease(int h);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_ElemEnsureId(int h);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_ElemGetText(int h);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemSetText(int h, string s);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_ElemGetHtml(int h);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemSetHtml(int h, string s);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemInsertHtml(int h, string where, string s);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_ElemGetAttr(int h, string name);
        [DllImport("__Internal")] public static extern int HtmlUI_ElemHasAttr(int h, string name);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemSetAttr(int h, string name, string value);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemRemoveAttr(int h, string name);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_ElemGetProp(int h, string name);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemSetProp(int h, string name, string value);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemSetBoolProp(int h, string name, int value);
        [DllImport("__Internal")] public static extern int HtmlUI_ElemGetBoolProp(int h, string name);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemSetStyle(int h, string name, string value);
        [DllImport("__Internal")] public static extern IntPtr HtmlUI_ElemGetStyle(int h, string name);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemAddClass(int h, string c);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemRemoveClass(int h, string c);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemToggleClass(int h, string c, int force);
        [DllImport("__Internal")] public static extern int HtmlUI_ElemHasClass(int h, string c);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemFocus(int h);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemBlur(int h);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemClick(int h);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemRemove(int h);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemShowModal(int h, int show);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemGetBounds(int h, float[] outXYWH);
        [DllImport("__Internal")] public static extern int HtmlUI_ElemQuery(int h, string selector);
        [DllImport("__Internal")] public static extern int HtmlUI_ElemParent(int h);
        [DllImport("__Internal")] public static extern int HtmlUI_ElemMatches(int h, string selector);
        [DllImport("__Internal")] public static extern void HtmlUI_ElemScrollIntoView(int h);
#else
        /// <summary>
        /// True once an <see cref="IHtmlBackend"/> is registered (the Editor preview). Without one every call below
        /// is a harmless no-op so scenes still load on platforms that have no bridge at all.
        /// </summary>
        public static bool Available => HtmlBackend.Current != null;

        // ---- Editor / non-web stubs. They forward to the registered backend, or keep just enough
        //      state for the API to stay usable (and testable) when there is none.
        private static int _nextPanel = 1;

        /// <summary>Non-null string returned by an unimplemented backend getter, so callers never see null.</summary>
        private static IntPtr AllocUtf8(string s)
        {
            if (string.IsNullOrEmpty(s)) return IntPtr.Zero;
            var bytes = Encoding.UTF8.GetBytes(s);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            return ptr;
        }

        public static int HtmlUI_Init(int backend, int linear, int forceOverlay, int debug, EventCallback cb)
            => HtmlBackend.Current?.Init(backend, linear, forceOverlay, debug) ?? -1;
        public static IntPtr HtmlUI_GetFeatures() => IntPtr.Zero;
        public static void HtmlUI_GetCanvasInfo(float[] outInfo)
        {
            var b = HtmlBackend.Current;
            if (b != null) { b.GetCanvasInfo(outInfo); return; }
            outInfo[0] = UnityEngine.Screen.width; outInfo[1] = UnityEngine.Screen.height; outInfo[2] = 1f;
            outInfo[3] = UnityEngine.Screen.width; outInfo[4] = UnityEngine.Screen.height;
        }
        public static void HtmlUI_SetUpdateMode(int mode) { }
        public static void HtmlUI_SetGeometryMode(int mode) { }
        public static void HtmlUI_Update() => HtmlBackend.Current?.Update();
        public static void HtmlUI_Free(IntPtr ptr) { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }

        public static int HtmlUI_PanelCreate(int w, int h) => HtmlBackend.Current?.PanelCreate(w, h) ?? _nextPanel++;
        public static void HtmlUI_PanelDestroy(int id) => HtmlBackend.Current?.PanelDestroy(id);
        public static void HtmlUI_PanelSetHtml(int id, string html) => HtmlBackend.Current?.PanelSetHtml(id, html);
        public static void HtmlUI_PanelSetCss(int id, string css) => HtmlBackend.Current?.PanelSetCss(id, css);
        public static void HtmlUI_PanelSetSize(int id, int w, int h) => HtmlBackend.Current?.PanelSetSize(id, w, h);
        public static void HtmlUI_PanelSetVisible(int id, int visible) => HtmlBackend.Current?.PanelSetVisible(id, visible != 0);
        public static void HtmlUI_PanelSetPointerMode(int id, int mode) { }
        public static void HtmlUI_PanelSetBlockInput(int id, int block) { }
        public static void HtmlUI_PanelSetPremultiplied(int id, int v) { }
        public static void HtmlUI_PanelSetPreventSubmit(int id, int v) { }
        public static void HtmlUI_PanelSetResolutionScale(int id, float scale) => HtmlBackend.Current?.PanelSetResolutionScale(id, scale);
        public static void HtmlUI_PanelSetMipmaps(int id, int v) { }
        public static int HtmlUI_PanelTakeUpdated(int id) => 0;
        public static void HtmlUI_PanelListen(int id, string type, int enabled) => HtmlBackend.Current?.PanelListen(id, type, enabled != 0);
        public static void HtmlUI_PanelInvalidate(int id) => HtmlBackend.Current?.PanelInvalidate(id);
        public static void HtmlUI_PanelSetGeometry(int id, float[] columnMajor16) => HtmlBackend.Current?.PanelSetGeometry(id, columnMajor16);
        public static void HtmlUI_PanelGetTextureSize(int id, int[] outWH)
        {
            var b = HtmlBackend.Current;
            if (b != null) { b.PanelGetTextureSize(id, outWH); return; }
            outWH[0] = 0; outWH[1] = 0;
        }
        public static int HtmlUI_PanelCreateGLTexture(int id) => 0;
        public static void HtmlUI_PanelBindGPUTexture(int id, IntPtr texturePtr) { }
        public static void HtmlUI_PanelAnnounce(int id, string text, int assertive) => HtmlBackend.Current?.PanelAnnounce(id, text, assertive != 0);
        public static IntPtr HtmlUI_PanelEval(int id, string code) => AllocUtf8(HtmlBackend.Current?.PanelEval(id, code));

        public static int HtmlUI_Query(int id, string selector) => HtmlBackend.Current?.Query(id, selector) ?? 0;
        public static IntPtr HtmlUI_QueryAll(int id, string selector) => AllocUtf8(HtmlBackend.Current?.QueryAll(id, selector));
        public static void HtmlUI_ElemRelease(int h) => HtmlBackend.Current?.ElemRelease(h);
        public static IntPtr HtmlUI_ElemEnsureId(int h) => AllocUtf8(HtmlBackend.Current?.ElemEnsureId(h));
        public static IntPtr HtmlUI_ElemGetText(int h) => AllocUtf8(HtmlBackend.Current?.ElemGetText(h));
        public static void HtmlUI_ElemSetText(int h, string s) => HtmlBackend.Current?.ElemSetText(h, s);
        public static IntPtr HtmlUI_ElemGetHtml(int h) => AllocUtf8(HtmlBackend.Current?.ElemGetHtml(h));
        public static void HtmlUI_ElemSetHtml(int h, string s) => HtmlBackend.Current?.ElemSetHtml(h, s);
        public static void HtmlUI_ElemInsertHtml(int h, string where, string s) => HtmlBackend.Current?.ElemInsertHtml(h, where, s);
        public static IntPtr HtmlUI_ElemGetAttr(int h, string name) => AllocUtf8(HtmlBackend.Current?.ElemGetAttr(h, name));
        public static int HtmlUI_ElemHasAttr(int h, string name) => (HtmlBackend.Current?.ElemHasAttr(h, name) ?? false) ? 1 : 0;
        public static void HtmlUI_ElemSetAttr(int h, string name, string value) => HtmlBackend.Current?.ElemSetAttr(h, name, value);
        public static void HtmlUI_ElemRemoveAttr(int h, string name) => HtmlBackend.Current?.ElemRemoveAttr(h, name);
        public static IntPtr HtmlUI_ElemGetProp(int h, string name) => AllocUtf8(HtmlBackend.Current?.ElemGetProp(h, name));
        public static void HtmlUI_ElemSetProp(int h, string name, string value) => HtmlBackend.Current?.ElemSetProp(h, name, value);
        public static void HtmlUI_ElemSetBoolProp(int h, string name, int value) => HtmlBackend.Current?.ElemSetBoolProp(h, name, value != 0);
        public static int HtmlUI_ElemGetBoolProp(int h, string name) => (HtmlBackend.Current?.ElemGetBoolProp(h, name) ?? false) ? 1 : 0;
        public static void HtmlUI_ElemSetStyle(int h, string name, string value) => HtmlBackend.Current?.ElemSetStyle(h, name, value);
        public static IntPtr HtmlUI_ElemGetStyle(int h, string name) => AllocUtf8(HtmlBackend.Current?.ElemGetStyle(h, name));
        public static void HtmlUI_ElemAddClass(int h, string c) => HtmlBackend.Current?.ElemAddClass(h, c);
        public static void HtmlUI_ElemRemoveClass(int h, string c) => HtmlBackend.Current?.ElemRemoveClass(h, c);
        public static void HtmlUI_ElemToggleClass(int h, string c, int force) => HtmlBackend.Current?.ElemToggleClass(h, c, force);
        public static int HtmlUI_ElemHasClass(int h, string c) => (HtmlBackend.Current?.ElemHasClass(h, c) ?? false) ? 1 : 0;
        public static void HtmlUI_ElemFocus(int h) => HtmlBackend.Current?.ElemFocus(h);
        public static void HtmlUI_ElemBlur(int h) => HtmlBackend.Current?.ElemBlur(h);
        public static void HtmlUI_ElemClick(int h) => HtmlBackend.Current?.ElemClick(h);
        public static void HtmlUI_ElemRemove(int h) => HtmlBackend.Current?.ElemRemove(h);
        public static void HtmlUI_ElemShowModal(int h, int show) => HtmlBackend.Current?.ElemShowModal(h, show != 0);
        public static void HtmlUI_ElemGetBounds(int h, float[] outXYWH)
        {
            var b = HtmlBackend.Current;
            if (b != null) { b.ElemGetBounds(h, outXYWH); return; }
            outXYWH[0] = outXYWH[1] = outXYWH[2] = outXYWH[3] = 0;
        }
        public static int HtmlUI_ElemQuery(int h, string selector) => HtmlBackend.Current?.ElemQuery(h, selector) ?? 0;
        public static int HtmlUI_ElemParent(int h) => HtmlBackend.Current?.ElemParent(h) ?? 0;
        public static int HtmlUI_ElemMatches(int h, string selector) => (HtmlBackend.Current?.ElemMatches(h, selector) ?? false) ? 1 : 0;
        public static void HtmlUI_ElemScrollIntoView(int h) => HtmlBackend.Current?.ElemScrollIntoView(h);
#endif
    }
}
