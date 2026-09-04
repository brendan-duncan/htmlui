using UnityEngine;
using UnityEngine.InputSystem;

namespace Hiccup.Samples
{
    /// <summary>
    /// The mouse on the desk. Press on it and drag to slide it around the pad; where it sits on the pad is where
    /// the cursor sits on the screen, like a tablet. A press and release without moving is a click, and the
    /// scroll wheel scrolls the page. No physics: the desk is a plane and the mouse is a bounds test.
    /// </summary>
    public class DeskMouse : MonoBehaviour
    {
        public Camera Camera;
        public DeskScreenController Screen;
        [Tooltip("Pad extents on the desk: x is world x, y is world z.")]
        public Rect Pad;
        [Tooltip("World y of the pad surface.")]
        public float PadTop;

        private Renderer _renderer;
        private bool _grabbing;
        private bool _moved;
        private Vector3 _grabOffset;
        private Vector3 _restScale;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _restScale = transform.localScale;
        }

        private void Start()
        {
            SendPosition();   // put the cursor where the mouse starts
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || Camera == null) return;
            var ray = Camera.ScreenPointToRay(mouse.position.ReadValue());

            if (!_grabbing && mouse.leftButton.wasPressedThisFrame && TryHitPadPlane(ray, out var hit))
            {
                var b = _renderer.bounds;
                b.Expand(0.03f);   // a small target; be forgiving
                if (b.IntersectRay(ray))
                {
                    _grabbing = true;
                    _moved = false;
                    _grabOffset = transform.position - hit;
                    transform.localScale = new Vector3(_restScale.x, _restScale.y * 0.8f, _restScale.z);   // "pressed"
                    Screen?.Press();
                }
            }

            if (_grabbing)
            {
                if (TryHitPadPlane(ray, out hit))
                {
                    var p = hit + _grabOffset;
                    p.x = Mathf.Clamp(p.x, Pad.xMin, Pad.xMax);
                    p.z = Mathf.Clamp(p.z, Pad.yMin, Pad.yMax);
                    p.y = transform.position.y;
                    if ((p - transform.position).sqrMagnitude > 1e-6f) _moved = true;
                    transform.position = p;
                    SendPosition();
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    _grabbing = false;
                    transform.localScale = _restScale;
                    Screen?.Release(click: !_moved);
                }
            }

            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f) Screen?.Wheel(wheel);
        }

        private void SendPosition()
        {
            if (Screen == null) return;
            var p = transform.position;
            // Far edge of the pad (larger z) is the top of the screen.
            Screen.MoveTo(Mathf.InverseLerp(Pad.xMin, Pad.xMax, p.x), 1f - Mathf.InverseLerp(Pad.yMin, Pad.yMax, p.z));
        }

        private bool TryHitPadPlane(Ray ray, out Vector3 point)
        {
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (PadTop - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            point = ray.GetPoint(t);
            return true;
        }
    }
}
