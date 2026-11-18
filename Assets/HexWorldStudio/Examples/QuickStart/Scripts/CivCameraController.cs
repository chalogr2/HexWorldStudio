using HexWorldStudio.Input;
using HexWorldStudio.Rendering;
using UnityEngine;

namespace HexWorldStudio.Examples.QuickStart
{
    /// <summary>
    /// Civ-style camera controller for the QuickStart demo scene.
    /// Pans with WASD / arrows / edge-scroll, zooms with the scroll
    /// wheel, yaws (orbital around the focus point) with middle-mouse
    /// drag and Q/E, tilts with right-mouse drag and R/F.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads input through <see cref="ICameraInput"/> so the camera
    /// controller is agnostic to which input stack the project uses.
    /// The QuickStart scene wires a <see cref="CameraInputInstaller"/>
    /// onto the camera GameObject; the installer picks
    /// <c>InputSystemInputProvider</c> when <c>ENABLE_INPUT_SYSTEM</c>
    /// is defined and falls back to
    /// <see cref="LegacyUnityInputProvider"/> otherwise (ADR-0018).
    /// </para>
    /// <para>
    /// Camera state has three dimensions: a focus point on the ground
    /// plane (the "look-at target"), a height above the ground, and
    /// a yaw / pitch pair controlling orientation around the focus
    /// point. Pan moves the focus point in the camera's local XZ
    /// plane (so panning "right" feels right regardless of yaw).
    /// Zoom changes the height. Rotate orbits the camera around the
    /// focus point at constant tilt. Tilt adjusts the camera's pitch
    /// while keeping the focus point fixed.
    /// </para>
    /// <para>
    /// Horizontal pan is unbounded — when the camera's X drifts past
    /// the cylindrical wrap threshold, the controller normalizes both
    /// the focus point and the camera back into the canonical range.
    /// Vertical pan is soft-clamped a couple of world units past the
    /// pole rows. Zoom and tilt are hard-clamped.
    /// </para>
    /// </remarks>
    public sealed class CivCameraController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Where camera input comes from. If null, searches the same GameObject for an ICameraInput component on Awake.")]
        [SerializeField] private MonoBehaviour _inputSource;

        [Header("Wrap")]
        [Tooltip("Wrap renderer used to compute the world's width in world units for horizontal normalization.")]
        [SerializeField] private WorldWrapRenderer _wrapRenderer;

        [Header("Pan")]
        [Tooltip("Focus-point movement speed in world units / second at the camera's current height.")]
        [SerializeField] private float _panSpeed = 25f;

        [Header("Zoom")]
        [SerializeField] private float _zoomSpeed = 5f;
        [SerializeField] private float _minHeight = 6f;
        [Tooltip("Furthest zoom-out, as camera height above the map. The camera also starts here.")]
        [SerializeField] private float _maxHeight = 22f;

        [Header("Rotate")]
        [Tooltip("Degrees per second of yaw rotation at full input.")]
        [SerializeField] private float _rotateSpeed = 90f;

        [Header("Tilt")]
        [Tooltip("Degrees per second of tilt at full input.")]
        [SerializeField] private float _tiltSpeed = 60f;
        [Tooltip("Shallowest camera pitch in degrees (camera X rotation). Lower values look more toward the horizon.")]
        [SerializeField] private float _minTilt = 40f;
        [SerializeField] private float _maxTilt = 65f;
        [SerializeField] private float _defaultTilt = 50f;

        [Header("Vertical bounds (focus point)")]
        [SerializeField] private float _verticalSoftMin = -2f;
        [SerializeField] private float _verticalSoftMax = 32f;

        private ICameraInput _input;
        private Vector3 _focusPoint;
        private float _yaw;
        private float _pitch;
        private float _height;

        private void Awake()
        {
            ResolveInput();
            _focusPoint = ComputeInitialFocusPoint();
            _yaw = 0f;
            _pitch = _defaultTilt;
            // Start fully zoomed out, so the first view is the widest one
            // the player is allowed and nothing above the cap is ever shown.
            _height = Mathf.Max(_minHeight, _maxHeight);
            ApplyCameraTransform();
        }

