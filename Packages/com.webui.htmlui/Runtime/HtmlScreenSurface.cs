using UnityEngine;
using UnityEngine.UI;

namespace WebUI.Html
{
    /// <summary>
    /// Draws an <see cref="HtmlDocument"/> through a uGUI RawImage and keeps the browser-side geometry in sync with the
    /// RectTransform, so DOM hit testing, focus rings and screen-reader bounds line up with what is on screen.
    /// </summary>
    [AddComponentMenu("HTML UI/HTML Screen Surface")]
    [RequireComponent(typeof(RawImage))]
    [ExecuteAlways]
    public class HtmlScreenSurface : MonoBehaviour
    {
        [SerializeField] private HtmlDocument document;
        [Tooltip("Resize the document (in CSS pixels) to match this RectTransform every frame.")]
        [SerializeField] private bool sizeDocumentToRect = true;
        [Tooltip("Camera used by the parent Canvas when it is not Screen Space - Overlay. Leave empty to use the Canvas' world camera.")]
        [SerializeField] private Camera uiCamera;

        private RawImage _rawImage;
        private RectTransform _rect;
        private Canvas _canvas;
        private Material _material;
        private readonly Vector3[] _corners = new Vector3[4];

        public HtmlDocument Document
        {
            get => document;
            set => document = value;
        }

        private void OnEnable()
        {
            _rawImage = GetComponent<RawImage>();
            _rect = GetComponent<RectTransform>();
            _rawImage.raycastTarget = false;
            if (document == null) document = GetComponent<HtmlDocument>();
            if (Application.isPlaying) EnsureMaterial();
        }

        private void OnDisable()
        {
            if (_material != null)
            {
                if (_rawImage != null && _rawImage.material == _material) _rawImage.material = null;
                Destroy(_material);
                _material = null;
            }
        }

        private void EnsureMaterial()
        {
            if (_material != null || document == null) return;
            var shader = Shader.Find("HtmlUI/UI Premultiplied");
            if (shader == null || !document.PremultipliedAlpha) return;
            _material = new Material(shader) { name = "HtmlUI UI Premultiplied (instance)", hideFlags = HideFlags.HideAndDontSave };
            _rawImage.material = _material;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || document == null) return;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (!document.IsCreated) return;
            EnsureMaterial();

            var runtime = HtmlRuntime.Instance;
            var cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? (_canvas.worldCamera != null ? _canvas.worldCamera : uiCamera) : null;

            // RectTransform corners in screen pixels: 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right.
            _rect.GetWorldCorners(_corners);
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
            Vector2 tl = RectTransformUtility.WorldToScreenPoint(cam, _corners[1]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);

            if (sizeDocumentToRect)
            {
                float css = runtime.CssPerScreenPixel;
                int w = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(tl, tr) * css));
                int h = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(tl, bl) * css));
                if (w != document.Size.x || h != document.Size.y) document.SetSize(w, h);
            }

            // Screen pixels -> Unity clip space (y up).
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            Vector2 ndcTL = new Vector2(tl.x / sw * 2f - 1f, tl.y / sh * 2f - 1f);
            Vector2 ndcTR = new Vector2(tr.x / sw * 2f - 1f, tr.y / sh * 2f - 1f);
            Vector2 ndcBL = new Vector2(bl.x / sw * 2f - 1f, bl.y / sh * 2f - 1f);

            var docSize = document.Size;
            Vector2 dx = (ndcTR - ndcTL) / docSize.x;   // one CSS pixel to the right
            Vector2 dy = (ndcBL - ndcTL) / docSize.y;   // one CSS pixel down

            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(dx.x, dx.y, 0f, 0f));
            m.SetColumn(1, new Vector4(dy.x, dy.y, 0f, 0f));
            m.SetColumn(2, new Vector4(0f, 0f, 1f, 0f));
            m.SetColumn(3, new Vector4(ndcTL.x, ndcTL.y, 0f, 1f));
            document.SetGeometry(m);

            var tex = document.Texture;
            _rawImage.texture = tex;
            _rawImage.uvRect = document.TextureIsTopDown ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
            _rawImage.enabled = tex != null && document.Visible;
        }
    }
}
