using UnityEngine;

namespace HtmlUI
{
    /// <summary>
    /// Draws an <see cref="HtmlDocument"/> on a mesh (typically a Quad) in the 3D scene. Every frame the document's
    /// pixel-to-clip transform is derived from the camera and the mesh bounds, so the browser can hit test the
    /// perspective-projected DOM and report correct bounds to assistive technology.
    /// </summary>
    [AddComponentMenu("HTML UI/HTML World Surface")]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class HtmlWorldSurface : MonoBehaviour
    {
        [SerializeField] private HtmlDocument document;
        [Tooltip("Camera the UI is projected for. Defaults to Camera.main.")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("If > 0, the document size (CSS px) is derived from the mesh bounds multiplied by this value.")]
        [SerializeField] private float pixelsPerUnit = 0f;
        [SerializeField] private bool doubleSided = false;

        private MeshRenderer _renderer;
        private MeshFilter _filter;
        private Material _material;
        private static readonly int s_FlipY = Shader.PropertyToID("_FlipY");
        private static readonly int s_Cull = Shader.PropertyToID("_Cull");

        public HtmlDocument Document { get => document; set => document = value; }
        public Camera TargetCamera { get => targetCamera; set => targetCamera = value; }

        private void OnEnable()
        {
            _renderer = GetComponent<MeshRenderer>();
            _filter = GetComponent<MeshFilter>();
            if (document == null) document = GetComponent<HtmlDocument>();
            var shader = Shader.Find("HtmlUI/Unlit Premultiplied");
            if (shader != null)
            {
                _material = new Material(shader) { name = "HtmlUI Unlit Premultiplied (instance)", hideFlags = HideFlags.HideAndDontSave };
                _material.SetFloat(s_Cull, doubleSided ? 0f : 2f);
                _renderer.material = _material;
            }
        }

        private void OnDisable()
        {
            if (_material != null) { Destroy(_material); _material = null; }
        }

        private void LateUpdate()
        {
            if (document == null || !document.IsCreated) return;
            var cam = targetCamera != null ? targetCamera : Camera.main;
            var mesh = _filter.sharedMesh;
            if (cam == null || mesh == null) return;

            var b = mesh.bounds;
            var sizeLocal = b.size;
            if (sizeLocal.x <= 0f || sizeLocal.y <= 0f) return;

            if (pixelsPerUnit > 0f)
            {
                var ls = transform.lossyScale;
                int w = Mathf.Max(1, Mathf.RoundToInt(sizeLocal.x * ls.x * pixelsPerUnit));
                int h = Mathf.Max(1, Mathf.RoundToInt(sizeLocal.y * ls.y * pixelsPerUnit));
                if (w != document.Size.x || h != document.Size.y) document.SetSize(w, h);
            }

            var docSize = document.Size;
            // Document CSS pixel (0,0) = top-left of the front face; (W,H) = bottom-right; the face is at max z.
            var pixelToLocal = Matrix4x4.identity;
            pixelToLocal.SetColumn(0, new Vector4(sizeLocal.x / docSize.x, 0f, 0f, 0f));
            pixelToLocal.SetColumn(1, new Vector4(0f, -sizeLocal.y / docSize.y, 0f, 0f));
            pixelToLocal.SetColumn(2, new Vector4(0f, 0f, 1f, 0f));
            pixelToLocal.SetColumn(3, new Vector4(b.min.x, b.max.y, b.max.z, 1f));

            var clip = cam.projectionMatrix * cam.worldToCameraMatrix * transform.localToWorldMatrix * pixelToLocal;
            document.SetGeometry(clip);

            if (_material != null)
            {
                _material.mainTexture = document.Texture;
                _material.SetFloat(s_FlipY, document.TextureIsTopDown ? 1f : 0f);
            }
            _renderer.enabled = document.Texture != null && document.Visible;
        }
    }
}
