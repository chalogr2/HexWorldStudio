using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Input;
using HexWorldStudio.Rendering;
using UnityEngine;

namespace HexWorldStudio.Examples.QuickStart
{
    /// <summary>
    /// Hover detection for the demo scene: every frame casts a ray
    /// from the camera through the mouse cursor, resolves the tile
    /// under the cursor via <see cref="HexTileBinding"/> on the
    /// raycast hit, and positions a <see cref="HexOutline"/> overlay
    /// at the hovered tile (ADR-0016).
    /// </summary>
    /// <remarks>
    /// <para>
    /// On raycast miss (e.g., the cursor points at sky, or PhysX's
    /// flat-mesh raycast precision drops a hit), the math fallback
    /// intersects the camera ray with the <c>Y = 0</c> plane and
    /// resolves through <see cref="WorldWrapRenderer.WorldToCoord"/>.
    /// </para>
    /// <para>
    /// Tile materials are never touched — the outline is a separate
    /// scene-graph GameObject that follows the hovered tile.
    /// </para>
    /// </remarks>
    public sealed class TileHover : MonoBehaviour
    {
        [Header("Scene wiring")]
        [SerializeField] private Camera _camera;
        [SerializeField] private WorldWrapRenderer _wrapRenderer;
        [SerializeField] private HexWorldStudio.Rendering.WorldGeneratorBootstrap _bootstrap;

        [Tooltip("MonoBehaviour implementing ICameraInput. If null, searches the GameObject for one on Awake.")]
        [SerializeField] private MonoBehaviour _inputSource;

        [Header("Outline visual")]
        [SerializeField] private Color _outlineColor = new Color(1f, 1f, 1f, 1f);
        [Range(0.01f, 0.5f)]
        [SerializeField] private float _outlineWidth = 0.05f;

        private ICameraInput _input;
        private GameObject _outline;

        private HexCoord? _hoveredCoord;
        private float _hoverStartTime;

        /// <summary>Currently hovered tile coord, or <c>null</c> when nothing is hovered.</summary>
        public HexCoord? HoveredCoord => _hoveredCoord;

        /// <summary>
        /// Seconds elapsed since the current hover began. Returns 0
        /// when nothing is hovered. Slice 10's tooltip reads this
        /// directly to decide when to reveal.
        /// </summary>
        public float HoverDuration => _hoveredCoord.HasValue ? (Time.realtimeSinceStartup - _hoverStartTime) : 0f;

        /// <summary>
        /// World-space position of the currently hovered tile (the
        /// anchor point used by the hover outline). Useful for
        /// downstream visuals like the selection outline.
        /// Defaults to <see cref="Vector3.zero"/> when nothing is
        /// hovered.
        /// </summary>
        public Vector3 HoveredWorldPosition { get; private set; }

        /// <summary>Fires when the hovered coord changes (including transitions to / from null).</summary>
        public event Action<HexCoord?> OnHoverChanged;

        private void Awake()
        {
            _outline = HexOutline.Create("HoverOutline", _outlineColor, _outlineWidth);
            _outline.transform.SetParent(transform, worldPositionStays: false);
            SetOutlineVisible(false);
        }

        private void Update()
        {
            Tick();
        }

        /// <summary>
        /// One step of the hover loop. Normally driven by
        /// <see cref="Update"/> every frame; exposed publicly so
        /// tests and buyer scripts can advance the state machine
        /// deterministically (the editor's MonoBehaviour auto-update
        /// doesn't always fire on runtime-AddComponent'd instances
        /// inside EditMode-driven test fixtures).
        /// </summary>
        public void Tick()
        {
            // Resolve input lazily — Awake may run before _inputSource
            // has been wired (tests AddComponent the controller then
            // populate fields; buyer scripts may do the same).
            if (_input == null) ResolveInput();
            if (_input == null || _camera == null) { ClearHover(); return; }
            if (!_input.IsMouseInWindow) { ClearHover(); return; }

            HexCoord? newCoord = PickHoveredCoord(out Vector3 hitWorldPos);
            ApplyHover(newCoord, hitWorldPos);
        }

        private void OnDestroy()
        {
            if (_outline != null) UnityEngine.Object.Destroy(_outline);
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

        // Returns the canonical coord under the cursor + the world
        // position to anchor the outline at. Returns (null, default)
        // when nothing is hovered (off-map, math fallback also misses).
        private HexCoord? PickHoveredCoord(out Vector3 hitWorldPos)
        {
            hitWorldPos = default;

            Ray ray = _camera.ScreenPointToRay(_input.MousePosition);

            // Primary path: raycast against scene colliders. Walks
            // up the transform parent chain because per-ADR-0017
            // prefab visuals can plant their own colliders on child
            // GameObjects under the tile root; the binding always
            // lives on the tile root itself.
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var binding = hit.collider.GetComponentInParent<HexTileBinding>();
                if (binding != null)
                {
                    hitWorldPos = binding.transform.position;
                    return binding.Coord;
                }
            }

            // Fallback: intersect with the Y=0 plane and resolve via
            // WorldWrapRenderer.WorldToCoord. Only useful for ground-
            // level tiles — elevated tiles are owned by the primary
            // raycast path.
            if (_wrapRenderer != null && _bootstrap != null && _bootstrap.LatestMap != null)
            {
                if (TryIntersectGroundPlane(ray, out Vector3 groundPoint))
                {
                    HexCoord candidate = _wrapRenderer.WorldToCoord(groundPoint, _bootstrap.LatestMap);
                    if (_bootstrap.LatestMap.Contains(candidate))
                    {
                        // Anchor the outline at the ground projection.
                        hitWorldPos = new Vector3(groundPoint.x, 0f, groundPoint.z);
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static bool TryIntersectGroundPlane(Ray ray, out Vector3 point)
        {
            // The ground plane is y = 0. Parametric: origin.y + dir.y * t = 0.
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false; // plane behind camera
            point = ray.origin + ray.direction * t;
            return true;
        }

        private void ApplyHover(HexCoord? newCoord, Vector3 hitWorldPos)
        {
            bool changed = !NullableCoordEquals(newCoord, _hoveredCoord);
            if (changed)
            {
                _hoveredCoord = newCoord;
                _hoverStartTime = newCoord.HasValue ? Time.realtimeSinceStartup : 0f;
                OnHoverChanged?.Invoke(_hoveredCoord);
            }

            if (newCoord.HasValue)
            {
                HoveredWorldPosition = hitWorldPos;
                if (_outline != null) _outline.transform.position = hitWorldPos;
                SetOutlineVisible(true);
            }
            else
            {
                HoveredWorldPosition = Vector3.zero;
                SetOutlineVisible(false);
            }
        }

        private void ClearHover()
        {
            if (_hoveredCoord.HasValue)
            {
                _hoveredCoord = null;
                _hoverStartTime = 0f;
                HoveredWorldPosition = Vector3.zero;
                OnHoverChanged?.Invoke(null);
            }
            SetOutlineVisible(false);
        }

        private void SetOutlineVisible(bool visible)
        {
            if (_outline != null && _outline.activeSelf != visible)
            {
                _outline.SetActive(visible);
            }
        }

        private static bool NullableCoordEquals(HexCoord? a, HexCoord? b)
        {
            if (a.HasValue != b.HasValue) return false;
            if (!a.HasValue) return true;
            return a.Value.Equals(b.Value);
        }
    }
}
