using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace HexWorldStudio.Input
{
    /// <summary>
    /// <see cref="ICameraInput"/> implementation backed by the new
    /// Input System package. Mirrors the legacy provider's semantics
    /// exactly so the same camera controller and picking scripts work
    /// without retuning: pan from WASD / arrows / edge-scroll, zoom
    /// from scroll wheel, yaw rotate from middle-mouse drag + Q/E,
    /// tilt from right-mouse drag + R/F.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in the <c>HexWorldStudio.Runtime.InputSystem</c> asmdef,
    /// which is <c>defineConstraints: ["ENABLE_INPUT_SYSTEM"]</c> —
    /// the whole assembly only compiles when the new Input System is
    /// available. <see cref="CameraInputInstaller"/> chooses this
    /// provider over <see cref="LegacyUnityInputProvider"/> whenever
    /// <c>ENABLE_INPUT_SYSTEM</c> is defined.
    /// </para>
    /// <para>
    /// Press-frame semantics for <see cref="PrimaryClickPressed"/> /
    /// <see cref="SecondaryClickPressed"/> use
    /// <c>ButtonControl.wasPressedThisFrame</c>, which matches
    /// <c>UnityEngine.Input.GetMouseButtonDown</c>: <c>true</c> only on
    /// the single frame of transition from up to down. See ADR-0018.
    /// </para>
    /// </remarks>
    public sealed class InputSystemInputProvider : MonoBehaviour, ICameraInput
    {
        [Tooltip("Whether moving the mouse near the screen edge contributes to PanAxis. Default true.")]
        [SerializeField] private bool _edgeScrollEnabled = true;

        [Tooltip("Distance in pixels from the screen edge at which edge-scroll engages.")]
        [SerializeField] private float _edgeScrollThreshold = 20f;

        [Tooltip("Mouse-drag delta multiplier converted into rotate axis input.")]
        [SerializeField] private float _mouseDragRotateSensitivity = 0.25f;

        [Tooltip("Mouse-drag delta multiplier converted into tilt axis input.")]
        [SerializeField] private float _mouseDragTiltSensitivity = 0.25f;

        [Tooltip("Scroll-wheel sensitivity. The new Input System reports scroll deltas in pixels, so the raw value is divided by this constant to match the legacy ~0.1-per-click scale.")]
        [SerializeField] private float _scrollScale = 120f;

        /// <inheritdoc/>
        public Vector2 PanAxis
        {
            get
            {
                Keyboard kb = Keyboard.current;
                Vector2 keyboard = Vector2.zero;
                if (kb != null)
                {
                    float x = AxisFromKeys(kb.dKey, kb.aKey, kb.rightArrowKey, kb.leftArrowKey);
                    float y = AxisFromKeys(kb.wKey, kb.sKey, kb.upArrowKey, kb.downArrowKey);
                    keyboard = new Vector2(x, y);
                }

                Vector2 edge = _edgeScrollEnabled ? ComputeEdgeScroll() : Vector2.zero;

                Vector2 combined = keyboard + edge;
                if (combined.sqrMagnitude > 1f) combined.Normalize();
                return combined;
            }
        }

        /// <inheritdoc/>
        public float ZoomDelta
        {
            get
            {
                Mouse mouse = Mouse.current;
                if (mouse == null) return 0f;
                // Legacy Input returns ±0.1 per click; the new system reports pixels (typically ±120 per click).
                return mouse.scroll.ReadValue().y / _scrollScale;
            }
        }

        /// <inheritdoc/>
        public float RotateAxis
        {
            get
            {
                Keyboard kb = Keyboard.current;
                Mouse mouse = Mouse.current;
                float keyboard = (kb != null) ? AxisFromKeys(kb.eKey, kb.qKey) : 0f;
                float drag = 0f;
                if (mouse != null && mouse.middleButton.isPressed)
                {
                    drag = mouse.delta.ReadValue().x * _mouseDragRotateSensitivity;
                }
                return Mathf.Clamp(keyboard + drag, -1f, 1f);
            }
        }

        /// <inheritdoc/>
        public float TiltAxis
        {
            get
            {
                Keyboard kb = Keyboard.current;
                Mouse mouse = Mouse.current;
                float keyboard = (kb != null) ? AxisFromKeys(kb.rKey, kb.fKey) : 0f;
                float drag = 0f;
                if (mouse != null && mouse.rightButton.isPressed)
                {
                    drag = mouse.delta.ReadValue().y * _mouseDragTiltSensitivity;
                }
                return Mathf.Clamp(keyboard + drag, -1f, 1f);
            }
        }

        /// <inheritdoc/>
        public bool IsMouseInWindow
        {
            get
            {
                Vector2 m = MousePosition;
                return m.x >= 0f && m.y >= 0f && m.x <= Screen.width && m.y <= Screen.height;
            }
        }

        /// <inheritdoc/>
        public Vector2 MousePosition
        {
            get
            {
                Pointer pointer = Pointer.current;
                return pointer != null ? pointer.position.ReadValue() : Vector2.zero;
            }
        }

        /// <inheritdoc/>
        public bool PrimaryClickPressed
        {
            get
            {
                Mouse mouse = Mouse.current;
                return mouse != null && mouse.leftButton.wasPressedThisFrame;
            }
        }

        /// <inheritdoc/>
        public bool SecondaryClickPressed
        {
            get
            {
                Mouse mouse = Mouse.current;
                return mouse != null && mouse.rightButton.wasPressedThisFrame;
            }
        }

        // Reads two pairs of (positive, negative) keys and returns a clamped axis. Each pair contributes ±1 if held.
        private static float AxisFromKeys(ButtonControl positive1, ButtonControl negative1, ButtonControl positive2, ButtonControl negative2)
        {
            float v = 0f;
            if (positive1 != null && positive1.isPressed) v += 1f;
            if (negative1 != null && negative1.isPressed) v -= 1f;
            if (positive2 != null && positive2.isPressed) v += 1f;
            if (negative2 != null && negative2.isPressed) v -= 1f;
            return Mathf.Clamp(v, -1f, 1f);
        }

        private static float AxisFromKeys(ButtonControl positive, ButtonControl negative)
        {
            float v = 0f;
            if (positive != null && positive.isPressed) v += 1f;
            if (negative != null && negative.isPressed) v -= 1f;
            return Mathf.Clamp(v, -1f, 1f);
        }

        private Vector2 ComputeEdgeScroll()
        {
            Vector2 m = MousePosition;
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
