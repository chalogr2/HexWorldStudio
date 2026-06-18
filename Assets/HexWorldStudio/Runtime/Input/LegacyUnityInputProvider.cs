using UnityEngine;
#if ENABLE_LEGACY_INPUT_MANAGER
using UnityInput = UnityEngine.Input;
#endif

namespace HexWorldStudio.Input
{
    /// <summary>
    /// Legacy <see cref="ICameraInput"/> implementation backed by the
    /// <c>UnityEngine.Input</c> class. Pan from WASD / arrows /
    /// edge-scroll, zoom from scroll wheel, yaw rotate from
    /// middle-mouse drag + Q/E, tilt from right-mouse drag + R/F.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compiles under any input-handling configuration. Every property
    /// is guarded with <c>#if ENABLE_LEGACY_INPUT_MANAGER</c> so the
    /// provider degrades to a zero / <c>false</c> stub when a project's
    /// <c>Active Input Handling</c> is set to "Input System Package"
    /// only — calling <c>UnityEngine.Input</c> in that mode throws
    /// <c>InvalidOperationException</c>. The matching
    /// <c>InputSystemInputProvider</c> in
    /// <c>Runtime/Input/InputSystem/</c> handles the new-system case;
    /// <see cref="CameraInputInstaller"/> picks between them at
    /// <c>Awake</c>. See ADR-0018.
    /// </para>
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
#if ENABLE_LEGACY_INPUT_MANAGER
                Vector2 keyboard = new Vector2(
                    AxisFromKeys(KeyCode.D, KeyCode.A, KeyCode.RightArrow, KeyCode.LeftArrow),
                    AxisFromKeys(KeyCode.W, KeyCode.S, KeyCode.UpArrow, KeyCode.DownArrow));

                Vector2 edge = _edgeScrollEnabled ? ComputeEdgeScroll() : Vector2.zero;

                Vector2 combined = keyboard + edge;
                if (combined.sqrMagnitude > 1f) combined.Normalize();
                return combined;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <inheritdoc/>
        public float ZoomDelta
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return UnityInput.mouseScrollDelta.y;
#else
                return 0f;
#endif
            }
        }

        /// <inheritdoc/>
        public float RotateAxis
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                float keyboard = AxisFromKeys(KeyCode.E, KeyCode.Q);
                float drag = 0f;
                if (UnityInput.GetMouseButton(2))
                {
                    drag = UnityInput.GetAxis("Mouse X") * _mouseDragRotateSensitivity;
                }
                return Mathf.Clamp(keyboard + drag, -1f, 1f);
#else
                return 0f;
#endif
            }
        }

        /// <inheritdoc/>
        public float TiltAxis
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                float keyboard = AxisFromKeys(KeyCode.R, KeyCode.F);
                float drag = 0f;
                if (UnityInput.GetMouseButton(1))
                {
                    drag = UnityInput.GetAxis("Mouse Y") * _mouseDragTiltSensitivity;
                }
                return Mathf.Clamp(keyboard + drag, -1f, 1f);
#else
                return 0f;
#endif
            }
        }

        /// <inheritdoc/>
        public bool IsMouseInWindow
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                Vector3 m = UnityInput.mousePosition;
                return m.x >= 0f && m.y >= 0f && m.x <= Screen.width && m.y <= Screen.height;
#else
                return false;
#endif
            }
        }

        /// <inheritdoc/>
        public Vector2 MousePosition
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return UnityInput.mousePosition;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <inheritdoc/>
        public bool PrimaryClickPressed
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return UnityInput.GetMouseButtonDown(0);
#else
                return false;
#endif
            }
        }

        /// <inheritdoc/>
        public bool SecondaryClickPressed
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return UnityInput.GetMouseButtonDown(1);
#else
                return false;
#endif
            }
        }

#if ENABLE_LEGACY_INPUT_MANAGER
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
#endif
    }
}
