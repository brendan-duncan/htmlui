using UnityEditor;
using UnityEngine;

namespace WebUI.Html.Editor
{
    [CustomEditor(typeof(HtmlDocument))]
    [CanEditMultipleObjects]
    public class HtmlDocumentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "HTML documents render in WebGL/WebGPU builds only. In the Editor a placeholder texture marks the surface.\n" +
                    "Chrome 148+ with HTML-in-Canvas (chrome://flags/#canvas-draw-element or an Origin Trial token) composites the DOM into the texture; other browsers get a DOM overlay.",
                    MessageType.Info);
                return;
            }

            var doc = (HtmlDocument)target;
            var mode = doc.RenderMode;
            var runtime = HtmlRuntime.HasInstance ? HtmlRuntime.Instance : null;
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Render mode", mode.ToString());
            EditorGUILayout.LabelField("Created", doc.IsCreated ? "yes" : "no");
            EditorGUILayout.LabelField("Texture", doc.Texture != null ? $"{doc.TextureSize.x} x {doc.TextureSize.y}" : "none");
            if (runtime != null)
            {
                EditorGUILayout.LabelField("Canvas (CSS px)", $"{runtime.CanvasCssSize.x} x {runtime.CanvasCssSize.y}  @ {runtime.DevicePixelRatio}x");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reload content")) doc.Reload();
                if (GUILayout.Button("Invalidate")) doc.Invalidate();
            }
        }
    }
}
