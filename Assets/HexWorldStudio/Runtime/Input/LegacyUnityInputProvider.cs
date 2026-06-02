using UnityEngine;
using UnityInput = UnityEngine.Input;

namespace HexWorldStudio.Input
{
    /// <summary>
    /// Default <see cref="ICameraInput"/> implementation backed by the
    /// legacy <c>UnityEngine.Input</c> class. Pan from WASD / arrows /
    /// edge-scroll, zoom from scroll wheel, yaw rotate from
    /// middle-mouse drag + Q/E, tilt from right-mouse drag + R/F.
    /// </summary>
    /// <remarks>
    /// Lives in <c>Runtime/Input/</c> so buyers can extract it as a
    /// standalone input feature. The new Input System variant arrives
    /// later per the v1.1 stretch goal in <c>FEATURES.md</c>.
    /// </remarks>
    public sealed class LegacyUnityInputProvider : MonoBehaviour, ICameraInput
    {
        [Tooltip("Whether moving the mouse near the screen edge contributes to PanAxis. Default true.")]
        [SerializeField] private bool _edgeScrollEnabled = true;

        [Tooltip("Distance in pixels from the screen edge at which edge-scroll engages.")]
        [SerializeField] private float _edgeScrollThreshold = 20f;

        [Tooltip("Mouse-drag delta multiplier converted into rotate axis input.")]
        [SerializeField] private float _mouseDragRotateSensitivity = 0.25f;

        [Tooltip("Mouse-drag delta multiplier converted into tilt axis input.")]
        [SerializeField] private float _mouseDragTiltSensitivity = 0.25f;

        /// <inheritdoc/>
        public Vector2 PanAxis
        {
            get
            {
                Vector2 keyboard = new Vector2(
                    AxisFromKeys(KeyCode.D, KeyCode.A, KeyCode.RightArrow, KeyCode.LeftArrow),
                    AxisFromKeys(KeyCode.W, KeyCode.S, KeyCode.UpArrow, KeyCode.DownArrow));

                Vector2 edge = _edgeScrollEnabled ? ComputeEdgeScroll() : Vector2.zero;

                Vector2 combined = keyboard + edge;
                // Clamp to unit length so simultaneous keyboard + edge
                // scroll doesn't double pan speed.
                if (combined.sqrMagnitude > 1f) combined.Normalize();
                return combined;
            }
        }

        /// <inheritdoc/>
        public float ZoomDelta => UnityInput.mouseScrollDelta.y;

        /// <inheritdoc/>
        public float RotateAxis
        {
            get
            {
                float keyboard = AxisFromKeys(KeyCode.E, KeyCode.Q);
                float drag = 0f;
                if (UnityInput.GetMouseButton(2))
                {
                    drag = UnityInput.GetAxis("Mouse X") * _mouseDragRotateSensitivity;
                }
                return Mathf.Clamp(keyboard + drag, -1f, 1f);
            }
        }

        /// <inheritdoc/>
        public float TiltAxis
        {
            get
            {
                float keyboard = AxisFromKeys(KeyCode.R, KeyCode.F);
                float drag = 0f;
                if (UnityInput.GetMouseButton(1))
                {
                    drag = UnityInput.GetAxis("Mouse Y") * _mouseDragTiltSensitivity;
                }
                return Mathf.Clamp(keyboard + drag, -1f, 1f);
            }
        }

        /// <inheritdoc/>
        public bool IsMouseInWindow
        {
            get
            {
                Vector3 m = UnityInput.mousePosition;
                return m.x >= 0f && m.y >= 0f && m.x <= Screen.width && m.y <= Screen.height;
            }
        }

        /// <inheritdoc/>
        public Vector2 MousePosition => UnityInput.mousePosition;

        /// <inheritdoc/>
        public bool PrimaryClickPressed => UnityInput.GetMouseButtonDown(0);

        /// <inheritdoc/>
        public bool SecondaryClickPressed => UnityInput.GetMouseButtonDown(1);

        // Returns +1 if any "positive" key is held, -1 if any "negative" key is held, 0 otherwise.
        private static float AxisFromKeys(params KeyCode[] keys)
        {
            // Even-indexed keys are "positive", odd-indexed are "negative" (alternating pairs).
            float val = 0f;
            for (int i = 0; i < keys.Length; i++)
            {
                if (UnityInput.GetKey(keys[i]))
                {
                    val += (i % 2 == 0) ? 1f : -1f;
                }
            }
            return Mathf.Clamp(val, -1f, 1f);
        }

        private Vector2 ComputeEdgeScroll()
        {
            Vector3 m = UnityInput.mousePosition;
            if (m.x < 0 || m.y < 0 || m.x > Screen.width || m.y > Screen.height) return Vector2.zero;

            float ex = 0f, ey = 0f;
            if (m.x < _edgeScrollThreshold) ex = -1f;
            else if (m.x > Screen.width - _edgeScrollThreshold) ex = +1f;
            if (m.y < _edgeScrollThreshold) ey = -1f;
            else if (m.y > Screen.height - _edgeScrollThreshold) ey = +1f;

            return new Vector2(ex, ey);
        }
    }
}