        private void Update()
        {
            // Lazy re-resolution covers two cases:
            //   1. CameraInputInstaller.AddComponent ran after our Awake.
            //   2. Buyer scripts attached a provider at runtime.
            if (_input == null) ResolveInput();
            if (_input == null) return;

            ApplyPan();
            ApplyZoom();
            ApplyRotate();
            ApplyTilt();
            ClampFocusPoint();
            NormalizeHorizontalIfWrapAvailable();
            ApplyCameraTransform();
        }

        private void ResolveInput()
        {
            if (_inputSource != null)
            {
                if (_inputSource is ICameraInput direct)
                {
                    _input = direct;
                    return;
                }
                // _inputSource may be a CameraInputInstaller (or any MonoBehaviour
                // sitting on the GameObject that holds the actual provider).
                ICameraInput sibling = _inputSource.GetComponent<ICameraInput>();
                if (sibling != null)
                {
                    _input = sibling;
                    return;
                }
            }
            _input = GetComponent<ICameraInput>();
        }

        private Vector3 ComputeInitialFocusPoint()
        {
            // Default to the camera's current XZ if there's a transform
            // already, else origin.
            return new Vector3(transform.position.x, 0f, transform.position.z);
        }

        private void ApplyPan()
        {
            Vector2 axis = _input.PanAxis;
            if (axis.sqrMagnitude < 1e-6f) return;

            // Direction "right" and "forward" relative to current yaw.
            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 right = yawRot * Vector3.right;
            Vector3 forward = yawRot * Vector3.forward;

            float dt = Time.deltaTime;
            // Scale pan speed by height so the world feels consistent at
            // any zoom level.
            float heightScale = Mathf.Lerp(0.5f, 2f, Mathf.InverseLerp(_minHeight, _maxHeight, _height));
            float speed = _panSpeed * heightScale * dt;

            _focusPoint += right * (axis.x * speed) + forward * (axis.y * speed);
        }

        private void ApplyZoom()
        {
            float delta = _input.ZoomDelta;
            if (Mathf.Abs(delta) < 1e-6f) return;
            // Zoom-in (positive delta) lowers the camera.
            _height = Mathf.Clamp(_height - delta * _zoomSpeed, _minHeight, _maxHeight);
        }

        private void ApplyRotate()
        {
            float axis = _input.RotateAxis;
            if (Mathf.Abs(axis) < 1e-6f) return;
            _yaw += axis * _rotateSpeed * Time.deltaTime;
            // Keep yaw in [0, 360) for numeric sanity over long sessions.
            _yaw = Mathf.Repeat(_yaw, 360f);
        }

        private void ApplyTilt()
        {
            float axis = _input.TiltAxis;
            if (Mathf.Abs(axis) < 1e-6f) return;
            _pitch = Mathf.Clamp(_pitch + axis * _tiltSpeed * Time.deltaTime, _minTilt, _maxTilt);
        }

        private void ClampFocusPoint()
        {
            _focusPoint.y = 0f;
            _focusPoint.z = Mathf.Clamp(_focusPoint.z, _verticalSoftMin, _verticalSoftMax);
        }

        private void NormalizeHorizontalIfWrapAvailable()
        {
            if (_wrapRenderer == null || _wrapRenderer.WorldTileWidth <= 0) return;
            float worldWidth = _wrapRenderer.WorldWidthInWorldUnits;
            if (worldWidth <= 0f) return;

            if (_focusPoint.x >= worldWidth) _focusPoint.x -= worldWidth;
            else if (_focusPoint.x < 0f)    _focusPoint.x += worldWidth;
        }

        private void ApplyCameraTransform()
        {
            // Compute the camera offset from the focus point based on
            // yaw, pitch, and height. The offset places the camera
            // behind the focus point so pitch tilts down-toward-target.
            //
            // Horizontal distance shrinks with steeper pitch (cos
            // term) so the camera doesn't end up far behind when
            // tilted vertical. The height field controls the overall
            // scale.
            float pitchRad = _pitch * Mathf.Deg2Rad;
            float yawRad = _yaw * Mathf.Deg2Rad;

            float horizontalDistance = _height / Mathf.Tan(pitchRad);
            Vector3 offset = new Vector3(
                -Mathf.Sin(yawRad) * horizontalDistance,
                _height,
                -Mathf.Cos(yawRad) * horizontalDistance);

            transform.position = _focusPoint + offset;
            transform.LookAt(_focusPoint, Vector3.up);
        }
    }
}
